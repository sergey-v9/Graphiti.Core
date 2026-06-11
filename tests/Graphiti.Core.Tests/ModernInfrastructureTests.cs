using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Graphiti.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Graphiti.Core.Tests;

[Collection(ContentChunkingTestCollection.Name)]
public class ModernInfrastructureTests
{
    public static TheoryData<EpisodeType, string> EpisodeTypeWireValues =>
        new()
        {
            { EpisodeType.Message, "message" },
            { EpisodeType.Json, "json" },
            { EpisodeType.Text, "text" },
            { EpisodeType.FactTriple, "fact_triple" }
        };

    [Fact]
    public void CoreModelDefaults_DoNotUseAmbientClock()
    {
        Assert.Equal(GraphitiHelpers.DefaultTimestamp, new EntityNode().CreatedAt);

        var episode = new EpisodicNode();
        Assert.Equal(GraphitiHelpers.DefaultTimestamp, episode.CreatedAt);
        Assert.Equal(GraphitiHelpers.DefaultTimestamp, episode.ValidAt);

        Assert.Equal(GraphitiHelpers.DefaultTimestamp, new EntityEdge().CreatedAt);
        Assert.Equal(GraphitiHelpers.DefaultTimestamp, new RawEpisode().ReferenceTime);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_UsesChatClientAndStandardCache()
    {
        var chatClient = new FakeChatClient();
        var graphitiClient = new MicrosoftExtensionsAIChatClient(
            chatClient,
            cache: new MemoryLlmResponseCache());

        var first = await graphitiClient.GenerateResponseAsync(
            new[] { new Message("user", "extract") },
            promptName: "test");
        var second = await graphitiClient.GenerateResponseAsync(
            new[] { new Message("user", "extract") },
            promptName: "test");

        Assert.True(first["ok"]?.GetValue<bool>());
        Assert.Equal(first.ToJsonString(), second.ToJsonString());
        Assert.Equal(1, chatClient.Calls);
        Assert.True(graphitiClient.TokenTracker.Usage.ContainsKey("test"));
        Assert.Equal(3, graphitiClient.TokenTracker.GetTotalUsage().TotalTokens);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_ValidatesStructuredResponses()
    {
        var graphitiClient = new MicrosoftExtensionsAIChatClient(new FakeChatClient("{\"ok\":true}"));

        var response = await graphitiClient.GenerateResponseAsync(
            new[] { new Message("user", "extract") },
            responseModel: typeof(StructuredResponse));

        Assert.True(response["ok"]?.GetValue<bool>());
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_RetriesInvalidJsonWithFeedbackWithoutPipeline()
    {
        var chatClient = new SequencedChatClient("not-json", "{\"ok\":true}");
        var graphitiClient = new MicrosoftExtensionsAIChatClient(chatClient);

        var response = await graphitiClient.GenerateResponseAsync(
            new[] { new Message("user", "extract") },
            responseModel: typeof(StructuredResponse));

        Assert.True(response["ok"]?.GetValue<bool>());
        Assert.Equal(2, chatClient.Calls);
        Assert.Equal(2, chatClient.MessageSnapshots.Count);
        var retryMessages = chatClient.MessageSnapshots[1];
        Assert.Contains(
            "The previous response attempt was invalid.",
            retryMessages[^1].Text,
            StringComparison.Ordinal);
        Assert.Contains("Error type: JsonException.", retryMessages[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_UsesSharedStructuredResponseSchema()
    {
        var chatClient = new FakeChatClient("{\"ok\":true}");
        var graphitiClient = new MicrosoftExtensionsAIChatClient(chatClient);

        await graphitiClient.GenerateResponseAsync(
            new[] { new Message("user", "extract") },
            responseModel: typeof(StructuredResponse));

        var responseFormat = Assert.IsType<ChatResponseFormatJson>(chatClient.LastOptions?.ResponseFormat);
        Assert.Equal("StructuredResponse", responseFormat.SchemaName);
        Assert.Equal(
            StructuredResponseValidator.GetSchemaFingerprint(typeof(StructuredResponse)),
            Fingerprint(responseFormat.Schema!.Value));
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_UsesRuntimeStructuredResponseSchema()
    {
        var schema = RuntimeOkSchema();
        var chatClient = new FakeChatClient("{\"ok\":true}");
        var graphitiClient = new MicrosoftExtensionsAIChatClient(chatClient);

        await graphitiClient.GenerateResponseAsync(
            new[] { new Message("user", "extract") },
            responseSchema: schema);

        var responseFormat = Assert.IsType<ChatResponseFormatJson>(chatClient.LastOptions?.ResponseFormat);
        Assert.Equal(schema.Name, responseFormat.SchemaName);
        Assert.Equal(schema.Fingerprint, Fingerprint(responseFormat.Schema!.Value));
    }

    private static string Fingerprint(JsonElement element) =>
        Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(element.GetRawText())));

    private static StructuredResponseSchema RuntimeOkSchema() =>
        new(
            "RuntimeOk",
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray(JsonValue.Create("ok")),
                ["properties"] = new JsonObject
                {
                    ["ok"] = new JsonObject { ["type"] = "boolean" }
                }
            });

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_RejectsInvalidStructuredResponses()
    {
        var graphitiClient = new MicrosoftExtensionsAIChatClient(new FakeChatClient("{\"ok\":\"yes\"}"));

        await Assert.ThrowsAsync<JsonException>(() =>
            graphitiClient.GenerateResponseAsync(
                new[] { new Message("user", "extract") },
                responseModel: typeof(StructuredResponse)));
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_ExtractsJsonFromFencedResponse()
    {
        var graphitiClient = new MicrosoftExtensionsAIChatClient(
            new FakeChatClient("Sure - {not-json}\n```json\n{\"ok\":true}\n```\nDone."));

        var response = await graphitiClient.GenerateResponseAsync(
            new[] { new Message("user", "extract") },
            responseModel: typeof(StructuredResponse));

        Assert.True(response["ok"]?.GetValue<bool>());
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_ExtractsJsonWithBracesInsideString()
    {
        var graphitiClient = new MicrosoftExtensionsAIChatClient(
            new FakeChatClient("Intro {\"ok\":true,\"text\":\"keep { this } literal\"} trailing"));

        var response = await graphitiClient.GenerateResponseAsync(
            new[] { new Message("user", "extract") });

        Assert.True(response["ok"]?.GetValue<bool>());
        Assert.Equal("keep { this } literal", response["text"]?.GetValue<string>());
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_ExtractsJsonWithEscapesInsideString()
    {
        var graphitiClient = new MicrosoftExtensionsAIChatClient(
            new FakeChatClient("Intro {\"ok\":true,\"text\":\"quote: \\\"hi\\\", slash: \\\\, unicode: \\u007bnot structural\\u007d, braces: { keep }\"} trailing"));

        var response = await graphitiClient.GenerateResponseAsync(
            new[] { new Message("user", "extract") });

        Assert.True(response["ok"]?.GetValue<bool>());
        Assert.Equal(
            "quote: \"hi\", slash: \\, unicode: {not structural}, braces: { keep }",
            response["text"]?.GetValue<string>());
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_SkipsMalformedArrayCandidate()
    {
        var graphitiClient = new MicrosoftExtensionsAIChatClient(
            new FakeChatClient("prefix [not-json]\n```json\n{\"ok\":true}\n```"));

        var response = await graphitiClient.GenerateResponseAsync(
            new[] { new Message("user", "extract") },
            responseModel: typeof(StructuredResponse));

        Assert.True(response["ok"]?.GetValue<bool>());
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_SkipsManyInvalidJsonCandidates()
    {
        var noisyPrefix = string.Join(
            ' ',
            Enumerable.Range(0, 40).Select(index => $"{{invalid-{index}}}"));
        var graphitiClient = new MicrosoftExtensionsAIChatClient(
            new FakeChatClient($"{noisyPrefix} ```json\n{{\"ok\":true}}\n```"));

        var response = await graphitiClient.GenerateResponseAsync(
            new[] { new Message("user", "extract") },
            responseModel: typeof(StructuredResponse));

        Assert.True(response["ok"]?.GetValue<bool>());
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_WrapsNestedArrayExtractedFromText()
    {
        var graphitiClient = new MicrosoftExtensionsAIChatClient(
            new FakeChatClient("prefix [{\"nested\":{\"ok\":true}}] suffix"));

        var response = await graphitiClient.GenerateResponseAsync(new[] { new Message("user", "extract") });
        var value = Assert.IsType<JsonArray>(response["value"]);
        var nested = Assert.IsType<JsonObject>(value[0])["nested"];

        Assert.True(nested?["ok"]?.GetValue<bool>());
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_WrapsNonObjectJsonResponse()
    {
        var graphitiClient = new MicrosoftExtensionsAIChatClient(new FakeChatClient("```json\n[1,2,3]\n```"));

        var response = await graphitiClient.GenerateResponseAsync(new[] { new Message("user", "extract") });

        Assert.Equal(3, response["value"]?.AsArray().Count);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_WrapsScalarJsonResponse()
    {
        var graphitiClient = new MicrosoftExtensionsAIChatClient(new FakeChatClient("true"));

        var response = await graphitiClient.GenerateResponseAsync(new[] { new Message("user", "extract") });

        Assert.True(response["value"]?.GetValue<bool>());
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_UsesOptionalResiliencePipeline()
    {
        var chatClient = new FakeChatClient(failuresBeforeSuccess: 1);
        var graphitiClient = new MicrosoftExtensionsAIChatClient(
            chatClient,
            pipeline: RetryOnce<ChatResponse>());

        var response = await graphitiClient.GenerateResponseAsync(new[] { new Message("user", "extract") });

        Assert.True(response["ok"]?.GetValue<bool>());
        Assert.Equal(2, chatClient.Calls);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_RetryReacquiresRateLimitPermit()
    {
        var chatClient = new FakeChatClient(failuresBeforeSuccess: 1);
        using var rateLimiter = new CountingRateLimiter();
        var graphitiClient = new MicrosoftExtensionsAIChatClient(
            chatClient,
            pipeline: RetryOnce<ChatResponse>(),
            rateLimiter: rateLimiter);

        var response = await graphitiClient.GenerateResponseAsync(new[] { new Message("user", "extract") });

        Assert.True(response["ok"]?.GetValue<bool>());
        Assert.Equal(2, chatClient.Calls);
        Assert.Equal(2, rateLimiter.AcquireAsyncCalls);
        Assert.Equal(2, rateLimiter.LeasesDisposed);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_DoesNotRetryWithoutPipeline()
    {
        var chatClient = new FakeChatClient(failuresBeforeSuccess: 1);
        var graphitiClient = new MicrosoftExtensionsAIChatClient(chatClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            graphitiClient.GenerateResponseAsync(new[] { new Message("user", "extract") }));
        Assert.Equal(1, chatClient.Calls);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_UsesOptionalRateLimiter()
    {
        var chatClient = new ConcurrencyTrackingChatClient(TimeSpan.FromMilliseconds(25));
        using var rateLimiter = SingleConcurrencyLimiter(queueLimit: 8);
        var graphitiClient = new MicrosoftExtensionsAIChatClient(
            chatClient,
            rateLimiter: rateLimiter);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            graphitiClient.GenerateResponseAsync(new[] { new Message("user", "extract") })));

        Assert.Equal(4, chatClient.Calls);
        Assert.Equal(1, chatClient.MaxObservedConcurrency);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_RejectedRateLimitPermitSkipsProviderCall()
    {
        var chatClient = new FakeChatClient();
        using var rateLimiter = new RejectedRateLimiter();
        var graphitiClient = new MicrosoftExtensionsAIChatClient(
            chatClient,
            rateLimiter: rateLimiter);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            graphitiClient.GenerateResponseAsync(new[] { new Message("user", "extract") }));

        Assert.Equal("Could not acquire a Graphiti AI provider rate-limit permit.", exception.Message);
        Assert.Equal(0, chatClient.Calls);
        Assert.Equal(1, rateLimiter.AcquireAsyncCalls);
        Assert.Equal(1, rateLimiter.LeasesDisposed);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_ReleasesRateLimitPermitWhenResponseParsingFails()
    {
        var chatClient = new FakeChatClient("not-json");
        using var rateLimiter = new CountingRateLimiter();
        var graphitiClient = new MicrosoftExtensionsAIChatClient(
            chatClient,
            rateLimiter: rateLimiter);

        await Assert.ThrowsAsync<JsonException>(() =>
            graphitiClient.GenerateResponseAsync(new[] { new Message("user", "extract") }));

        Assert.Equal(3, chatClient.Calls);
        Assert.Equal(3, rateLimiter.AcquireAsyncCalls);
        Assert.Equal(3, rateLimiter.LeasesDisposed);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_CancelledQueuedRateLimitWaitSkipsProviderCall()
    {
        var chatClient = new BlockingChatClient();
        using var rateLimiter = SingleConcurrencyLimiter(queueLimit: 1);
        var graphitiClient = new MicrosoftExtensionsAIChatClient(
            chatClient,
            rateLimiter: rateLimiter);
        using var cancellation = new CancellationTokenSource();

        var first = graphitiClient.GenerateResponseAsync(new[] { new Message("user", "first") });
        await chatClient.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = graphitiClient.GenerateResponseAsync(
            new[] { new Message("user", "second") },
            cancellationToken: cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, chatClient.Calls);

        chatClient.Release.SetResult();
        var response = await first.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(response["ok"]?.GetValue<bool>());
        Assert.Equal(1, chatClient.Calls);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_UsesEmbeddingGenerator()
    {
        var embedder = new MicrosoftExtensionsAIEmbedderClient(new FakeEmbeddingGenerator(), embeddingDimension: 3);

        var vector = await embedder.CreateAsync("abc");
        var batch = await embedder.CreateBatchAsync(new[] { "abc", "de" });

        Assert.Equal(new[] { 3f, 6f, 9f }, vector);
        Assert.Equal(2, batch.Count);
        Assert.Equal(new[] { 2f, 4f, 6f }, batch[1]);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_RejectsEmbeddingCountMismatch()
    {
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            new FixedEmbeddingGenerator(new[] { 1f, 2f, 3f }),
            embeddingDimension: 3);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            embedder.CreateBatchAsync(new[] { "first", "second" }));

        Assert.Contains("returned 1 embedding(s) for 2 input(s)", exception.Message);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_RejectsEmbeddingDimensionMismatch()
    {
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            new FixedEmbeddingGenerator(new[] { 1f, 2f }),
            embeddingDimension: 3);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            embedder.CreateAsync("abc"));

        Assert.Contains("dimension 2", exception.Message);
        Assert.Contains("expected 3", exception.Message);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_RejectedRateLimitPermitSkipsProviderCall()
    {
        var generator = new FakeEmbeddingGenerator();
        using var rateLimiter = new RejectedRateLimiter();
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            generator,
            embeddingDimension: 3,
            rateLimiter: rateLimiter);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            embedder.CreateAsync("abc"));

        Assert.Equal("Could not acquire a Graphiti AI provider rate-limit permit.", exception.Message);
        Assert.Equal(0, generator.Calls);
        Assert.Equal(1, rateLimiter.AcquireAsyncCalls);
        Assert.Equal(1, rateLimiter.LeasesDisposed);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_ReleasesRateLimitPermitWhenValidationFails()
    {
        using var rateLimiter = new CountingRateLimiter();
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            new FixedEmbeddingGenerator(new[] { 1f, 2f }),
            embeddingDimension: 3,
            rateLimiter: rateLimiter);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            embedder.CreateAsync("abc"));

        Assert.Equal(1, rateLimiter.AcquireAsyncCalls);
        Assert.Equal(1, rateLimiter.LeasesDisposed);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_CancelledQueuedRateLimitWaitSkipsProviderCall()
    {
        var generator = new BlockingEmbeddingGenerator();
        using var rateLimiter = SingleConcurrencyLimiter(queueLimit: 1);
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            generator,
            embeddingDimension: 3,
            rateLimiter: rateLimiter);
        using var cancellation = new CancellationTokenSource();

        var first = embedder.CreateAsync("first");
        await generator.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = embedder.CreateAsync("second", cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, generator.Calls);

        generator.Release.SetResult();
        var vector = await first.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { 5f, 10f, 15f }, vector);
        Assert.Equal(1, generator.Calls);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_RejectsNonFiniteEmbeddings()
    {
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            new FixedEmbeddingGenerator(new[] { 1f, float.NaN, 3f }),
            embeddingDimension: 3);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            embedder.CreateAsync("abc"));

        Assert.Contains("non-finite value", exception.Message);
        Assert.Contains("dimension 1", exception.Message);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_CopiesProviderVectorsBeforeReturning()
    {
        var generator = new MutableEmbeddingGenerator();
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            generator,
            embeddingDimension: 3,
            batchSize: 2,
            batchConcurrency: 1);

        var vector = await embedder.CreateAsync("abc");
        generator.MutateLastVectors(999f);

        Assert.Equal(new[] { 3f, 6f, 9f }, vector);

        var batch = await embedder.CreateBatchAsync(new[] { "a", "bb" });
        generator.MutateLastVectors(777f);

        Assert.Equal(new[] { 1f, 2f, 3f }, batch[0]);
        Assert.Equal(new[] { 2f, 4f, 6f }, batch[1]);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_UsesOptionalResiliencePipeline()
    {
        var generator = new FakeEmbeddingGenerator(failuresBeforeSuccess: 1);
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            generator,
            embeddingDimension: 3,
            pipeline: RetryOnce<GeneratedEmbeddings<Embedding<float>>>());

        var vector = await embedder.CreateAsync("abc");

        Assert.Equal(new[] { 3f, 6f, 9f }, vector);
        Assert.Equal(2, generator.Calls);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_RetryReacquiresRateLimitPermit()
    {
        var generator = new FakeEmbeddingGenerator(failuresBeforeSuccess: 1);
        using var rateLimiter = new CountingRateLimiter();
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            generator,
            embeddingDimension: 3,
            pipeline: RetryOnce<GeneratedEmbeddings<Embedding<float>>>(),
            rateLimiter: rateLimiter);

        var vector = await embedder.CreateAsync("abc");

        Assert.Equal(new[] { 3f, 6f, 9f }, vector);
        Assert.Equal(2, generator.Calls);
        Assert.Equal(2, rateLimiter.AcquireAsyncCalls);
        Assert.Equal(2, rateLimiter.LeasesDisposed);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_UsesOptionalRateLimiter()
    {
        var generator = new ConcurrencyTrackingEmbeddingGenerator(TimeSpan.FromMilliseconds(25));
        using var rateLimiter = SingleConcurrencyLimiter(queueLimit: 8);
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            generator,
            embeddingDimension: 3,
            rateLimiter: rateLimiter);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            embedder.CreateAsync(index.ToString(CultureInfo.InvariantCulture))));

        Assert.Equal(4, generator.Calls);
        Assert.Equal(1, generator.MaxObservedConcurrency);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_ChunksBatchRequestsAndPreservesOrder()
    {
        var generator = new RecordingEmbeddingGenerator();
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            generator,
            embeddingDimension: 3,
            batchSize: 2,
            batchConcurrency: 1);
        var input = new[] { "a", "bb", "ccc", "dddd", "eeeee" };

        var batch = await embedder.CreateBatchAsync(input);

        Assert.Collection(
            generator.Batches,
            chunk => Assert.Equal(new[] { "a", "bb" }, chunk),
            chunk => Assert.Equal(new[] { "ccc", "dddd" }, chunk),
            chunk => Assert.Equal(new[] { "eeeee" }, chunk));
        Assert.Equal(input.Length, batch.Count);
        for (var i = 0; i < input.Length; i++)
        {
            Assert.Equal(CreateEmbeddingVector(input[i].Length, 3), batch[i]);
        }
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_BoundsConcurrentBatchChunks()
    {
        var generator = new ConcurrencyTrackingEmbeddingGenerator(TimeSpan.FromMilliseconds(25));
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            generator,
            embeddingDimension: 3,
            batchSize: 1,
            batchConcurrency: 2);

        await embedder.CreateBatchAsync(
            Enumerable.Range(0, 8)
                .Select(index => index.ToString(CultureInfo.InvariantCulture))
                .ToList());

        Assert.Equal(8, generator.Calls);
        Assert.Equal(2, generator.MaxObservedConcurrency);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_RateLimiterBoundsConcurrentBatchChunks()
    {
        var generator = new ConcurrencyTrackingEmbeddingGenerator(TimeSpan.FromMilliseconds(25));
        using var rateLimiter = SingleConcurrencyLimiter(queueLimit: 8);
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            generator,
            embeddingDimension: 3,
            rateLimiter: rateLimiter,
            batchSize: 1,
            batchConcurrency: 4);

        await embedder.CreateBatchAsync(
            Enumerable.Range(0, 8)
                .Select(index => index.ToString(CultureInfo.InvariantCulture))
                .ToList());

        Assert.Equal(8, generator.Calls);
        Assert.Equal(1, generator.MaxObservedConcurrency);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_CancelsPendingBatchChunks()
    {
        using var cts = new CancellationTokenSource();
        var generator = new CancelAfterFirstEmbeddingGenerator(cts);
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            generator,
            embeddingDimension: 3,
            batchSize: 1,
            batchConcurrency: 1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            embedder.CreateBatchAsync(new[] { "a", "bb", "ccc" }, cts.Token));

        Assert.Equal(1, generator.Calls);
    }

    [Fact]
    public async Task EmbedderClient_DefaultBatchUsesBoundedConcurrencyAndPreservesOrder()
    {
        var embedder = new DelayedEmbedder(batchConcurrency: 3);

        var batch = await embedder.CreateBatchAsync(
            Enumerable.Range(0, 10).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray());

        Assert.Equal(Enumerable.Range(0, 10), batch.Select(vector => (int)vector[0]));
        Assert.InRange(embedder.MaxObservedConcurrency, 2, 3);
    }

    [Fact]
    public async Task EmbedderClient_DefaultBatchSnapshotsMutableInputValues()
    {
        var embedder = new DelayedEmbedder(batchConcurrency: 1);
        var input = new[] { "1", "2", "3" };

        var batchTask = embedder.CreateBatchAsync(input);
        input[0] = "100";
        input[1] = "200";
        input[2] = "300";
        var batch = await batchTask;

        Assert.Equal(new[] { 1, 2, 3 }, batch.Select(vector => (int)vector[0]));
    }

    [Fact]
    public void EmbedderConfig_ValidatesDimensionsAndBatchSettings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmbedderConfig(embeddingDimension: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmbedderConfig(batchConcurrency: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EmbedderConfig(batchSize: 0));
    }

    [Fact]
    public async Task AddGraphitiCore_RegistersModernDefaultsAndAdapters()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient, FakeChatClient>();
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, FakeEmbeddingGenerator>();
        services.AddGraphitiCore(options => options.EmbeddingDimension = 3);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        Assert.IsType<InMemoryGraphDriver>(graphiti.Driver);
        Assert.IsType<MicrosoftExtensionsAIChatClient>(graphiti.LlmClient);
        Assert.IsType<MicrosoftExtensionsAIEmbedderClient>(graphiti.Embedder);
        Assert.IsType<DefaultContentChunker>(scope.ServiceProvider.GetRequiredService<IContentChunker>());
    }

    [Fact]
    public async Task AddGraphitiCore_PassesConfiguredDatabaseToInMemoryDriver()
    {
        var services = new ServiceCollection();
        services.AddGraphitiCore(options => options.Database = "tenant-db");

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var driver = Assert.IsType<InMemoryGraphDriver>(
            scope.ServiceProvider.GetRequiredService<IGraphDriver>());
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        Assert.Equal("tenant-db", driver.Database);
        Assert.Equal(string.Empty, driver.DefaultGroupId);
        Assert.Same(driver, graphiti.Driver);
    }

    [Fact]
    public async Task AddGraphitiCore_RegistersProviderResiliencePipelines()
    {
        var chatClient = new FakeChatClient(failuresBeforeSuccess: 1);
        var embeddingGenerator = new FakeEmbeddingGenerator(failuresBeforeSuccess: 1);
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(chatClient);
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embeddingGenerator);
        services.AddGraphitiCore(options => options.EmbeddingDimension = 3);
        services.Configure<GraphitiResilienceOptions>(options =>
        {
            options.MaxRetryAttempts = 1;
            options.RetryDelay = TimeSpan.Zero;
            options.UseJitter = false;
            options.AttemptTimeout = TimeSpan.Zero;
        });

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        var response = await graphiti.LlmClient.GenerateResponseAsync(new[] { new Message("user", "extract") });
        var vector = await graphiti.Embedder.CreateAsync("abc");

        Assert.True(response["ok"]?.GetValue<bool>());
        Assert.Equal(new[] { 3f, 6f, 9f }, vector);
        Assert.Equal(2, chatClient.Calls);
        Assert.Equal(2, embeddingGenerator.Calls);
    }

    [Fact]
    public async Task AddGraphitiCore_BindsResilienceOptionsFromConfiguration()
    {
        var chatClient = new FakeChatClient(failuresBeforeSuccess: 1);
        var configuration = new ConfigurationManager
        {
            ["Resilience:MaxRetryAttempts"] = "0",
            ["Resilience:RetryDelay"] = "00:00:00",
            ["Resilience:MaxRetryDelay"] = "00:00:00",
            ["Resilience:UseJitter"] = "false",
            ["Resilience:AttemptTimeout"] = "00:00:00",
            ["Resilience:ProviderConcurrencyLimit"] = "2",
            ["Resilience:ProviderQueueLimit"] = "5",
            ["Resilience:ProviderQueueProcessingOrder"] = "NewestFirst"
        };
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(chatClient);
        services.AddGraphitiCore(configuration, options => options.EmbeddingDimension = 3);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<GraphitiResilienceOptions>>().Value;
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            graphiti.LlmClient.GenerateResponseAsync(new[] { new Message("user", "extract") }));
        Assert.Equal(0, options.MaxRetryAttempts);
        Assert.Equal(TimeSpan.Zero, options.RetryDelay);
        Assert.Equal(TimeSpan.Zero, options.MaxRetryDelay);
        Assert.False(options.UseJitter);
        Assert.Equal(TimeSpan.Zero, options.AttemptTimeout);
        Assert.Equal(2, options.ProviderConcurrencyLimit);
        Assert.Equal(5, options.ProviderQueueLimit);
        Assert.Equal(QueueProcessingOrder.NewestFirst, options.ProviderQueueProcessingOrder);
        Assert.Equal(1, chatClient.Calls);
    }

    [Fact]
    public async Task AddGraphitiCore_AppliesConfiguredProviderTimeout()
    {
        var chatClient = new ConcurrencyTrackingChatClient(TimeSpan.FromSeconds(5));
        var configuration = new ConfigurationManager
        {
            ["Resilience:MaxRetryAttempts"] = "0",
            ["Resilience:AttemptTimeout"] = "00:00:00.020"
        };
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(chatClient);
        services.AddGraphitiCore(configuration, options => options.EmbeddingDimension = 3);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        await Assert.ThrowsAsync<TimeoutRejectedException>(() =>
            graphiti.LlmClient.GenerateResponseAsync(new[] { new Message("user", "extract") }));
        Assert.Equal(1, chatClient.Calls);
    }

    [Fact]
    public async Task AddGraphitiCore_BuildsConfiguredProviderRateLimiter()
    {
        var chatClient = new ConcurrencyTrackingChatClient(TimeSpan.FromMilliseconds(25));
        var configuration = new ConfigurationManager
        {
            ["Resilience:MaxRetryAttempts"] = "0",
            ["Resilience:AttemptTimeout"] = "00:00:00",
            ["Resilience:ProviderConcurrencyLimit"] = "1",
            ["Resilience:ProviderQueueLimit"] = "8"
        };
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(chatClient);
        services.AddGraphitiCore(configuration, options => options.EmbeddingDimension = 3);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            graphiti.LlmClient.GenerateResponseAsync(
                new[] { new Message("user", $"extract {index}") },
                promptName: index.ToString(CultureInfo.InvariantCulture))));

        Assert.Equal(4, chatClient.Calls);
        Assert.Equal(1, chatClient.MaxObservedConcurrency);
    }

    [Fact]
    public async Task AddGraphitiCore_AllowsScopedMicrosoftAiClientsWithValidateScopes()
    {
        var services = new ServiceCollection();
        services.AddScoped<IChatClient, FakeChatClient>();
        services.AddScoped<IEmbeddingGenerator<string, Embedding<float>>, FakeEmbeddingGenerator>();
        services.AddGraphitiCore(options => options.EmbeddingDimension = 3);

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();

        var first = firstScope.ServiceProvider.GetRequiredService<Graphiti>();
        var second = secondScope.ServiceProvider.GetRequiredService<Graphiti>();

        Assert.IsType<MicrosoftExtensionsAIChatClient>(first.LlmClient);
        Assert.IsType<MicrosoftExtensionsAIEmbedderClient>(first.Embedder);
        Assert.NotSame(first.LlmClient, second.LlmClient);
        Assert.NotSame(first.Embedder, second.Embedder);
    }

    [Fact]
    public async Task AddGraphitiCore_PassesRegisteredRateLimiterToAiAdapters()
    {
        var chatClient = new ConcurrencyTrackingChatClient(TimeSpan.FromMilliseconds(25));
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(chatClient);
        services.AddSingleton<RateLimiter>(_ => SingleConcurrencyLimiter(queueLimit: 8));
        services.AddGraphitiCore(options => options.EmbeddingDimension = 3);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            graphiti.LlmClient.GenerateResponseAsync(
                new[] { new Message("user", $"extract {index}") },
                promptName: index.ToString(CultureInfo.InvariantCulture))));

        Assert.Equal(4, chatClient.Calls);
        Assert.Equal(1, chatClient.MaxObservedConcurrency);
    }

    [Fact]
    public async Task Graphiti_DisposeAsync_DoesNotCloseExternalGraphDriver()
    {
        var driver = new TrackingGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);

        await graphiti.CloseAsync();
        await graphiti.DisposeAsync();

        Assert.Equal(0, driver.CloseCalls);
        await driver.DisposeAsync();
        Assert.Equal(1, driver.CloseCalls);
    }

    [Fact]
    public async Task AddGraphitiCore_DisposesScopedGraphDriverOnce()
    {
        var services = new ServiceCollection();
        services.AddGraphitiCore(options =>
        {
            options.GraphDriverFactory = _ => new TrackingGraphDriver();
        });

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });

