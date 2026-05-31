using Graphiti.Core;

namespace Graphiti.Core.Tests.Search;

public class SearchEngineRrfTests
{
    [Fact]
    public async Task EdgeRrf_FusesTextAndVectorRankings()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var textWinner = Edge("text", "alpha beta", new[] { 0f, 1f }, now);
        var vectorWinner = Edge("vector", "gamma", new[] { 0.95f, 0.05f }, now);
        var fusedWinner = Edge("fused", "alpha", new[] { 1f, 0f }, now);

        await driver.SaveEdgeAsync(textWinner);
        await driver.SaveEdgeAsync(vectorWinner);
        await driver.SaveEdgeAsync(fusedWinner);

        var ranked = await SearchEngine.EdgeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "alpha beta",
            new[] { 1f, 0f },
            groupIds: null,
            new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity },
                Reranker = EdgeReranker.Rrf
            },
            new SearchFilters(),
            limit: 3);

        Assert.Equal(fusedWinner.Uuid, ranked[0].Item.Uuid);
        Assert.True(ranked[0].Score > ranked[1].Score);
    }

    [Fact]
    public async Task NodeRrf_FusesTextAndVectorRankings()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var textWinner = Node("text", "alpha beta", new[] { 0f, 1f }, now);
        var vectorWinner = Node("vector", "gamma", new[] { 0.95f, 0.05f }, now);
        var fusedWinner = Node("fused", "alpha", new[] { 1f, 0f }, now);

        await driver.SaveNodeAsync(textWinner);
        await driver.SaveNodeAsync(vectorWinner);
        await driver.SaveNodeAsync(fusedWinner);

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "alpha beta",
            new[] { 1f, 0f },
            groupIds: null,
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity },
                Reranker = NodeReranker.Rrf
            },
            new SearchFilters(),
            limit: 3);

        Assert.Equal(fusedWinner.Uuid, ranked[0].Item.Uuid);
        Assert.True(ranked[0].Score > ranked[1].Score);
    }

    [Fact]
    public async Task FallbackVectorSearch_RequestsEmbeddingsFromNonSearchDrivers()
    {
        var driver = new NonSearchGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var node = Node("node", "gamma", new[] { 1f, 0f }, now);
        var edge = Edge("edge", "gamma", new[] { 1f, 0f }, now);
        await driver.SaveNodeAsync(node);
        await driver.SaveEdgeAsync(edge);

        var rankedNodes = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "alpha",
            new[] { 1f, 0f },
            groupIds: null,
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.CosineSimilarity },
                Reranker = NodeReranker.Rrf,
                SimMinScore = 0.1
            },
            new SearchFilters(),
            limit: 1);
        var rankedEdges = await SearchEngine.EdgeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "alpha",
            new[] { 1f, 0f },
            groupIds: null,
            new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.CosineSimilarity },
                Reranker = EdgeReranker.Rrf,
                SimMinScore = 0.1
            },
            new SearchFilters(),
            limit: 1);

        Assert.Equal(node.Uuid, Assert.Single(rankedNodes).Item.Uuid);
        Assert.Equal(edge.Uuid, Assert.Single(rankedEdges).Item.Uuid);
        Assert.Contains(true, driver.NodeWithEmbeddingRequests);
        Assert.Contains(true, driver.EdgeWithEmbeddingRequests);
    }

    [Fact]
    public async Task FallbackNodeSearchWithoutGroupIdsUsesAllKnownEntityGroups()
    {
        var driver = new NonSearchGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var first = Node("tenant-a-node", "shared alpha", new[] { 1f, 0f }, now);
        var second = Node("tenant-b-node", "shared beta", new[] { 1f, 0f }, now);
        first.GroupId = "tenant-a";
        second.GroupId = "tenant-b";

        await driver.SaveNodeAsync(first);
        await driver.SaveNodeAsync(second);

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "shared",
            queryVector: null,
            groupIds: null,
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25 },
                Reranker = NodeReranker.Rrf
            },
            new SearchFilters(),
            limit: 10);

        Assert.Equal(
            new[] { first.Uuid, second.Uuid },
            ranked.Select(hit => hit.Item.Uuid).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task FallbackNodeFulltextSearch_UsesBm25CorpusRanking()
    {
        var driver = new NonSearchGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alphaOnly = Node("alpha-only", "alpha alpha alpha alpha", new[] { 1f, 0f }, now);
        var bothTerms = Node("both-terms", "alpha beta", new[] { 1f, 0f }, now);

        await driver.SaveNodeAsync(alphaOnly);
        await driver.SaveNodeAsync(bothTerms);

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "alpha beta",
            queryVector: null,
            groupIds: null,
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25 },
                Reranker = NodeReranker.Rrf
            },
            new SearchFilters(),
            limit: 2);

        Assert.Equal(new[] { bothTerms.Uuid, alphaOnly.Uuid }, ranked.Select(hit => hit.Item.Uuid));
        Assert.True(ranked[0].Score > ranked[1].Score);
    }

    [Fact]
    public async Task FallbackEdgeSearchWithoutGroupIdsUsesAllKnownEntityGroups()
    {
        var driver = new NonSearchGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var first = Edge("tenant-a-edge", "shared alpha", new[] { 1f, 0f }, now);
        var second = Edge("tenant-b-edge", "shared beta", new[] { 1f, 0f }, now);
        first.GroupId = "tenant-a";
        second.GroupId = "tenant-b";

        await driver.SaveEdgeAsync(first);
        await driver.SaveEdgeAsync(second);

        var ranked = await SearchEngine.EdgeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "shared",
            queryVector: null,
            groupIds: null,
            new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25 },
                Reranker = EdgeReranker.Rrf
            },
            new SearchFilters(),
            limit: 10);

        Assert.Equal(
            new[] { first.Uuid, second.Uuid },
            ranked.Select(hit => hit.Item.Uuid).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task FallbackFulltextSearch_UsesDatabaseIndexedTextFields()
    {
        var driver = new NonSearchGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var entity = Node("entity", "ordinary summary", new[] { 1f, 0f }, now);
        entity.GroupId = "tenant-x";
        var edge = Edge("edge", "ordinary fact", new[] { 1f, 0f }, now);
        edge.GroupId = "tenant-x";
        var episode = new EpisodicNode
        {
            Uuid = "episode",
            Name = "Episode",
            Content = "ordinary body",
            Source = EpisodeType.Json,
            SourceDescription = "support email",
            GroupId = "tenant-x",
            CreatedAt = now,
            ValidAt = now
        };
        var community = new CommunityNode
        {
            Uuid = "community",
            Name = "Community",
            Summary = "summary-only-secret",
            GroupId = "tenant-x",
            CreatedAt = now
        };

        await driver.SaveNodeAsync(entity);
        await driver.SaveEdgeAsync(edge);
        await driver.SaveNodeAsync(episode);
        await driver.SaveNodeAsync(community);

        Assert.Equal(entity.Uuid, Assert.Single(await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "tenant-x",
            queryVector: null,
            groupIds: null,
            new NodeSearchConfig { SearchMethods = { NodeSearchMethod.Bm25 }, Reranker = NodeReranker.Rrf },
            new SearchFilters(),
            limit: 10)).Item.Uuid);
        Assert.Equal(edge.Uuid, Assert.Single(await SearchEngine.EdgeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "tenant-x",
            queryVector: null,
            groupIds: null,
            new EdgeSearchConfig { SearchMethods = { EdgeSearchMethod.Bm25 }, Reranker = EdgeReranker.Rrf },
            new SearchFilters(),
            limit: 10)).Item.Uuid);
        Assert.Equal(episode.Uuid, Assert.Single(await SearchEngine.EpisodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "json",
            groupIds: null,
            new EpisodeSearchConfig { SearchMethods = { EpisodeSearchMethod.Bm25 }, Reranker = EpisodeReranker.Rrf },
            limit: 10)).Item.Uuid);
        Assert.Equal(episode.Uuid, Assert.Single(await SearchEngine.EpisodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "email",
            groupIds: null,
            new EpisodeSearchConfig { SearchMethods = { EpisodeSearchMethod.Bm25 }, Reranker = EpisodeReranker.Rrf },
            limit: 10)).Item.Uuid);
        Assert.Equal(community.Uuid, Assert.Single(await SearchEngine.CommunitySearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "tenant-x",
            queryVector: null,
            groupIds: null,
            new CommunitySearchConfig
            {
                SearchMethods = { CommunitySearchMethod.Bm25 },
                Reranker = CommunityReranker.Rrf
            },
            limit: 10)).Item.Uuid);
        Assert.Empty(await SearchEngine.CommunitySearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "summary-only-secret",
            queryVector: null,
            groupIds: null,
            new CommunitySearchConfig
            {
                SearchMethods = { CommunitySearchMethod.Bm25 },
                Reranker = CommunityReranker.Rrf
            },
            limit: 10));
    }

    [Fact]
    public async Task FallbackNodeBfs_KeepsResultsInOriginGroupWhenGroupIdsNull()
    {
        var driver = new NonSearchGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var episode = new EpisodicNode
        {
            Uuid = "episode",
            Name = "Episode",
            Content = "same other",
            GroupId = "group-a",
            CreatedAt = now,
            ValidAt = now
        };
        var sameGroup = Node("same", "same", new[] { 1f, 0f }, now);
        sameGroup.GroupId = "group-a";
        var otherGroup = Node("other", "other", new[] { 1f, 0f }, now);
        otherGroup.GroupId = "group-b";

        await driver.SaveNodeAsync(episode);
        await driver.SaveNodeAsync(sameGroup);
        await driver.SaveNodeAsync(otherGroup);
        foreach (var target in new[] { sameGroup, otherGroup })
        {
            await driver.SaveEdgeAsync(new EpisodicEdge
            {
                SourceNodeUuid = episode.Uuid,
                TargetNodeUuid = target.Uuid,
                GroupId = "group-a",
                CreatedAt = now
            });
        }

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "ignored",
            queryVector: null,
            groupIds: null,
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bfs },
                Reranker = NodeReranker.Rrf,
                BfsMaxDepth = 1
            },
            new SearchFilters { NodeLabels = new List<string> { "Entity" } },
            limit: 10,
            bfsOriginNodeUuids: new[] { episode.Uuid });

        Assert.Equal(new[] { sameGroup.Uuid }, ranked.Select(item => item.Item.Uuid));
    }

    [Fact]
    public async Task SearchAsync_ReplacesNewlinesBeforeEmbeddingQuery()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var node = Node("node", "alpha beta", new[] { 1f, 0f }, now);
        await driver.SaveNodeAsync(node);

        var embedder = new RecordingEmbedder(new[] { 1f, 0f });
        var clients = new GraphitiClients(
            driver,
            new NoOpLlmClient(),
            embedder,
            new IdentityCrossEncoderClient());

        var results = await SearchEngine.SearchAsync(
            clients,
            "alpha\nbeta",
            groupIds: null,
            new SearchConfig
            {
                Limit = 1,
                NodeConfig = new NodeSearchConfig
                {
                    SearchMethods = { NodeSearchMethod.CosineSimilarity },
                    Reranker = NodeReranker.Rrf,
                    SimMinScore = 0
                }
            },
            new SearchFilters());

        Assert.Equal("alpha beta", Assert.Single(embedder.Inputs));
        Assert.Equal(node.Uuid, Assert.Single(results.Nodes).Uuid);
    }

    [Fact]
    public async Task SearchAsync_RejectsInvalidGroupIdsBeforeBlankQueryReturn()
    {
        var clients = new GraphitiClients(
            new InMemoryGraphDriver(),
            new NoOpLlmClient(),
            new DimensionThrowingEmbedder(),
            new IdentityCrossEncoderClient());

        await Assert.ThrowsAsync<GroupIdValidationException>(() =>
            SearchEngine.SearchAsync(
                clients,
                "   ",
                new[] { "tenant:bad" },
                new SearchConfig
                {
                    Limit = 1,
                    NodeConfig = new NodeSearchConfig
                    {
                        SearchMethods = { NodeSearchMethod.Bm25 },
                        Reranker = NodeReranker.Rrf
                    }
                },
                new SearchFilters()));
    }

    [Fact]
    public async Task SearchAsync_VectorOnlyRejectsInvalidGroupIdsBeforeEmbedding()
    {
        var clients = new GraphitiClients(
            new InMemoryGraphDriver(),
            new NoOpLlmClient(),
            new DimensionThrowingEmbedder(),
            new IdentityCrossEncoderClient());

        await Assert.ThrowsAsync<GroupIdValidationException>(() =>
            SearchEngine.SearchAsync(
                clients,
                "alpha",
                new[] { "tenant:bad" },
                new SearchConfig
                {
                    Limit = 1,
                    NodeConfig = new NodeSearchConfig
                    {
                        SearchMethods = { NodeSearchMethod.CosineSimilarity },
                        Reranker = NodeReranker.Rrf
                    }
                },
                new SearchFilters()));
    }

    [Fact]
    public async Task NodeSearchAsync_VectorOnlyRejectsInvalidGroupIdsBeforeDriverCall()
    {
        await Assert.ThrowsAsync<GroupIdValidationException>(() =>
            SearchEngine.NodeSearchAsync(
                new InMemoryGraphDriver(),
                new IdentityCrossEncoderClient(),
                "alpha",
                new[] { 1f, 0f },
                new[] { "tenant:bad" },
                new NodeSearchConfig
                {
                    SearchMethods = { NodeSearchMethod.CosineSimilarity },
                    Reranker = NodeReranker.Rrf
                },
                new SearchFilters(),
                limit: 1));
    }

    [Fact]
    public async Task CommunitySearchAsync_CosineOnlyRejectsInvalidGroupIdsBeforeDriverCall()
    {
        await Assert.ThrowsAsync<GroupIdValidationException>(() =>
            SearchEngine.CommunitySearchAsync(
                new InMemoryGraphDriver(),
                new IdentityCrossEncoderClient(),
                "alpha",
                new[] { 1f, 0f },
                new[] { "tenant:bad" },
                new CommunitySearchConfig
                {
                    SearchMethods = { CommunitySearchMethod.CosineSimilarity },
                    Reranker = CommunityReranker.Rrf
                },
                limit: 1));
    }

    [Fact]
    public async Task SearchAsync_UsesProvidedQueryVectorWithoutCallingEmbedder()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var vectorWinner = Node("vector-winner", "gamma", new[] { 1f, 0f }, now);
        var embedderWinner = Node("embedder-winner", "gamma", new[] { 0f, 1f }, now);
        await driver.SaveNodeAsync(vectorWinner);
        await driver.SaveNodeAsync(embedderWinner);

        var embedder = new RecordingEmbedder(new[] { 0f, 1f });
        var clients = new GraphitiClients(
            driver,
            new NoOpLlmClient(),
            embedder,
            new IdentityCrossEncoderClient());

        var results = await SearchEngine.SearchAsync(
            clients,
            "alpha\nbeta",
            groupIds: null,
            new SearchConfig
            {
                Limit = 1,
                NodeConfig = new NodeSearchConfig
                {
                    SearchMethods = { NodeSearchMethod.CosineSimilarity },
                    Reranker = NodeReranker.Rrf,
                    SimMinScore = 0
                }
            },
            new SearchFilters(),
            queryVector: new[] { 1f, 0f });

        Assert.Empty(embedder.Inputs);
        Assert.Equal(vectorWinner.Uuid, Assert.Single(results.Nodes).Uuid);
    }

    [Fact]
    public async Task SearchAsync_RejectsProvidedQueryVectorWithWrongDimension()
    {
        var clients = new GraphitiClients(
            new InMemoryGraphDriver(),
            new NoOpLlmClient(),
            new RecordingEmbedder(new[] { 1f, 0f }),
            new IdentityCrossEncoderClient());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SearchEngine.SearchAsync(
                clients,
                "alpha",
                groupIds: null,
                new SearchConfig
                {
                    Limit = 1,
                    NodeConfig = new NodeSearchConfig
                    {
                        SearchMethods = { NodeSearchMethod.CosineSimilarity },
                        Reranker = NodeReranker.Rrf
                    }
                },
                new SearchFilters(),
                queryVector: new[] { 1f }));

        Assert.Contains("search query vector", exception.Message, StringComparison.Ordinal);
        Assert.Contains("expected 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_IgnoresProvidedQueryVectorWhenNoVectorPathNeeded()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var node = Node("node", "alpha beta", new[] { 1f, 0f }, now);
        await driver.SaveNodeAsync(node);

        var embedder = new RecordingEmbedder(new[] { 1f, 0f });
        var clients = new GraphitiClients(
            driver,
            new NoOpLlmClient(),
            embedder,
            new IdentityCrossEncoderClient());

        var results = await SearchEngine.SearchAsync(
            clients,
            "alpha",
            groupIds: null,
            new SearchConfig
            {
                Limit = 1,
                NodeConfig = new NodeSearchConfig
                {
                    SearchMethods = { NodeSearchMethod.Bm25 },
                    Reranker = NodeReranker.Rrf
                }
            },
            new SearchFilters(),
            queryVector: new[] { 1f });

        Assert.Empty(embedder.Inputs);
        Assert.Equal(node.Uuid, Assert.Single(results.Nodes).Uuid);
    }

    [Fact]
    public async Task SearchAsync_Bm25OnlyDoesNotRequireEmbedderDimension()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var node = Node("node", "alpha beta", new[] { 1f, 0f }, now);
        await driver.SaveNodeAsync(node);

        var clients = new GraphitiClients(
            driver,
            new NoOpLlmClient(),
            new DimensionThrowingEmbedder(),
            new IdentityCrossEncoderClient());

        var results = await SearchEngine.SearchAsync(
            clients,
            "alpha",
            groupIds: null,
            new SearchConfig
            {
                Limit = 1,
                NodeConfig = new NodeSearchConfig
                {
                    SearchMethods = { NodeSearchMethod.Bm25 },
                    Reranker = NodeReranker.Rrf
                }
            },
            new SearchFilters(),
            queryVector: new[] { 1f });

        Assert.Equal(node.Uuid, Assert.Single(results.Nodes).Uuid);
    }

    [Fact]
    public async Task SearchAsync_UsesProvidedQueryVectorForMmrWithoutCosine()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var vectorWinner = Node("vector-winner", "alpha gamma", new[] { 1f, 0f }, now);
        var other = Node("other", "alpha delta", new[] { 0f, 1f }, now);
        await driver.SaveNodeAsync(vectorWinner);
        await driver.SaveNodeAsync(other);

        var embedder = new RecordingEmbedder(new[] { 0f, 1f });
        var clients = new GraphitiClients(
            driver,
            new NoOpLlmClient(),
            embedder,
            new IdentityCrossEncoderClient());

        var results = await SearchEngine.SearchAsync(
            clients,
            "alpha",
            groupIds: null,
            new SearchConfig
            {
                Limit = 1,
                NodeConfig = new NodeSearchConfig
                {
                    SearchMethods = { NodeSearchMethod.Bm25 },
                    Reranker = NodeReranker.Mmr,
                    MmrLambda = 1
                }
            },
            new SearchFilters(),
            queryVector: new[] { 1f, 0f });

        Assert.Empty(embedder.Inputs);
        Assert.Equal(vectorWinner.Uuid, Assert.Single(results.Nodes).Uuid);
    }

    [Fact]
    public async Task FallbackMmrWithoutCosineMaterializesEmbeddingsFromNonSearchDrivers()
    {
        var driver = new NonSearchGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var vectorWinner = Node("vector-winner", "alpha gamma", new[] { 1f, 0f }, now);
        var other = Node("other", "alpha delta", new[] { 0f, 1f }, now);
        await driver.SaveNodeAsync(vectorWinner);
        await driver.SaveNodeAsync(other);

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "alpha",
            queryVector: new[] { 1f, 0f },
            groupIds: null,
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25 },
                Reranker = NodeReranker.Mmr,
                MmrLambda = 1
            },
            new SearchFilters(),
            limit: 1);

        Assert.Equal(vectorWinner.Uuid, Assert.Single(ranked).Item.Uuid);
        Assert.Contains(true, driver.NodeWithEmbeddingRequests);
    }

    [Fact]
    public async Task GraphitiSearchAsync_AdvancedForwardsProvidedQueryVector()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var vectorWinner = Node("vector-winner", "gamma", new[] { 1f, 0f }, now);
        var embedderWinner = Node("embedder-winner", "gamma", new[] { 0f, 1f }, now);
        await driver.SaveNodeAsync(vectorWinner);
        await driver.SaveNodeAsync(embedderWinner);

        var embedder = new RecordingEmbedder(new[] { 0f, 1f });
        var graphiti = new Graphiti(graphDriver: driver, embedder: embedder);

        var results = await graphiti.SearchAsync(
            "alpha",
            new SearchConfig
            {
                Limit = 1,
                NodeConfig = new NodeSearchConfig
                {
                    SearchMethods = { NodeSearchMethod.CosineSimilarity },
                    Reranker = NodeReranker.Rrf,
                    SimMinScore = 0
                }
            },
            queryVector: new[] { 1f, 0f });

        Assert.Empty(embedder.Inputs);
        Assert.Equal(vectorWinner.Uuid, Assert.Single(results.Nodes).Uuid);
    }

    [Fact]
    public async Task GraphitiSearchAsync_WithConfigForwardsProvidedQueryVector()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var vectorWinner = Node("vector-winner", "gamma", new[] { 1f, 0f }, now);
        var embedderWinner = Node("embedder-winner", "gamma", new[] { 0f, 1f }, now);
        await driver.SaveNodeAsync(vectorWinner);
        await driver.SaveNodeAsync(embedderWinner);

        var embedder = new RecordingEmbedder(new[] { 0f, 1f });
        var graphiti = new Graphiti(graphDriver: driver, embedder: embedder);

        var results = await graphiti.SearchAsync(
            "alpha",
            new SearchConfig
            {
                Limit = 1,
                NodeConfig = new NodeSearchConfig
                {
                    SearchMethods = { NodeSearchMethod.CosineSimilarity },
                    Reranker = NodeReranker.Rrf,
                    SimMinScore = 0
                }
            },
            queryVector: new[] { 1f, 0f });

        Assert.Empty(embedder.Inputs);
        Assert.Equal(vectorWinner.Uuid, Assert.Single(results.Nodes).Uuid);
    }

    [Fact]
    public async Task SearchAsync_AppliesRerankerMinScoreBeforeEdgeEpisodeMentionsSort()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var highRrfScore = Edge("high-rrf", "alpha beta", new[] { 1f, 0f }, now);
        var lowRrfScoreManyEpisodes = Edge("low-rrf", "alpha", new[] { 1f, 0f }, now);
        lowRrfScoreManyEpisodes.Episodes = new List<string> { "e1", "e2", "e3" };
        await driver.SaveEdgeAsync(highRrfScore);
        await driver.SaveEdgeAsync(lowRrfScoreManyEpisodes);
        var clients = new GraphitiClients(
            driver,
            new NoOpLlmClient(),
            new HashEmbedder(2),
            new IdentityCrossEncoderClient());

        var results = await SearchEngine.SearchAsync(
            clients,
            "alpha beta",
            groupIds: null,
            new SearchConfig
            {
                Limit = 1,
                RerankerMinScore = 0.75,
                EdgeConfig = new EdgeSearchConfig
                {
                    SearchMethods = { EdgeSearchMethod.Bm25 },
                    Reranker = EdgeReranker.EpisodeMentions
                }
            },
            new SearchFilters());

        var edge = Assert.Single(results.Edges);
        Assert.Equal(highRrfScore.Uuid, edge.Uuid);
        Assert.Equal(1d, Assert.Single(results.EdgeRerankerScores), precision: 6);
    }

    [Fact]
    public async Task NodeDistance_RerankerMinScoreFiltersSeedRrfBeforeDistanceRanking()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var center = Node("center", "alpha beta", new[] { 1f, 0f }, now);
        var neighbor = Node("neighbor", "alpha", new[] { 1f, 0f }, now);
        var relation = Edge("relation", "center connected to neighbor", new[] { 1f, 0f }, now);
        relation.SourceNodeUuid = center.Uuid;
        relation.TargetNodeUuid = neighbor.Uuid;
        await driver.SaveNodeAsync(center);
        await driver.SaveNodeAsync(neighbor);
        await driver.SaveEdgeAsync(relation);

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "alpha beta",
            queryVector: null,
            groupIds: null,
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25 },
                Reranker = NodeReranker.NodeDistance
            },
            new SearchFilters(),
            limit: 2,
            rerankerMinScore: 0.75,
            centerNodeUuid: center.Uuid);

        var result = Assert.Single(ranked);
        Assert.Equal(center.Uuid, result.Item.Uuid);
        Assert.Equal(10f, result.Score);
    }

    private static EntityEdge Edge(string name, string fact, IReadOnlyList<float> embedding, DateTime now) =>
        new()
        {
            Uuid = name,
            Name = name,
            Fact = fact,
            FactEmbedding = embedding.ToList(),
            GroupId = string.Empty,
            SourceNodeUuid = $"{name}-source",
            TargetNodeUuid = $"{name}-target",
            CreatedAt = now
        };

    private static EntityNode Node(string name, string summary, IReadOnlyList<float> embedding, DateTime now) =>
        new()
        {
            Uuid = name,
            Name = name,
            Summary = summary,
            NameEmbedding = embedding.ToList(),
            GroupId = string.Empty,
            Labels = { "Entity" },
            CreatedAt = now
        };

    private sealed class RecordingEmbedder : EmbedderClient
    {
        private readonly IReadOnlyList<float> _vector;

        public RecordingEmbedder(IReadOnlyList<float> vector)
            : base(new EmbedderConfig(vector.Count))
        {
            _vector = vector;
        }

        public List<string> Inputs { get; } = new();

        public override Task<IReadOnlyList<float>> CreateAsync(
            string input,
            CancellationToken cancellationToken = default)
        {
            Inputs.Add(input);
            return Task.FromResult(_vector);
        }
    }

    private sealed class DimensionThrowingEmbedder : IEmbedderClient
    {
        public int EmbeddingDimension => throw new InvalidOperationException("Embedding dimension should not be read.");

        public Task<IReadOnlyList<float>> CreateAsync(
            string input,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Embedding should not be created.");
    }

    private sealed class NonSearchGraphDriver : GraphDriverBase
    {
        private readonly List<EntityNode> _nodes = new();
        private readonly List<EpisodicNode> _episodes = new();
        private readonly List<CommunityNode> _communities = new();
        private readonly List<EntityEdge> _edges = new();
        private readonly List<EpisodicEdge> _episodicEdges = new();

        public NonSearchGraphDriver()
            : base(GraphProvider.InMemory)
        {
        }

        public List<bool> NodeWithEmbeddingRequests { get; } = new();
        public List<bool> EdgeWithEmbeddingRequests { get; } = new();

        public override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override IGraphDriver Clone(string database) => this;

        public override Task<IReadOnlyList<string>> GetEntityGroupIdsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<string> groupIds = _nodes
                .Select(node => node.GroupId)
                .Concat(_episodes.Select(episode => episode.GroupId))
                .Concat(_edges.Select(edge => edge.GroupId))
                .Concat(_episodicEdges.Select(edge => edge.GroupId))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(groupIds);
        }

        public override Task<IReadOnlyList<string>> GetCommunityGroupIdsAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<string> groupIds = _communities
                .Select(community => community.GroupId)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(groupIds);
        }

        public override Task SaveNodeAsync(Node node, CancellationToken cancellationToken = default)
        {
            switch (node)
            {
                case EntityNode entity:
                    _nodes.Add(Clone(entity, withEmbeddings: true));
                    break;
                case EpisodicNode episode:
                    _episodes.Add(Clone(episode));
                    break;
                case CommunityNode community:
                    _communities.Add(Clone(community, withEmbeddings: true));
                    break;
            }

            return Task.CompletedTask;
        }

        public override Task SaveEdgeAsync(Edge edge, CancellationToken cancellationToken = default)
        {
            switch (edge)
            {
                case EntityEdge entity:
                    _edges.Add(Clone(entity, withEmbeddings: true));
                    break;
                case EpisodicEdge episodic:
                    _episodicEdges.Add(Clone(episodic));
                    break;
            }

            return Task.CompletedTask;
        }

        public override Task<IReadOnlyList<TNode>> GetNodesByGroupIdsAsync<TNode>(
            IEnumerable<string> groupIds,
            int? limit = null,
            string? uuidCursor = null,
            bool withEmbeddings = false,
            CancellationToken cancellationToken = default)
        {
            NodeWithEmbeddingRequests.Add(withEmbeddings);
            var groupSet = groupIds.ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<TNode> nodes = AllNodes<TNode>()
                .Where(node => groupSet.Contains(node.GroupId))
                .Select(node => CloneNode(node, withEmbeddings))
                .ToList();
            return Task.FromResult(nodes);
        }

        public override Task<IReadOnlyList<T>> GetEdgesByGroupIdsAsync<T>(
            IEnumerable<string> groupIds,
            int? limit = null,
            string? uuidCursor = null,
            bool withEmbeddings = false,
            CancellationToken cancellationToken = default)
        {
            EdgeWithEmbeddingRequests.Add(withEmbeddings);
            var groupSet = groupIds.ToHashSet(StringComparer.Ordinal);
            IReadOnlyList<T> edges = AllEdges<T>()
                .Where(edge => groupSet.Contains(edge.GroupId))
                .Select(edge => CloneEdge(edge, withEmbeddings))
                .ToList();
            return Task.FromResult(edges);
        }

        public override Task DeleteNodeAsync(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteNodesByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteNodesByUuidsAsync(IEnumerable<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteEdgeAsync(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteEdgesByUuidsAsync(IEnumerable<string> uuids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task ClearDataAsync(IReadOnlyList<string>? groupIds = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<TNode> GetNodeByUuidAsync<TNode>(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<TNode>> GetNodesByUuidsAsync<TNode>(IEnumerable<string> uuids, string? groupId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<T> GetEdgeByUuidAsync<T>(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<T>> GetEdgesByUuidsAsync<T>(IEnumerable<string> uuids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesBetweenNodesAsync(string sourceNodeUuid, string targetNodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesByNodeUuidAsync(string nodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EpisodicNode>> GetEpisodesByEntityNodeUuidAsync(string entityNodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(DateTime referenceTime, int lastN, IReadOnlyList<string>? groupIds = null, EpisodeType? source = null, string? saga = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EntityNode>> GetMentionedNodesAsync(IReadOnlyList<EpisodicNode> episodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<CommunityNode>> GetCommunitiesByNodesAsync(IReadOnlyList<EntityNode> nodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<SagaNode?> FindSagaByNameAsync(string name, string groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<string?> GetSagaPreviousEpisodeUuidAsync(string sagaUuid, string currentEpisodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<SagaEpisodeContent>> GetSagaEpisodeContentsAsync(string sagaUuid, DateTime? since = null, int limit = 200, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private IEnumerable<TNode> AllNodes<TNode>()
            where TNode : Node =>
            typeof(TNode) == typeof(EntityNode)
                ? _nodes.Cast<TNode>()
                : typeof(TNode) == typeof(EpisodicNode)
                    ? _episodes.Cast<TNode>()
                    : typeof(TNode) == typeof(CommunityNode)
                        ? _communities.Cast<TNode>()
                        : Enumerable.Empty<TNode>();

        private static TNode CloneNode<TNode>(TNode node, bool withEmbeddings)
            where TNode : Node =>
            node switch
            {
                EntityNode entity => (TNode)(Node)Clone(entity, withEmbeddings),
                EpisodicNode episode => (TNode)(Node)Clone(episode),
                CommunityNode community => (TNode)(Node)Clone(community, withEmbeddings),
                _ => throw new NotSupportedException(typeof(TNode).Name)
            };

        private IEnumerable<T> AllEdges<T>()
            where T : Edge =>
            typeof(T) == typeof(EntityEdge)
                ? _edges.Cast<T>()
                : typeof(T) == typeof(EpisodicEdge)
                    ? _episodicEdges.Cast<T>()
                    : Enumerable.Empty<T>();

        private static T CloneEdge<T>(T edge, bool withEmbeddings)
            where T : Edge =>
            edge switch
            {
                EntityEdge entity => (T)(Edge)Clone(entity, withEmbeddings),
                EpisodicEdge episodic => (T)(Edge)Clone(episodic),
                _ => throw new NotSupportedException(typeof(T).Name)
            };

        private static EntityNode Clone(EntityNode node, bool withEmbeddings) =>
            new()
            {
                Uuid = node.Uuid,
                Name = node.Name,
                GroupId = node.GroupId,
                Labels = node.Labels.ToList(),
                CreatedAt = node.CreatedAt,
                Summary = node.Summary,
                NameEmbedding = withEmbeddings ? node.NameEmbedding?.ToList() : null,
                Attributes = node.Attributes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            };

        private static EpisodicNode Clone(EpisodicNode episode) =>
            new()
            {
                Uuid = episode.Uuid,
                Name = episode.Name,
                GroupId = episode.GroupId,
                Labels = episode.Labels.ToList(),
                CreatedAt = episode.CreatedAt,
                Source = episode.Source,
                SourceDescription = episode.SourceDescription,
                Content = episode.Content,
                ValidAt = episode.ValidAt,
                EntityEdges = episode.EntityEdges.ToList(),
                EpisodeMetadata = episode.EpisodeMetadata?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            };

        private static CommunityNode Clone(CommunityNode node, bool withEmbeddings) =>
            new()
            {
                Uuid = node.Uuid,
                Name = node.Name,
                GroupId = node.GroupId,
                Labels = node.Labels.ToList(),
                CreatedAt = node.CreatedAt,
                Summary = node.Summary,
                NameEmbedding = withEmbeddings ? node.NameEmbedding?.ToList() : null
            };

        private static EpisodicEdge Clone(EpisodicEdge edge) =>
            new()
            {
                Uuid = edge.Uuid,
                GroupId = edge.GroupId,
                SourceNodeUuid = edge.SourceNodeUuid,
                TargetNodeUuid = edge.TargetNodeUuid,
                CreatedAt = edge.CreatedAt
            };

        private static EntityEdge Clone(EntityEdge edge, bool withEmbeddings) =>
            new()
            {
                Uuid = edge.Uuid,
                GroupId = edge.GroupId,
                SourceNodeUuid = edge.SourceNodeUuid,
                TargetNodeUuid = edge.TargetNodeUuid,
                CreatedAt = edge.CreatedAt,
                Name = edge.Name,
                Fact = edge.Fact,
                FactEmbedding = withEmbeddings ? edge.FactEmbedding?.ToList() : null,
                Episodes = edge.Episodes.ToList(),
                ExpiredAt = edge.ExpiredAt,
                ValidAt = edge.ValidAt,
                InvalidAt = edge.InvalidAt,
                ReferenceTime = edge.ReferenceTime,
                Attributes = edge.Attributes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            };
    }
}
