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

        foreach (var chunk in items.Chunk(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (chunk.Length == 0)
            {
                continue;
            }

            if (chunk.Length == 1)
            {
                await saveAsync(chunk[0], cancellationToken).ConfigureAwait(false);
                continue;
            }

            var options = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Math.Min(chunk.Length, MaxNamespaceSaveConcurrency)
            };
            await Parallel.ForEachAsync(
                chunk,
                options,
                async (item, token) => await saveAsync(item, token).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }

    public static async Task DeleteNodesByGroupIdAsync<TNode>(
        IGraphDriver driver,
        string groupId,
        int batchSize,
        CancellationToken cancellationToken)
        where TNode : Node
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var nodes = await driver.GetNodesByGroupIdsAsync<TNode>(
            new[] { groupId },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await DeleteNodesByUuidsAsync<TNode>(
            driver,
            nodes.Select(node => node.Uuid),
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
        ArgumentNullException.ThrowIfNull(uuids);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var typedNodes = await driver.GetNodesByUuidsAsync<TNode>(
            uuids,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var typedUuids = typedNodes.Select(node => node.Uuid).ToList();
        if (typedUuids.Count == 0)
        {
            return;
        }

        await driver.DeleteNodesByUuidsAsync(typedUuids, batchSize, cancellationToken).ConfigureAwait(false);
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
        var typedUuids = typedEdges.Select(edge => edge.Uuid).ToList();
        if (typedUuids.Count == 0)
        {
            return;
        }

        await driver.DeleteEdgesByUuidsAsync(typedUuids, cancellationToken).ConfigureAwait(false);
    }

    public static async Task EnsureCommunityNodeEmbeddingsAsync(
        IReadOnlyList<CommunityNode> nodes,
        IEmbedderClient embedder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        var nodesMissingEmbeddings = nodes
            .Where(node => node.NameEmbedding is null)
            .ToList();
        if (nodesMissingEmbeddings.Count == 0)
        {
            return;
        }

        var inputs = nodesMissingEmbeddings
            .Select(node => (node.Name ?? string.Empty).Replace('\n', ' '))
            .ToList();
        var embeddings = EmbeddingVectorValidation.MaterializeBatch(
            await embedder.CreateBatchAsync(inputs, cancellationToken).ConfigureAwait(false),
            nodesMissingEmbeddings.Count,
            embedder.EmbeddingDimension,
            "community node name embeddings",
            index => $"community node '{nodesMissingEmbeddings[index].Name}' at index {index}");
        for (var i = 0; i < nodesMissingEmbeddings.Count; i++)
        {
            nodesMissingEmbeddings[i].NameEmbedding = embeddings[i];
        }
    }

    public static async Task LoadEntityNodeEmbeddingsAsync(
        IGraphDriver driver,
        IEnumerable<EntityNode> nodes,
        CancellationToken cancellationToken)
    {
        var nodeList = nodes.ToList();
        if (nodeList.Count == 0)
        {
            return;
        }

        var storedNodes = await driver.GetNodesByUuidsAsync<EntityNode>(
            nodeList.Select(node => node.Uuid),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var storedByUuid = storedNodes.ToDictionary(node => node.Uuid, StringComparer.Ordinal);
        foreach (var node in nodeList)
        {
            if (storedByUuid.TryGetValue(node.Uuid, out var stored))
            {
                node.NameEmbedding = stored.NameEmbedding?.ToList();
            }
        }
    }

    public static async Task LoadEntityEdgeEmbeddingsAsync(
        IGraphDriver driver,
        IEnumerable<EntityEdge> edges,
        CancellationToken cancellationToken)
    {
        var edgeList = edges.ToList();
        if (edgeList.Count == 0)
        {
            return;
        }

        var storedEdges = await driver.GetEdgesByUuidsAsync<EntityEdge>(
            edgeList.Select(edge => edge.Uuid),
            cancellationToken).ConfigureAwait(false);
        var storedByUuid = storedEdges.ToDictionary(edge => edge.Uuid, StringComparer.Ordinal);
        foreach (var edge in edgeList)
        {
            if (storedByUuid.TryGetValue(edge.Uuid, out var stored))
            {
                edge.FactEmbedding = stored.FactEmbedding?.ToList();
            }
        }
    }
}