        TrackingGraphDriver driver;
        await using (var scope = provider.CreateAsyncScope())
        {
            var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();
            driver = Assert.IsType<TrackingGraphDriver>(graphiti.Driver);

            await graphiti.CloseAsync();
            await graphiti.DisposeAsync();

            Assert.Equal(0, driver.CloseCalls);
        }

        Assert.Equal(1, driver.CloseCalls);
    }

    [Fact]
    public async Task AddGraphitiCore_UsesRegisteredTimeProvider()
    {
        var fixedNow = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(fixedNow));
        services.AddGraphitiCore();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice likes Bob",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        Assert.Equal(fixedNow.UtcDateTime, result.Episode.CreatedAt);
        Assert.All(result.Nodes, node => Assert.Equal(fixedNow.UtcDateTime, node.CreatedAt));
        Assert.All(result.Edges, edge => Assert.Equal(fixedNow.UtcDateTime, edge.CreatedAt));
        Assert.All(result.EpisodicEdges, edge => Assert.Equal(fixedNow.UtcDateTime, edge.CreatedAt));
    }

    [Fact]
    public async Task AddGraphitiCore_UsesRegisteredLogger()
    {
        var logger = new ListLogger<Graphiti>();
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<Graphiti>>(logger);
        services.AddGraphitiCore();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice likes Bob",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        Assert.Contains(logger.Entries, entry => entry.EventId == 1000);
        Assert.Contains(logger.Entries, entry => entry.EventId == 1001);
    }

    [Fact]
    public async Task AddGraphitiCore_BindsOptionsFromConfiguration()
    {
        var configuration = new ConfigurationManager
        {
            ["Provider"] = "InMemory",
            ["EmbeddingDimension"] = "3",
            ["MaxCoroutines"] = "2",
            ["StoreRawEpisodeContent"] = "false",
            ["Database"] = "tenant-a"
        };
        var services = new ServiceCollection();
        services.AddGraphitiCore(configuration);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<GraphitiOptions>>().Value;
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        Assert.Equal(GraphProvider.InMemory, options.Provider);
        Assert.Equal(3, options.EmbeddingDimension);
        Assert.Equal(2, options.MaxCoroutines);
        Assert.False(options.StoreRawEpisodeContent);
        Assert.Equal("tenant-a", options.Database);
        Assert.Equal(3, graphiti.Embedder.EmbeddingDimension);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice likes Bob",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");
        Assert.Empty(result.Episode.Content);
    }

    [Fact]
    public async Task AddGraphitiCore_PassesConfiguredDatabaseToNeo4jDriver()
    {
        var configuration = new ConfigurationManager
        {
            ["Provider"] = "Neo4j",
            ["Uri"] = "bolt://localhost:7687",
            ["User"] = "neo4j",
            ["Password"] = "password",
            ["Database"] = "tenant-db"
        };
        var services = new ServiceCollection();
        services.AddGraphitiCore(configuration);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var options = scope.ServiceProvider.GetRequiredService<IOptions<GraphitiOptions>>().Value;
        var driver = Assert.IsType<Neo4jGraphDriver>(scope.ServiceProvider.GetRequiredService<IGraphDriver>());
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        Assert.Equal("tenant-db", options.Database);
        Assert.Equal("tenant-db", driver.Database);
        Assert.Same(driver, graphiti.Driver);
    }

    [Fact]
    public async Task AddGraphitiCore_BindsLlmConfigFromConfiguration()
    {
        var configuration = new ConfigurationManager
        {
            ["Llm:Model"] = "gpt-main",
            ["Llm:SmallModel"] = "gpt-small",
            ["Llm:Temperature"] = "0.25",
            ["Llm:MaxTokens"] = "321"
        };
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient, FakeChatClient>();
        services.AddGraphitiCore(configuration, options => options.EmbeddingDimension = 3);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();
        var client = Assert.IsType<MicrosoftExtensionsAIChatClient>(graphiti.LlmClient);

        Assert.Equal("gpt-main", client.Config.Model);
        Assert.Equal("gpt-small", client.Config.SmallModel);
        Assert.Equal(0.25, client.Config.Temperature);
        Assert.Equal(321, client.Config.MaxTokens);
    }

    [Fact]
    public async Task AddGraphitiCore_DiChatClientUsesConfiguredLlmOptions()
    {
        var chatClient = new FakeChatClient();
        var configuration = new ConfigurationManager
        {
            ["Llm:Model"] = "gpt-main",
            ["Llm:SmallModel"] = "gpt-small",
            ["Llm:Temperature"] = "0.25",
            ["Llm:MaxTokens"] = "321"
        };
        var services = new ServiceCollection();
        services.AddSingleton<IChatClient>(chatClient);
        services.AddGraphitiCore(configuration, options => options.EmbeddingDimension = 3);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        await graphiti.LlmClient.GenerateResponseAsync(
            new[] { new Message("user", "extract") },
            modelSize: ModelSize.Small);

        Assert.NotNull(chatClient.LastOptions);
        Assert.Equal("gpt-small", chatClient.LastOptions.ModelId);
        Assert.Equal(0.25f, chatClient.LastOptions.Temperature);
        Assert.Equal(321, chatClient.LastOptions.MaxOutputTokens);
    }

    [Fact]
    public async Task AddGraphitiCore_BindsEmbeddingConfigFromConfiguration()
    {
        var configuration = new ConfigurationManager
        {
            ["Embedding:ModelId"] = "text-embedding-3-large",
            ["Embedding:EmbeddingDimension"] = "7",
            ["Embedding:BatchSize"] = "2",
            ["Embedding:BatchConcurrency"] = "3"
        };
        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, FakeEmbeddingGenerator>();
        services.AddGraphitiCore(configuration);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var embeddingConfig = scope.ServiceProvider.GetRequiredService<IOptions<EmbeddingConfig>>().Value;
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        Assert.Equal("text-embedding-3-large", embeddingConfig.ModelId);
        Assert.Equal(7, embeddingConfig.EmbeddingDimension);
        Assert.Equal(2, embeddingConfig.BatchSize);
        Assert.Equal(3, embeddingConfig.BatchConcurrency);
        Assert.Equal(7, graphiti.Embedder.EmbeddingDimension);
        var embedder = Assert.IsAssignableFrom<EmbedderClient>(graphiti.Embedder);
        Assert.Equal(2, embedder.Config.BatchSize);
        Assert.Equal(3, embedder.Config.BatchConcurrency);
    }

    [Fact]
    public async Task AddGraphitiCore_DiEmbedderUsesConfiguredEmbeddingModel()
    {
        var generator = new FakeEmbeddingGenerator();
        var configuration = new ConfigurationManager
        {
            ["Embedding:ModelId"] = "text-embedding-3-large",
            ["Embedding:EmbeddingDimension"] = "7"
        };
        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(generator);
        services.AddGraphitiCore(configuration);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        await graphiti.Embedder.CreateAsync("Alice knows Bob");

        Assert.NotNull(generator.LastOptions);
        Assert.Equal("text-embedding-3-large", generator.LastOptions.ModelId);
        Assert.Equal(7, generator.LastOptions.Dimensions);
    }

    [Fact]
    public void AddGraphitiCore_BindsContentChunkingOptionsFromConfiguration()
    {
        var configuration = new ConfigurationManager
        {
            ["ContentChunking:ChunkTokenSize"] = "4",
            ["ContentChunking:ChunkOverlapTokens"] = "1",
            ["ContentChunking:ChunkMinTokens"] = "2",
            ["ContentChunking:ChunkDensityThreshold"] = "0.01"
        };
        var services = new ServiceCollection();
        services.AddSingleton<ITokenCounter, WordTokenCounter>();
        services.AddGraphitiCore(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ContentChunkingOptions>>().Value;
        var chunker = provider.GetRequiredService<IContentChunker>();
        var chunks = chunker.ChunkTextContent("Alpha beta gamma. Delta epsilon zeta.");

        Assert.Equal(4, options.ChunkTokenSize);
        Assert.Equal(1, options.ChunkOverlapTokens);
        Assert.Equal(2, options.ChunkMinTokens);
        Assert.Equal(0.01, options.ChunkDensityThreshold);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunker.EstimateTokens(chunk) <= 4));
    }

    [Fact]
    public void DefaultContentChunker_UsesInjectedTokenCounterWithoutChangingStaticCounter()
    {
        var original = ContentChunking.TokenCounter;
        var chunker = new DefaultContentChunker(
            new WordTokenCounter(),
            Options.Create(new ContentChunkingOptions
            {
                ChunkTokenSize = 4,
                ChunkOverlapTokens = 1
            }));

        var chunks = chunker.ChunkTextContent("Alpha beta gamma. Delta epsilon zeta.");

        Assert.Same(original, ContentChunking.TokenCounter);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunker.EstimateTokens(chunk) <= 4));
    }

    [Fact]
    public async Task DefaultContentChunker_ConcurrentInstancesUseIndependentTokenCounters()
    {
        var original = ContentChunking.TokenCounter;
        var staticCounter = new ThrowingTokenCounter();
        try
        {
            ContentChunking.TokenCounter = staticCounter;
            var options = Options.Create(new ContentChunkingOptions
            {
                ChunkTokenSize = 4,
                ChunkOverlapTokens = 0
            });
            var wordChunker = new DefaultContentChunker(new WordTokenCounter(), options);
            var weightedChunker = new DefaultContentChunker(new WeightedWordTokenCounter(weight: 2), options);

            var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(index => Task.Run(() =>
            {
                var chunker = index % 2 == 0 ? wordChunker : weightedChunker;
                var expectedTokenCount = index % 2 == 0 ? 3 : 6;

                Assert.Equal(expectedTokenCount, chunker.EstimateTokens("Alpha beta gamma"));
                var chunks = chunker.ChunkTextContent("Alpha beta gamma. Delta epsilon zeta.");
                Assert.True(chunks.Count > 1);
                Assert.All(chunks, chunk => Assert.True(chunker.EstimateTokens(chunk) <= 4));
                return chunks.Count;
            })));

            Assert.All(results, count => Assert.True(count > 1));
            Assert.Same(staticCounter, ContentChunking.TokenCounter);
        }
        finally
        {
            ContentChunking.TokenCounter = original;
        }
    }

    [Fact]
    public async Task AddGraphitiCore_AllowsConfigurationOverrides()
    {
        var configuration = new ConfigurationManager
        {
            ["EmbeddingDimension"] = "3"
        };
        var services = new ServiceCollection();
        services.AddGraphitiCore(configuration, options => options.EmbeddingDimension = 5);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        Assert.Equal(5, graphiti.Embedder.EmbeddingDimension);
    }

    [Theory]
    [MemberData(nameof(EpisodeTypeWireValues))]
    public void EpisodeTypeExtensions_RoundTripPythonWireValues(
        EpisodeType episodeType,
        string wireValue)
    {
        Assert.True(EpisodeTypeExtensions.TryFromWireValue(wireValue, out var parsed));
        Assert.Equal(episodeType, parsed);
        Assert.Equal(episodeType, EpisodeTypeExtensions.FromWireValue(wireValue));
        Assert.Equal(wireValue, episodeType.ToWireValue());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unsupported")]
    [InlineData("MESSAGE")]
    public void EpisodeTypeExtensions_RejectInvalidWireValues(string? wireValue)
    {
        Assert.False(EpisodeTypeExtensions.TryFromWireValue(wireValue, out _));

        if (wireValue is null)
        {
            Assert.Throws<ArgumentNullException>(() => EpisodeTypeExtensions.FromWireValue(wireValue!));
        }
        else
        {
            Assert.Throws<ArgumentException>(() => EpisodeTypeExtensions.FromWireValue(wireValue));
        }
    }

    [Theory]
    [MemberData(nameof(EpisodeTypeWireValues))]
    public void GraphitiJsonSerializer_PreservesEpisodeTypeWireValues(
        EpisodeType episodeType,
        string wireValue)
    {
        var episode = new EpisodicNode
        {
            Source = episodeType
        };

        var json = JsonSerializer.Serialize(episode, GraphitiJsonSerializer.Options);
        var deserialized = JsonSerializer.Deserialize<EpisodicNode>(
            json,
            GraphitiJsonSerializer.Options);

        Assert.Contains($"\"source\":\"{wireValue}\"", json);
        Assert.Equal(episodeType, deserialized?.Source);
    }

    [Fact]
    public void GraphitiJsonSerializer_UsesPythonSnakeCaseForCoreModels()
    {
        var node = new EntityNode
        {
            Uuid = "node-1",
            Name = "Alice",
            GroupId = "tenant",
            CreatedAt = DateTime.UnixEpoch,
            Summary = "Person",
            NameEmbedding = new List<float> { 0.1f, 0.2f },
            Attributes = { ["customCamelCase"] = true }
        };

        var json = JsonSerializer.Serialize(node, GraphitiJsonSerializer.Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("tenant", root.GetProperty("group_id").GetString());
        Assert.Equal(2, root.GetProperty("name_embedding").GetArrayLength());
        Assert.Equal(DateTime.UnixEpoch, root.GetProperty("created_at").GetDateTime());
        Assert.False(root.TryGetProperty("groupId", out _));
        Assert.False(root.TryGetProperty("nameEmbedding", out _));
        Assert.True(root.GetProperty("attributes").GetProperty("customCamelCase").GetBoolean());
    }

    [Fact]
    public void GraphitiJsonSerializer_ReadsPythonSnakeCaseForCoreModels()
    {
        const string nodeJson = """
            {
              "uuid": "node-1",
              "name": "Alice",
              "group_id": "tenant",
              "created_at": "1970-01-01T00:00:00Z",
              "summary": "Person",
              "name_embedding": [0.1, 0.2]
            }
            """;
        const string edgeJson = """
            {
              "uuid": "edge-1",
              "group_id": "tenant",
              "source_node_uuid": "source",
              "target_node_uuid": "target",
              "created_at": "1970-01-01T00:00:00Z",
              "name": "KNOWS",
              "fact": "Alice knows Bob",
              "fact_embedding": [0.3],
              "valid_at": "2026-05-27T12:00:00Z",
              "invalid_at": null
            }
            """;

        var node = JsonSerializer.Deserialize<EntityNode>(nodeJson, GraphitiJsonSerializer.Options)!;
        var edge = JsonSerializer.Deserialize<EntityEdge>(edgeJson, GraphitiJsonSerializer.Options)!;

        Assert.Equal("tenant", node.GroupId);
        Assert.Equal(DateTime.UnixEpoch, node.CreatedAt);
        Assert.Equal(new List<float> { 0.1f, 0.2f }, node.NameEmbedding);
        Assert.Equal("tenant", edge.GroupId);
        Assert.Equal("source", edge.SourceNodeUuid);
        Assert.Equal("target", edge.TargetNodeUuid);
        Assert.Equal(new List<float> { 0.3f }, edge.FactEmbedding);
        Assert.Equal(new DateTime(2026, 5, 27, 12, 0, 0, DateTimeKind.Utc), edge.ValidAt);
        Assert.Null(edge.InvalidAt);
    }

    [Theory]
    [InlineData("""{"source":0}""")]
    [InlineData("""{"source":"unsupported"}""")]
    public void GraphitiJsonSerializer_RejectsInvalidEpisodeTypeWireValues(string json)
    {
        var exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<EpisodicNode>(
                json,
                GraphitiJsonSerializer.Options));

        Assert.Null(exception.InnerException);
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(eventId.Id, logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(int EventId, LogLevel Level, string Message);

    private sealed class WordTokenCounter : ITokenCounter
    {
        public int CountTokens(string? text) =>
            string.IsNullOrWhiteSpace(text)
                ? 0
                : text.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private sealed class WeightedWordTokenCounter(int weight) : ITokenCounter
    {
        public int CountTokens(string? text) =>
            string.IsNullOrWhiteSpace(text)
                ? 0
                : weight * text.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private sealed class ThrowingTokenCounter : ITokenCounter
    {
        public int CountTokens(string? text) =>
            throw new InvalidOperationException("Static token counter should not be used.");
    }

    private sealed class FakeChatClient : IChatClient
    {
        private readonly string _responseText;
        private int _failuresBeforeSuccess;

        public FakeChatClient(string responseText = "{\"ok\":true}", int failuresBeforeSuccess = 0)
        {
            _responseText = responseText;
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public int Calls { get; private set; }
        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastOptions = options;
            if (_failuresBeforeSuccess > 0)
            {
                _failuresBeforeSuccess--;
                throw new InvalidOperationException("transient chat failure");
            }

            var response = new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, _responseText))
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 1,
                    OutputTokenCount = 2
                }
            };

            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class SequencedChatClient : IChatClient
    {
        private readonly Queue<string> _responses;

        public SequencedChatClient(params string[] responses) =>
            _responses = new Queue<string>(responses);

        public int Calls { get; private set; }
        public List<IReadOnlyList<Microsoft.Extensions.AI.ChatMessage>> MessageSnapshots { get; } = new();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            MessageSnapshots.Add(messages
                .Select(message => new Microsoft.Extensions.AI.ChatMessage(message.Role, message.Text))
                .ToArray());
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No sequenced chat response is available.");
            }

            var response = new ChatResponse(
                new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, _responses.Dequeue()))
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 1,
                    OutputTokenCount = 2
                }
            };

            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ConcurrencyTrackingChatClient : IChatClient
    {
        private readonly TimeSpan _delay;
        private int _activeCalls;
        private int _calls;
        private int _maxObservedConcurrency;

        public ConcurrencyTrackingChatClient(TimeSpan delay) => _delay = delay;

        public int Calls => Volatile.Read(ref _calls);
        public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            var activeCalls = Interlocked.Increment(ref _activeCalls);
            RecordMaxObservedConcurrency(activeCalls, ref _maxObservedConcurrency);
            try
            {
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
                return new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, "{\"ok\":true}"));
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class BlockingChatClient : IChatClient
    {
        private int _calls;

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls => Volatile.Read(ref _calls);

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, "{\"ok\":true}"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class StructuredResponse
    {
        public bool Ok { get; set; }
    }

    private sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private int _failuresBeforeSuccess;

        public FakeEmbeddingGenerator(int failuresBeforeSuccess = 0)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public int Calls { get; private set; }
        public EmbeddingGenerationOptions? LastOptions { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastOptions = options;
            if (_failuresBeforeSuccess > 0)
            {
                _failuresBeforeSuccess--;
                throw new InvalidOperationException("transient embedding failure");
            }

            var dimensions = options?.Dimensions ?? 3;
            var embeddings = values.Select(value =>
            {
                var length = value.Length;
                return new Embedding<float>(CreateEmbeddingVector(length, dimensions));
            });

            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<IReadOnlyList<string>> Batches { get; } = new();

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = values.ToArray();
            Batches.Add(batch);
            var dimensions = options?.Dimensions ?? 3;
            var embeddings = batch.Select(value =>
                new Embedding<float>(CreateEmbeddingVector(value.Length, dimensions)));
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class CancelAfterFirstEmbeddingGenerator
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly CancellationTokenSource _cancellationTokenSource;
        private int _calls;

        public CancelAfterFirstEmbeddingGenerator(CancellationTokenSource cancellationTokenSource) =>
            _cancellationTokenSource = cancellationTokenSource;

        public int Calls => Volatile.Read(ref _calls);

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 1)
            {
                _cancellationTokenSource.Cancel();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var dimensions = options?.Dimensions ?? 3;
            var embeddings = values.Select(value =>
                new Embedding<float>(CreateEmbeddingVector(value.Length, dimensions)));
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class FixedEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly IReadOnlyList<Embedding<float>> _embeddings;

        public FixedEmbeddingGenerator(params float[][] vectors)
        {
            _embeddings = vectors
                .Select(vector => new Embedding<float>(vector))
                .ToList();
        }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(_embeddings));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class MutableEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<float[]> LastVectors { get; } = new();

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastVectors.Clear();
            var dimensions = options?.Dimensions ?? 3;
            var embeddings = new List<Embedding<float>>();
            foreach (var value in values)
            {
                var vector = CreateEmbeddingVector(value.Length, dimensions);
                LastVectors.Add(vector);
                embeddings.Add(new Embedding<float>(vector));
            }

            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public void MutateLastVectors(float value)
        {
            for (var i = 0; i < LastVectors.Count; i++)
            {
                LastVectors[i][0] = value;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class BlockingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private int _calls;

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls => Volatile.Read(ref _calls);

        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            var inputs = values.ToArray();
            var dimensions = options?.Dimensions ?? 3;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            var embeddings = inputs.Select(value =>
                new Embedding<float>(CreateEmbeddingVector(value.Length, dimensions)));
            return new GeneratedEmbeddings<Embedding<float>>(embeddings);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ConcurrencyTrackingEmbeddingGenerator
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly TimeSpan _delay;
        private int _activeCalls;
        private int _calls;
        private int _maxObservedConcurrency;

        public ConcurrencyTrackingEmbeddingGenerator(TimeSpan delay) => _delay = delay;

        public int Calls => Volatile.Read(ref _calls);
        public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);

        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            var activeCalls = Interlocked.Increment(ref _activeCalls);
            RecordMaxObservedConcurrency(activeCalls, ref _maxObservedConcurrency);
            try
            {
                await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
                var dimensions = options?.Dimensions ?? 3;
                return new GeneratedEmbeddings<Embedding<float>>(
                    values.Select(value =>
                        new Embedding<float>(CreateEmbeddingVector(value.Length, dimensions))));
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static float[] CreateEmbeddingVector(int length, int dimensions) =>
        Enumerable.Range(1, dimensions).Select(index => length * (float)index).ToArray();

    private static ConcurrencyLimiter SingleConcurrencyLimiter(int queueLimit) =>
        new(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });

    private sealed class CountingRateLimiter : RateLimiter
    {
        private int _acquireAsyncCalls;
        private int _leasesDisposed;

        public int AcquireAsyncCalls => Volatile.Read(ref _acquireAsyncCalls);
        public int LeasesDisposed => Volatile.Read(ref _leasesDisposed);
        public override TimeSpan? IdleDuration => null;

        public override RateLimiterStatistics? GetStatistics() => null;

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
        {
            Interlocked.Increment(ref _acquireAsyncCalls);
            return new CountingRateLimitLease(this);
        }

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(
            int permitCount,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _acquireAsyncCalls);
            return ValueTask.FromResult<RateLimitLease>(new CountingRateLimitLease(this));
        }

        private void RecordLeaseDisposed() => Interlocked.Increment(ref _leasesDisposed);

        private sealed class CountingRateLimitLease : RateLimitLease
        {
            private readonly CountingRateLimiter _owner;
            private int _disposed;

            public CountingRateLimitLease(CountingRateLimiter owner) => _owner = owner;

            public override bool IsAcquired => true;
            public override IEnumerable<string> MetadataNames => Array.Empty<string>();

            public override bool TryGetMetadata(string metadataName, out object? metadata)
            {
                metadata = null;
                return false;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _owner.RecordLeaseDisposed();
                }

                base.Dispose(disposing);
            }
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

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
        {
            Interlocked.Increment(ref _acquireAsyncCalls);
            return new RejectedRateLimitLease(this);
        }

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(
            int permitCount,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _acquireAsyncCalls);
            return ValueTask.FromResult<RateLimitLease>(new RejectedRateLimitLease(this));
        }

        private void RecordLeaseDisposed() => Interlocked.Increment(ref _leasesDisposed);

        private sealed class RejectedRateLimitLease : RateLimitLease
        {
            private readonly RejectedRateLimiter _owner;
            private int _disposed;

            public RejectedRateLimitLease(RejectedRateLimiter owner) => _owner = owner;

            public override bool IsAcquired => false;
            public override IEnumerable<string> MetadataNames => Array.Empty<string>();

            public override bool TryGetMetadata(string metadataName, out object? metadata)
            {
                metadata = null;
                return false;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _owner.RecordLeaseDisposed();
                }

                base.Dispose(disposing);
            }
        }
    }

    private static void RecordMaxObservedConcurrency(int activeCalls, ref int maximum)
    {
        var currentMaximum = Volatile.Read(ref maximum);
        while (activeCalls > currentMaximum)
        {
            var exchanged = Interlocked.CompareExchange(ref maximum, activeCalls, currentMaximum);
            if (exchanged == currentMaximum)
            {
                return;
            }

            currentMaximum = exchanged;
        }
    }

    private sealed class DelayedEmbedder : EmbedderClient
    {
        private int _activeCount;
        private int _maxObservedConcurrency;

        public DelayedEmbedder(int batchConcurrency)
            : base(new EmbedderConfig(embeddingDimension: 1, batchConcurrency: batchConcurrency))
        {
        }

        public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);

        public override async Task<IReadOnlyList<float>> CreateAsync(
            string input,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _activeCount);
            UpdateMax(ref _maxObservedConcurrency, active);
            try
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                return new[] { float.Parse(input, System.Globalization.CultureInfo.InvariantCulture) };
            }
            finally
            {
                Interlocked.Decrement(ref _activeCount);
            }
        }
    }

    private sealed class TrackingGraphDriver : GraphDriverBase
    {
        private int _closeCalls;

        public TrackingGraphDriver()
            : base(GraphProvider.InMemory)
        {
        }

        public int CloseCalls => Volatile.Read(ref _closeCalls);

        public override Task BuildIndicesAndConstraintsAsync(
            bool deleteExisting = false,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public override Task CloseAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _closeCalls);
            return Task.CompletedTask;
        }

        public override IGraphDriver Clone(string database) => throw new NotSupportedException();
        public override Task SaveNodeAsync(Node node, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task SaveEdgeAsync(Edge edge, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteNodeAsync(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteNodesByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteNodesByUuidsAsync(IEnumerable<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteEdgeAsync(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteEdgesByUuidsAsync(IEnumerable<string> uuids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task ClearDataAsync(IReadOnlyList<string>? groupIds = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<TNode> GetNodeByUuidAsync<TNode>(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<TNode>> GetNodesByUuidsAsync<TNode>(IEnumerable<string> uuids, string? groupId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<TNode>> GetNodesByGroupIdsAsync<TNode>(IEnumerable<string> groupIds, int? limit = null, string? uuidCursor = null, bool withEmbeddings = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<T> GetEdgeByUuidAsync<T>(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<T>> GetEdgesByUuidsAsync<T>(IEnumerable<string> uuids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<T>> GetEdgesByGroupIdsAsync<T>(IEnumerable<string> groupIds, int? limit = null, string? uuidCursor = null, bool withEmbeddings = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesBetweenNodesAsync(string sourceNodeUuid, string targetNodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesByNodeUuidAsync(string nodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EpisodicNode>> GetEpisodesByEntityNodeUuidAsync(string entityNodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(DateTime referenceTime, int lastN, IReadOnlyList<string>? groupIds = null, EpisodeType? source = null, string? saga = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EntityNode>> GetMentionedNodesAsync(IReadOnlyList<EpisodicNode> episodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<CommunityNode>> GetCommunitiesByNodesAsync(IReadOnlyList<EntityNode> nodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<SagaNode?> FindSagaByNameAsync(string name, string groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<string?> GetSagaPreviousEpisodeUuidAsync(string sagaUuid, string currentEpisodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<SagaEpisodeContent>> GetSagaEpisodeContentsAsync(string sagaUuid, DateTime? since = null, int limit = 200, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static ResiliencePipeline<T> RetryOnce<T>() =>
        new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                Delay = TimeSpan.Zero,
                MaxRetryAttempts = 1,
                ShouldHandle = new PredicateBuilder<T>().Handle<InvalidOperationException>()
            })
            .Build();

    private static void UpdateMax(ref int target, int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (value <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
