using Graphiti.Core;

namespace Graphiti.Core.Tests.Search;

public class SearchGraphTraversalTests
{
    [Fact]
    public async Task EdgeBfsSearch_FollowsEpisodeMentionsThenEntityEdges()
    {
        var (driver, episode, a, _, _, e1, e2) = await CreateChainAsync();

        var depthOne = await SearchEngine.EdgeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "ignored",
            queryVector: null,
            groupIds: new[] { "group" },
            new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bfs },
                Reranker = EdgeReranker.Rrf,
                BfsMaxDepth = 1
            },
            EdgeFilter(),
            limit: 10,
            bfsOriginNodeUuids: new[] { episode.Uuid });

        var depthTwo = await SearchEngine.EdgeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "ignored",
            queryVector: null,
            groupIds: new[] { "group" },
            new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bfs },
                Reranker = EdgeReranker.Rrf,
                BfsMaxDepth = 2
            },
            EdgeFilter(),
            limit: 10,
            bfsOriginNodeUuids: new[] { episode.Uuid });

        var fromEntity = await SearchEngine.EdgeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "ignored",
            queryVector: null,
            groupIds: new[] { "group" },
            new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bfs },
                Reranker = EdgeReranker.Rrf,
                BfsMaxDepth = 2
            },
            EdgeFilter(),
            limit: 10,
            bfsOriginNodeUuids: new[] { a.Uuid });

        Assert.Empty(depthOne);
        Assert.Equal(new[] { e1.Uuid }, depthTwo.Select(item => item.Item.Uuid));
        Assert.Equal(new[] { e1.Uuid, e2.Uuid }, fromEntity.Select(item => item.Item.Uuid));
    }

    [Fact]
    public async Task NodeBfsSearch_FollowsEpisodeMentionsAndEntityEdges()
    {
        var (driver, episode, a, b, _, _, _) = await CreateChainAsync();

        var depthOne = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "ignored",
            queryVector: null,
            groupIds: new[] { "group" },
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bfs },
                Reranker = NodeReranker.Rrf,
                BfsMaxDepth = 1
            },
            NodeFilter(),
            limit: 10,
            bfsOriginNodeUuids: new[] { episode.Uuid });

        var fromEntity = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "ignored",
            queryVector: null,
            groupIds: new[] { "group" },
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bfs },
                Reranker = NodeReranker.Rrf,
                BfsMaxDepth = 1
            },
            NodeFilter(),
            limit: 10,
            bfsOriginNodeUuids: new[] { a.Uuid });

        Assert.Equal(new[] { a.Uuid }, depthOne.Select(item => item.Item.Uuid));
        Assert.Equal(new[] { b.Uuid }, fromEntity.Select(item => item.Item.Uuid));
    }

    [Fact]
    public async Task MaterializedNodeBfs_KeepsShortestFirstTraversalHit()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var origin = Entity("origin", now);
        var target = Entity("target", now);
        var middle = Entity("middle", now);

        foreach (var node in new[] { origin, target, middle })
        {
            await driver.SaveNodeAsync(node);
        }

        await driver.SaveEdgeAsync(Edge(origin, middle, "origin middle", now));
        await driver.SaveEdgeAsync(Edge(origin, target, "origin target", now));
        await driver.SaveEdgeAsync(Edge(middle, target, "middle target", now));

        var searchDriver = new MaterializingSearchGraphDriver(driver);

        var hits = await searchDriver.SearchEntityNodesBfsAsync(
            new[] { origin.Uuid },
            NodeFilter(),
            maxDepth: 2,
            groupIds: new[] { "group" },
            limit: 10);

        var targetHit = Assert.Single(hits, hit => hit.Item.Uuid == target.Uuid);
        Assert.Equal(1f, targetHit.Score);
    }

    [Fact]
    public async Task InMemoryNodeBfs_KeepsShortestFirstTraversalHit()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var origin = Entity("origin", now);
        var target = Entity("target", now);
        var middle = Entity("middle", now);

        foreach (var node in new[] { origin, target, middle })
        {
            await driver.SaveNodeAsync(node);
        }

        await driver.SaveEdgeAsync(Edge(origin, middle, "origin middle", now));
        await driver.SaveEdgeAsync(Edge(origin, target, "origin target", now));
        await driver.SaveEdgeAsync(Edge(middle, target, "middle target", now));

        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var hits = await searchDriver.SearchEntityNodesBfsAsync(
            new[] { origin.Uuid },
            NodeFilter(),
            maxDepth: 2,
            groupIds: new[] { "group" },
            limit: 10);

        var targetHit = Assert.Single(hits, hit => hit.Item.Uuid == target.Uuid);
        Assert.Equal(1f, targetHit.Score);
    }

    [Fact]
    public async Task MaterializedSearch_FiltersFulltextAndVectorCandidatesBeforeRanking()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var allowed = Entity("allowed", now);
        allowed.Uuid = "allowed-node";
        allowed.Name = "Alpha";
        allowed.NameEmbedding = new List<float> { 1f, 0f };
        allowed.Labels.Add("Person");
        var target = Entity("target", now);
        target.Uuid = "target-node";
        target.NameEmbedding = new List<float> { 0f, 1f };
        target.Labels.Add("Person");
        var blockedLabel = Entity("blocked-label", now);
        blockedLabel.Uuid = "blocked-label-node";
        blockedLabel.Name = "Alpha";
        blockedLabel.NameEmbedding = new List<float> { 1f, 0f };
        blockedLabel.Labels.Add("Project");
        var otherGroup = Entity("other-group", now);
        otherGroup.Uuid = "other-group-node";
        otherGroup.Name = "Alpha";
        otherGroup.GroupId = "other";
        otherGroup.NameEmbedding = new List<float> { 1f, 0f };
        otherGroup.Labels.Add("Person");

        foreach (var node in new[] { allowed, target, blockedLabel, otherGroup })
        {
            await driver.SaveNodeAsync(node);
        }

        var allowedEdge = Edge(allowed, target, "alpha relation", now);
        allowedEdge.Uuid = "allowed-edge";
        allowedEdge.FactEmbedding = new List<float> { 1f, 0f };
        var blockedEndpointEdge = Edge(allowed, blockedLabel, "alpha relation", now);
        blockedEndpointEdge.Uuid = "blocked-endpoint-edge";
        blockedEndpointEdge.FactEmbedding = new List<float> { 1f, 0f };
        var blockedTypeEdge = Edge(allowed, target, "alpha relation", now);
        blockedTypeEdge.Uuid = "blocked-type-edge";
        blockedTypeEdge.Name = "IGNORES";
        blockedTypeEdge.FactEmbedding = new List<float> { 1f, 0f };
        foreach (var edge in new[] { allowedEdge, blockedEndpointEdge, blockedTypeEdge })
        {
            await driver.SaveEdgeAsync(edge);
        }

        var searchDriver = new MaterializingSearchGraphDriver(driver);
        var nodeFilter = new SearchFilters { NodeLabels = new List<string> { "Person" } };
        var edgeFilter = new SearchFilters
        {
            NodeLabels = new List<string> { "Person" },
            EdgeTypes = new List<string> { "RELATES_TO" }
        };

        var nodeFulltext = await searchDriver.SearchEntityNodesFulltextAsync(
            "alpha",
            nodeFilter,
            new[] { "group" },
            limit: 10);
        var nodeVector = await searchDriver.SearchEntityNodesByEmbeddingAsync(
            new[] { 1f, 0f },
            nodeFilter,
            new[] { "group" },
            limit: 10,
            minScore: 0.5f);
        var edgeFulltext = await searchDriver.SearchEntityEdgesFulltextAsync(
            "alpha",
            edgeFilter,
            new[] { "group" },
            limit: 10);
        var edgeVector = await searchDriver.SearchEntityEdgesByEmbeddingAsync(
            new[] { 1f, 0f },
            edgeFilter,
            new[] { "group" },
            limit: 10,
            minScore: 0.5f,
            sourceNodeUuid: allowed.Uuid,
            targetNodeUuid: target.Uuid);

        Assert.Equal(allowed.Uuid, Assert.Single(nodeFulltext).Item.Uuid);
        Assert.Equal(allowed.Uuid, Assert.Single(nodeVector).Item.Uuid);
        Assert.Equal(allowedEdge.Uuid, Assert.Single(edgeFulltext).Item.Uuid);
        Assert.Equal(allowedEdge.Uuid, Assert.Single(edgeVector).Item.Uuid);
    }

    [Fact]
    public async Task MaterializedEdgeEmbeddingSearch_IgnoresEndpointFiltersWhenGroupScopeIsNull()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var source = Entity("source", now);
        var target = Entity("target", now);
        var otherTarget = Entity("other-target", now);
        foreach (var node in new[] { source, target, otherTarget })
        {
            await driver.SaveNodeAsync(node);
        }

        var edge = Edge(source, target, "alpha relation", now);
        edge.Uuid = "edge";
        edge.FactEmbedding = new List<float> { 1f, 0f };
        var otherEdge = Edge(source, otherTarget, "alpha relation", now);
        otherEdge.Uuid = "other-edge";
        otherEdge.FactEmbedding = new List<float> { 1f, 0f };
        await driver.SaveEdgeAsync(edge);
        await driver.SaveEdgeAsync(otherEdge);
        var searchDriver = new MaterializingSearchGraphDriver(driver);

        var hits = await searchDriver.SearchEntityEdgesByEmbeddingAsync(
            new[] { 1f, 0f },
            new SearchFilters { EdgeTypes = new List<string> { "RELATES_TO" } },
            groupIds: null,
            limit: 10,
            minScore: 0.5f,
            sourceNodeUuid: source.Uuid,
            targetNodeUuid: target.Uuid);

        Assert.Equal(
            new[] { edge.Uuid, otherEdge.Uuid }.Order(StringComparer.Ordinal),
            hits.Select(hit => hit.Item.Uuid).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task MaterializedNodeDistanceRanker_DeduplicatesInputsAndKeepsStableTies()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var center = Entity("center", now);
        var near = Entity("near", now);
        var farA = Entity("far-a", now);
        var farB = Entity("far-b", now);
        await driver.SaveNodeAsync(center);
        await driver.SaveNodeAsync(near);
        await driver.SaveEdgeAsync(Edge(center, near, "center near", now));
        var searchDriver = new MaterializingSearchGraphDriver(driver);

        var ranks = await searchDriver.RankNodeDistanceAsync(
            new[] { farB.Uuid, near.Uuid, farA.Uuid, near.Uuid, center.Uuid },
            center.Uuid);

        Assert.Equal(new[] { center.Uuid, near.Uuid, farB.Uuid, farA.Uuid }, ranks.Select(rank => rank.Uuid));
        Assert.Equal(new[] { 10f, 1f, 0f, 0f }, ranks.Select(rank => rank.Score));
    }

    [Fact]
    public async Task InMemoryNodeDistanceRanker_DeduplicatesInputsAndKeepsStableTies()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var center = Entity("center", now);
        var near = Entity("near", now);
        var farA = Entity("far-a", now);
        var farB = Entity("far-b", now);
        await driver.SaveNodeAsync(center);
        await driver.SaveNodeAsync(near);
        await driver.SaveEdgeAsync(Edge(center, near, "center near", now));
        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);

        var ranks = await searchDriver.RankNodeDistanceAsync(
            new[] { farB.Uuid, near.Uuid, farA.Uuid, near.Uuid, center.Uuid },
            center.Uuid);

        Assert.Equal(new[] { center.Uuid, near.Uuid, farB.Uuid, farA.Uuid }, ranks.Select(rank => rank.Uuid));
        Assert.Equal(new[] { 10f, 1f, 0f, 0f }, ranks.Select(rank => rank.Score));
    }

    [Fact]
    public async Task MaterializedEpisodeMentionsRanker_CountsMentionsAndKeepsStableTies()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var once = Entity("once", now);
        var twice = Entity("twice", now);
        var unmentionedA = Entity("unmentioned-a", now);
        var unmentionedB = Entity("unmentioned-b", now);
        await driver.SaveNodeAsync(twice);
        await driver.SaveNodeAsync(once);
        await driver.SaveNodeAsync(Episode("episode-1", now));
        await driver.SaveNodeAsync(Episode("episode-2", now));
        await driver.SaveNodeAsync(Episode("episode-3", now));
        await driver.SaveEdgeAsync(EpisodicMention("episode-1", twice.Uuid, now));
        await driver.SaveEdgeAsync(EpisodicMention("episode-2", once.Uuid, now));
        await driver.SaveEdgeAsync(EpisodicMention("episode-3", twice.Uuid, now));
        var searchDriver = new MaterializingSearchGraphDriver(driver);

        var ranks = await searchDriver.RankNodeEpisodeMentionsAsync(
            new[] { unmentionedA.Uuid, twice.Uuid, once.Uuid, unmentionedB.Uuid, twice.Uuid });

        Assert.Equal(
            new[] { once.Uuid, twice.Uuid, unmentionedA.Uuid, unmentionedB.Uuid },
            ranks.Select(rank => rank.Uuid));
        Assert.Equal(
            new[] { 1f, 2f, float.PositiveInfinity, float.PositiveInfinity },
            ranks.Select(rank => rank.Score));
    }

    [Fact]
    public async Task InMemoryEpisodeMentionsRanker_CountsMentionsAndKeepsStableTies()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var once = Entity("once", now);
        var twice = Entity("twice", now);
        var unmentionedA = Entity("unmentioned-a", now);
        var unmentionedB = Entity("unmentioned-b", now);
        await driver.SaveNodeAsync(twice);
        await driver.SaveNodeAsync(once);
        await driver.SaveNodeAsync(Episode("episode-1", now));
        await driver.SaveNodeAsync(Episode("episode-2", now));
        await driver.SaveNodeAsync(Episode("episode-3", now));
        await driver.SaveEdgeAsync(EpisodicMention("episode-1", twice.Uuid, now));
        await driver.SaveEdgeAsync(EpisodicMention("episode-2", once.Uuid, now));
        await driver.SaveEdgeAsync(EpisodicMention("episode-3", twice.Uuid, now));
        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);

        var ranks = await searchDriver.RankNodeEpisodeMentionsAsync(
            new[] { unmentionedA.Uuid, twice.Uuid, once.Uuid, unmentionedB.Uuid, twice.Uuid });

        Assert.Equal(
            new[] { once.Uuid, twice.Uuid, unmentionedA.Uuid, unmentionedB.Uuid },
            ranks.Select(rank => rank.Uuid));
        Assert.Equal(
            new[] { 1f, 2f, float.PositiveInfinity, float.PositiveInfinity },
            ranks.Select(rank => rank.Score));
    }

    [Fact]
    public async Task NodeDistanceReranker_PrioritizesCenterNeighbors()
    {
        var (driver, _, a, b, c, _, _) = await CreateChainAsync();

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "match",
            queryVector: null,
            groupIds: new[] { "group" },
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25 },
                Reranker = NodeReranker.NodeDistance
            },
            NodeFilter(),
            limit: 10,
            centerNodeUuid: a.Uuid);

        Assert.Equal(new[] { b.Uuid, c.Uuid }, ranked.Where(item => item.Item.Uuid != a.Uuid).Select(item => item.Item.Uuid).Take(2));
        Assert.Equal(1, ranked.First(item => item.Item.Uuid == b.Uuid).Score);
        Assert.Equal(0, ranked.First(item => item.Item.Uuid == c.Uuid).Score);
    }

    [Fact]
    public async Task EpisodeMentionsReranker_PrioritizesMentionedNodes()
    {
        var (driver, _, a, b, _, _, _) = await CreateChainAsync();

        var ranked = await SearchEngine.NodeSearchAsync(
            driver,
            new IdentityCrossEncoderClient(),
            "match",
            queryVector: null,
            groupIds: new[] { "group" },
            new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25 },
                Reranker = NodeReranker.EpisodeMentions
            },
            NodeFilter(),
            limit: 10);

        Assert.Equal(a.Uuid, ranked[0].Item.Uuid);
        Assert.Equal(1, ranked[0].Score);
        Assert.Contains(b.Uuid, ranked.Skip(1).Select(item => item.Item.Uuid));
        Assert.All(ranked.Skip(1), item => Assert.True(float.IsPositiveInfinity(item.Score)));
    }

    [Fact]
    public async Task GraphitiCenteredSearch_UsesNodeDistanceReranker()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var center = Entity("center", now);
        var near = Entity("near", now);
        var far = Entity("far", now);
        var other = Entity("other", now);
        var nearEdge = Edge(center, near, "project near", now);
        var farEdge = Edge(far, other, "project far", now);

        foreach (var node in new[] { center, near, far, other })
        {
            await driver.SaveNodeAsync(node);
        }

        await driver.SaveEdgeAsync(farEdge);
        await driver.SaveEdgeAsync(nearEdge);

        var edges = await graphiti.SearchAsync(
            "project",
            centerNodeUuid: center.Uuid,
            groupIds: new[] { "group" },
            numResults: 2);

        Assert.Equal(new[] { nearEdge.Uuid, farEdge.Uuid }, edges.Select(edge => edge.Uuid));
    }

    private static async Task<(
        InMemoryGraphDriver Driver,
        EpisodicNode Episode,
        EntityNode A,
        EntityNode B,
        EntityNode C,
        EntityEdge E1,
        EntityEdge E2)> CreateChainAsync()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var episode = new EpisodicNode
        {
            Name = "episode",
            GroupId = "group",
            Source = EpisodeType.Message,
            SourceDescription = "message",
            Content = "content",
            CreatedAt = now,
            ValidAt = now
        };
        var a = Entity("a", now);
        var b = Entity("b", now);
        var c = Entity("c", now);
        var e1 = Edge(a, b, "match a b", now);
        var e2 = Edge(b, c, "match b c", now);
        var mention = new EpisodicEdge
        {
            SourceNodeUuid = episode.Uuid,
            TargetNodeUuid = a.Uuid,
            GroupId = "group",
            CreatedAt = now
        };

        foreach (var node in new Node[] { episode, a, b, c })
        {
            await driver.SaveNodeAsync(node);
        }

        await driver.SaveEdgeAsync(e1);
        await driver.SaveEdgeAsync(e2);
        await driver.SaveEdgeAsync(mention);

        return (driver, episode, a, b, c, e1, e2);
    }

    private static EntityNode Entity(string name, DateTime now) =>
        new()
        {
            Name = name,
            Summary = "match",
            GroupId = "group",
            Labels = { "Entity" },
            CreatedAt = now
        };

    private static EpisodicNode Episode(string uuid, DateTime now) =>
        new()
        {
            Uuid = uuid,
            Name = uuid,
            GroupId = "group",
            CreatedAt = now,
            ValidAt = now
        };

    private static EntityEdge Edge(EntityNode source, EntityNode target, string fact, DateTime now) =>
        new()
        {
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "group",
            CreatedAt = now,
            Name = "RELATES_TO",
            Fact = fact,
            Episodes = { "episode" }
        };

    private static EpisodicEdge EpisodicMention(string sourceNodeUuid, string targetNodeUuid, DateTime now) =>
        new()
        {
            SourceNodeUuid = sourceNodeUuid,
            TargetNodeUuid = targetNodeUuid,
            GroupId = "group",
            CreatedAt = now
        };

    private static SearchFilters EdgeFilter() =>
        new()
        {
            EdgeTypes = new List<string> { "RELATES_TO" }
        };

    private static SearchFilters NodeFilter() =>
        new()
        {
            NodeLabels = new List<string> { "Entity" }
        };
}
