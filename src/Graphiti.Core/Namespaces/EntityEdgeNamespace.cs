namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="EntityEdge"/> (facts), handling fact embeddings.</summary>
public sealed class EntityEdgeNamespace
{
    private readonly IGraphDriver _driver;
    private readonly IEmbedderClient _embedder;

    /// <summary>Creates the namespace bound to a driver and embedder.</summary>
    public EntityEdgeNamespace(IGraphDriver driver, IEmbedderClient embedder)
    {
        _driver = driver;
        _embedder = embedder;
    }

    public async Task<EntityEdge> SaveAsync(EntityEdge edge, CancellationToken cancellationToken = default)
    {
        if (edge.FactEmbedding is null)
        {
            await edge.GenerateEmbeddingAsync(_embedder, cancellationToken).ConfigureAwait(false);
        }

        await edge.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return edge;
    }

    public async Task SaveBulkAsync(
        IEnumerable<EntityEdge> edges,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        NamespaceDriverHelpers.ValidateBatchSize(batchSize);
        ArgumentNullException.ThrowIfNull(edges);

        await NamespaceDriverHelpers.SaveEntityEdgesBulkAsync(
            _driver,
            edges,
            batchSize,
            _embedder,
            cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(EntityEdge edge, CancellationToken cancellationToken = default) =>
        edge.DeleteAsync(_driver, cancellationToken);

    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteEdgesByUuidsAsync<EntityEdge>(_driver, uuids, cancellationToken);

    public Task<EntityEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) =>
        EntityEdge.GetByUuidAsync(_driver, uuid, cancellationToken);

    public Task<IReadOnlyList<EntityEdge>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        EntityEdge.GetByUuidsAsync(_driver, uuids, cancellationToken);

    public Task<IReadOnlyList<EntityEdge>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default) =>
        EntityEdge.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, withEmbeddings, cancellationToken);

    public Task<IReadOnlyList<EntityEdge>> GetBetweenNodesAsync(
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default) =>
        EntityEdge.GetBetweenNodesAsync(_driver, sourceNodeUuid, targetNodeUuid, cancellationToken);

    public Task<IReadOnlyList<EntityEdge>> GetByNodeUuidAsync(
        string nodeUuid,
        CancellationToken cancellationToken = default) =>
        EntityEdge.GetByNodeUuidAsync(_driver, nodeUuid, cancellationToken);

    public Task LoadEmbeddingsAsync(EntityEdge edge, CancellationToken cancellationToken = default) =>
        edge.LoadFactEmbeddingAsync(_driver, cancellationToken);

    public Task LoadEmbeddingsBulkAsync(
        IEnumerable<EntityEdge> edges,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        _ = batchSize;
        return NamespaceDriverHelpers.LoadEntityEdgeEmbeddingsAsync(_driver, edges, cancellationToken);
    }
}
