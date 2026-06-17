using Graphiti.Core;

namespace Graphiti.Core.Tests.Drivers;

public sealed class InMemoryGraphDriverReadTests
{
    [Fact]
    public async Task UuidLookups_DeduplicateInInputOrderAndReturnClones()
    {
        var driver = new InMemoryGraphDriver();
        var alice = Entity("alice", "Alice", "tenant");
        var bob = Entity("bob", "Bob", "tenant");
        var other = Entity("other", "Other", "other");
        var episode = Episode("episode", new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        foreach (var node in new Node[] { alice, bob, other, episode })
        {
            await driver.SaveNodeAsync(node);
        }

        await driver.SaveEdgeAsync(Relates("edge-a", alice, bob, "alice to bob"));
        await driver.SaveEdgeAsync(Relates("edge-b", bob, alice, "bob to alice"));
        await driver.SaveEdgeAsync(Member("membership", Community("community", "Community"), alice));

        var nodes = await driver.GetNodesByUuidsAsync<EntityNode>(
            new[] { "bob", "missing", "episode", "alice", "bob", "other" },
            groupId: "tenant");
        var edges = await driver.GetEdgesByUuidsAsync<EntityEdge>(
            new[] { "edge-b", "missing", "membership", "edge-a", "edge-b" });

        Assert.Equal(new[] { "bob", "alice" }, nodes.Select(node => node.Uuid));
        Assert.Equal(new[] { "edge-b", "edge-a" }, edges.Select(edge => edge.Uuid));

        nodes[0].Name = "mutated";
        edges[0].Fact = "mutated";

        Assert.Equal("Bob", (await driver.GetNodeByUuidAsync<EntityNode>("bob")).Name);
        Assert.Equal("bob to alice", (await driver.GetEdgeByUuidAsync<EntityEdge>("edge-b")).Fact);
    }

    [Fact]
    public async Task UuidLookups_RejectNullInputs()
    {
        var driver = new InMemoryGraphDriver();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            driver.GetNodesByUuidsAsync<EntityNode>(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            driver.GetEdgesByUuidsAsync<EntityEdge>(null!));
    }

    [Fact]
    public async Task UuidLookups_ObserveCancellationWhileEnumeratingLazyInputs()
    {
        var driver = new InMemoryGraphDriver();
        using var nodeCancellation = new CancellationTokenSource();
        using var edgeCancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await driver.GetNodesByUuidsAsync<EntityNode>(
                CancelAfterFirst("node", nodeCancellation),
                cancellationToken: nodeCancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await driver.GetEdgesByUuidsAsync<EntityEdge>(
                CancelAfterFirst("edge", edgeCancellation),
                cancellationToken: edgeCancellation.Token));
    }

    [Fact]
    public async Task GroupLookups_FilterSortPageProjectAndReturnClones()
    {
        var driver = new InMemoryGraphDriver();
        var nodeA = Entity("node-a", "A", "group-b");
        var nodeB = Entity("node-b", "B", "group-a");
        var nodeC = Entity("node-c", "C", "group-a");
        var hidden = Entity("node-hidden", "Hidden", string.Empty);
        var episode = Episode("episode", new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        episode.GroupId = "episode-group";
        nodeA.NameEmbedding = new List<float> { 1f, 0f };
        nodeB.NameEmbedding = new List<float> { 0f, 1f };
        nodeC.NameEmbedding = new List<float> { 1f, 1f };
        foreach (var node in new Node[] { nodeA, nodeB, nodeC, hidden, episode })
        {
            await driver.SaveNodeAsync(node);
        }

        var communityZ = Community("community-z", "Community Z", "community-z");
        var communityA = Community("community-a", "Community A", "community-a");
        await driver.SaveNodeAsync(communityZ);
        await driver.SaveNodeAsync(communityA);

        await driver.SaveEdgeAsync(Relates("edge-a", nodeA, nodeB, "first", "group-a", [1f, 0f]));
        await driver.SaveEdgeAsync(Relates("edge-b", nodeB, nodeC, "second", "group-a", [0f, 1f]));
        await driver.SaveEdgeAsync(Relates("edge-c", nodeC, nodeA, "third", "group-a", [1f, 1f]));
        await driver.SaveEdgeAsync(Relates("edge-other", nodeA, nodeC, "other", "group-b", [0.5f, 0.5f]));

        Assert.Equal(new[] { string.Empty, "group-a", "group-b" }, await driver.GetEntityGroupIdsAsync());
        Assert.Equal(new[] { "community-a", "community-z" }, await driver.GetCommunityGroupIdsAsync());

        var nodes = await driver.GetNodesByGroupIdsAsync<EntityNode>(
            new[] { "group-a", "group-b", "group-a" },
            limit: 2,
            uuidCursor: "node-d");
        var nodesWithEmbeddings = await driver.GetNodesByGroupIdsAsync<EntityNode>(
            new[] { "group-a" },
            limit: 1,
            uuidCursor: "node-d",
            withEmbeddings: true);
        var edges = await driver.GetEdgesByGroupIdsAsync<EntityEdge>(
            new[] { "group-a", "group-a" },
            limit: 2,
            uuidCursor: "edge-d");
        var edgesWithEmbeddings = await driver.GetEdgesByGroupIdsAsync<EntityEdge>(
            new[] { "group-a" },
            limit: 1,
            uuidCursor: "edge-d",
            withEmbeddings: true);
        var zeroLimitNodes = await driver.GetNodesByGroupIdsAsync<EntityNode>(
            new[] { "group-a" },
            limit: 0,
            uuidCursor: "node-d");
        var negativeLimitNodes = await driver.GetNodesByGroupIdsAsync<EntityNode>(
            new[] { "group-a" },
            limit: -1,
            uuidCursor: "node-d");
        var zeroLimitEdges = await driver.GetEdgesByGroupIdsAsync<EntityEdge>(
            new[] { "group-a" },
            limit: 0,
            uuidCursor: "edge-d");
        var negativeLimitEdges = await driver.GetEdgesByGroupIdsAsync<EntityEdge>(
            new[] { "group-a" },
            limit: -1,
            uuidCursor: "edge-d");
        var missingGroupNodes = await driver.GetNodesByGroupIdsAsync<EntityNode>(
            new[] { "missing-group" },
            limit: 10);
        var missingGroupEdges = await driver.GetEdgesByGroupIdsAsync<EntityEdge>(
            new[] { "missing-group" },
            limit: 10);

        Assert.Equal(new[] { "node-c", "node-b" }, nodes.Select(node => node.Uuid));
        Assert.All(nodes, node => Assert.Null(node.NameEmbedding));
        Assert.Equal("node-c", Assert.Single(nodesWithEmbeddings).Uuid);
        Assert.Equal(new List<float> { 1f, 1f }, nodesWithEmbeddings[0].NameEmbedding);
        Assert.Equal(new[] { "edge-c", "edge-b" }, edges.Select(edge => edge.Uuid));
        Assert.All(edges, edge => Assert.Null(edge.FactEmbedding));
        Assert.Equal("edge-c", Assert.Single(edgesWithEmbeddings).Uuid);
        Assert.Equal(new List<float> { 1f, 1f }, edgesWithEmbeddings[0].FactEmbedding);
        Assert.Empty(zeroLimitNodes);
        Assert.Empty(negativeLimitNodes);
        Assert.Empty(zeroLimitEdges);
        Assert.Empty(negativeLimitEdges);
        Assert.Empty(missingGroupNodes);
        Assert.Empty(missingGroupEdges);

        nodes[0].Summary = "mutated";
        nodesWithEmbeddings[0].NameEmbedding![0] = 99f;
        edges[0].Fact = "mutated";
        edgesWithEmbeddings[0].FactEmbedding![0] = 99f;

        Assert.Equal("C summary", (await driver.GetNodeByUuidAsync<EntityNode>("node-c")).Summary);
        Assert.Equal(new List<float> { 1f, 1f }, (await driver.GetNodeByUuidAsync<EntityNode>("node-c")).NameEmbedding);
        Assert.Equal("third", (await driver.GetEdgeByUuidAsync<EntityEdge>("edge-c")).Fact);
        Assert.Equal(new List<float> { 1f, 1f }, (await driver.GetEdgeByUuidAsync<EntityEdge>("edge-c")).FactEmbedding);
    }

    [Fact]
    public async Task GroupLookups_RejectNullInputs()
    {
        var driver = new InMemoryGraphDriver();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            driver.GetNodesByGroupIdsAsync<EntityNode>(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            driver.GetEdgesByGroupIdsAsync<EntityEdge>(null!));
    }

    [Fact]
    public async Task GroupLookups_ObserveCancellationWhileEnumeratingLazyInputs()
    {
        var driver = new InMemoryGraphDriver();
        using var nodeCancellation = new CancellationTokenSource();
        using var edgeCancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await driver.GetNodesByGroupIdsAsync<EntityNode>(
                CancelAfterFirst("group", nodeCancellation),
                cancellationToken: nodeCancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await driver.GetEdgesByGroupIdsAsync<EntityEdge>(
                CancelAfterFirst("group", edgeCancellation),
                cancellationToken: edgeCancellation.Token));
    }

    [Fact]
    public async Task RelationshipLookups_DeduplicateInDeterministicTraversalOrderAndReturnClones()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var episodeA = Episode("episode-a", now);
        var episodeB = Episode("episode-b", now.AddMinutes(1));
        var alpha = Entity("alpha", "Alpha", "tenant");
        var beta = Entity("beta", "Beta", "tenant");
        var gamma = Entity("gamma", "Gamma", "tenant");
        var communityZ = Community("z-community", "Z Community");
        var communityA = Community("a-community", "A Community");
        foreach (var node in new Node[] { episodeA, episodeB, alpha, beta, gamma, communityZ, communityA })
        {
            await driver.SaveNodeAsync(node);
        }

        await driver.SaveEdgeAsync(Mention("mention-b", episodeA, beta));
        await driver.SaveEdgeAsync(Mention("mention-a", episodeA, alpha));
        await driver.SaveEdgeAsync(Mention("mention-d", episodeB, beta));
        await driver.SaveEdgeAsync(Mention("mention-c", episodeB, gamma));
        await driver.SaveEdgeAsync(Member("membership-b", communityA, beta));
        await driver.SaveEdgeAsync(Member("membership-a", communityZ, alpha));
        await driver.SaveEdgeAsync(Member("membership-c", communityZ, beta));
        await driver.SaveEdgeAsync(Relates("edge-b", alpha, beta, "second"));
        await driver.SaveEdgeAsync(Relates("edge-a", alpha, beta, "first"));

        var episodes = await driver.GetEpisodesByEntityNodeUuidAsync(beta.Uuid);
        var mentioned = await driver.GetMentionedNodesAsync(new[] { episodeA, episodeB, episodeA });
        var communities = await driver.GetCommunitiesByNodesAsync(new[] { alpha, beta, alpha });
        var endpointEdges = await driver.GetEntityEdgesBetweenNodesAsync(alpha.Uuid, beta.Uuid);
        var incidentEdges = await driver.GetEntityEdgesByNodeUuidAsync(alpha.Uuid);

        Assert.Equal(new[] { "episode-a", "episode-b" }, episodes.Select(episode => episode.Uuid));
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, mentioned.Select(node => node.Uuid));
        Assert.Equal(new[] { "z-community", "a-community" }, communities.Select(node => node.Uuid));
        Assert.Equal(new[] { "edge-a", "edge-b" }, endpointEdges.Select(edge => edge.Uuid));
        Assert.Equal(new[] { "edge-a", "edge-b" }, incidentEdges.Select(edge => edge.Uuid));

        episodes[0].Name = "mutated";
        mentioned[0].Name = "mutated";
        communities[0].Name = "mutated";
        endpointEdges[0].Fact = "mutated";
        incidentEdges[1].Fact = "mutated";

        Assert.Equal("episode-a", (await driver.GetNodeByUuidAsync<EpisodicNode>("episode-a")).Name);
        Assert.Equal("Alpha", (await driver.GetNodeByUuidAsync<EntityNode>("alpha")).Name);
        Assert.Equal("Z Community", (await driver.GetNodeByUuidAsync<CommunityNode>("z-community")).Name);
        Assert.Equal("first", (await driver.GetEdgeByUuidAsync<EntityEdge>("edge-a")).Fact);
        Assert.Equal("second", (await driver.GetEdgeByUuidAsync<EntityEdge>("edge-b")).Fact);
    }

