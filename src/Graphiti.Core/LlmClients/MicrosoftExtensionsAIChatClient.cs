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

        ThrowIfRefused(response);
        var parsed = ParseJsonResponse(response.Text);
        SetPendingUsage(response, promptName);
        return parsed;

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

    private void SetPendingUsage(ChatResponse response, string? promptName)
    {
        var usage = response.Usage;
        if (usage is null)
        {
            return;
        }

        var input = usage.InputTokenCount.GetValueOrDefault();
        var output = usage.OutputTokenCount.GetValueOrDefault();
        SetPendingTokenUsage(promptName, input, output);
    }

    private static Microsoft.Extensions.AI.ChatMessage ToAIMessage(Message message) =>
        new(new ChatRole(message.Role), message.Content);

    /// <summary>
    /// Surfaces a non-retryable refusal when the provider signals a content filter, excluding it from
    /// the retry loop. Microsoft.Extensions.AI does not expose a structured refusal field through the
    /// abstraction, so the only refusal signal reliably available is
    /// <see cref="ChatFinishReason.ContentFilter"/>; an explicit textual refusal that the provider
    /// reports with a normal finish reason cannot be distinguished here and falls through to JSON
    /// validation (where it is retried like any other malformed response).
    /// </summary>
    private static void ThrowIfRefused(ChatResponse response)
    {
        if (response.FinishReason == ChatFinishReason.ContentFilter)
        {
            throw new LlmRefusalException(
                "The LLM refused to generate a response (content filter finish reason).");
        }
    }

    private static JsonObject ParseJsonResponse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            // An empty/whitespace model response is a failure, not a valid (empty) object. Throwing
            // JsonException here routes through LlmClient.GenerateValidatedResponseWithRetryAsync to
            // re-prompt with feedback instead of silently returning {}.
            throw new JsonException("Invalid response from LLM: the model returned an empty response.");
        }

        var trimmed = StripWrappingCodeFence(text);
        if (TryParseJsonNode(trimmed, out var parsed))
        {
            return ToResponseObject(parsed);
        }

        throw new JsonException("Could not parse a JSON value from the chat response.");
    }

    private static JsonObject ToResponseObject(JsonNode? parsed) =>
        parsed is JsonObject jsonObject
            ? jsonObject
            : new JsonObject { ["value"] = parsed };

    private static string StripWrappingCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var contentStart = 3;
        while (contentStart < trimmed.Length && IsFenceInfoCharacter(trimmed[contentStart]))
        {
            contentStart++;
        }

        while (contentStart < trimmed.Length && trimmed[contentStart] is ' ' or '\t')
        {
            contentStart++;
        }

        if (contentStart < trimmed.Length && trimmed[contentStart] == '\r')
        {
            contentStart++;
            if (contentStart < trimmed.Length && trimmed[contentStart] == '\n')
            {
                contentStart++;
            }
        }
        else if (contentStart < trimmed.Length && trimmed[contentStart] == '\n')
        {
            contentStart++;
        }

        var withoutOpeningFence = trimmed[contentStart..];
        withoutOpeningFence = withoutOpeningFence.Trim();
        if (withoutOpeningFence.EndsWith("```", StringComparison.Ordinal))
        {
            withoutOpeningFence = withoutOpeningFence[..^3].TrimEnd();
        }

        return withoutOpeningFence.Trim();
    }

    private static bool IsFenceInfoCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '_' or '-';

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
}
