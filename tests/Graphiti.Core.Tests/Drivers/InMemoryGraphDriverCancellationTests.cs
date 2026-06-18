using Graphiti.Core;

namespace Graphiti.Core.Tests.Drivers;

public class InMemoryGraphDriverCancellationTests
{
    [Fact]
    public async Task InMemoryGraphDriver_PublicOperationsObserveCanceledToken()
    {
        var driver = new InMemoryGraphDriver();
        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var node = new EntityNode { Uuid = "node", Name = "Alice", GroupId = "group" };
        var edge = new EntityEdge
        {
            Uuid = "edge",
            GroupId = "group",
            SourceNodeUuid = "source",
            TargetNodeUuid = "target",
            Name = "RELATES_TO",
            Fact = "Alice relates to Bob"
        };
        var token = cancellation.Token;
        var operations = new Func<Task>[]
        {
            () => driver.BuildIndicesAndConstraintsAsync(cancellationToken: token),
            () => driver.DeleteAllIndexesAsync(token),
            () => driver.CloseAsync(token),
            () => driver.GetEntityGroupIdsAsync(token),
            () => driver.GetCommunityGroupIdsAsync(token),
            () => driver.SaveNodeAsync(node, token),
            () => driver.SaveEdgeAsync(edge, token),
            () => driver.DeleteNodeAsync(node.Uuid, token),
            () => driver.DeleteNodesByGroupIdAsync("group", cancellationToken: token),
            () => driver.DeleteNodesByUuidsAsync(new[] { node.Uuid }, cancellationToken: token),
            () => driver.DeleteEdgeAsync(edge.Uuid, token),
            () => driver.DeleteEdgesByUuidsAsync(new[] { edge.Uuid }, token),
            () => driver.ClearDataAsync(cancellationToken: token),
            () => driver.GetNodeByUuidAsync<EntityNode>(node.Uuid, token),
            () => driver.GetNodesByUuidsAsync<EntityNode>(new[] { node.Uuid }, cancellationToken: token),
            () => driver.GetNodesByGroupIdsAsync<EntityNode>(new[] { "group" }, cancellationToken: token),
            () => driver.GetEdgeByUuidAsync<EntityEdge>(edge.Uuid, token),
            () => driver.GetEdgesByUuidsAsync<EntityEdge>(new[] { edge.Uuid }, token),
            () => driver.GetEdgesByGroupIdsAsync<EntityEdge>(new[] { "group" }, cancellationToken: token),
            () => driver.GetEntityEdgesBetweenNodesAsync("source", "target", token),
            () => driver.GetEntityEdgesByNodeUuidAsync("source", token),
            () => driver.GetEpisodesByEntityNodeUuidAsync(node.Uuid, token),
            () => driver.RetrieveEpisodesAsync(DateTime.UtcNow, 10, cancellationToken: token),
            () => driver.GetMentionedNodesAsync(Array.Empty<EpisodicNode>(), token),
            () => driver.GetCommunitiesByNodesAsync(Array.Empty<EntityNode>(), token),
            () => driver.FindSagaByNameAsync("saga", "group", token),
            () => driver.GetSagaPreviousEpisodeUuidAsync("saga", "episode", token),
            () => driver.GetSagaEpisodeContentsAsync("saga", cancellationToken: token),
            () => searchDriver.SearchEntityNodesFulltextAsync("alice", new SearchFilters(), null, 10, token),
            () => searchDriver.SearchEntityNodesByEmbeddingAsync(new[] { 1f, 0f }, new SearchFilters(), null, 10, 0, token),
            () => searchDriver.SearchEntityEdgesFulltextAsync("alice", new SearchFilters(), null, 10, token),
            () => searchDriver.SearchEntityEdgesByEmbeddingAsync(new[] { 1f, 0f }, new SearchFilters(), null, 10, 0, cancellationToken: token),
            () => searchDriver.SearchEntityNodesBfsAsync(new[] { node.Uuid }, new SearchFilters(), 2, null, 10, token),
            () => searchDriver.SearchEntityEdgesBfsAsync(new[] { node.Uuid }, new SearchFilters(), 2, null, 10, token),
            () => searchDriver.SearchEpisodesFulltextAsync("alice", new SearchFilters(), null, 10, token),
            () => searchDriver.SearchCommunitiesFulltextAsync("alice", null, 10, token),
            () => searchDriver.SearchCommunitiesByEmbeddingAsync(new[] { 1f, 0f }, null, 10, 0, token),
            () => searchDriver.RankNodeDistanceAsync(new[] { node.Uuid }, node.Uuid, cancellationToken: token),
            () => searchDriver.RankNodeEpisodeMentionsAsync(new[] { node.Uuid }, cancellationToken: token)
        };

        foreach (var operation in operations)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await operation());
        }
    }

    [Fact]
    public async Task InMemoryGraphDriver_CanceledSaveDoesNotMutateState()
    {
        var driver = new InMemoryGraphDriver();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await driver.SaveNodeAsync(
                new EntityNode { Name = "Alice", GroupId = "group" },
                cancellation.Token));

        var stored = await driver.GetNodesByGroupIdsAsync<EntityNode>(new[] { "group" });
        Assert.Empty(stored);
    }
}
