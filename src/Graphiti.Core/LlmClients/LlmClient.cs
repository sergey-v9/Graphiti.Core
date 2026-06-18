using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Graphiti.Core.LlmClients;

/// <summary>
/// Base class for LLM clients. Handles the shared request pipeline: message preparation (language and
/// schema instructions, input cleaning), optional response caching, structured-response validation,
/// telemetry, and token tracking. Concrete providers implement <see cref="GenerateResponseCoreAsync"/>.
/// </summary>
public abstract class LlmClient : ILlmClient, IDisposable
{
    private const string AttributeExtractionSentinel = "<<graphiti.attr_extraction.preamble.v1>>";
    private const int MaxValidationRetries = 2;
    private static readonly JsonTypeInfo<LlmCacheKeyPayload> CacheKeyPayloadJsonTypeInfo =
        (JsonTypeInfo<LlmCacheKeyPayload>)GraphitiJsonSerializer.Options.GetTypeInfo(typeof(LlmCacheKeyPayload));

    private readonly bool _ownsCache;
    private bool _disposed;

    /// <summary>Creates the client with optional config and an externally owned response cache.</summary>
    protected LlmClient(LlmConfig? config = null, ILlmResponseCache? cache = null)
        : this(config, cache, ownsCache: false)
    {
    }

    private LlmClient(LlmConfig? config, ILlmResponseCache? cache, bool ownsCache)
    {
        Config = config ?? new LlmConfig();
        LlmConfigValidation.ThrowIfInvalid(Config);
        Cache = cache;
        _ownsCache = ownsCache;
    }

    /// <summary>
    /// Creates the client, optionally constructing an owned SQLite response cache at
    /// <paramref name="cacheDirectory"/> when <paramref name="cache"/> is <c>true</c>.
    /// </summary>
    protected LlmClient(LlmConfig? config = null, bool cache = false, string cacheDirectory = "./llm_cache")
        : this(config, cache ? new SqliteLlmResponseCache(cacheDirectory) : null, ownsCache: cache)
    {
    }

    /// <summary>The validated configuration in effect for this client.</summary>
    public LlmConfig Config { get; }

    /// <summary>The primary model identifier.</summary>
    public string Model => Config.Model;

    /// <summary>The small-model identifier, if configured.</summary>
    public string? SmallModel => Config.SmallModel;

    /// <summary>The configured sampling temperature.</summary>
    public double Temperature => Config.Temperature;

    /// <summary>The configured maximum tokens per request.</summary>
    public int MaxTokens => Config.MaxTokens;

    /// <summary>Whether response caching is active.</summary>
    public bool CacheEnabled => Cache is not null;

    /// <summary>The response cache, or <c>null</c> if caching is disabled.</summary>
    public ILlmResponseCache? Cache { get; }

    /// <summary>Accumulates token usage reported across requests.</summary>
    public TokenUsageTracker TokenTracker { get; } = new();

    /// <summary>Returns the standard instruction that keeps extracted text in its source language.</summary>
    public static string GetExtractionLanguageInstruction(string? groupId = null) =>
        "\n\nAny extracted information should be returned in the same language as it was written in. " +
        "Only output non-English text when the user has written full sentences or phrases in that non-English language. " +
        "Otherwise, output English.";

