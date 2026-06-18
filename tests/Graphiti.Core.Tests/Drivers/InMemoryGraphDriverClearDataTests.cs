using Graphiti.Core;

namespace Graphiti.Core.Tests.Drivers;

public class InMemoryGraphDriverClearDataTests
{
    [Fact]
    public async Task ClearDataAsync_GroupIdsDeletesIncidentCrossGroupEdges()
    {
        var driver = new InMemoryGraphDriver();
        var alice = new EntityNode { Uuid = "alice", Name = "Alice", GroupId = "group-a" };
        var bob = new EntityNode { Uuid = "bob", Name = "Bob", GroupId = "group-b" };
        await driver.SaveNodeAsync(alice);
        await driver.SaveNodeAsync(bob);

        var edge = new EntityEdge
        {
            Uuid = "cross-edge",
            GroupId = "edge-group",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            Name = "KNOWS",
            Fact = "Alice knows Bob"
        };
        await driver.SaveEdgeAsync(edge);

        await driver.ClearDataAsync(new[] { "group-a" });

        await Assert.ThrowsAsync<NodeNotFoundException>(() =>
            driver.GetNodeByUuidAsync<EntityNode>(alice.Uuid));
        var remaining = await driver.GetNodeByUuidAsync<EntityNode>(bob.Uuid);
        Assert.Equal(bob.Uuid, remaining.Uuid);
        await Assert.ThrowsAsync<EdgeNotFoundException>(() =>
            driver.GetEdgeByUuidAsync<EntityEdge>(edge.Uuid));
    }

    [Fact]
    public async Task ClearDataAsync_GroupIdsDoesNotDeleteUnattachedEdgeByEdgeGroup()
    {
        var driver = new InMemoryGraphDriver();
        var alice = new EntityNode { Uuid = "alice", Name = "Alice", GroupId = "group-a" };
        var bob = new EntityNode { Uuid = "bob", Name = "Bob", GroupId = "group-b" };
        await driver.SaveNodeAsync(alice);
        await driver.SaveNodeAsync(bob);

        var edge = new EntityEdge
        {
            Uuid = "edge-with-cleared-group",
            GroupId = "group-to-clear",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            Name = "KNOWS",
            Fact = "Alice knows Bob"
        };
        await driver.SaveEdgeAsync(edge);

        await driver.ClearDataAsync(new[] { "group-to-clear" });

        var storedEdge = await driver.GetEdgeByUuidAsync<EntityEdge>(edge.Uuid);
        Assert.Equal(edge.Uuid, storedEdge.Uuid);
        Assert.Equal(edge.GroupId, storedEdge.GroupId);
    }

    [Fact]
    public async Task ClearDataAsync_GroupIdsPreservesSagaNodesAndDeletesIncidentEpisodeEdges()
    {
        var driver = new InMemoryGraphDriver();
        var saga = new SagaNode { Uuid = "saga", Name = "checkout", GroupId = "group-a" };
        var episode = new EpisodicNode
        {
            Uuid = "episode",
            Name = "episode",
            GroupId = "group-a",
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Saga episode",
            ValidAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };
        var edge = new HasEpisodeEdge
        {
            Uuid = "has-episode",
            SourceNodeUuid = saga.Uuid,
            TargetNodeUuid = episode.Uuid,
            GroupId = "group-a"
        };
        await driver.SaveNodeAsync(saga);
        await driver.SaveNodeAsync(episode);
        await driver.SaveEdgeAsync(edge);

        await driver.ClearDataAsync(new[] { "group-a" });

        Assert.Equal(saga.Uuid, (await driver.GetNodeByUuidAsync<SagaNode>(saga.Uuid)).Uuid);
        await Assert.ThrowsAsync<NodeNotFoundException>(() =>
            driver.GetNodeByUuidAsync<EpisodicNode>(episode.Uuid));
        await Assert.ThrowsAsync<EdgeNotFoundException>(() =>
            driver.GetEdgeByUuidAsync<HasEpisodeEdge>(edge.Uuid));

        await driver.ClearDataAsync();

        await Assert.ThrowsAsync<NodeNotFoundException>(() =>
            driver.GetNodeByUuidAsync<SagaNode>(saga.Uuid));
    }

