using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Microsoft.Extensions.AI;
using Polly;
using Polly.Retry;

namespace Graphiti.Core.Tests;

public class ProviderResilienceWorkflowTests
{
    [Fact]
    public async Task AddEpisode_TransientRateLimitFailureRetriesAndPersistsGraph()
    {
        var groupId = "transient-retry";
        var driver = new InMemoryGraphDriver();
        var chatClient = new WorkflowChatClient(failuresBeforeSuccess: 1);
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new MicrosoftExtensionsAIChatClient(
                chatClient,
                pipeline: RetryTwice<ChatResponse>()),
            embedder: new HashEmbedder(embeddingDimension: 3));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: groupId);

        Assert.Equal(3, chatClient.Calls);
        Assert.Equal(2, result.Nodes.Count);
        Assert.Single(result.Edges);
        Assert.Single(await driver.GetNodesByGroupIdsAsync<EpisodicNode>([groupId]));
        Assert.Equal(2, (await driver.GetNodesByGroupIdsAsync<EntityNode>([groupId])).Count);
        Assert.Single(await driver.GetEdgesByGroupIdsAsync<EntityEdge>([groupId]));
    }

    [Fact]
    public async Task AddEpisode_RejectedRateLimitPermitLeavesGraphEmpty()
    {
        var groupId = "rate-limit-rejected";
        var driver = new InMemoryGraphDriver();
        var chatClient = new WorkflowChatClient();
        using var rateLimiter = new RejectedRateLimiter();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new MicrosoftExtensionsAIChatClient(chatClient, rateLimiter: rateLimiter),
            embedder: new HashEmbedder(embeddingDimension: 3));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            graphiti.AddEpisodeAsync(
                "conversation",
                "Alice works at Acme.",
                "message",
                new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                groupId: groupId));

        Assert.Equal("Could not acquire a Graphiti AI provider rate-limit permit.", exception.Message);
        Assert.Equal(0, chatClient.Calls);
        Assert.Equal(1, rateLimiter.AcquireAsyncCalls);
        Assert.Equal(1, rateLimiter.LeasesDisposed);
        Assert.Empty(await driver.GetNodesByGroupIdsAsync<EpisodicNode>([groupId]));
        Assert.Empty(await driver.GetNodesByGroupIdsAsync<EntityNode>([groupId]));
        Assert.Empty(await driver.GetEdgesByGroupIdsAsync<EpisodicEdge>([groupId]));
        Assert.Empty(await driver.GetEdgesByGroupIdsAsync<EntityEdge>([groupId]));
    }

    [Fact]
    public async Task AddEpisode_EmptyProviderResponseRetriesThenLeavesGraphEmpty()
    {
        var groupId = "empty-response";
        var driver = new InMemoryGraphDriver();
        var chatClient = new EmptyResponseChatClient();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new MicrosoftExtensionsAIChatClient(chatClient),
            embedder: new HashEmbedder(embeddingDimension: 3));

        await Assert.ThrowsAsync<JsonException>(() =>
            graphiti.AddEpisodeAsync(
                "conversation",
                "Alice works at Acme.",
                "message",
                new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                groupId: groupId));

        Assert.Equal(3, chatClient.Calls);
        Assert.Empty(await driver.GetNodesByGroupIdsAsync<EpisodicNode>([groupId]));
        Assert.Empty(await driver.GetNodesByGroupIdsAsync<EntityNode>([groupId]));
        Assert.Empty(await driver.GetEdgesByGroupIdsAsync<EpisodicEdge>([groupId]));
        Assert.Empty(await driver.GetEdgesByGroupIdsAsync<EntityEdge>([groupId]));
    }

    [Fact]
    public async Task AddEpisode_SchemaValidationFailureDoesNotPersistGraphContent()
    {
        var groupId = "schema-failure";
        var driver = new InMemoryGraphDriver();
        var llm = new InvalidNodeExtractionLlmClient();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: llm,
            embedder: new HashEmbedder(embeddingDimension: 3));

        await Assert.ThrowsAsync<JsonException>(() =>
            graphiti.AddEpisodeAsync(
                "conversation",
                "Alice works at Acme.",
                "message",
                new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                groupId: groupId));

        Assert.Equal(3, llm.Calls);
        Assert.Empty(await driver.GetNodesByGroupIdsAsync<EpisodicNode>([groupId]));
        Assert.Empty(await driver.GetNodesByGroupIdsAsync<EntityNode>([groupId]));
        Assert.Empty(await driver.GetEdgesByGroupIdsAsync<EpisodicEdge>([groupId]));
        Assert.Empty(await driver.GetEdgesByGroupIdsAsync<EntityEdge>([groupId]));
    }

    [Fact]
    public async Task AddEpisodeBulk_PartialSchemaValidationFailureLeavesOnlyEpisodeProvenance()
    {
        var groupId = "bulk-partial-failure";
        var driver = new InMemoryGraphDriver();
        var llm = new BulkPartialValidationFailureLlmClient();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: llm,
            embedder: new HashEmbedder(embeddingDimension: 3),
            maxCoroutines: 2);

        await Assert.ThrowsAsync<JsonException>(() =>
            graphiti.AddEpisodeBulkAsync(
                [
                    new RawEpisode
                    {
                        Name = "valid",
                        Content = "Alice works at Acme.",
                        SourceDescription = "message",
                        ReferenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                    },
                    new RawEpisode
                    {
                        Name = "broken",
                        Content = "Broken extraction payload.",
                        SourceDescription = "message",
                        ReferenceTime = new DateTime(2026, 1, 1, 12, 1, 0, DateTimeKind.Utc)
                    }
                ],
                groupId: groupId));

        Assert.Equal(3, llm.BrokenNodeExtractionCalls);
        Assert.Equal(2, (await driver.GetNodesByGroupIdsAsync<EpisodicNode>([groupId])).Count);
        Assert.Empty(await driver.GetNodesByGroupIdsAsync<EntityNode>([groupId]));
        Assert.Empty(await driver.GetEdgesByGroupIdsAsync<EpisodicEdge>([groupId]));
        Assert.Empty(await driver.GetEdgesByGroupIdsAsync<EntityEdge>([groupId]));
    }

    [Fact]
    public async Task AddEpisode_EmbeddingDimensionFailureLeavesGraphEmpty()
    {
        var groupId = "embedding-failure";
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new ValidExtractionLlmClient(),
            embedder: new WrongDimensionBatchEmbedder());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            graphiti.AddEpisodeAsync(
                "conversation",
                "Alice works at Acme.",
                "message",
                new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                groupId: groupId));

        Assert.Contains("entity node", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dimension 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("expected 3", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await driver.GetNodesByGroupIdsAsync<EpisodicNode>([groupId]));
        Assert.Empty(await driver.GetNodesByGroupIdsAsync<EntityNode>([groupId]));
        Assert.Empty(await driver.GetEdgesByGroupIdsAsync<EpisodicEdge>([groupId]));
        Assert.Empty(await driver.GetEdgesByGroupIdsAsync<EntityEdge>([groupId]));
    }

    [Fact]
    public async Task Search_CrossEncoderFailureSurfacesWithoutMutatingGraph()
    {
        var groupId = "cross-encoder-failure";
        var driver = new InMemoryGraphDriver();
        var embedder = new HashEmbedder(embeddingDimension: 4);
        var alice = new EntityNode { Uuid = "alice", Name = "Alice", GroupId = groupId };
        var acme = new EntityNode { Uuid = "acme", Name = "Acme", GroupId = groupId };
        var edge = new EntityEdge
        {
            Uuid = "edge-alice-acme",
            Name = "WORKS_AT",
            Fact = "Alice works at Acme.",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = acme.Uuid,
            GroupId = groupId
        };
        await driver.SaveBulkAsync([], [], [alice, acme], [edge], embedder);
        var crossEncoder = new ThrowingCrossEncoder();
        var graphiti = new Graphiti(
            graphDriver: driver,
            embedder: embedder,
            crossEncoder: crossEncoder);
        var config = new SearchConfig
        {
            EdgeConfig = new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25 },
                Reranker = EdgeReranker.CrossEncoder
            },
            Limit = 2
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            graphiti.SearchAdvancedAsync("Alice Acme", config, groupIds: [groupId]));

        Assert.Equal("cross encoder unavailable", exception.Message);
        Assert.Equal(1, crossEncoder.Calls);
        Assert.Equal(["Alice works at Acme."], crossEncoder.LastPassages);
        Assert.Equal(["acme", "alice"], (await driver.GetNodesByGroupIdsAsync<EntityNode>([groupId]))
            .Select(node => node.Uuid)
            .Order(StringComparer.Ordinal));
        Assert.Equal("edge-alice-acme", Assert.Single(await driver.GetEdgesByGroupIdsAsync<EntityEdge>([groupId])).Uuid);
    }

    private sealed class InvalidNodeExtractionLlmClient : LlmClient
    {
        public InvalidNodeExtractionLlmClient()
            : base(config: null, cache: (ILlmResponseCache?)null)
        {
        }

        public int Calls { get; private set; }

        protected override Task<JsonObject> GenerateResponseCoreAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel,
            StructuredResponseSchema? responseSchema,
            int maxTokens,
            ModelSize modelSize,
            string? promptName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new JsonObject
            {
                ["extracted_entities"] = new JsonArray(
                    new JsonObject { ["name"] = "Alice" })
            });
        }
    }

    private sealed class BulkPartialValidationFailureLlmClient : LlmClient
    {
        public BulkPartialValidationFailureLlmClient()
            : base(config: null, cache: (ILlmResponseCache?)null)
        {
        }

        public int BrokenNodeExtractionCalls { get; private set; }

        protected override Task<JsonObject> GenerateResponseCoreAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel,
            StructuredResponseSchema? responseSchema,
            int maxTokens,
            ModelSize modelSize,
            string? promptName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsNodeExtractionPrompt(promptName))
            {
                if (messages.Any(message =>
                        message.Content.Contains("Broken extraction payload.", StringComparison.Ordinal)))
                {
                    BrokenNodeExtractionCalls++;
                    return Task.FromResult(new JsonObject
                    {
                        ["extracted_entities"] = new JsonArray(
                            new JsonObject { ["name"] = "Broken" })
                    });
                }

                return Task.FromResult(new JsonObject
                {
                    ["extracted_entities"] = new JsonArray(
                        new JsonObject { ["name"] = "Alice", ["entity_type_id"] = 0 })
                });
            }

            if (string.Equals(promptName, "extract_edges.edge", StringComparison.Ordinal))
            {
                return Task.FromResult(new JsonObject { ["edges"] = new JsonArray() });
            }

            return Task.FromResult(new JsonObject());
        }
    }

    private sealed class ValidExtractionLlmClient : ILlmClient
    {
        public TokenUsageTracker TokenTracker { get; } = new();

        public Task<JsonObject> GenerateResponseAsync(
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
            cancellationToken.ThrowIfCancellationRequested();
            if (IsNodeExtractionPrompt(promptName))
            {
                return Task.FromResult(new JsonObject
                {
                    ["extracted_entities"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "Alice", ["entity_type_id"] = 0 },
                        new JsonObject { ["name"] = "Acme", ["entity_type_id"] = 0 }
                    }
                });
            }

            if (string.Equals(promptName, "extract_edges.edge", StringComparison.Ordinal))
            {
                return Task.FromResult(new JsonObject
                {
                    ["edges"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["source_entity_name"] = "Alice",
                            ["target_entity_name"] = "Acme",
                            ["relation_type"] = "WORKS_AT",
                            ["fact"] = "Alice works at Acme.",
                            ["valid_at"] = "2026-01-01T00:00:00Z"
                        }
                    }
                });
            }

            return Task.FromResult(new JsonObject());
        }
    }

    private sealed class WrongDimensionBatchEmbedder : EmbedderClient
    {
        public WrongDimensionBatchEmbedder()
            : base(new EmbedderConfig(embeddingDimension: 3))
        {
        }

        public override Task<IReadOnlyList<float>> CreateAsync(
            string input,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<float>>([1f, 2f, 3f]);
        }

        public override Task<IReadOnlyList<IReadOnlyList<float>>> CreateBatchAsync(
            IReadOnlyList<string> input,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<IReadOnlyList<float>> embeddings = input
                .Select(_ => (IReadOnlyList<float>)[1f, 2f])
                .ToArray();
            return Task.FromResult(embeddings);
        }
    }

    private sealed class ThrowingCrossEncoder : CrossEncoderClient
    {
        public int Calls { get; private set; }
        public IReadOnlyList<string> LastPassages { get; private set; } = [];

        public override Task<IReadOnlyList<(string Passage, float Score)>> RankAsync(
            string query,
            IReadOnlyList<string> passages,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("cross encoder unavailable");

        public override Task<IReadOnlyList<CrossEncoderRank>> RankIndexedAsync(
            string query,
            IReadOnlyList<string> passages,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastPassages = passages.ToArray();
            throw new InvalidOperationException("cross encoder unavailable");
        }
    }

    private sealed class WorkflowChatClient : IChatClient
    {
        private int _failuresBeforeSuccess;

        public WorkflowChatClient(int failuresBeforeSuccess = 0) =>
            _failuresBeforeSuccess = failuresBeforeSuccess;

        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (_failuresBeforeSuccess > 0)
            {
                _failuresBeforeSuccess--;
                throw new InvalidOperationException("429 Too Many Requests");
            }

            var promptText = string.Join(
                "\n",
                messages.Select(message => message.Text));
            var responseText = IsEdgeExtractionPrompt(promptText)
                ? """
                  {"edges":[{"source_entity_name":"Alice","target_entity_name":"Acme","relation_type":"WORKS_AT","fact":"Alice works at Acme.","valid_at":"2026-01-01T00:00:00Z"}]}
                  """
                : """
                  {"extracted_entities":[{"name":"Alice","entity_type_id":0},{"name":"Acme","entity_type_id":0}]}
                  """;
            return Task.FromResult(new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(
                ChatRole.Assistant,
                responseText)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class EmptyResponseChatClient : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(
                ChatRole.Assistant,
                "   ")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RejectedRateLimiter : RateLimiter
    {
        private int _acquireAsyncCalls;
        private int _leasesDisposed;

        public int AcquireAsyncCalls => Volatile.Read(ref _acquireAsyncCalls);

        public int LeasesDisposed => Volatile.Read(ref _leasesDisposed);

        public override TimeSpan? IdleDuration => null;

        public override RateLimiterStatistics? GetStatistics() => null;

        protected override RateLimitLease AttemptAcquireCore(int permitCount) =>
            throw new NotSupportedException();

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(
            int permitCount,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _acquireAsyncCalls);
            return ValueTask.FromResult<RateLimitLease>(new TrackingRateLimitLease(
                isAcquired: false,
                () => Interlocked.Increment(ref _leasesDisposed)));
        }
    }

    private sealed class TrackingRateLimitLease(
        bool isAcquired,
        Action onDispose) : RateLimitLease
    {
        public override bool IsAcquired => isAcquired;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                onDispose();
            }

            base.Dispose(disposing);
        }
    }

    private static ResiliencePipeline<T> RetryTwice<T>() =>
        new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                Delay = TimeSpan.Zero,
                MaxRetryAttempts = 2,
                ShouldHandle = new PredicateBuilder<T>().Handle<InvalidOperationException>()
            })
            .Build();

    private static bool IsEdgeExtractionPrompt(string promptText) =>
        promptText.Contains("source_entity_name", StringComparison.Ordinal)
        || promptText.Contains("target_entity_name", StringComparison.Ordinal)
        || promptText.Contains("relation_type", StringComparison.Ordinal);

    private static bool IsNodeExtractionPrompt(string? promptName) =>
        string.Equals(promptName, "extract_nodes.extract_message", StringComparison.Ordinal)
        || string.Equals(promptName, "extract_nodes.extract_text", StringComparison.Ordinal)
        || string.Equals(promptName, "extract_nodes.extract_json", StringComparison.Ordinal);
}