    /// <summary>
    /// Generates a structured JSON response for the conversation. Optionally validates the result against
    /// a static <paramref name="responseModel"/> type or a runtime <paramref name="responseSchema"/>
    /// (not both), serves/stores via the cache when enabled, and records telemetry.
    /// </summary>
    /// <param name="messages">The conversation messages.</param>
    /// <param name="responseModel">Static type the response must conform to, or <c>null</c>.</param>
    /// <param name="responseSchema">Runtime schema the response must conform to, or <c>null</c>.</param>
    /// <param name="maxTokens">Per-request token cap; defaults to <see cref="MaxTokens"/>.</param>
    /// <param name="modelSize">Selects the primary or small model.</param>
    /// <param name="groupId">Optional graph partition, used for language instructions.</param>
    /// <param name="promptName">Optional prompt label for telemetry.</param>
    /// <param name="attributeExtraction">Adds the attribute-extraction preamble when <c>true</c>.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <exception cref="ArgumentException">Both a response model and a response schema were supplied.</exception>
    public async Task<JsonObject> GenerateResponseAsync(
        IReadOnlyList<Message> messages,
        Type? responseModel = null,
        StructuredResponseSchema? responseSchema = null,
        int? maxTokens = null,
        ModelSize modelSize = ModelSize.Medium,
        string? groupId = null,
        string? promptName = null,
        bool attributeExtraction = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (responseModel is not null && responseSchema is not null)
        {
            throw new ArgumentException(
                "Specify either a static response model or a runtime response schema, not both.",
                nameof(responseSchema));
        }

        var resolvedMaxTokens = LlmConfigValidation.ResolveMaxTokens(maxTokens, MaxTokens);
        var resolvedModel = modelSize == ModelSize.Small
            ? SmallModel ?? Model
            : Model;
        using var activity = GraphitiTelemetry.StartActivity("Llm.GenerateResponse");
        activity?.SetTag("gen_ai.operation.name", "chat");
        activity?.SetTag("gen_ai.request.model", resolvedModel);
        activity?.SetTag("graphiti.prompt_name", promptName);
        activity?.SetTag("graphiti.group_id", groupId);
        activity?.SetTag("graphiti.llm.model_size", modelSize.ToString());
        activity?.SetTag("graphiti.llm.max_tokens", resolvedMaxTokens);
        activity?.SetTag("graphiti.llm.cache_enabled", Cache is not null);
        activity?.SetTag("graphiti.llm.attribute_extraction", attributeExtraction);
        activity?.SetTag("graphiti.llm.message_count", messages.Count);
        activity?.SetTag("graphiti.llm.response_model", responseModel?.Name ?? responseSchema?.Name);

        try
        {
            var prepared = PrepareMessages(messages, responseModel, responseSchema, groupId, attributeExtraction);
            var cacheKey = GetCacheKey(prepared, responseModel, responseSchema, resolvedMaxTokens, modelSize, promptName);
            if (Cache is not null)
            {
                var cachedOrCreated = await Cache.GetOrCreateAsync(
                    cacheKey,
                    async token =>
                    {
                        var generated = await GenerateValidatedResponseWithRetryAsync(
                            prepared,
                            responseModel,
                            responseSchema,
                            resolvedMaxTokens,
                            modelSize,
                            promptName,
                            token).ConfigureAwait(false);
                        return generated;
                    },
                    cancellationToken).ConfigureAwait(false);
                ValidateResponse(cachedOrCreated, responseModel, responseSchema);
                activity?.SetTag("graphiti.llm.response_property_count", cachedOrCreated.Count);
                GraphitiTelemetry.SetOk(activity);
                return cachedOrCreated;
            }

            var response = await GenerateValidatedResponseWithRetryAsync(
                prepared,
                responseModel,
                responseSchema,
                resolvedMaxTokens,
                modelSize,
                promptName,
                cancellationToken).ConfigureAwait(false);

            activity?.SetTag("graphiti.llm.response_property_count", response.Count);
            GraphitiTelemetry.SetOk(activity);
            return response;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private async Task<JsonObject> GenerateValidatedResponseWithRetryAsync(
        IReadOnlyList<Message> preparedMessages,
        Type? responseModel,
        StructuredResponseSchema? responseSchema,
        int maxTokens,
        ModelSize modelSize,
        string? promptName,
        CancellationToken cancellationToken)
    {
        var liveMessages = preparedMessages;
        var retryCount = 0;

        while (true)
        {
            try
            {
                var response = await GenerateResponseCoreAsync(
                    liveMessages,
                    responseModel,
                    responseSchema,
                    maxTokens,
                    modelSize,
                    promptName,
                    cancellationToken).ConfigureAwait(false);
                ValidateResponse(response, responseModel, responseSchema);
                return response;
            }
            catch (JsonException exception) when (retryCount < MaxValidationRetries)
            {
                retryCount++;
                liveMessages = AppendValidationRetryMessage(liveMessages, exception);
            }
        }
    }

    private static List<Message> AppendValidationRetryMessage(
        IReadOnlyList<Message> messages,
        JsonException exception)
    {
        var retryMessages = new List<Message>(messages.Count + 1);
        for (var i = 0; i < messages.Count; i++)
        {
            retryMessages.Add(messages[i]);
        }

        retryMessages.Add(new Message(
            "user",
            "The previous response attempt was invalid. " +
            $"Error type: {exception.GetType().Name}. " +
            $"Error details: {exception.Message}. " +
            "Please try again with a valid response, ensuring the output matches " +
            "the expected format and constraints."));

        return retryMessages;
    }

    /// <summary>Disposes the owned response cache, if any.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_ownsCache && Cache is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static void ValidateResponse(
        JsonObject response,
        Type? responseModel,
        StructuredResponseSchema? responseSchema)
    {
        if (responseModel is not null)
        {
            StructuredResponseValidator.Validate(response, responseModel);
            return;
        }

        if (responseSchema is not null)
        {
            StructuredResponseValidator.Validate(response, responseSchema);
        }
    }

    /// <summary>
    /// Provider-specific generation. Receives already-prepared messages and resolved parameters and must
    /// return the model's JSON response. Validation and caching are handled by the base class.
    /// </summary>
    protected abstract Task<JsonObject> GenerateResponseCoreAsync(
        IReadOnlyList<Message> messages,
        Type? responseModel,
        StructuredResponseSchema? responseSchema,
        int maxTokens,
        ModelSize modelSize,
        string? promptName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Clones and augments the messages with attribute-extraction, schema, and language instructions and
    /// cleans control characters from their content.
    /// </summary>
    protected static IReadOnlyList<Message> PrepareMessages(
        IReadOnlyList<Message> messages,
        Type? responseModel,
        StructuredResponseSchema? responseSchema,
        string? groupId,
        bool attributeExtraction)
    {
        if (messages.Count == 0)
        {
            return Array.Empty<Message>();
        }

        var prepared = new List<Message>(messages.Count);
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            prepared.Add(new Message(message.Role, message.Content));
        }
        if (attributeExtraction)
        {
            ApplyAttributeExtractionPreamble(prepared);
        }

        var schemaJson = StructuredResponseValidator.GetSchemaJson(responseModel, responseSchema);
        if (schemaJson is not null)
        {
            var schemaNote = $"\n\nRespond with a JSON object in the following format:\n\n{schemaJson}";
            var last = prepared[^1];
            prepared[^1] = last with { Content = last.Content + schemaNote };
        }

        var first = prepared[0];
        prepared[0] = first with { Content = first.Content + GetExtractionLanguageInstruction(groupId) };

        for (var i = 0; i < prepared.Count; i++)
        {
            var message = prepared[i];
            var cleaned = CleanInput(message.Content);
            if (!ReferenceEquals(cleaned, message.Content))
            {
                prepared[i] = message with { Content = cleaned };
            }
        }

        return prepared;
    }

    /// <summary>Removes zero-width and non-printable control characters (keeping newlines and tabs).</summary>
    protected static string CleanInput(string input)
    {
        if (IsCleanInput(input))
        {
            return input;
        }

        return CleanInputSlow(input);
    }

    private static bool IsCleanInput(string input)
    {
        var remaining = input.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var charsConsumed);
            if (status != OperationStatus.Done || ShouldRemoveRune(rune.Value))
            {
                return false;
            }

            remaining = remaining[charsConsumed..];
        }

