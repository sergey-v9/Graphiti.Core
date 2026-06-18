using Graphiti.Core;

namespace Graphiti.Core.Tests.Models;

public class EdgeModelTests
{
    [Fact]
    public void EdgeEquality_UsesNodeUuidBoundary()
    {
        const string uuid = "shared-uuid";

        var edge = new EntityEdge { Uuid = uuid, GroupId = "group" };
        var sameUuidEdge = new EntityEdge { Uuid = uuid, GroupId = "group" };
        var sameUuidNode = new EntityNode { Uuid = uuid, GroupId = "group" };
        var otherNode = new EntityNode { Uuid = "other-uuid", GroupId = "group" };

        Assert.True(edge.Equals((object)sameUuidNode));
        Assert.False(edge.Equals(sameUuidEdge));
        Assert.False(edge.Equals((object)sameUuidEdge));
        Assert.False(edge.Equals((object)edge));
        Assert.False(sameUuidNode.Equals((object)edge));
        Assert.False(edge.Equals((object)otherNode));
    }

    [Fact]
    public async Task EntityEdgeGetByGroupIdsAsync_ThrowsWhenNoEdges()
    {
        var driver = new InMemoryGraphDriver();

        await Assert.ThrowsAsync<GroupsEdgesNotFoundException>(() =>
            EntityEdge.GetByGroupIdsAsync(driver, new[] { "missing" }));
    }

    [Fact]
    public async Task EpisodicEdgeGetByGroupIdsAsync_ThrowsWhenNoEdges()
    {
        var driver = new InMemoryGraphDriver();

        await Assert.ThrowsAsync<GroupsEdgesNotFoundException>(() =>
            EpisodicEdge.GetByGroupIdsAsync(driver, new[] { "missing" }));
    }
}
