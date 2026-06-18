namespace Graphiti.Core.Drivers;

internal static class TypedNodeDeletion
{
    public static Task DeleteNodeAsync(
        Node node,
        IGraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node switch
        {
            EntityNode entity => DeleteNodeAsync<EntityNode>(entity.Uuid, driver, cancellationToken),
            EpisodicNode episode => DeleteNodeAsync<EpisodicNode>(episode.Uuid, driver, cancellationToken),
            CommunityNode community => DeleteNodeAsync<CommunityNode>(community.Uuid, driver, cancellationToken),
            SagaNode saga => DeleteNodeAsync<SagaNode>(saga.Uuid, driver, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType().Name)
        };
    }

    public static async Task DeleteNodeAsync<TNode>(
        string uuid,
        IGraphDriver driver,
        CancellationToken cancellationToken = default)
        where TNode : Node
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (driver is ITypedNodeDeleteGraphDriver typedDriver)
        {
            await typedDriver.DeleteNodeAsync<TNode>(uuid, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!await ExistsAsync<TNode>(uuid, driver, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await driver.DeleteNodeAsync(uuid, cancellationToken).ConfigureAwait(false);
    }

    public static async Task DeleteNodesByGroupIdAsync<TNode>(
        IGraphDriver driver,
        string groupId,
        int batchSize,
        CancellationToken cancellationToken = default)
        where TNode : Node
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (driver is ITypedNodeDeleteGraphDriver typedDriver)
        {
            await typedDriver.DeleteNodesByGroupIdAsync<TNode>(groupId, batchSize, cancellationToken).ConfigureAwait(false);
            return;
        }

        var nodes = await driver.GetNodesByGroupIdsAsync<TNode>(
            new[] { groupId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await DeleteNodesByUuidsAsync<TNode>(
            driver,
            BuildNodeUuidList(nodes),
            batchSize,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task DeleteNodesByUuidsAsync<TNode>(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        int batchSize,
        CancellationToken cancellationToken = default)
        where TNode : Node
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(uuids);
        if (driver is ITypedNodeDeleteGraphDriver typedDriver)
        {
            await typedDriver.DeleteNodesByUuidsAsync<TNode>(uuids, batchSize, cancellationToken).ConfigureAwait(false);
            return;
        }

        var typedNodes = await driver.GetNodesByUuidsAsync<TNode>(
            uuids,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var typedUuids = BuildNodeUuidList(typedNodes);
        if (typedUuids.Count == 0)
        {
            return;
        }

        await driver.DeleteNodesByUuidsAsync(typedUuids, batchSize, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ExistsAsync<TNode>(
        string uuid,
        IGraphDriver driver,
        CancellationToken cancellationToken)
        where TNode : Node
    {
        try
        {
            _ = await driver.GetNodeByUuidAsync<TNode>(uuid, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (NodeNotFoundException)
        {
            return false;
        }
    }

    private static List<string> BuildNodeUuidList<TNode>(IReadOnlyList<TNode> nodes)
        where TNode : Node
    {
        var uuids = new List<string>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            uuids.Add(nodes[i].Uuid);
        }

        return uuids;
    }
}