        return true;
    }

    private static string CleanInputSlow(string input)
    {
        var builder = new StringBuilder(input.Length);
        var remaining = input.AsSpan();
        Span<char> runeBuffer = stackalloc char[2];
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var charsConsumed);
            if (status != OperationStatus.Done)
            {
                remaining = remaining[1..];
                continue;
            }

            remaining = remaining[charsConsumed..];
            if (ShouldRemoveRune(rune.Value))
            {
                continue;
            }

            var charsWritten = rune.EncodeToUtf16(runeBuffer);
            builder.Append(runeBuffer[..charsWritten]);
        }

        return builder.ToString();
    }

    private static bool ShouldRemoveRune(int value) =>
        value is 0x200B or 0x200C or 0x200D or 0xFEFF or 0x2060 ||
        value < 32 && value is not ('\n' or '\r' or '\t');

    /// <summary>
    /// Computes a deterministic SHA-256 cache key over the messages, model selection, sampling settings,
    /// and response-schema fingerprint.
    /// </summary>
    protected string GetCacheKey(
        IReadOnlyList<Message> messages,
        Type? responseModel,
        StructuredResponseSchema? responseSchema,
        int maxTokens,
        ModelSize modelSize,
        string? promptName)
    {
        var resolvedModel = modelSize == ModelSize.Small
            ? SmallModel ?? Model
            : Model;
        var payload = new LlmCacheKeyPayload(
            Model,
            SmallModel,
            resolvedModel,
            modelSize.ToString(),
            Temperature,
            maxTokens,
            responseModel?.AssemblyQualifiedName ?? responseSchema?.Name,
            StructuredResponseValidator.GetSchemaFingerprint(responseModel, responseSchema),
            messages);
        var keyBytes = JsonSerializer.SerializeToUtf8Bytes(payload, CacheKeyPayloadJsonTypeInfo);
        var hash = SHA256.HashData(keyBytes);
        return Convert.ToHexStringLower(hash);
    }

    private static void ApplyAttributeExtractionPreamble(List<Message> messages)
    {
        if (messages.Count == 0 || messages[0].Content.Contains(AttributeExtractionSentinel, StringComparison.Ordinal))
        {
            return;
        }

        const string note =
            "\n\n" + AttributeExtractionSentinel + "\n" +
            "ATTRIBUTE EXTRACTION: Field descriptions in the response schema describe " +
            "what a real value LOOKS LIKE - they are NEVER themselves valid values and " +
            "must NEVER be copied into any field. If you have no value for a field, set " +
            "it to null; never explain the absence in the field itself.";

        var target = messages[0];
        messages[0] = target.Role == "system"
            ? target with { Content = target.Content + note }
            : target with { Content = note.TrimStart() + "\n\n" + target.Content };
    }
}
