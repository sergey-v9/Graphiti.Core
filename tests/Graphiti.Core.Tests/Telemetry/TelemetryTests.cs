using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Graphiti.Core;
using Microsoft.Extensions.AI;
using Polly;
using Polly.Retry;

namespace Graphiti.Core.Tests.Telemetry;

public class TelemetryTests
{
    [Fact]
    public async Task LlmClient_EmitsActivityForGenerateResponse()
    {
        var client = new StaticJsonLlmClient(_ => new JsonObject { ["ok"] = true });

        var (activities, exception) = await CaptureActivitiesAsync(() =>
            client.GenerateResponseAsync(
                new[] { new Message("user", "extract") },
                maxTokens: 123,
                groupId: "group",
                promptName: "extract_nodes"));

        Assert.Null(exception);
        var activity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.Llm.GenerateResponse");

        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal("chat", GetTag(activity, "gen_ai.operation.name"));
        Assert.Equal("gpt-4.1-mini", GetTag(activity, "gen_ai.request.model"));
        Assert.Equal("extract_nodes", GetTag(activity, "graphiti.prompt_name"));
        Assert.Equal("group", GetTag(activity, "graphiti.group_id"));
        Assert.Equal(123, GetTag(activity, "graphiti.llm.max_tokens"));
        Assert.Equal(false, GetTag(activity, "graphiti.llm.cache_enabled"));
        Assert.Equal(1, GetTag(activity, "graphiti.llm.response_property_count"));
    }

    [Fact]
    public async Task EmbedderClient_EmitsActivityForHashEmbedding()
    {
        var embedder = new HashEmbedder(embeddingDimension: 8);

        var (activities, exception) = await CaptureActivitiesAsync(() =>
            embedder.CreateAsync("Alice knows Bob"));

        Assert.Null(exception);
        var activity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.Embedder.Create");

        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal("embeddings", GetTag(activity, "gen_ai.operation.name"));
        Assert.Equal("hash", GetTag(activity, "gen_ai.request.model"));
        Assert.Equal(8, GetTag(activity, "graphiti.embedding.dimension"));
        Assert.Equal(1, GetTag(activity, "graphiti.embedding.input_count"));
        Assert.Equal(1, GetTag(activity, "graphiti.embedding.output_count"));
    }

    [Fact]
    public async Task MicrosoftExtensionsAIChatClient_EmitsProviderCallActivitiesPerRetryAttempt()
    {
        var chatClient = new RetryingChatClient(failuresBeforeSuccess: 1);
        var client = new MicrosoftExtensionsAIChatClient(
            chatClient,
            new LlmConfig
            {
                Model = "chat-main",
                SmallModel = "chat-small"
            },
            pipeline: RetryOnce<ChatResponse>());

        var (activities, exception) = await CaptureActivitiesAsync(() => client.GenerateResponseAsync(
            new[]
            {
                new Message("system", "extract"),
                new Message("user", "Alice likes Bob")
            },
            maxTokens: 42,
            modelSize: ModelSize.Small,
            promptName: "extract_nodes"));

        Assert.Null(exception);
        Assert.Equal(2, chatClient.Calls);

        var providerCalls = activities
            .Where(activity => activity.OperationName == "Graphiti.Llm.ProviderCall")
            .ToList();
        Assert.Equal(2, providerCalls.Count);
        var failed = Assert.Single(providerCalls, activity => activity.Status == ActivityStatusCode.Error);
        AssertExceptionRecorded(failed, new InvalidOperationException("transient chat failure"));
        var succeeded = Assert.Single(providerCalls, activity => activity.Status == ActivityStatusCode.Ok);
        Assert.Equal("microsoft_extensions_ai", GetTag(succeeded, "graphiti.provider.abstraction"));
        Assert.Equal("chat", GetTag(succeeded, "gen_ai.operation.name"));
        Assert.Equal("chat-small", GetTag(succeeded, "gen_ai.request.model"));
        Assert.Equal("extract_nodes", GetTag(succeeded, "graphiti.prompt_name"));
        Assert.Equal("Small", GetTag(succeeded, "graphiti.llm.model_size"));
        Assert.Equal(42, GetTag(succeeded, "graphiti.llm.max_tokens"));
        Assert.Equal(2, GetTag(succeeded, "graphiti.llm.message_count"));
        Assert.Equal(false, GetTag(succeeded, "graphiti.provider.rate_limited"));
        Assert.Equal(11, GetTag(succeeded, "graphiti.llm.provider_response.length"));
        Assert.Equal(1L, GetTag(succeeded, "gen_ai.usage.input_tokens"));
        Assert.Equal(2L, GetTag(succeeded, "gen_ai.usage.output_tokens"));

        var parent = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.Llm.GenerateResponse");
        Assert.Equal(ActivityStatusCode.Ok, parent.Status);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_EmitsProviderCallActivitiesPerRetryAttempt()
    {
        var generator = new RetryingEmbeddingGenerator(failuresBeforeSuccess: 1);
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            generator,
            embeddingDimension: 3,
            modelId: "embedding-model",
            pipeline: RetryOnce<GeneratedEmbeddings<Embedding<float>>>());

        var (activities, exception) = await CaptureActivitiesAsync(async () =>
        {
            var vector = await embedder.CreateAsync("abc").ConfigureAwait(false);
            Assert.Equal(new[] { 3f, 6f, 9f }, vector);
        });

        Assert.Null(exception);
        Assert.Equal(2, generator.Calls);

        var providerCalls = activities
            .Where(activity => activity.OperationName == "Graphiti.Embedder.ProviderCall")
            .ToList();
        Assert.Equal(2, providerCalls.Count);
        var failed = Assert.Single(providerCalls, activity => activity.Status == ActivityStatusCode.Error);
        AssertExceptionRecorded(failed, new InvalidOperationException("transient embedding failure"));
        var succeeded = Assert.Single(providerCalls, activity => activity.Status == ActivityStatusCode.Ok);
        Assert.Equal("microsoft_extensions_ai", GetTag(succeeded, "graphiti.provider.abstraction"));
        Assert.Equal("embeddings", GetTag(succeeded, "gen_ai.operation.name"));
        Assert.Equal("embedding-model", GetTag(succeeded, "gen_ai.request.model"));
        Assert.Equal(3, GetTag(succeeded, "graphiti.embedding.dimension"));
        Assert.Equal(1, GetTag(succeeded, "graphiti.embedding.input_count"));
        Assert.Equal(0, GetTag(succeeded, "graphiti.embedding.batch_index"));
        Assert.Equal(0, GetTag(succeeded, "graphiti.embedding.batch_start_index"));
        Assert.Equal(1, GetTag(succeeded, "graphiti.embedding.batch_size"));
        Assert.Equal(1, GetTag(succeeded, "graphiti.embedding.output_count"));
        Assert.Equal(false, GetTag(succeeded, "graphiti.provider.rate_limited"));

        var parent = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.Embedder.Create");
        Assert.Equal(ActivityStatusCode.Ok, parent.Status);
    }