    private static IEnumerable<string> CancelAfterFirst(string first, CancellationTokenSource cancellation)
    {
        yield return first;
        cancellation.Cancel();
        yield return "never-read";
    }

    private static EntityNode Entity(string uuid, string name, string groupId) =>
        new()
        {
            Uuid = uuid,
            Name = name,
            GroupId = groupId,
            Summary = $"{name} summary"
        };

    private static EpisodicNode Episode(string uuid, DateTime validAt) =>
        new()
        {
            Uuid = uuid,
            Name = uuid,
            GroupId = "tenant",
            Content = uuid,
            CreatedAt = validAt,
            ValidAt = validAt
        };

    private static CommunityNode Community(string uuid, string name) =>
        Community(uuid, name, "tenant");

    private static CommunityNode Community(string uuid, string name, string groupId) =>
        new()
        {
            Uuid = uuid,
            Name = name,
            GroupId = groupId,
            Summary = $"{name} summary"
        };

    private static EntityEdge Relates(string uuid, EntityNode source, EntityNode target, string fact) =>
        new()
        {
            Uuid = uuid,
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = source.GroupId,
            Name = "RELATES_TO",
            Fact = fact
        };

    private static EntityEdge Relates(
        string uuid,
        EntityNode source,
        EntityNode target,
        string fact,
        string groupId,
        List<float> factEmbedding) =>
        new()
        {
            Uuid = uuid,
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = groupId,
            Name = "RELATES_TO",
            Fact = fact,
            FactEmbedding = factEmbedding
        };

    private static EpisodicEdge Mention(string uuid, EpisodicNode episode, EntityNode entity) =>
        new()
        {
            Uuid = uuid,
            SourceNodeUuid = episode.Uuid,
            TargetNodeUuid = entity.Uuid,
            GroupId = entity.GroupId
        };

    private static CommunityEdge Member(string uuid, CommunityNode community, EntityNode entity) =>
        new()
        {
            Uuid = uuid,
            SourceNodeUuid = community.Uuid,
            TargetNodeUuid = entity.Uuid,
            GroupId = community.GroupId
        };
}
