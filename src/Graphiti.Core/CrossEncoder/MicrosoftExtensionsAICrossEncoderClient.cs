using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Graphiti.Core.Internal;
using Microsoft.Extensions.AI;
using Polly;

namespace Graphiti.Core.CrossEncoder;

/// <summary>
/// Cross-encoder reranker backed by a <c>Microsoft.Extensions.AI</c> <see cref="IChatClient"/>.
/// It asks the model for a structured boolean relevance decision per passage and converts the
/// decision confidence into a relevance score.
/// </summary>
public sealed class MicrosoftExtensionsAICrossEncoderClient : CrossEncoderClient
{
    /// <summary>Default model used when no LLM configuration is supplied.</summary>
    public const string DefaultModel = "gpt-4.1-nano";

    private const int DefaultMaxConcurrency = 20;
    private const int MaxOutputTokens = 64;

    private readonly IChatClient _chatClient;
    private readonly LlmConfig _config;
    private readonly ResiliencePipeline<ChatResponse>? _pipeline;
    private readonly RateLimiter? _rateLimiter;
    private readonly int _maxConcurrency;

    /// <summary>
    /// Creates the reranker over the given chat client with optional model configuration,
    /// resilience, rate limiting, and concurrency control.
    /// </summary>
    public MicrosoftExtensionsAICrossEncoderClient(
        IChatClient chatClient,
        LlmConfig? config = null,
        ResiliencePipeline<ChatResponse>? pipeline = null,
        RateLimiter? rateLimiter = null,
        int maxConcurrency = DefaultMaxConcurrency)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxConcurrency);

        _chatClient = chatClient;
        _config = config ?? new LlmConfig
        {
            Model = DefaultModel,
            SmallModel = DefaultModel,
            Temperature = 0,
            MaxTokens = MaxOutputTokens
        };
        LlmConfigValidation.ThrowIfInvalid(_config);
        _pipeline = pipeline;
        _rateLimiter = rateLimiter;
        _maxConcurrency = maxConcurrency;
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<(string Passage, float Score)>> RankAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken = default)
    {
        var ranks = await RankIndexedAsync(query, passages, cancellationToken).ConfigureAwait(false);
        var results = new List<(string Passage, float Score)>(ranks.Count);
        for (var i = 0; i < ranks.Count; i++)
        {
            results.Add((ranks[i].Passage, ranks[i].Score));
        }

        return results;
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<CrossEncoderRank>> RankIndexedAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(passages);
        cancellationToken.ThrowIfCancellationRequested();
        if (passages.Count == 0)
        {
            return Array.Empty<CrossEncoderRank>();
        }

        using var semaphore = new SemaphoreSlim(Math.Min(_maxConcurrency, passages.Count));
        var tasks = new Task<CrossEncoderRank>[passages.Count];
        for (var i = 0; i < passages.Count; i++)
        {
            tasks[i] = ScorePassageIndexedAsync(
                query,
                passages[i],
                i,
                semaphore,
                cancellationToken);
        }

        var ranks = await Task.WhenAll(tasks).ConfigureAwait(false);
        Array.Sort(ranks, CompareRanks);
        return ranks;
    }

    private async Task<CrossEncoderRank> ScorePassageIndexedAsync(
        string query,
        string passage,
        int index,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var score = await ScorePassageAsync(query, passage, cancellationToken).ConfigureAwait(false);
            return new CrossEncoderRank(index, passage, score);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<float> ScorePassageAsync(
        string query,
        string passage,
        CancellationToken cancellationToken)
    {
        var options = new ChatOptions
        {
            MaxOutputTokens = Math.Min(_config.MaxTokens, MaxOutputTokens),
            // The reranker always uses the PRIMARY model (never the small model), falling back to the
            // default reranker model only when the configured model is unset/blank.
            ModelId = string.IsNullOrWhiteSpace(_config.Model) ? DefaultModel : _config.Model,
            Temperature = 0,
            ResponseFormat = StructuredResponseValidator.CreateResponseFormat(typeof(RerankerRelevanceResponse))
        };
        var messages = BuildMessages(query, passage);
        var response = _pipeline is null
            ? await ExecuteProviderCallAsync(cancellationToken).ConfigureAwait(false)
            : await _pipeline.ExecuteAsync(
                ExecuteProviderCallAsync,
                cancellationToken).ConfigureAwait(false);
        var responseObject = ParseJsonResponse(response.Text);
        StructuredResponseValidator.Validate(responseObject, typeof(RerankerRelevanceResponse));
        var relevance = responseObject.Deserialize<RerankerRelevanceResponse>(GraphitiJsonSerializer.Options)
                        ?? new RerankerRelevanceResponse();
        return RelevanceProbability(relevance);

        async ValueTask<ChatResponse> ExecuteProviderCallAsync(CancellationToken token)
        {
            using var activity = GraphitiTelemetry.StartActivity("CrossEncoder.ProviderCall");
            activity?.SetTag("graphiti.provider.abstraction", "microsoft_extensions_ai");
            activity?.SetTag("gen_ai.operation.name", "chat");
            activity?.SetTag("gen_ai.request.model", options.ModelId);
            activity?.SetTag("graphiti.reranker.max_tokens", options.MaxOutputTokens);
            activity?.SetTag("graphiti.query.length", query.Length);
            activity?.SetTag("graphiti.passage.length", passage.Length);
            activity?.SetTag("graphiti.provider.rate_limited", _rateLimiter is not null);

            try
            {
                using var lease = await AIProviderRateLimiter.AcquireAsync(
                    _rateLimiter,
                    token).ConfigureAwait(false);
                var providerResponse = await _chatClient.GetResponseAsync(
                    messages,
                    options,
                    token).ConfigureAwait(false);
                activity?.SetTag("graphiti.reranker.provider_response.length", providerResponse.Text?.Length ?? 0);
                GraphitiTelemetry.SetOk(activity);
                return providerResponse;
            }
            catch (Exception exception)
            {
                GraphitiTelemetry.RecordException(activity, exception);
                throw;
            }
        }
    }

    private static Microsoft.Extensions.AI.ChatMessage[] BuildMessages(string query, string passage) =>
        new[]
        {
            new Microsoft.Extensions.AI.ChatMessage(
                ChatRole.System,
                "You are an expert tasked with determining whether the passage is relevant to the query"),
            new Microsoft.Extensions.AI.ChatMessage(
                ChatRole.User,
                $"""
                Respond with "True" if PASSAGE is relevant to QUERY and "False" otherwise.
                Return your decision and confidence as JSON matching the provided schema.
                <PASSAGE>
                {passage}
                </PASSAGE>
                <QUERY>
                {query}
                </QUERY>
                """)
        };

    private static JsonObject ParseJsonResponse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new JsonException("Could not parse a JSON object from the reranker response.");
        }

        var node = JsonNode.Parse(text);
        return node as JsonObject
               ?? throw new JsonException("Reranker response must be a JSON object.");
    }

    private static float RelevanceProbability(RerankerRelevanceResponse response)
    {
        var confidence = float.IsFinite(response.Confidence)
            ? Math.Clamp(response.Confidence, 0, 1)
            : 0;
        return response.IsRelevant ? confidence : 1 - confidence;
    }

    private static int CompareRanks(CrossEncoderRank left, CrossEncoderRank right)
    {
        var scoreComparison = right.Score.CompareTo(left.Score);
        return scoreComparison != 0
            ? scoreComparison
            : left.Index.CompareTo(right.Index);
    }

    internal sealed class RerankerRelevanceResponse
    {
        public bool IsRelevant { get; set; }

        public float Confidence { get; set; }
    }
}