    [Fact]
    public async Task MicrosoftExtensionsAIEmbedderClient_EmitsProviderCallActivityForBatchChunks()
    {
        var generator = new RetryingEmbeddingGenerator();
        var embedder = new MicrosoftExtensionsAIEmbedderClient(
            generator,
            embeddingDimension: 3,
            modelId: "embedding-model",
            batchSize: 2,
            batchConcurrency: 1);

        var (activities, exception) = await CaptureActivitiesAsync(async () =>
        {
            var vectors = await embedder
                .CreateBatchAsync(new[] { "a", "bb", "ccc" })
                .ConfigureAwait(false);
            Assert.Equal(3, vectors.Count);
        });

        Assert.Null(exception);
        Assert.Equal(2, generator.Calls);

        var providerCalls = activities
            .Where(activity => activity.OperationName == "Graphiti.Embedder.ProviderCall")
            .OrderBy(activity => GetTag(activity, "graphiti.embedding.batch_index"))
            .ToList();
        Assert.Equal(2, providerCalls.Count);
        Assert.All(providerCalls, activity => Assert.Equal(ActivityStatusCode.Ok, activity.Status));

        Assert.Equal(0, GetTag(providerCalls[0], "graphiti.embedding.batch_index"));
        Assert.Equal(0, GetTag(providerCalls[0], "graphiti.embedding.batch_start_index"));
        Assert.Equal(2, GetTag(providerCalls[0], "graphiti.embedding.batch_size"));
        Assert.Equal(2, GetTag(providerCalls[0], "graphiti.embedding.input_count"));
        Assert.Equal(2, GetTag(providerCalls[0], "graphiti.embedding.output_count"));

        Assert.Equal(1, GetTag(providerCalls[1], "graphiti.embedding.batch_index"));
        Assert.Equal(2, GetTag(providerCalls[1], "graphiti.embedding.batch_start_index"));
        Assert.Equal(1, GetTag(providerCalls[1], "graphiti.embedding.batch_size"));
        Assert.Equal(1, GetTag(providerCalls[1], "graphiti.embedding.input_count"));
        Assert.Equal(1, GetTag(providerCalls[1], "graphiti.embedding.output_count"));
    }

