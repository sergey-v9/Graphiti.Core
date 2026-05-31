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
}
