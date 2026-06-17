namespace Graphiti.Core.Namespaces;

internal static class NamespaceDriverHelpers
{
    private const int MaxNamespaceSaveConcurrency = 8;

    public static void ValidateBatchSize(int batchSize) =>
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

    public static Task SaveNodesAsync<TNode>(
        IGraphDriver driver,
        IEnumerable<TNode> nodes,
        int batchSize,
        CancellationToken cancellationToken)
        where TNode : Node =>
        SaveItemsAsync(
            nodes,
            batchSize,
            driver.SaveNodeAsync,
            cancellationToken);

    public static Task SaveEdgesAsync<TEdge>(
        IGraphDriver driver,
        IEnumerable<TEdge> edges,
        int batchSize,
        CancellationToken cancellationToken)
        where TEdge : Edge =>
        SaveItemsAsync(
            edges,
            batchSize,
            driver.SaveEdgeAsync,
            cancellationToken);

    private static async Task SaveItemsAsync<TItem>(
        IEnumerable<TItem> items,
        int batchSize,
        Func<TItem, CancellationToken, Task> saveAsync,
        CancellationToken cancellationToken)
    {
        ValidateBatchSize(batchSize);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(saveAsync);

        await ForEachBatchAsync(
            items,
            batchSize,
            async (batch, token) =>
            {
                if (batch.Count == 1)
                {
                    await saveAsync(batch[0], token).ConfigureAwait(false);
                    return;
                }

                var options = new ParallelOptions
                {
                    CancellationToken = token,
                    MaxDegreeOfParallelism = Math.Min(batch.Count, MaxNamespaceSaveConcurrency)
                };
                await Parallel.ForEachAsync(
                    batch,
                    options,
                    async (item, itemToken) => await saveAsync(item, itemToken).ConfigureAwait(false)).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task ForEachBatchAsync<TItem>(
        IEnumerable<TItem> items,
        int batchSize,
        Func<IReadOnlyList<TItem>, CancellationToken, Task> processBatchAsync,
        CancellationToken cancellationToken)
    {
        ValidateBatchSize(batchSize);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(processBatchAsync);
        cancellationToken.ThrowIfCancellationRequested();

        var batch = new List<TItem>(batchSize);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batch.Add(item);
            if (batch.Count == batchSize)
            {
                await processBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                batch = new List<TItem>(batchSize);
            }
        }

        if (batch.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await processBatchAsync(batch, cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task DeleteNodesByGroupIdAsync<TNode>(
        IGraphDriver driver,
        string groupId,
        int batchSize,
        CancellationToken cancellationToken)
        where TNode : Node
    {
        await TypedNodeDeletion.DeleteNodesByGroupIdAsync<TNode>(
            driver,
            groupId,
            batchSize,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task DeleteNodesByUuidsAsync<TNode>(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        int batchSize,
        CancellationToken cancellationToken)
        where TNode : Node
    {
        await TypedNodeDeletion.DeleteNodesByUuidsAsync<TNode>(
            driver,
            uuids,
            batchSize,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task DeleteEdgesByUuidsAsync<TEdge>(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        CancellationToken cancellationToken)
        where TEdge : Edge
    {
        ArgumentNullException.ThrowIfNull(uuids);
        var typedEdges = await driver.GetEdgesByUuidsAsync<TEdge>(
            uuids,
            cancellationToken).ConfigureAwait(false);
        var typedUuids = BuildEdgeUuidList(typedEdges);
        if (typedUuids.Count == 0)
        {
            return;
        }

        await driver.DeleteEdgesByUuidsAsync(typedUuids, cancellationToken).ConfigureAwait(false);
    }

    public static async Task LoadEntityNodeEmbeddingsAsync(
        IGraphDriver driver,
        IEnumerable<EntityNode> nodes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var nodeList = MaterializeList(nodes, cancellationToken);
        if (nodeList.Count == 0)
        {
            return;
        }

        var nodeUuids = BuildNodeUuidList(nodeList);
        if (driver is IEmbeddingLoadGraphDriver embeddingDriver)
        {
            var embeddings = await embeddingDriver
                .LoadEntityNodeEmbeddingsByUuidAsync(nodeUuids, cancellationToken)
                .ConfigureAwait(false);
            foreach (var node in nodeList)
            {
                if (embeddings.TryGetValue(node.Uuid, out var embedding))
                {
                    node.NameEmbedding = CopyFloatList(embedding);
                }
            }

            return;
        }

        var storedNodes = await driver.GetNodesByUuidsAsync<EntityNode>(
            nodeUuids,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var storedByUuid = BuildNodeLookup(storedNodes);
        foreach (var node in nodeList)
        {
            if (storedByUuid.TryGetValue(node.Uuid, out var stored))
            {
                node.NameEmbedding = CopyFloatList(stored.NameEmbedding);
            }
        }
    }

    public static async Task LoadEntityEdgeEmbeddingsAsync(
        IGraphDriver driver,
        IEnumerable<EntityEdge> edges,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edges);

        var edgeList = MaterializeList(edges, cancellationToken);
        if (edgeList.Count == 0)
        {
            return;
        }

        var edgeUuids = BuildEdgeUuidList(edgeList);
        if (driver is IEmbeddingLoadGraphDriver embeddingDriver)
        {
            var embeddings = await embeddingDriver
                .LoadEntityEdgeEmbeddingsByUuidAsync(edgeUuids, cancellationToken)
                .ConfigureAwait(false);
            foreach (var edge in edgeList)
            {
                if (embeddings.TryGetValue(edge.Uuid, out var embedding))
                {
                    edge.FactEmbedding = CopyFloatList(embedding);
                }
            }

            return;
        }

        var storedEdges = await driver.GetEdgesByUuidsAsync<EntityEdge>(
            edgeUuids,
            cancellationToken).ConfigureAwait(false);
        var storedByUuid = BuildEdgeLookup(storedEdges);
        foreach (var edge in edgeList)
        {
            if (storedByUuid.TryGetValue(edge.Uuid, out var stored))
            {
                edge.FactEmbedding = CopyFloatList(stored.FactEmbedding);
            }
        }
    }

    public static List<TItem> MaterializeList<TItem>(
        IEnumerable<TItem> items,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        cancellationToken.ThrowIfCancellationRequested();
        var capacity = items.TryGetNonEnumeratedCount(out var count) ? count : 0;
        var list = capacity == 0 ? new List<TItem>() : new List<TItem>(capacity);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            list.Add(item);
        }

        return list;
    }

    private static List<string> BuildNodeUuidList(List<EntityNode> nodes)
    {
        var uuids = new List<string>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            uuids.Add(nodes[i].Uuid);
        }

        return uuids;
    }

    private static List<string> BuildEdgeUuidList<TEdge>(IReadOnlyList<TEdge> edges)
        where TEdge : Edge
    {
        var uuids = new List<string>(edges.Count);
        for (var i = 0; i < edges.Count; i++)
        {
            uuids.Add(edges[i].Uuid);
        }

        return uuids;
    }

    private static Dictionary<string, EntityNode> BuildNodeLookup(IReadOnlyList<EntityNode> nodes)
    {
        var lookup = new Dictionary<string, EntityNode>(nodes.Count, StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            lookup.Add(nodes[i].Uuid, nodes[i]);
        }

        return lookup;
    }

    private static Dictionary<string, EntityEdge> BuildEdgeLookup(IReadOnlyList<EntityEdge> edges)
    {
        var lookup = new Dictionary<string, EntityEdge>(edges.Count, StringComparer.Ordinal);
        for (var i = 0; i < edges.Count; i++)
        {
            lookup.Add(edges[i].Uuid, edges[i]);
        }

        return lookup;
    }

    private static List<float>? CopyFloatList(List<float>? source)
    {
        if (source is null)
        {
            return null;
        }

        var copy = new List<float>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            copy.Add(source[i]);
        }

        return copy;
    }
}