    [Fact]
    public async Task Graphiti_EmitsActivitiesForIngestionAndSearch()
    {
        var (activities, exception) = await CaptureActivitiesAsync(async () =>
        {
            var graphiti = new Graphiti(
                graphDriver: new InMemoryGraphDriver(),
                llmClient: new StaticJsonLlmClient(messages =>
                {
                    var prompt = messages.Count == 0 ? string.Empty : messages[^1].Content;
                    if (prompt.Contains("<ENTITIES>", StringComparison.Ordinal))
                    {
                        return new JsonObject
                        {
                            ["edges"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["source"] = "Alice",
                                    ["target"] = "Bob",
                                    ["relation_type"] = "LIKES",
                                    ["fact"] = "Alice likes Bob",
                                    ["valid_at"] = "2026-01-01T00:00:00Z"
                                }
                            }
                        };
                    }

                    if (prompt.Contains("<CURRENT MESSAGE>", StringComparison.Ordinal))
                    {
                        return new JsonObject
                        {
                            ["extracted_entities"] = new JsonArray
                            {
                                new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                                new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                            }
                        };
                    }

                    return new JsonObject();
                }));
            await graphiti.AddEpisodeAsync(
                "conversation",
                "Alice likes Bob",
                "message",
                new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                groupId: "group");
            await graphiti.SearchAsync("Alice Bob", groupIds: new[] { "group" });
        });

        Assert.Null(exception);

        var addEpisode = Assert.Single(activities, activity => activity.OperationName == "Graphiti.AddEpisode");
        Assert.Equal("group", GetTag(addEpisode, "graphiti.group_id"));
        Assert.Equal(2, GetTag(addEpisode, "graphiti.result.nodes"));
        Assert.Equal(1, GetTag(addEpisode, "graphiti.result.edges"));

        var graphExtraction = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.Extraction.EpisodeGraph");
        Assert.Equal(ActivityStatusCode.Ok, graphExtraction.Status);
        Assert.Equal("group", GetTag(graphExtraction, "graphiti.group_id"));
        Assert.Equal("Message", GetTag(graphExtraction, "graphiti.episode.source"));
        Assert.Equal(0, GetTag(graphExtraction, "graphiti.previous_episodes.count"));
        Assert.Equal(2, GetTag(graphExtraction, "graphiti.result.nodes"));
        Assert.Equal(1, GetTag(graphExtraction, "graphiti.result.edges"));
        Assert.Equal(2, GetTag(graphExtraction, "graphiti.result.attributions"));

        var nodeExtraction = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.Extraction.Nodes");
        Assert.Equal(ActivityStatusCode.Ok, nodeExtraction.Status);
        Assert.Equal(false, GetTag(nodeExtraction, "graphiti.extraction.fallback"));
        Assert.Equal(2, GetTag(nodeExtraction, "graphiti.extraction.candidates"));
        Assert.Equal(2, GetTag(nodeExtraction, "graphiti.result.nodes"));

        var edgeExtraction = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.Extraction.Edges");
        Assert.Equal(ActivityStatusCode.Ok, edgeExtraction.Status);
        Assert.Equal(2, GetTag(edgeExtraction, "graphiti.input.nodes"));
        Assert.Equal(false, GetTag(edgeExtraction, "graphiti.extraction.fallback"));
        Assert.Equal(1, GetTag(edgeExtraction, "graphiti.result.edges"));

        var nodeResolution = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.Resolution.Nodes");
        Assert.Equal(ActivityStatusCode.Ok, nodeResolution.Status);
        Assert.Equal(2, GetTag(nodeResolution, "graphiti.input.nodes"));
        Assert.Equal(0, GetTag(nodeResolution, "graphiti.existing.nodes"));
        Assert.Equal(2, GetTag(nodeResolution, "graphiti.result.nodes"));

        var edgeResolution = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.Resolution.Edges");
        Assert.Equal(ActivityStatusCode.Ok, edgeResolution.Status);
        Assert.Equal(1, GetTag(edgeResolution, "graphiti.input.edges"));
        Assert.Equal(2, GetTag(edgeResolution, "graphiti.input.nodes"));
        Assert.Equal(1, GetTag(edgeResolution, "graphiti.result.edges"));
        Assert.Equal(1, GetTag(edgeResolution, "graphiti.result.created_edges"));

        var edgeAttributes = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.Extraction.EdgeAttributes");
        Assert.Equal(ActivityStatusCode.Ok, edgeAttributes.Status);
        Assert.Equal(true, GetTag(edgeAttributes, "graphiti.extraction.skipped"));
        Assert.Equal(0, GetTag(edgeAttributes, "graphiti.extraction.targets"));

        var nodeAttributes = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.Extraction.NodeAttributes");
        Assert.Equal(ActivityStatusCode.Ok, nodeAttributes.Status);
        Assert.Equal(true, GetTag(nodeAttributes, "graphiti.extraction.skipped"));
        Assert.Equal(0, GetTag(nodeAttributes, "graphiti.extraction.targets"));

