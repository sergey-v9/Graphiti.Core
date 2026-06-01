using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Microsoft.Extensions.AI;
using Polly;

namespace Graphiti.Core.LlmClients;

/// <summary>
/// An <see cref="LlmClient"/> that delegates to any <c>Microsoft.Extensions.AI</c>
/// <see cref="IChatClient"/>, bridging Graphiti's structured-response and caching pipeline to the M.E.AI
/// abstraction. Supports an optional Polly resilience pipeline and rate limiter.
/// </summary>
public sealed class MicrosoftExtensionsAIChatClient : LlmClient
{
    private readonly IChatClient _chatClient;
    private readonly ResiliencePipeline<ChatResponse>? _pipeline;
    private readonly RateLimiter? _rateLimiter;

    /// <summary>Creates the client over the given chat client with optional resilience and rate limiting.</summary>
    public MicrosoftExtensionsAIChatClient(
        IChatClient chatClient,
        LlmConfig? config = null,
        ILlmResponseCache? cache = null,
        ResiliencePipeline<ChatResponse>? pipeline = null,
        RateLimiter? rateLimiter = null)
        : base(config, cache)
    {
        _chatClient = chatClient;
        _pipeline = pipeline;
        _rateLimiter = rateLimiter;
    }

    /// <inheritdoc />
    protected override async Task<JsonObject> GenerateResponseCoreAsync(
        IReadOnlyList<Message> messages,
        Type? responseModel,
        StructuredResponseSchema? responseSchema,
        int maxTokens,
        ModelSize modelSize,
        string? promptName,
        CancellationToken cancellationToken)
    {
        var options = new ChatOptions
        {
            MaxOutputTokens = maxTokens,
            ModelId = modelSize == ModelSize.Small ? SmallModel ?? Model : Model,
            Temperature = (float)Temperature,
            ResponseFormat = responseSchema is not null
                ? StructuredResponseValidator.CreateResponseFormat(responseSchema)
                : responseModel is not null
                    ? StructuredResponseValidator.CreateResponseFormat(responseModel)
                    : ChatResponseFormat.Json
        };

        var aiMessages = new List<Microsoft.Extensions.AI.ChatMessage>(messages.Count);
        for (var i = 0; i < messages.Count; i++)
        {
            aiMessages.Add(ToAIMessage(messages[i]));
        }

        var response = _pipeline is null
            ? await ExecuteProviderCallAsync(cancellationToken).ConfigureAwait(false)
            : await _pipeline.ExecuteAsync(
                ExecuteProviderCallAsync,
                cancellationToken).ConfigureAwait(false);

        TrackUsage(response, promptName);
        return ParseJsonResponse(response.Text);

        async ValueTask<ChatResponse> ExecuteProviderCallAsync(CancellationToken token)
        {
            using var activity = GraphitiTelemetry.StartActivity("Llm.ProviderCall");
            activity?.SetTag("graphiti.provider.abstraction", "microsoft_extensions_ai");
            activity?.SetTag("gen_ai.operation.name", "chat");
            activity?.SetTag("gen_ai.request.model", options.ModelId);
            activity?.SetTag("graphiti.prompt_name", promptName);
            activity?.SetTag("graphiti.llm.model_size", modelSize.ToString());
            activity?.SetTag("graphiti.llm.max_tokens", maxTokens);
            activity?.SetTag("graphiti.llm.message_count", messages.Count);
            activity?.SetTag("graphiti.provider.rate_limited", _rateLimiter is not null);

            try
            {
                using var lease = await AIProviderRateLimiter.AcquireAsync(
                    _rateLimiter,
                    token).ConfigureAwait(false);
                var response = await _chatClient.GetResponseAsync(
                    aiMessages,
                    options,
                    token).ConfigureAwait(false);
                activity?.SetTag("graphiti.llm.provider_response.length", response.Text?.Length ?? 0);
                SetUsageTags(activity, response.Usage);
                GraphitiTelemetry.SetOk(activity);
                return response;
            }
            catch (Exception exception)
            {
                GraphitiTelemetry.RecordException(activity, exception);
                throw;
            }
        }
    }

    private static void SetUsageTags(System.Diagnostics.Activity? activity, UsageDetails? usage)
    {
        if (usage is null)
        {
            return;
        }

        if (usage.InputTokenCount is { } inputTokens)
        {
            activity?.SetTag("gen_ai.usage.input_tokens", inputTokens);
        }

        if (usage.OutputTokenCount is { } outputTokens)
        {
            activity?.SetTag("gen_ai.usage.output_tokens", outputTokens);
        }

        if (usage.TotalTokenCount is { } totalTokens)
        {
            activity?.SetTag("gen_ai.usage.total_tokens", totalTokens);
        }
    }

    private void TrackUsage(ChatResponse response, string? promptName)
    {
        var usage = response.Usage;
        if (usage is null)
        {
            return;
        }

        var input = usage.InputTokenCount.GetValueOrDefault();
        var output = usage.OutputTokenCount.GetValueOrDefault();
        TokenTracker.AddUsage(promptName ?? string.Empty, input, output);
    }

    private static Microsoft.Extensions.AI.ChatMessage ToAIMessage(Message message) =>
        new(new ChatRole(message.Role), message.Content);

    private static JsonObject ParseJsonResponse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonObject();
        }

        var trimmed = text.Trim();
        if (TryParseJsonNode(trimmed, out var parsed)
            || TryExtractJsonNode(trimmed, out parsed))
        {
            return ToResponseObject(parsed);
        }

        throw new JsonException("Could not parse a JSON value from the chat response.");
    }

    private static JsonObject ToResponseObject(JsonNode? parsed) =>
        parsed is JsonObject jsonObject
            ? jsonObject
            : new JsonObject { ["value"] = parsed };

    private static bool TryExtractJsonNode(string text, out JsonNode? parsed)
    {
        var searchStart = 0;
        while (searchStart < text.Length)
        {
            var relativeStart = text.AsSpan(searchStart).IndexOfAny('{', '[');
            if (relativeStart < 0)
            {
                break;
            }

            var candidateStart = searchStart + relativeStart;
            if (TryParseJsonNodePrefix(text.AsSpan(candidateStart), out parsed))
            {
                return true;
            }

            searchStart = candidateStart + 1;
        }

        parsed = null;
        return false;
    }

    private static bool TryParseJsonNode(string text, out JsonNode? parsed)
    {
        return TryParseJsonNode(text.AsSpan(), out parsed);
    }

    private static bool TryParseJsonNode(ReadOnlySpan<char> text, out JsonNode? parsed)
    {
        try
        {
            parsed = JsonSerializer.Deserialize<JsonNode>(text, GraphitiJsonSerializer.Options);
            return true;
        }
        catch (JsonException)
        {
            parsed = null;
            return false;
        }
    }

    private static bool TryParseJsonNodePrefix(ReadOnlySpan<char> text, out JsonNode? parsed)
    {
        if (text.IsEmpty || text[0] is not ('{' or '['))
        {
            parsed = null;
            return false;
        }

        byte[]? rented = null;
        var maxByteCount = Encoding.UTF8.GetMaxByteCount(text.Length);
        Span<byte> utf8 = maxByteCount <= 4096
            ? stackalloc byte[maxByteCount]
            : rented = ArrayPool<byte>.Shared.Rent(maxByteCount);
        try
        {
            var byteCount = Encoding.UTF8.GetBytes(text, utf8);
            var reader = new Utf8JsonReader(utf8[..byteCount]);
            parsed = JsonNode.Parse(ref reader);
            return true;
        }
        catch (JsonException)
        {
            parsed = null;
            return false;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
