using Graphiti.Core;

namespace Graphiti.Core.Tests.Search;

public class SearchEngineDriverBackedTests
{
    [Fact]
    public async Task SearchAsync_ExecutesConfiguredScopesConcurrently()
    {
        var edge = new EntityEdge { Uuid = "edge", Fact = "alpha edge", GroupId = "group" };
        var node = new EntityNode { Uuid = "node", Name = "Alpha", GroupId = "group" };
        var episode = new EpisodicNode { Uuid = "episode", Name = "Episode", Content = "alpha episode", GroupId = "group" };
        var driver = new DriverBackedSearchDriver
        {
            SearchDelay = TimeSpan.FromMilliseconds(50),
            EdgeFulltextHits = { new SearchHit<EntityEdge>(edge, 2) },
            NodeFulltextHits = { new SearchHit<EntityNode>(node, 2) },
            EpisodeFulltextHits = { new SearchHit<EpisodicNode>(episode, 2) }
        };
        var clients = new GraphitiClients(
            driver,
            new NoOpLlmClient(),
            new HashEmbedder(2),
            new IdentityCrossEncoderClient());

        var results = await SearchEngine.SearchAsync(
            clients,
            "alpha",
            new[] { "group" },
            new SearchConfig
            {
                Limit = 3,
                EdgeConfig = new EdgeSearchConfig
                {
                    SearchMethods = { EdgeSearchMethod.Bm25 },
                    Reranker = EdgeReranker.Rrf
                },
                NodeConfig = new NodeSearchConfig
                {
                    SearchMethods = { NodeSearchMethod.Bm25 },
                    Reranker = NodeReranker.Rrf
                },
                EpisodeConfig = new EpisodeSearchConfig()
            },
            new SearchFilters());

        Assert.True(driver.MaxConcurrentSearchCalls > 1);
        Assert.Equal("edge", Assert.Single(results.Edges).Uuid);
        Assert.Equal("node", Assert.Single(results.Nodes).Uuid);
        Assert.Equal("episode", Assert.Single(results.Episodes).Uuid);
    }