        var graphWrite = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.GraphWrite.SaveBulk");
        Assert.Equal(ActivityStatusCode.Ok, graphWrite.Status);
        Assert.Equal("group", GetTag(graphWrite, "graphiti.group_id"));
        Assert.Equal("InMemory", GetTag(graphWrite, "graphiti.graph.provider"));
        Assert.Equal("add_episode.graph", GetTag(graphWrite, "graphiti.write.phase"));
        Assert.Equal(1, GetTag(graphWrite, "graphiti.write.episodic_nodes"));
        Assert.Equal(2, GetTag(graphWrite, "graphiti.write.entity_nodes"));
        Assert.Equal(3, GetTag(graphWrite, "graphiti.write.total_nodes"));
        Assert.Equal(2, GetTag(graphWrite, "graphiti.write.episodic_edges"));
        Assert.Equal(1, GetTag(graphWrite, "graphiti.write.entity_edges"));
        Assert.Equal(3, GetTag(graphWrite, "graphiti.write.total_edges"));

        Assert.Contains(activities, activity => activity.OperationName == "Graphiti.SearchEdges");
        Assert.Contains(activities, activity => activity.OperationName == "Graphiti.SearchEngine.Search");
        var edgeSearch = Assert.Single(
            activities.Where(activity => activity.OperationName == "Graphiti.SearchEngine.EdgeSearch"),
            activity => Equals(GetTag(activity, "graphiti.result.count"), 1));
        Assert.Equal(ActivityStatusCode.Ok, edgeSearch.Status);
        Assert.Equal("edge", GetTag(edgeSearch, "graphiti.search.scope"));
        Assert.Equal("bm25,cosine_similarity", GetTag(edgeSearch, "graphiti.search.methods"));
        Assert.Equal("reciprocal_rank_fusion", GetTag(edgeSearch, "graphiti.search.reranker"));
        Assert.Equal(1, GetTag(edgeSearch, "graphiti.result.count"));
    }

    [Fact]
    public async Task SearchResultComposer_EmitsActivityForCrossEncoderReranking()
    {
        var ranked = new[]
        {
            (Item: new EntityNode { Uuid = "alice", Name = "Alice" }, Score: 1f),
            (Item: new EntityNode { Uuid = "bob", Name = "Bob" }, Score: 0.5f)
        };

        var (activities, exception) = await CaptureActivitiesAsync(() =>
            SearchResultComposer.ApplyCrossEncoderRerankerAsync(
                new IdentityCrossEncoderClient(),
                "Alice",
                ranked,
                node => node.Name,
                minScore: 0,
                CancellationToken.None));

        Assert.Null(exception);
        var activity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.SearchEngine.Rerank.CrossEncoder");
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal("cross_encoder", GetTag(activity, "graphiti.search.reranker"));
        Assert.Equal(2, GetTag(activity, "graphiti.candidate.count"));
        Assert.Equal(2, GetTag(activity, "graphiti.result.count"));
    }

    [Fact]
    public async Task SearchRetrievalRunner_EmitsActivitiesForDriverRetrieval()
    {
        var driver = new RecordingSearchDriver();
        var groupIds = new[] { "group" };
        var searchFilter = new SearchFilters();

        var (activities, exception) = await CaptureActivitiesAsync(async () =>
        {
            await SearchRetrievalRunner.GetNodeFulltextRankedAsync(
                driver,
                "Alice",
                groupIds,
                searchFilter,
                limit: 3,
                CancellationToken.None);
            await SearchRetrievalRunner.GetEdgeVectorRankedAsync(
                driver,
                new[] { 1f, 0f },
                groupIds,
                searchFilter,
                limit: 4,
                minScore: 0.7f,
                CancellationToken.None);
            await SearchRetrievalRunner.GetCommunityVectorRankedAsync(
                driver,
                new[] { 1f, 0f, 0f },
                groupIds,
                limit: 5,
                minScore: 0.8f,
                CancellationToken.None);
            await SearchRetrievalRunner.NodeBfsSearchAsync(
                driver,
                new[] { "origin" },
                maxDepth: 2,
                groupIds,
                searchFilter,
                limit: 6,
                CancellationToken.None);
        });

        Assert.Null(exception);
        Assert.Equal(1, driver.NodeFulltextCalls);
        Assert.Equal(1, driver.EdgeVectorCalls);
        Assert.Equal(1, driver.CommunityVectorCalls);
        Assert.Equal(1, driver.NodeBfsCalls);

        var nodeFulltext = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.SearchEngine.Retrieve.NodeFulltext");
        Assert.Equal(ActivityStatusCode.Ok, nodeFulltext.Status);
        Assert.Equal("node", GetTag(nodeFulltext, "graphiti.search.scope"));
        Assert.Equal("bm25", GetTag(nodeFulltext, "graphiti.search.method"));
        Assert.Equal(3, GetTag(nodeFulltext, "graphiti.limit"));
        Assert.Equal(5, GetTag(nodeFulltext, "graphiti.query.length"));
        Assert.Equal("group", GetTag(nodeFulltext, "graphiti.group_ids"));
        Assert.Equal(1, GetTag(nodeFulltext, "graphiti.result.count"));

        var edgeVector = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.SearchEngine.Retrieve.EdgeVector");
        Assert.Equal("edge", GetTag(edgeVector, "graphiti.search.scope"));
        Assert.Equal("cosine_similarity", GetTag(edgeVector, "graphiti.search.method"));
        Assert.Equal(4, GetTag(edgeVector, "graphiti.limit"));
        Assert.Equal(0.7f, GetTag(edgeVector, "graphiti.search.min_score"));
        Assert.Equal(2, GetTag(edgeVector, "graphiti.query_vector.dimension"));
        Assert.Equal(1, GetTag(edgeVector, "graphiti.result.count"));

        var communityVector = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.SearchEngine.Retrieve.CommunityVector");
        Assert.Equal("community", GetTag(communityVector, "graphiti.search.scope"));
        Assert.Equal(3, GetTag(communityVector, "graphiti.query_vector.dimension"));
        Assert.Equal(1, GetTag(communityVector, "graphiti.result.count"));

        var nodeBfs = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.SearchEngine.Retrieve.NodeBfs");
        Assert.Equal("breadth_first_search", GetTag(nodeBfs, "graphiti.search.method"));
        Assert.Equal(1, GetTag(nodeBfs, "graphiti.search.origin_count"));
        Assert.Equal(2, GetTag(nodeBfs, "graphiti.search.bfs_max_depth"));
        Assert.Equal(1, GetTag(nodeBfs, "graphiti.result.count"));
    }

    [Fact]
    public async Task Graphiti_EmitsGraphWriteActivityForAddTriplet()
    {
        var graphiti = new Graphiti(graphDriver: new InMemoryGraphDriver());

        var (activities, exception) = await CaptureActivitiesAsync(() => graphiti.AddTripletAsync(
            new EntityNode { Name = "Alice", GroupId = "group" },
            new EntityEdge
            {
                Name = "KNOWS",
                Fact = "Alice knows Bob.",
                GroupId = "group",
                ValidAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
            },
            new EntityNode { Name = "Bob", GroupId = "group" }));

        Assert.Null(exception);
        var graphWrite = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.GraphWrite.SaveBulk");
        Assert.Equal(ActivityStatusCode.Ok, graphWrite.Status);
        Assert.Equal("group", GetTag(graphWrite, "graphiti.group_id"));
        Assert.Equal("add_triplet.graph", GetTag(graphWrite, "graphiti.write.phase"));
        Assert.Equal(0, GetTag(graphWrite, "graphiti.write.episodic_nodes"));
        Assert.Equal(2, GetTag(graphWrite, "graphiti.write.entity_nodes"));
        Assert.Equal(2, GetTag(graphWrite, "graphiti.write.total_nodes"));
        Assert.Equal(0, GetTag(graphWrite, "graphiti.write.episodic_edges"));
        Assert.Equal(1, GetTag(graphWrite, "graphiti.write.entity_edges"));
        Assert.Equal(1, GetTag(graphWrite, "graphiti.write.total_edges"));
    }

    [Fact]
    public async Task SearchRetrievalRunner_RecordsRetrievalFailures()
    {
        var driver = new RecordingSearchDriver
        {
            NodeFulltextException = new InvalidOperationException("node fulltext failed")
        };

        var (activities, exception) = await CaptureActivitiesAsync(() =>
            SearchRetrievalRunner.GetNodeFulltextRankedAsync(
                driver,
                "Alice",
                new[] { "group" },
                new SearchFilters(),
                limit: 3,
                CancellationToken.None));

        Assert.IsType<InvalidOperationException>(exception);
        var activity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.SearchEngine.Retrieve.NodeFulltext");
        AssertExceptionRecorded(activity, exception!);
    }

    [Fact]
    public async Task Graphiti_RecordsExtractionFailureOnActivities()
    {
        var graphiti = new Graphiti(
            graphDriver: new InMemoryGraphDriver(),
            llmClient: new ThrowingNodeExtractionLlmClient());

        var (activities, exception) = await CaptureActivitiesAsync(() => graphiti.AddEpisodeAsync(
            "conversation",
            "Alice likes Bob",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group"));

        Assert.IsType<InvalidOperationException>(exception);
        var nodeExtraction = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.Extraction.Nodes");
        AssertExceptionRecorded(nodeExtraction, exception!);
        var graphExtraction = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.Extraction.EpisodeGraph");
        AssertExceptionRecorded(graphExtraction, exception!);
        var addEpisode = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.AddEpisode");
        AssertExceptionRecorded(addEpisode, exception!);
    }

    [Fact]
    public async Task Graphiti_RecordsBuildIndexFailureOnActivity()
    {
        var graphiti = new Graphiti(graphDriver: new ThrowingBuildDriver());

        var (activities, exception) = await CaptureActivitiesAsync(
            () => graphiti.BuildIndicesAndConstraintsAsync());

        Assert.IsType<InvalidOperationException>(exception);
        var activity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.BuildIndicesAndConstraints");
        AssertExceptionRecorded(activity, exception!);
    }

    [Fact]
    public async Task Graphiti_RecordsSearchFailureOnActivity()
    {
        var graphiti = new Graphiti(
            graphDriver: new InMemoryGraphDriver(),
            embedder: new ThrowingEmbedder());

        var (activities, exception) = await CaptureActivitiesAsync(
            () => graphiti.SearchAsync("Alice Bob", groupIds: new[] { "group" }));

        Assert.IsType<InvalidOperationException>(exception);
        var activity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.SearchEdges");
        AssertExceptionRecorded(activity, exception!);
        var searchEngineActivity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.SearchEngine.Search");
        AssertExceptionRecorded(searchEngineActivity, exception!);
    }

    [Fact]
    public async Task Graphiti_RecordsAddEpisodeFailureOnActivity()
    {
        var graphiti = new Graphiti(graphDriver: new InMemoryGraphDriver());

        var (activities, exception) = await CaptureActivitiesAsync(() => graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new("Person")
            },
            excludedEntityTypes: new[] { "Location" }));

        Assert.IsType<ArgumentException>(exception);
        var activity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.AddEpisode");
        AssertExceptionRecorded(activity, exception!);
    }

    [Fact]
    public async Task Graphiti_RecordsGraphWriteFailureOnActivity()
    {
        var graphiti = new Graphiti(graphDriver: new ThrowingSaveBulkDriver());

        var (activities, exception) = await CaptureActivitiesAsync(() => graphiti.AddTripletAsync(
            new EntityNode { Name = "Alice", GroupId = "group" },
            new EntityEdge
            {
                Name = "KNOWS",
                Fact = "Alice knows Bob.",
                GroupId = "group",
                ValidAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
            },
            new EntityNode { Name = "Bob", GroupId = "group" }));

        Assert.IsType<InvalidOperationException>(exception);
        var graphWrite = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.GraphWrite.SaveBulk");
        Assert.Equal("add_triplet.graph", GetTag(graphWrite, "graphiti.write.phase"));
        Assert.Equal(2, GetTag(graphWrite, "graphiti.write.entity_nodes"));
        Assert.Equal(1, GetTag(graphWrite, "graphiti.write.entity_edges"));
        AssertExceptionRecorded(graphWrite, exception!);
        var addTriplet = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.AddTriplet");
        AssertExceptionRecorded(addTriplet, exception!);
    }

    [Fact]
    public async Task Graphiti_RecordsAddTripletFailureOnActivity()
    {
        var graphiti = new Graphiti(
            graphDriver: new InMemoryGraphDriver(),
            embedder: new ThrowingEmbedder());

        var (activities, exception) = await CaptureActivitiesAsync(() => graphiti.AddTripletAsync(
            new EntityNode { Name = "Alice", GroupId = "group" },
            new EntityEdge { Name = "KNOWS", Fact = "Alice knows Bob.", GroupId = "group" },
            new EntityNode { Name = "Bob", GroupId = "group" }));

        Assert.IsType<InvalidOperationException>(exception);
        var activity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.AddTriplet");
        AssertExceptionRecorded(activity, exception!);
    }

    private static async Task<(IReadOnlyList<Activity> Activities, Exception? Exception)> CaptureActivitiesAsync(
        Func<Task> action)
    {
        var activities = new List<Activity>();
        var traceId = default(ActivityTraceId);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == GraphitiTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId)
                {
                    lock (activities)
                    {
                        activities.Add(activity);
                    }
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        Exception? exception = null;
        using (var rootActivity = new Activity("Graphiti.TelemetryTest").Start())
        {
            traceId = rootActivity.TraceId;
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        }

        return (activities, exception);
    }

    private static void AssertExceptionRecorded(Activity activity, Exception exception)
    {
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(exception.Message, activity.StatusDescription);
        var exceptionEvent = Assert.Single(activity.Events, activityEvent => activityEvent.Name == "exception");
        Assert.Equal(
            exception.GetType().FullName,
            exceptionEvent.Tags.FirstOrDefault(tag => tag.Key == "exception.type").Value);
        Assert.Equal(
            exception.Message,
            exceptionEvent.Tags.FirstOrDefault(tag => tag.Key == "exception.message").Value);
        Assert.False(string.IsNullOrWhiteSpace(
            exceptionEvent.Tags.FirstOrDefault(tag => tag.Key == "exception.stacktrace").Value?.ToString()));
    }

    private static object? GetTag(Activity activity, string key) =>
        activity.TagObjects.FirstOrDefault(tag => tag.Key == key).Value;

    private static ResiliencePipeline<T> RetryOnce<T>() =>
        new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                Delay = TimeSpan.Zero,
                MaxRetryAttempts = 1,
                ShouldHandle = new PredicateBuilder<T>().Handle<InvalidOperationException>()
            })
            .Build();

    private sealed class ThrowingEmbedder : EmbedderClient
    {
        public override Task<IReadOnlyList<float>> CreateAsync(
            string input,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("embedding failed");
    }

    private sealed class RetryingChatClient : IChatClient
    {
        private int _failuresBeforeSuccess;
        private int _calls;

        public RetryingChatClient(int failuresBeforeSuccess = 0) =>
            _failuresBeforeSuccess = failuresBeforeSuccess;

        public int Calls => Volatile.Read(ref _calls);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _calls);
            if (_failuresBeforeSuccess > 0)
            {
                _failuresBeforeSuccess--;
                throw new InvalidOperationException("transient chat failure");
            }

            return Task.FromResult(new ChatResponse(
                new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, "{\"ok\":true}"))
            {
                Usage = new UsageDetails
                {
                    InputTokenCount = 1,
                    OutputTokenCount = 2
                }
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _ = messages;
            _ = options;
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RetryingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private int _failuresBeforeSuccess;
        private int _calls;

        public RetryingEmbeddingGenerator(int failuresBeforeSuccess = 0) =>
            _failuresBeforeSuccess = failuresBeforeSuccess;

        public int Calls => Volatile.Read(ref _calls);

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputs = values.ToArray();
            Interlocked.Increment(ref _calls);
            if (_failuresBeforeSuccess > 0)
            {
                _failuresBeforeSuccess--;
                throw new InvalidOperationException("transient embedding failure");
            }

            var dimensions = options?.Dimensions ?? 3;
            var embeddings = inputs.Select(input =>
                new Embedding<float>(
                    Enumerable.Range(1, dimensions)
                        .Select(index => input.Length * (float)index)
                        .ToArray()));
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSearchDriver : ISearchGraphDriver
    {
        public int NodeFulltextCalls { get; private set; }
        public int EdgeVectorCalls { get; private set; }
        public int CommunityVectorCalls { get; private set; }
        public int NodeBfsCalls { get; private set; }
        public Exception? NodeFulltextException { get; set; }

        public Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesFulltextAsync(
            string query,
            SearchFilters searchFilter,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default)
        {
            NodeFulltextCalls++;
            if (NodeFulltextException is not null)
            {
                throw NodeFulltextException;
            }

            return Task.FromResult<IReadOnlyList<SearchHit<EntityNode>>>(
                new[] { new SearchHit<EntityNode>(new EntityNode { Uuid = "node", Name = "Alice" }, 1f) });
        }

        public Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesByEmbeddingAsync(
            IReadOnlyList<float> searchVector,
            SearchFilters searchFilter,
            IReadOnlyList<string>? groupIds,
            int limit,
            float minScore,
            string? sourceNodeUuid = null,
            string? targetNodeUuid = null,
            CancellationToken cancellationToken = default)
        {
            EdgeVectorCalls++;
            return Task.FromResult<IReadOnlyList<SearchHit<EntityEdge>>>(
                new[] { new SearchHit<EntityEdge>(new EntityEdge { Uuid = "edge", Fact = "Alice knows Bob" }, 0.9f) });
        }

        public Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesByEmbeddingAsync(
            IReadOnlyList<float> searchVector,
            IReadOnlyList<string>? groupIds,
            int limit,
            float minScore,
            CancellationToken cancellationToken = default)
        {
            CommunityVectorCalls++;
            return Task.FromResult<IReadOnlyList<SearchHit<CommunityNode>>>(
                new[] { new SearchHit<CommunityNode>(new CommunityNode { Uuid = "community", Name = "People" }, 0.8f) });
        }

        public Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesBfsAsync(
            IReadOnlyList<string>? originNodeUuids,
            SearchFilters searchFilter,
            int maxDepth,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default)
        {
            NodeBfsCalls++;
            return Task.FromResult<IReadOnlyList<SearchHit<EntityNode>>>(
                new[] { new SearchHit<EntityNode>(new EntityNode { Uuid = "neighbor", Name = "Bob" }, 1f) });
        }

        public Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesByEmbeddingAsync(
            IReadOnlyList<float> searchVector,
            SearchFilters searchFilter,
            IReadOnlyList<string>? groupIds,
            int limit,
            float minScore,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesFulltextAsync(
            string query,
            SearchFilters searchFilter,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesBfsAsync(
            IReadOnlyList<string>? originNodeUuids,
            SearchFilters searchFilter,
            int maxDepth,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SearchHit<EpisodicNode>>> SearchEpisodesFulltextAsync(
            string query,
            SearchFilters searchFilter,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesFulltextAsync(
            string query,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SearchRank>> RankNodeDistanceAsync(
            IReadOnlyList<string> nodeUuids,
            string centerNodeUuid,
            float minScore = 0,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SearchRank>> RankNodeEpisodeMentionsAsync(
            IReadOnlyList<string> nodeUuids,
            float minScore = 0,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingNodeExtractionLlmClient : LlmClient
    {
        public ThrowingNodeExtractionLlmClient()
            : base(config: null, cache: false)
        {
        }

        protected override Task<JsonObject> GenerateResponseCoreAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel,
            StructuredResponseSchema? responseSchema,
            int maxTokens,
            ModelSize modelSize,
            string? promptName,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("node extraction failed");
    }

    private sealed class ThrowingSaveBulkDriver : GraphDriverBase
    {
        private readonly InvalidOperationException _exception = new("bulk save failed");

        public ThrowingSaveBulkDriver() : base(GraphProvider.InMemory)
        {
        }

        public override Task SaveBulkAsync(
            IEnumerable<EpisodicNode> episodicNodes,
            IEnumerable<EpisodicEdge> episodicEdges,
            IEnumerable<EntityNode> entityNodes,
            IEnumerable<EntityEdge> entityEdges,
            IEmbedderClient embedder,
            CancellationToken cancellationToken = default) =>
            throw _exception;

        public override Task BuildIndicesAndConstraintsAsync(
            bool deleteExisting = false,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override IGraphDriver Clone(string database) => throw new NotSupportedException();
        public override Task SaveNodeAsync(Node node, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task SaveEdgeAsync(Edge edge, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteNodeAsync(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteNodesByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteNodesByUuidsAsync(IEnumerable<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteEdgeAsync(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteEdgesByUuidsAsync(IEnumerable<string> uuids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task ClearDataAsync(IReadOnlyList<string>? groupIds = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<TNode> GetNodeByUuidAsync<TNode>(string uuid, CancellationToken cancellationToken = default) => throw new NodeNotFoundException(uuid);

        public override Task<IReadOnlyList<TNode>> GetNodesByUuidsAsync<TNode>(
            IEnumerable<string> uuids,
            string? groupId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TNode>>(Array.Empty<TNode>());

        public override Task<IReadOnlyList<TNode>> GetNodesByGroupIdsAsync<TNode>(
            IEnumerable<string> groupIds,
            int? limit = null,
            string? uuidCursor = null,
            bool withEmbeddings = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TNode>>(Array.Empty<TNode>());

        public override Task<T> GetEdgeByUuidAsync<T>(
            string uuid,
            CancellationToken cancellationToken = default) =>
            throw new EdgeNotFoundException(uuid);

        public override Task<IReadOnlyList<T>> GetEdgesByUuidsAsync<T>(
            IEnumerable<string> uuids,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());

        public override Task<IReadOnlyList<T>> GetEdgesByGroupIdsAsync<T>(
            IEnumerable<string> groupIds,
            int? limit = null,
            string? uuidCursor = null,
            bool withEmbeddings = false,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());

        public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesBetweenNodesAsync(
            string sourceNodeUuid,
            string targetNodeUuid,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntityEdge>>(Array.Empty<EntityEdge>());

        public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesByNodeUuidAsync(
            string nodeUuid,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override Task<IReadOnlyList<EpisodicNode>> GetEpisodesByEntityNodeUuidAsync(
            string entityNodeUuid,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(
            DateTime referenceTime,
            int lastN,
            IReadOnlyList<string>? groupIds = null,
            EpisodeType? source = null,
            string? saga = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override Task<IReadOnlyList<EntityNode>> GetMentionedNodesAsync(
            IReadOnlyList<EpisodicNode> episodes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override Task<IReadOnlyList<CommunityNode>> GetCommunitiesByNodesAsync(
            IReadOnlyList<EntityNode> nodes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override Task<SagaNode?> FindSagaByNameAsync(
            string name,
            string groupId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override Task<string?> GetSagaPreviousEpisodeUuidAsync(
            string sagaUuid,
            string currentEpisodeUuid,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override Task<IReadOnlyList<SagaEpisodeContent>> GetSagaEpisodeContentsAsync(
            string sagaUuid,
            DateTime? since = null,
            int limit = 200,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingBuildDriver : GraphDriverBase
    {
        public ThrowingBuildDriver() : base(GraphProvider.InMemory)
        {
        }

        public override Task BuildIndicesAndConstraintsAsync(
            bool deleteExisting = false,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("schema build failed");

        public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
}