    [Fact]
    public async Task ClearDataAsync_EmptyGroupListDoesNotMutateState()
    {
        var driver = new InMemoryGraphDriver();
        var alice = new EntityNode { Uuid = "alice", Name = "Alice", GroupId = "group-a" };
        var bob = new EntityNode { Uuid = "bob", Name = "Bob", GroupId = "group-b" };
        await driver.SaveNodeAsync(alice);
        await driver.SaveNodeAsync(bob);

        var edge = new EntityEdge
        {
            Uuid = "edge",
            GroupId = "edge-group",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            Name = "KNOWS",
            Fact = "Alice knows Bob"
        };
        await driver.SaveEdgeAsync(edge);

        await driver.ClearDataAsync(Array.Empty<string>());

        Assert.Equal(alice.Uuid, (await driver.GetNodeByUuidAsync<EntityNode>(alice.Uuid)).Uuid);
        Assert.Equal(bob.Uuid, (await driver.GetNodeByUuidAsync<EntityNode>(bob.Uuid)).Uuid);
        Assert.Equal(edge.Uuid, (await driver.GetEdgeByUuidAsync<EntityEdge>(edge.Uuid)).Uuid);
    }

    [Fact]
    public async Task ClearDataAsync_NullGroupListClearsAllNodesEdgesAndIndexes()
    {
        var driver = new InMemoryGraphDriver();
        var alice = new EntityNode { Uuid = "alice", Name = "Alice", GroupId = "group-a" };
        var community = new CommunityNode { Uuid = "community", Name = "Community", GroupId = "group-a" };
        await driver.SaveNodeAsync(alice);
        await driver.SaveNodeAsync(community);
        await driver.SaveEdgeAsync(new CommunityEdge
        {
            Uuid = "membership",
            SourceNodeUuid = community.Uuid,
            TargetNodeUuid = alice.Uuid,
            GroupId = "group-a"
        });

        await driver.ClearDataAsync();

        Assert.Empty(await driver.GetEntityGroupIdsAsync());
        Assert.Empty(await driver.GetCommunityGroupIdsAsync());
        Assert.Empty(await driver.GetNodesByGroupIdsAsync<EntityNode>(new[] { "group-a" }));
        Assert.Empty(await driver.GetEdgesByGroupIdsAsync<CommunityEdge>(new[] { "group-a" }));
        await Assert.ThrowsAsync<NodeNotFoundException>(() =>
            driver.GetNodeByUuidAsync<EntityNode>(alice.Uuid));
        await Assert.ThrowsAsync<EdgeNotFoundException>(() =>
            driver.GetEdgeByUuidAsync<CommunityEdge>("membership"));
    }

    [Fact]
    public async Task ClearDataAsync_DuplicateGroupIdsDeleteScopedNodesAndIncidentEdgesOnce()
    {
        var driver = new InMemoryGraphDriver();
        var alice = new EntityNode { Uuid = "alice", Name = "Alice", GroupId = "group-a" };
        var anna = new EntityNode { Uuid = "anna", Name = "Anna", GroupId = "group-a" };
        var bob = new EntityNode { Uuid = "bob", Name = "Bob", GroupId = "group-b" };
        foreach (var node in new[] { alice, anna, bob })
        {
            await driver.SaveNodeAsync(node);
        }

        var crossEdge = new EntityEdge
        {
            Uuid = "cross-edge",
            GroupId = "edge-group",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            Name = "KNOWS",
            Fact = "Alice knows Bob"
        };
        var remainingEdge = new EntityEdge
        {
            Uuid = "remaining-edge",
            GroupId = "group-b",
            SourceNodeUuid = bob.Uuid,
            TargetNodeUuid = bob.Uuid,
            Name = "SELF",
            Fact = "Bob knows Bob"
        };
        await driver.SaveEdgeAsync(crossEdge);
        await driver.SaveEdgeAsync(remainingEdge);

        await driver.ClearDataAsync(new[] { "group-a", "group-a" });

        await Assert.ThrowsAsync<NodeNotFoundException>(() =>
            driver.GetNodeByUuidAsync<EntityNode>(alice.Uuid));
        await Assert.ThrowsAsync<NodeNotFoundException>(() =>
            driver.GetNodeByUuidAsync<EntityNode>(anna.Uuid));
        await Assert.ThrowsAsync<EdgeNotFoundException>(() =>
            driver.GetEdgeByUuidAsync<EntityEdge>(crossEdge.Uuid));
        Assert.Equal(bob.Uuid, (await driver.GetNodeByUuidAsync<EntityNode>(bob.Uuid)).Uuid);
        Assert.Equal(remainingEdge.Uuid, (await driver.GetEdgeByUuidAsync<EntityEdge>(remainingEdge.Uuid)).Uuid);
    }
}