    [Fact]
    public async Task SearchAsync_CancelsSiblingScopesWhenOneScopeFails()
    {
        var failure = new InvalidOperationException("node fulltext failed");
        var driver = new DriverBackedSearchDriver
        {
            EdgeFulltextWaitsForCancellation = true,
            NodeFulltextException = failure
        };
        var clients = new GraphitiClients(
            driver,
            new NoOpLlmClient(),
            new HashEmbedder(2),
            new IdentityCrossEncoderClient());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SearchEngine.SearchAsync(
                clients,
                "alpha",
                new[] { "group" },
                new SearchConfig
                {
                    Limit = 3,
                    EdgeConfig = new EdgeSearchConfig
                    {
                        SearchMethods = { EdgeSearchMethod.Bm25 },
                        Reranker = EdgeReranker.Rrf
                    },
                    NodeConfig = new NodeSearchConfig
                    {
                        SearchMethods = { NodeSearchMethod.Bm25 },
                        Reranker = NodeReranker.Rrf
                    }
                },
                new SearchFilters()).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(failure, exception);
        await driver.EdgeFulltextCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, driver.EdgeFulltextCalls);
        Assert.Equal(1, driver.NodeFulltextCalls);
    }

    [Fact]
    public async Task NodeSearch_UsesDriverBackedFulltextAndVectorSearch()
    {
        var textNode = new EntityNode { Uuid = "text", Name = "Alice", GroupId = "group" };
        var vectorNode = new EntityNode { Uuid = "vector", Name = "Bob", GroupId = "group" };
        var queryVector = new[] { 1f, 0f };
        var filter = new SearchFilters
        {
            PropertyFilters = new List<PropertyFilter>
            {
                new("role", ComparisonOperator.Equals, "engineer")
            }
        };
        var driver = new DriverBackedSearchDriver
        {
            SearchDelay = TimeSpan.FromMilliseconds(50),
            NodeFulltextHits = { new SearchHit<EntityNode>(textNode, 12) },
            NodeVectorHits = { new SearchHit<EntityNode>(vectorNode, 0.9f) }
        };

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "alice",
            queryVector,
            new[] { "group" },
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity },
                Reranker = NodeReranker.Rrf
            },
            filter,
            limit: 3);

        Assert.Equal(1, driver.NodeFulltextCalls);
        Assert.Equal(1, driver.NodeVectorCalls);
        Assert.True(driver.MaxConcurrentSearchCalls > 1);
        Assert.Equal(0, driver.NodeMaterializationCalls);
        Assert.Equal(new[] { "text", "vector" }, ranked.Select(item => item.Item.Uuid));
        Assert.Equal(new[] { "group" }, driver.LastNodeFulltextGroupIds);
        Assert.Equal(6, driver.LastNodeFulltextLimit);
        Assert.Same(filter, driver.LastNodeVectorSearchFilter);
        Assert.Same(queryVector, driver.LastNodeVectorQueryVector);
        Assert.Equal(new[] { "group" }, driver.LastNodeVectorGroupIds);
        Assert.Equal(6, driver.LastNodeVectorLimit);
        Assert.Equal(SearchConfiguration.DefaultMinScore, driver.LastNodeVectorMinScore, precision: 6);
    }

    [Fact]
    public async Task NodeSearch_CancelsSiblingMethodsWhenOneMethodFails()
    {
        var failure = new InvalidOperationException("node fulltext failed");
        var driver = new DriverBackedSearchDriver
        {
            NodeFulltextException = failure,
            NodeVectorWaitsForCancellation = true
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SearchEngine.NodeSearchAsync(
                driver,
                new IdentityCrossEncoderClient(),
                "alice",
                new[] { 1f, 0f },
                new[] { "group" },
                new NodeSearchConfig
                {
                    SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity },
                    Reranker = NodeReranker.Rrf
                },
                new SearchFilters(),
                limit: 3).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(failure, exception);
        await driver.NodeVectorCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, driver.NodeFulltextCalls);
        Assert.Equal(1, driver.NodeVectorCalls);
    }

    [Fact]
    public async Task NodeSearch_CrossEncoderUsesBoundedDriverCandidates()
    {
        var weak = new EntityNode { Uuid = "weak", Name = "Alpha", Summary = "other", GroupId = "group" };
        var strong = new EntityNode { Uuid = "strong", Name = "Beta", Summary = "zebra", GroupId = "group" };
        var driver = new DriverBackedSearchDriver
        {
            NodeFulltextHits =
            {
                new SearchHit<EntityNode>(weak, 2),
                new SearchHit<EntityNode>(strong, 1)
            }
        };

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "Beta",
            queryVector: null,
            new[] { "group" },
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25 },
                Reranker = NodeReranker.CrossEncoder
            },
            new SearchFilters(),
            limit: 2);

        Assert.Equal(1, driver.NodeFulltextCalls);
        Assert.Equal(0, driver.NodeMaterializationCalls);
        Assert.Equal("strong", ranked[0].Item.Uuid);
    }

    [Fact]
    public async Task NodeSearch_CrossEncoderRanksNamesOnly()
    {
        var alpha = new EntityNode { Uuid = "alpha", Name = "Alpha", Summary = "summary mentioning Beta", GroupId = "group" };
        var beta = new EntityNode { Uuid = "beta", Name = "Beta", Summary = "summary mentioning Alpha", GroupId = "group" };
        var driver = new DriverBackedSearchDriver
        {
            NodeFulltextHits =
            {
                new SearchHit<EntityNode>(alpha, 2),
                new SearchHit<EntityNode>(beta, 1)
            }
        };
        var crossEncoder = new RecordingCrossEncoder
        {
            Scores =
            {
                ["Alpha"] = 0.1f,
                ["Beta"] = 0.9f
            }
        };

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            crossEncoder,
            "query",
            queryVector: null,
            new[] { "group" },
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25 },
                Reranker = NodeReranker.CrossEncoder
            },
            new SearchFilters(),
            limit: 2);

        Assert.Equal(new[] { "Alpha", "Beta" }, crossEncoder.LastPassages);
        Assert.DoesNotContain(crossEncoder.LastPassages, passage => passage.Contains('\n', StringComparison.Ordinal));
        Assert.Equal(new[] { "beta", "alpha" }, ranked.Select(item => item.Item.Uuid));
    }

    [Fact]
    public async Task NodeSearch_CrossEncoderReranksEntirePreliminaryCandidatePool()
    {
        var weak = new EntityNode { Uuid = "weak", Name = "Weak", GroupId = "group" };
        var middle = new EntityNode { Uuid = "middle", Name = "Middle", GroupId = "group" };
        var rescued = new EntityNode { Uuid = "rescued", Name = "Rescued", GroupId = "group" };
        var driver = new DriverBackedSearchDriver
        {
            NodeFulltextHits =
            {
                new SearchHit<EntityNode>(weak, 3),
                new SearchHit<EntityNode>(middle, 2),
                new SearchHit<EntityNode>(rescued, 1)
            }
        };
        var crossEncoder = new RecordingCrossEncoder
        {
            Scores =
            {
                ["Weak"] = 0.1f,
                ["Middle"] = 0.2f,
                ["Rescued"] = 0.9f
            }
        };

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            crossEncoder,
            "query",
            queryVector: null,
            new[] { "group" },
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25 },
                Reranker = NodeReranker.CrossEncoder
            },
            new SearchFilters(),
            limit: 2);

        Assert.Equal(new[] { "Weak", "Middle", "Rescued" }, crossEncoder.LastPassages);
        Assert.Equal(new[] { "rescued", "middle" }, ranked.Select(item => item.Item.Uuid));
    }

    [Fact]
    public async Task NodeSearch_CrossEncoderPreservesSameNameCandidates()
    {
        var first = new EntityNode { Uuid = "node-first", Name = "Same", GroupId = "group" };
        var second = new EntityNode { Uuid = "node-second", Name = "Same", GroupId = "group" };
        var driver = new DriverBackedSearchDriver
        {
            NodeFulltextHits =
            {
                new SearchHit<EntityNode>(first, 2),
                new SearchHit<EntityNode>(second, 1)
            }
        };
        var crossEncoder = new RecordingCrossEncoder
        {
            IndexedScores = new List<float> { 0.1f, 0.9f }
        };

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            crossEncoder,
            "query",
            queryVector: null,
            new[] { "group" },
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25 },
                Reranker = NodeReranker.CrossEncoder
            },
            new SearchFilters(),
            limit: 2);

        Assert.Equal(new[] { "Same", "Same" }, crossEncoder.LastPassages);
        Assert.Equal(new[] { "node-second", "node-first" }, ranked.Select(item => item.Item.Uuid));
    }

    [Fact]
    public async Task NodeSearch_BfsUsesDerivedDriverOrigins()
    {
        var origin = new EntityNode { Uuid = "origin", Name = "Origin", GroupId = "group" };
        var vectorOrigin = new EntityNode { Uuid = "vector-origin", Name = "Vector Origin", GroupId = "group" };
        var neighbor = new EntityNode { Uuid = "neighbor", Name = "Neighbor", GroupId = "group" };
        var filter = new SearchFilters
        {
            PropertyFilters = new List<PropertyFilter>
            {
                new("status", ComparisonOperator.Equals, "active")
            }
        };
        var driver = new DriverBackedSearchDriver
        {
            NodeFulltextHits =
            {
                new SearchHit<EntityNode>(origin, 4),
                new SearchHit<EntityNode>(origin, 3)
            },
            NodeVectorHits =
            {
                new SearchHit<EntityNode>(vectorOrigin, 0.9f),
                new SearchHit<EntityNode>(origin, 0.8f)
            },
            NodeBfsHits = { new SearchHit<EntityNode>(neighbor, 1) }
        };

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "origin",
            new[] { 1f, 0f },
            new[] { "group" },
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity, NodeSearchMethod.Bfs },
                Reranker = NodeReranker.Rrf,
                BfsMaxDepth = 2
            },
            filter,
            limit: 3);

        Assert.Equal(1, driver.NodeBfsCalls);
        Assert.Equal(new[] { "origin", "vector-origin" }, driver.LastNodeBfsOrigins);
        Assert.Equal(2, driver.LastNodeBfsMaxDepth);
        Assert.Same(filter, driver.LastNodeBfsSearchFilter);
        Assert.Equal(new[] { "group" }, driver.LastNodeBfsGroupIds);
        Assert.Equal(6, driver.LastNodeBfsLimit);
        Assert.Equal(0, driver.NodeMaterializationCalls);
        Assert.Equal(new[] { "origin", "vector-origin", "neighbor" }, ranked.Select(item => item.Item.Uuid));
    }

    [Fact]
    public async Task NodeSearch_BfsUsesExplicitOriginsAsProvided()
    {
        var textOrigin = new EntityNode { Uuid = "text-origin", Name = "Text", GroupId = "group" };
        var vectorOrigin = new EntityNode { Uuid = "vector-origin", Name = "Vector", GroupId = "group" };
        var neighbor = new EntityNode { Uuid = "neighbor", Name = "Neighbor", GroupId = "group" };
        var explicitOrigins = new[] { "explicit-a", "explicit-a", "explicit-b" };
        var driver = new DriverBackedSearchDriver
        {
            NodeFulltextHits = { new SearchHit<EntityNode>(textOrigin, 4) },
            NodeVectorHits = { new SearchHit<EntityNode>(vectorOrigin, 0.9f) },
            NodeBfsHits = { new SearchHit<EntityNode>(neighbor, 1) }
        };

        await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "origin",
            new[] { 1f, 0f },
            new[] { "group" },
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity, NodeSearchMethod.Bfs },
                Reranker = NodeReranker.Rrf,
                BfsMaxDepth = 2
            },
            new SearchFilters(),
            limit: 3,
            bfsOriginNodeUuids: explicitOrigins);

        Assert.Equal(1, driver.NodeBfsCalls);
        Assert.Same(explicitOrigins, driver.LastNodeBfsOrigins);
        Assert.Equal(new[] { "explicit-a", "explicit-a", "explicit-b" }, driver.LastNodeBfsOrigins);
    }

    [Fact]
    public async Task NodeSearch_NodeDistanceUsesDriverRanker()
    {
        var far = new EntityNode { Uuid = "far", Name = "Far", GroupId = "group" };
        var near = new EntityNode { Uuid = "near", Name = "Near", GroupId = "group" };
        var driver = new DriverBackedSearchDriver
        {
            NodeFulltextHits =
            {
                new SearchHit<EntityNode>(far, 2),
                new SearchHit<EntityNode>(near, 1)
            },
            NodeDistanceRanks =
            {
                new SearchRank("near", 1),
                new SearchRank("far", 0)
            }
        };

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "match",
            queryVector: null,
            new[] { "group" },
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25 },
                Reranker = NodeReranker.NodeDistance
            },
            new SearchFilters(),
            limit: 2,
            centerNodeUuid: "center");

        Assert.Equal(1, driver.NodeDistanceRankCalls);
        Assert.Equal(0, driver.EdgeMaterializationCalls);
        Assert.Equal(new[] { "far", "near" }, driver.LastNodeDistanceRankUuids);
        Assert.Equal(new[] { "near", "far" }, ranked.Select(item => item.Item.Uuid));
    }

    [Fact]
    public async Task NodeSearch_EpisodeMentionsUsesDriverRanker()
    {
        var mentioned = new EntityNode { Uuid = "mentioned", Name = "Mentioned", GroupId = "group" };
        var unmentioned = new EntityNode { Uuid = "unmentioned", Name = "Unmentioned", GroupId = "group" };
        var driver = new DriverBackedSearchDriver
        {
            NodeFulltextHits =
            {
                new SearchHit<EntityNode>(mentioned, 2),
                new SearchHit<EntityNode>(unmentioned, 1)
            },
            NodeEpisodeMentionRanks =
            {
                new SearchRank("mentioned", 1),
                new SearchRank("unmentioned", float.PositiveInfinity)
            }
        };

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "match",
            queryVector: null,
            new[] { "group" },
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25 },
                Reranker = NodeReranker.EpisodeMentions
            },
            new SearchFilters(),
            limit: 2);

        Assert.Equal(1, driver.NodeEpisodeMentionRankCalls);
        Assert.Equal(0, driver.EdgeMaterializationCalls);
        Assert.Equal(new[] { "mentioned", "unmentioned" }, driver.LastNodeEpisodeMentionRankUuids);
        Assert.Equal(new[] { "mentioned", "unmentioned" }, ranked.Select(item => item.Item.Uuid));
    }

    [Fact]
    public async Task EdgeSearch_UsesDriverBackedFulltextAndVectorSearch()
    {
        var textEdge = new EntityEdge { Uuid = "text", Fact = "Alice likes Bob", GroupId = "group" };
        var vectorEdge = new EntityEdge { Uuid = "vector", Fact = "Alice knows Bob", GroupId = "group" };
        var queryVector = new[] { 1f, 0f };
        var filter = new SearchFilters
        {
            PropertyFilters = new List<PropertyFilter>
            {
                new("confidence", ComparisonOperator.GreaterThanEqual, 0.8)
            }
        };
        var driver = new DriverBackedSearchDriver
        {
            SearchDelay = TimeSpan.FromMilliseconds(50),
            EdgeFulltextHits = { new SearchHit<EntityEdge>(textEdge, 8) },
            EdgeVectorHits = { new SearchHit<EntityEdge>(vectorEdge, 0.8f) }
        };

        var ranked = await SearchEngine.EdgeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "alice",
            queryVector,
            new[] { "group" },
            new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity },
                Reranker = EdgeReranker.Rrf
            },
            filter,
            limit: 3);

        Assert.Equal(1, driver.EdgeFulltextCalls);
        Assert.Equal(1, driver.EdgeVectorCalls);
        Assert.True(driver.MaxConcurrentSearchCalls > 1);
        Assert.Equal(0, driver.EdgeMaterializationCalls);
        Assert.Equal(new[] { "text", "vector" }, ranked.Select(item => item.Item.Uuid));
        Assert.Equal(new[] { "group" }, driver.LastEdgeFulltextGroupIds);
        Assert.Equal(6, driver.LastEdgeFulltextLimit);
        Assert.Same(filter, driver.LastEdgeVectorSearchFilter);
        Assert.Same(queryVector, driver.LastEdgeVectorQueryVector);
        Assert.Equal(new[] { "group" }, driver.LastEdgeVectorGroupIds);
        Assert.Equal(6, driver.LastEdgeVectorLimit);
        Assert.Equal(SearchConfiguration.DefaultMinScore, driver.LastEdgeVectorMinScore, precision: 6);
    }

    [Fact]
    public async Task EdgeSearch_CancelsSiblingMethodsWhenOneMethodFails()
    {
        var failure = new InvalidOperationException("edge vector failed");
        var driver = new DriverBackedSearchDriver
        {
            EdgeFulltextWaitsForCancellation = true,
            EdgeVectorException = failure
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SearchEngine.EdgeSearchAsync(
                driver,
                new IdentityCrossEncoderClient(),
                "alice",
                new[] { 1f, 0f },
                new[] { "group" },
                new EdgeSearchConfig
                {
                    SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity },
                    Reranker = EdgeReranker.Rrf
                },
                new SearchFilters(),
                limit: 3).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Same(failure, exception);
        await driver.EdgeFulltextCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, driver.EdgeFulltextCalls);
        Assert.Equal(1, driver.EdgeVectorCalls);
    }

    [Fact]
    public async Task EdgeSearch_CrossEncoderPreservesDuplicateFacts()
    {
        var first = new EntityEdge { Uuid = "edge-first", Fact = "same fact", GroupId = "group" };
        var second = new EntityEdge { Uuid = "edge-second", Fact = "same fact", GroupId = "group" };
        var driver = new DriverBackedSearchDriver
        {
            EdgeFulltextHits =
            {
                new SearchHit<EntityEdge>(first, 2),
                new SearchHit<EntityEdge>(second, 1)
            }
        };
        var crossEncoder = new RecordingCrossEncoder
        {
            IndexedScores = new List<float> { 0.1f, 0.9f }
        };

        var ranked = await SearchEngine.EdgeSearchAsync(
            driver,
            crossEncoder,
            "query",
            queryVector: null,
            new[] { "group" },
            new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25 },
                Reranker = EdgeReranker.CrossEncoder
            },
            new SearchFilters(),
            limit: 2);

        Assert.Equal(new[] { "same fact", "same fact" }, crossEncoder.LastPassages);
        Assert.Equal(new[] { "edge-second", "edge-first" }, ranked.Select(item => item.Item.Uuid));
    }

    [Fact]
    public async Task EdgeSearch_BfsUsesDerivedSourceOrigins()
    {
        var textEdge = new EntityEdge { Uuid = "text", SourceNodeUuid = "source", TargetNodeUuid = "target", Fact = "Alice", GroupId = "group" };
        var vectorEdge = new EntityEdge { Uuid = "vector", SourceNodeUuid = "vector-source", TargetNodeUuid = "target", Fact = "Carol", GroupId = "group" };
        var bfsEdge = new EntityEdge { Uuid = "bfs", SourceNodeUuid = "target", TargetNodeUuid = "next", Fact = "Bob", GroupId = "group" };
        var filter = new SearchFilters
        {
            EdgeUuids = new List<string> { "text", "vector" }
        };
        var driver = new DriverBackedSearchDriver
        {
            EdgeFulltextHits =
            {
                new SearchHit<EntityEdge>(textEdge, 4),
                new SearchHit<EntityEdge>(textEdge, 3)
            },
            EdgeVectorHits =
            {
                new SearchHit<EntityEdge>(vectorEdge, 0.8f),
                new SearchHit<EntityEdge>(textEdge, 0.7f)
            },
            EdgeBfsHits = { new SearchHit<EntityEdge>(bfsEdge, 1) }
        };

        var ranked = await SearchEngine.EdgeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "alice",
            new[] { 1f, 0f },
            new[] { "group" },
            new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity, EdgeSearchMethod.Bfs },
                Reranker = EdgeReranker.Rrf,
                BfsMaxDepth = 2
            },
            filter,
            limit: 3);

        Assert.Equal(1, driver.EdgeBfsCalls);
        Assert.Equal(new[] { "source", "vector-source" }, driver.LastEdgeBfsOrigins);
        Assert.Equal(2, driver.LastEdgeBfsMaxDepth);
        Assert.Same(filter, driver.LastEdgeBfsSearchFilter);
        Assert.Equal(new[] { "group" }, driver.LastEdgeBfsGroupIds);
        Assert.Equal(6, driver.LastEdgeBfsLimit);
        Assert.Equal(0, driver.EdgeMaterializationCalls);
        Assert.Equal(new[] { "text", "vector", "bfs" }, ranked.Select(item => item.Item.Uuid));
    }

    [Fact]
    public async Task EdgeSearch_BfsUsesExplicitOriginsAsProvided()
    {
        var textEdge = new EntityEdge { Uuid = "text", SourceNodeUuid = "text-source", TargetNodeUuid = "target", Fact = "Alice", GroupId = "group" };
        var vectorEdge = new EntityEdge { Uuid = "vector", SourceNodeUuid = "vector-source", TargetNodeUuid = "target", Fact = "Carol", GroupId = "group" };
        var bfsEdge = new EntityEdge { Uuid = "bfs", SourceNodeUuid = "target", TargetNodeUuid = "next", Fact = "Bob", GroupId = "group" };
        var explicitOrigins = new[] { "explicit-a", "explicit-a", "explicit-b" };
        var driver = new DriverBackedSearchDriver
        {
            EdgeFulltextHits = { new SearchHit<EntityEdge>(textEdge, 4) },
            EdgeVectorHits = { new SearchHit<EntityEdge>(vectorEdge, 0.8f) },
            EdgeBfsHits = { new SearchHit<EntityEdge>(bfsEdge, 1) }
        };

        await SearchEngine.EdgeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "alice",
            new[] { 1f, 0f },
            new[] { "group" },
            new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity, EdgeSearchMethod.Bfs },
                Reranker = EdgeReranker.Rrf,
                BfsMaxDepth = 2
            },
            new SearchFilters(),
            limit: 3,
            bfsOriginNodeUuids: explicitOrigins);

        Assert.Equal(1, driver.EdgeBfsCalls);
        Assert.Same(explicitOrigins, driver.LastEdgeBfsOrigins);
        Assert.Equal(new[] { "explicit-a", "explicit-a", "explicit-b" }, driver.LastEdgeBfsOrigins);
    }

    [Fact]
    public async Task EdgeSearch_NodeDistanceUsesDriverRanker()
    {
        var far = new EntityEdge { Uuid = "far-edge", SourceNodeUuid = "far", TargetNodeUuid = "x", Fact = "project", GroupId = "group" };
        var near = new EntityEdge { Uuid = "near-edge", SourceNodeUuid = "near", TargetNodeUuid = "y", Fact = "project", GroupId = "group" };
        var driver = new DriverBackedSearchDriver
        {
            EdgeFulltextHits =
            {
                new SearchHit<EntityEdge>(far, 2),
                new SearchHit<EntityEdge>(near, 1)
            },
            NodeDistanceRanks =
            {
                new SearchRank("near", 1),
                new SearchRank("far", 0)
            }
        };

        var ranked = await SearchEngine.EdgeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "project",
            queryVector: null,
            new[] { "group" },
            new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25 },
                Reranker = EdgeReranker.NodeDistance
            },
            new SearchFilters(),
            limit: 2,
            centerNodeUuid: "center");

        Assert.Equal(1, driver.NodeDistanceRankCalls);
        Assert.Equal(0, driver.EdgeMaterializationCalls);
        Assert.Equal(new[] { "far", "near" }, driver.LastNodeDistanceRankUuids);
        Assert.Equal(new[] { "near-edge", "far-edge" }, ranked.Select(item => item.Item.Uuid));
    }

    [Fact]
    public async Task EpisodeSearch_UsesDriverBackedFulltext()
    {
        var episode = new EpisodicNode { Uuid = "episode", Name = "Episode", Content = "Alice project", GroupId = "group" };
        var driver = new DriverBackedSearchDriver
        {
            EpisodeFulltextHits = { new SearchHit<EpisodicNode>(episode, 5) }
        };

        var ranked = await SearchEngine.EpisodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "alice",
            new[] { "group" },
            new EpisodeSearchConfig(),
            limit: 3);

        Assert.Equal(1, driver.EpisodeFulltextCalls);
        Assert.Equal(0, driver.NodeMaterializationCalls);
        Assert.Equal("episode", ranked[0].Item.Uuid);
        Assert.Equal(6, driver.LastEpisodeFulltextLimit);
    }

    [Fact]
    public async Task SearchAsync_ForwardsSearchFilterToEpisodeDriverFulltext()
    {
        var episode = new EpisodicNode { Uuid = "episode", Name = "Episode", Content = "Alice project", GroupId = "group" };
        var filter = new SearchFilters
        {
            EdgeTypes = new List<string> { "DISTINCT_FILTER" }
        };
        var driver = new DriverBackedSearchDriver
        {
            EpisodeFulltextHits = { new SearchHit<EpisodicNode>(episode, 5) }
        };
        var clients = new GraphitiClients(
            driver,
            new NoOpLlmClient(),
            new HashEmbedder(2),
            new IdentityCrossEncoderClient());

        var results = await SearchEngine.SearchAsync(
            clients,
            "alice",
            new[] { "group" },
            new SearchConfig
            {
                Limit = 3,
                EpisodeConfig = new EpisodeSearchConfig
                {
                    SearchMethods = { EpisodeSearchMethod.Bm25 },
                    Reranker = EpisodeReranker.Rrf
                }
            },
            filter);

        Assert.Same(filter, driver.LastEpisodeSearchFilter);
        Assert.Equal("episode", Assert.Single(results.Episodes).Uuid);
    }

    [Fact]
    public async Task EpisodeSearch_ForwardsSearchFilterToDriverFulltext()
    {
        var episode = new EpisodicNode { Uuid = "episode", Name = "Episode", Content = "Alice project", GroupId = "group" };
        var filter = new SearchFilters
        {
            EdgeTypes = new List<string> { "DISTINCT_FILTER" }
        };
        var driver = new DriverBackedSearchDriver
        {
            EpisodeFulltextHits = { new SearchHit<EpisodicNode>(episode, 5) }
        };

        var ranked = await SearchEngine.EpisodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "alice",
            new[] { "group" },
            new EpisodeSearchConfig
            {
                SearchMethods = { EpisodeSearchMethod.Bm25 },
                Reranker = EpisodeReranker.Rrf
            },
            limit: 3,
            searchFilter: filter);

        Assert.Same(filter, driver.LastEpisodeSearchFilter);
        Assert.Equal("episode", Assert.Single(ranked).Item.Uuid);
    }

    [Fact]
    public async Task EpisodeSearch_CrossEncoderPreservesDuplicateContent()
    {
        var first = new EpisodicNode { Uuid = "episode-first", Name = "First", Content = "same content", GroupId = "group" };
        var second = new EpisodicNode { Uuid = "episode-second", Name = "Second", Content = "same content", GroupId = "group" };
        var driver = new DriverBackedSearchDriver
        {
            EpisodeFulltextHits =
            {
                new SearchHit<EpisodicNode>(first, 2),
                new SearchHit<EpisodicNode>(second, 1)
            }
        };
        var crossEncoder = new RecordingCrossEncoder
        {
            IndexedScores = new List<float> { 0.1f, 0.9f }
        };

        var ranked = await SearchEngine.EpisodeSearchAsync(
            driver,
            crossEncoder,
            "query",
            new[] { "group" },
            new EpisodeSearchConfig
            {
                SearchMethods = { EpisodeSearchMethod.Bm25 },
                Reranker = EpisodeReranker.CrossEncoder
            },
            limit: 2);

        Assert.Equal(new[] { "same content", "same content" }, crossEncoder.LastPassages);
        Assert.Equal(new[] { "episode-second", "episode-first" }, ranked.Select(item => item.Item.Uuid));
    }

    [Fact]
    public async Task CommunitySearch_UsesDriverBackedFulltextAndVectorSearch()
    {
        var textCommunity = new CommunityNode { Uuid = "text", Name = "Planning", GroupId = "group" };
        var vectorCommunity = new CommunityNode { Uuid = "vector", Name = "Roadmap", GroupId = "group" };
        var queryVector = new[] { 1f, 0f };
        var driver = new DriverBackedSearchDriver
        {
            SearchDelay = TimeSpan.FromMilliseconds(50),
            CommunityFulltextHits = { new SearchHit<CommunityNode>(textCommunity, 4) },
            CommunityVectorHits = { new SearchHit<CommunityNode>(vectorCommunity, 0.8f) }
        };

        var ranked = await SearchEngine.CommunitySearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "planning",
            queryVector,
            new[] { "group" },
            new CommunitySearchConfig(),
            limit: 3);

        Assert.Equal(1, driver.CommunityFulltextCalls);
        Assert.Equal(1, driver.CommunityVectorCalls);
        Assert.True(driver.MaxConcurrentSearchCalls > 1);
        Assert.Equal(0, driver.NodeMaterializationCalls);
        Assert.Equal(new[] { "text", "vector" }, ranked.Select(item => item.Item.Uuid));
        Assert.Equal(6, driver.LastCommunityFulltextLimit);
        Assert.Same(queryVector, driver.LastCommunityVectorQueryVector);
        Assert.Equal(new[] { "group" }, driver.LastCommunityVectorGroupIds);
        Assert.Equal(6, driver.LastCommunityVectorLimit);
        Assert.Equal(SearchConfiguration.DefaultMinScore, driver.LastCommunityVectorMinScore, precision: 6);
    }

    [Fact]
    public async Task SearchRetrievalRunner_BfsGuardsSkipDriverCalls()
    {
        var driver = new DriverBackedSearchDriver();

        Assert.Empty(await SearchRetrievalRunner.NodeBfsSearchAsync(
            driver,
            originNodeUuids: null,
            maxDepth: 2,
            groupIds: new[] { "group" },
            new SearchFilters(),
            limit: 10,
            CancellationToken.None));
        Assert.Empty(await SearchRetrievalRunner.NodeBfsSearchAsync(
            driver,
            Array.Empty<string>(),
            maxDepth: 2,
            groupIds: new[] { "group" },
            new SearchFilters(),
            limit: 10,
            CancellationToken.None));
        Assert.Empty(await SearchRetrievalRunner.NodeBfsSearchAsync(
            driver,
            new[] { "origin" },
            maxDepth: 0,
            groupIds: new[] { "group" },
            new SearchFilters(),
            limit: 10,
            CancellationToken.None));
        Assert.Empty(await SearchRetrievalRunner.EdgeBfsSearchAsync(
            driver,
            originNodeUuids: null,
            maxDepth: 2,
            groupIds: new[] { "group" },
            new SearchFilters(),
            limit: 10,
            CancellationToken.None));
        Assert.Empty(await SearchRetrievalRunner.EdgeBfsSearchAsync(
            driver,
            Array.Empty<string>(),
            maxDepth: 2,
            groupIds: new[] { "group" },
            new SearchFilters(),
            limit: 10,
            CancellationToken.None));
        Assert.Empty(await SearchRetrievalRunner.EdgeBfsSearchAsync(
            driver,
            new[] { "origin" },
            maxDepth: 0,
            groupIds: new[] { "group" },
            new SearchFilters(),
            limit: 10,
            CancellationToken.None));

        Assert.Equal(0, driver.NodeBfsCalls);
        Assert.Equal(0, driver.EdgeBfsCalls);
    }

    [Fact]
    public async Task CommunitySearch_CrossEncoderRanksNamesOnly()
    {
        var alpha = new CommunityNode { Uuid = "community-alpha", Name = "Alpha", Summary = "summary mentioning Beta", GroupId = "group" };
        var beta = new CommunityNode { Uuid = "community-beta", Name = "Beta", Summary = "summary mentioning Alpha", GroupId = "group" };
        var driver = new DriverBackedSearchDriver
        {
            CommunityFulltextHits =
            {
                new SearchHit<CommunityNode>(alpha, 2),
                new SearchHit<CommunityNode>(beta, 1)
            }
        };
        var crossEncoder = new RecordingCrossEncoder
        {
            Scores =
            {
                ["Alpha"] = 0.1f,
                ["Beta"] = 0.9f
            }
        };

        var ranked = await SearchEngine.CommunitySearchAsync(
            driver,
            crossEncoder,
            "query",
            queryVector: null,
            new[] { "group" },
            new CommunitySearchConfig
            {
                SearchMethods = { CommunitySearchMethod.Bm25 },
                Reranker = CommunityReranker.CrossEncoder
            },
            limit: 2);

        Assert.Equal(new[] { "Alpha", "Beta" }, crossEncoder.LastPassages);
        Assert.DoesNotContain(crossEncoder.LastPassages, passage => passage.Contains('\n', StringComparison.Ordinal));
        Assert.Equal(new[] { "community-beta", "community-alpha" }, ranked.Select(item => item.Item.Uuid));
    }

    [Fact]
    public async Task CommunitySearch_CrossEncoderPreservesSameNameCandidates()
    {
        var first = new CommunityNode { Uuid = "community-first", Name = "Same", GroupId = "group" };
        var second = new CommunityNode { Uuid = "community-second", Name = "Same", GroupId = "group" };
        var driver = new DriverBackedSearchDriver
        {
            CommunityFulltextHits =
            {
                new SearchHit<CommunityNode>(first, 2),
                new SearchHit<CommunityNode>(second, 1)
            }
        };
        var crossEncoder = new RecordingCrossEncoder
        {
            IndexedScores = new List<float> { 0.1f, 0.9f }
        };

        var ranked = await SearchEngine.CommunitySearchAsync(
            driver,
            crossEncoder,
            "query",
            queryVector: null,
            new[] { "group" },
            new CommunitySearchConfig
            {
                SearchMethods = { CommunitySearchMethod.Bm25 },
                Reranker = CommunityReranker.CrossEncoder
            },
            limit: 2);

        Assert.Equal(new[] { "Same", "Same" }, crossEncoder.LastPassages);
        Assert.Equal(new[] { "community-second", "community-first" }, ranked.Select(item => item.Item.Uuid));
    }

    private sealed class RecordingCrossEncoder : CrossEncoderClient
    {
        public Dictionary<string, float> Scores { get; } = new(StringComparer.Ordinal);
        public List<float>? IndexedScores { get; init; }
        public List<string> LastPassages { get; private set; } = new();

        public override Task<IReadOnlyList<(string Passage, float Score)>> RankAsync(
            string query,
            IReadOnlyList<string> passages,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastPassages = passages.ToList();
            IReadOnlyList<(string Passage, float Score)> results = passages
                .Select(passage => (Passage: passage, Score: Scores.GetValueOrDefault(passage, SearchUtilities.TextScore(query, passage))))
                .OrderByDescending(item => item.Score)
                .ToList();
            return Task.FromResult(results);
        }

        public override Task<IReadOnlyList<CrossEncoderRank>> RankIndexedAsync(
            string query,
            IReadOnlyList<string> passages,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastPassages = passages.ToList();
            IReadOnlyList<CrossEncoderRank> results = passages
                .Select((passage, index) => new CrossEncoderRank(
                    index,
                    passage,
                    ScoreFor(query, passage, index)))
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Index)
                .ToList();
            return Task.FromResult(results);
        }

        private float ScoreFor(string query, string passage, int index)
        {
            if (IndexedScores is not null && index < IndexedScores.Count)
            {
                return IndexedScores[index];
            }

            return Scores.GetValueOrDefault(passage, SearchUtilities.TextScore(query, passage));
        }
    }

    private sealed class DriverBackedSearchDriver : GraphDriverBase, ISearchGraphDriver
    {
        public DriverBackedSearchDriver() : base(GraphProvider.InMemory)
        {
        }

        public List<SearchHit<EntityNode>> NodeFulltextHits { get; } = new();
        public List<SearchHit<EntityNode>> NodeVectorHits { get; } = new();
        public List<SearchHit<EntityNode>> NodeBfsHits { get; } = new();
        public List<SearchHit<EntityEdge>> EdgeFulltextHits { get; } = new();
        public List<SearchHit<EntityEdge>> EdgeVectorHits { get; } = new();
        public List<SearchHit<EntityEdge>> EdgeBfsHits { get; } = new();
        public List<SearchHit<EpisodicNode>> EpisodeFulltextHits { get; } = new();
        public List<SearchHit<CommunityNode>> CommunityFulltextHits { get; } = new();
        public List<SearchHit<CommunityNode>> CommunityVectorHits { get; } = new();
        public List<SearchRank> NodeDistanceRanks { get; } = new();
        public List<SearchRank> NodeEpisodeMentionRanks { get; } = new();
        public TimeSpan SearchDelay { get; set; }
        public int MaxConcurrentSearchCalls => _maxConcurrentSearchCalls;
        public Exception? NodeFulltextException { get; set; }
        public bool NodeVectorWaitsForCancellation { get; set; }
        public TaskCompletionSource NodeVectorCancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool EdgeFulltextWaitsForCancellation { get; set; }
        public TaskCompletionSource EdgeFulltextCancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? EdgeVectorException { get; set; }
        public int NodeFulltextCalls { get; private set; }
        public int NodeVectorCalls { get; private set; }
        public int NodeBfsCalls { get; private set; }
        public int EdgeFulltextCalls { get; private set; }
        public int EdgeVectorCalls { get; private set; }
        public int EdgeBfsCalls { get; private set; }
        public int EpisodeFulltextCalls { get; private set; }
        public int CommunityFulltextCalls { get; private set; }
        public int CommunityVectorCalls { get; private set; }
        public int NodeDistanceRankCalls { get; private set; }
        public int NodeEpisodeMentionRankCalls { get; private set; }
        public int NodeMaterializationCalls { get; private set; }
        public int EdgeMaterializationCalls { get; private set; }
        public IReadOnlyList<string>? LastNodeFulltextGroupIds { get; private set; }
        public IReadOnlyList<string>? LastEdgeFulltextGroupIds { get; private set; }
        public IReadOnlyList<float>? LastNodeVectorQueryVector { get; private set; }
        public IReadOnlyList<float>? LastEdgeVectorQueryVector { get; private set; }
        public IReadOnlyList<float>? LastCommunityVectorQueryVector { get; private set; }
        public IReadOnlyList<string>? LastNodeVectorGroupIds { get; private set; }
        public IReadOnlyList<string>? LastEdgeVectorGroupIds { get; private set; }
        public IReadOnlyList<string>? LastCommunityVectorGroupIds { get; private set; }
        public IReadOnlyList<string>? LastNodeBfsOrigins { get; private set; }
        public IReadOnlyList<string>? LastEdgeBfsOrigins { get; private set; }
        public IReadOnlyList<string>? LastNodeBfsGroupIds { get; private set; }
        public IReadOnlyList<string>? LastEdgeBfsGroupIds { get; private set; }
        public IReadOnlyList<string>? LastNodeDistanceRankUuids { get; private set; }
        public IReadOnlyList<string>? LastNodeEpisodeMentionRankUuids { get; private set; }
        public SearchFilters? LastNodeVectorSearchFilter { get; private set; }
        public SearchFilters? LastEdgeVectorSearchFilter { get; private set; }
        public SearchFilters? LastNodeBfsSearchFilter { get; private set; }
        public SearchFilters? LastEdgeBfsSearchFilter { get; private set; }
        public SearchFilters? LastEpisodeSearchFilter { get; private set; }
        public int LastNodeFulltextLimit { get; private set; }
        public int LastEdgeFulltextLimit { get; private set; }
        public int LastNodeVectorLimit { get; private set; }
        public int LastEdgeVectorLimit { get; private set; }
        public int LastCommunityVectorLimit { get; private set; }
        public int LastNodeBfsLimit { get; private set; }
        public int LastEdgeBfsLimit { get; private set; }
        public int LastNodeBfsMaxDepth { get; private set; }
        public int LastEdgeBfsMaxDepth { get; private set; }
        public int LastEpisodeFulltextLimit { get; private set; }
        public int LastCommunityFulltextLimit { get; private set; }
        public float LastNodeVectorMinScore { get; private set; }
        public float LastEdgeVectorMinScore { get; private set; }
        public float LastCommunityVectorMinScore { get; private set; }
        private int _activeSearchCalls;
        private int _maxConcurrentSearchCalls;

        public Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesFulltextAsync(
            string query,
            SearchFilters searchFilter,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default)
        {
            NodeFulltextCalls++;
            LastNodeFulltextGroupIds = groupIds;
            LastNodeFulltextLimit = limit;
            if (NodeFulltextException is not null)
            {
                return Task.FromException<IReadOnlyList<SearchHit<EntityNode>>>(NodeFulltextException);
            }

            return CompleteSearchCallAsync(NodeFulltextHits, cancellationToken);
        }

        public Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesByEmbeddingAsync(
            IReadOnlyList<float> searchVector,
            SearchFilters searchFilter,
            IReadOnlyList<string>? groupIds,
            int limit,
            float minScore,
            CancellationToken cancellationToken = default)
        {
            NodeVectorCalls++;
            LastNodeVectorQueryVector = searchVector;
            LastNodeVectorSearchFilter = searchFilter;
            LastNodeVectorGroupIds = groupIds;
            LastNodeVectorLimit = limit;
            LastNodeVectorMinScore = minScore;
            if (NodeVectorWaitsForCancellation)
            {
                return WaitForSearchCancellationAsync<SearchHit<EntityNode>>(
                    NodeVectorCancellationObserved,
                    cancellationToken);
            }

            return CompleteSearchCallAsync(NodeVectorHits, cancellationToken);
        }

        public Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesFulltextAsync(
            string query,
            SearchFilters searchFilter,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default)
        {
            EdgeFulltextCalls++;
            LastEdgeFulltextGroupIds = groupIds;
            LastEdgeFulltextLimit = limit;
            if (EdgeFulltextWaitsForCancellation)
            {
                return WaitForSearchCancellationAsync<SearchHit<EntityEdge>>(
                    EdgeFulltextCancellationObserved,
                    cancellationToken);
            }

            return CompleteSearchCallAsync(EdgeFulltextHits, cancellationToken);
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
            LastEdgeVectorQueryVector = searchVector;
            LastEdgeVectorSearchFilter = searchFilter;
            LastEdgeVectorGroupIds = groupIds;
            LastEdgeVectorLimit = limit;
            LastEdgeVectorMinScore = minScore;
            if (EdgeVectorException is not null)
            {
                return Task.FromException<IReadOnlyList<SearchHit<EntityEdge>>>(EdgeVectorException);
            }

            return CompleteSearchCallAsync(EdgeVectorHits, cancellationToken);
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
            LastNodeBfsOrigins = originNodeUuids;
            LastNodeBfsSearchFilter = searchFilter;
            LastNodeBfsMaxDepth = maxDepth;
            LastNodeBfsGroupIds = groupIds;
            LastNodeBfsLimit = limit;
            return CompleteSearchCallAsync(NodeBfsHits, cancellationToken);
        }

        public Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesBfsAsync(
            IReadOnlyList<string>? originNodeUuids,
            SearchFilters searchFilter,
            int maxDepth,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default)
        {
            EdgeBfsCalls++;
            LastEdgeBfsOrigins = originNodeUuids;
            LastEdgeBfsSearchFilter = searchFilter;
            LastEdgeBfsMaxDepth = maxDepth;
            LastEdgeBfsGroupIds = groupIds;
            LastEdgeBfsLimit = limit;
            return CompleteSearchCallAsync(EdgeBfsHits, cancellationToken);
        }

        public Task<IReadOnlyList<SearchHit<EpisodicNode>>> SearchEpisodesFulltextAsync(
            string query,
            SearchFilters searchFilter,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default)
        {
            EpisodeFulltextCalls++;
            LastEpisodeSearchFilter = searchFilter;
            LastEpisodeFulltextLimit = limit;
            return CompleteSearchCallAsync(EpisodeFulltextHits, cancellationToken);
        }

        public Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesFulltextAsync(
            string query,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default)
        {
            CommunityFulltextCalls++;
            LastCommunityFulltextLimit = limit;
            return CompleteSearchCallAsync(CommunityFulltextHits, cancellationToken);
        }

        public Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesByEmbeddingAsync(
            IReadOnlyList<float> searchVector,
            IReadOnlyList<string>? groupIds,
            int limit,
            float minScore,
            CancellationToken cancellationToken = default)
        {
            CommunityVectorCalls++;
            LastCommunityVectorQueryVector = searchVector;
            LastCommunityVectorGroupIds = groupIds;
            LastCommunityVectorLimit = limit;
            LastCommunityVectorMinScore = minScore;
            return CompleteSearchCallAsync(CommunityVectorHits, cancellationToken);
        }

        public Task<IReadOnlyList<SearchRank>> RankNodeDistanceAsync(
            IReadOnlyList<string> nodeUuids,
            string centerNodeUuid,
            float minScore = 0,
            CancellationToken cancellationToken = default)
        {
            NodeDistanceRankCalls++;
            LastNodeDistanceRankUuids = nodeUuids;
            return CompleteSearchCallAsync(NodeDistanceRanks, cancellationToken);
        }

        public Task<IReadOnlyList<SearchRank>> RankNodeEpisodeMentionsAsync(
            IReadOnlyList<string> nodeUuids,
            float minScore = 0,
            CancellationToken cancellationToken = default)
        {
            NodeEpisodeMentionRankCalls++;
            LastNodeEpisodeMentionRankUuids = nodeUuids;
            return CompleteSearchCallAsync(NodeEpisodeMentionRanks, cancellationToken);
        }

        private async Task<IReadOnlyList<T>> CompleteSearchCallAsync<T>(
            IReadOnlyList<T> results,
            CancellationToken cancellationToken)
        {
            var active = System.Threading.Interlocked.Increment(ref _activeSearchCalls);
            UpdateMaxConcurrentSearchCalls(active);
            try
            {
                if (SearchDelay > TimeSpan.Zero)
                {
                    await Task.Delay(SearchDelay, cancellationToken).ConfigureAwait(false);
                }

                return results;
            }
            finally
            {
                System.Threading.Interlocked.Decrement(ref _activeSearchCalls);
            }
        }

        private async Task<IReadOnlyList<T>> WaitForSearchCancellationAsync<T>(
            TaskCompletionSource cancellationObserved,
            CancellationToken cancellationToken)
        {
            var active = System.Threading.Interlocked.Increment(ref _activeSearchCalls);
            UpdateMaxConcurrentSearchCalls(active);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return [];
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellationObserved.TrySetResult();
                throw;
            }
            finally
            {
                System.Threading.Interlocked.Decrement(ref _activeSearchCalls);
            }
        }

        private void UpdateMaxConcurrentSearchCalls(int active)
        {
            while (true)
            {
                var observed = _maxConcurrentSearchCalls;
                if (active <= observed)
                {
                    return;
                }

                if (System.Threading.Interlocked.CompareExchange(ref _maxConcurrentSearchCalls, active, observed) == observed)
                {
                    return;
                }
            }
        }

        public override Task<IReadOnlyList<TNode>> GetNodesByGroupIdsAsync<TNode>(
            IEnumerable<string> groupIds,
            int? limit = null,
            string? uuidCursor = null,
            bool withEmbeddings = false,
            CancellationToken cancellationToken = default)
        {
            NodeMaterializationCalls++;
            throw new InvalidOperationException("Node materialization fallback should not be used.");
        }

        public override Task<IReadOnlyList<T>> GetEdgesByGroupIdsAsync<T>(
            IEnumerable<string> groupIds,
            int? limit = null,
            string? uuidCursor = null,
            bool withEmbeddings = false,
            CancellationToken cancellationToken = default)
        {
            EdgeMaterializationCalls++;
            throw new InvalidOperationException("Edge materialization fallback should not be used.");
        }

        public override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override IGraphDriver Clone(string database) => this;
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
    }
}
