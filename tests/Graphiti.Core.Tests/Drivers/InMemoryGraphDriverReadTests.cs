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
        new()
        {
            Uuid = uuid,
            Name = name,
            GroupId = "tenant",
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
