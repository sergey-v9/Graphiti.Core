using Graphiti.Core;

namespace Graphiti.Core.Tests.Models;

public class NodeModelTests
{
    [Fact]
    public async Task DeleteByGroupIdAsync_DoesNotDeleteSagaNodes()
    {
        var driver = new InMemoryGraphDriver();
        var entity = new EntityNode { Uuid = "entity", Name = "Entity", GroupId = "tenant" };
        var episode = new EpisodicNode { Uuid = "episode", Name = "Episode", GroupId = "tenant" };
        var community = new CommunityNode { Uuid = "community", Name = "Community", GroupId = "tenant" };
        var saga = new SagaNode { Uuid = "saga", Name = "Saga", GroupId = "tenant" };
        await SaveNodesAsync(driver, entity, episode, community, saga);

        await Node.DeleteByGroupIdAsync(driver, "tenant", batchSize: 2);

        await AssertNodeNotFoundAsync<EntityNode>(driver, entity.Uuid);
        await AssertNodeNotFoundAsync<EpisodicNode>(driver, episode.Uuid);
        await AssertNodeNotFoundAsync<CommunityNode>(driver, community.Uuid);
        var storedSaga = await driver.GetNodeByUuidAsync<SagaNode>(saga.Uuid);
        Assert.Equal(saga.Uuid, storedSaga.Uuid);
    }

    [Fact]
    public async Task DeleteByUuidsAsync_DoesNotDeleteSagaNodes()
    {
        var driver = new InMemoryGraphDriver();
        var entity = new EntityNode { Uuid = "entity", Name = "Entity", GroupId = "tenant" };
        var episode = new EpisodicNode { Uuid = "episode", Name = "Episode", GroupId = "tenant" };
        var community = new CommunityNode { Uuid = "community", Name = "Community", GroupId = "tenant" };
        var saga = new SagaNode { Uuid = "saga", Name = "Saga", GroupId = "tenant" };
        await SaveNodesAsync(driver, entity, episode, community, saga);

        await Node.DeleteByUuidsAsync(
            driver,
            new[] { entity.Uuid, episode.Uuid, community.Uuid, saga.Uuid },
            batchSize: 2);

        await AssertNodeNotFoundAsync<EntityNode>(driver, entity.Uuid);
        await AssertNodeNotFoundAsync<EpisodicNode>(driver, episode.Uuid);
        await AssertNodeNotFoundAsync<CommunityNode>(driver, community.Uuid);
        var storedSaga = await driver.GetNodeByUuidAsync<SagaNode>(saga.Uuid);
        Assert.Equal(saga.Uuid, storedSaga.Uuid);
    }

    [Fact]
    public async Task EntityNodeGetByUuidsAsync_IgnoresGroupId()
    {
        var driver = new InMemoryGraphDriver();
        var entity = new EntityNode { Uuid = "entity", Name = "Entity", GroupId = "tenant-a" };
        await driver.SaveNodeAsync(entity);

        var nodes = await EntityNode.GetByUuidsAsync(
            driver,
            new[] { entity.Uuid },
            groupId: "tenant-b");

        Assert.Equal(entity.Uuid, Assert.Single(nodes).Uuid);
    }

    private static async Task SaveNodesAsync(InMemoryGraphDriver driver, params Node[] nodes)
    {
        for (var i = 0; i < nodes.Length; i++)
        {
            await driver.SaveNodeAsync(nodes[i]);
        }
    }

    private static async Task AssertNodeNotFoundAsync<TNode>(
        InMemoryGraphDriver driver,
        string uuid)
        where TNode : Node =>
        await Assert.ThrowsAsync<NodeNotFoundException>(() => driver.GetNodeByUuidAsync<TNode>(uuid));
}
