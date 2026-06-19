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

    /// <summary>
    /// Persists the entity edge (fact), regenerating its fact embedding first, and returns the saved
    /// edge.
    /// </summary>
    public async Task<EntityEdge> SaveAsync(EntityEdge edge, CancellationToken cancellationToken = default)
    {
        await edge.GenerateEmbeddingAsync(_embedder, cancellationToken).ConfigureAwait(false);
        await edge.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return edge;
    }

    /// <summary>Persists many entity edges (facts) in batches, preserving supplied fact embeddings as-is.</summary>
    public async Task SaveBulkAsync(
        IEnumerable<EntityEdge> edges,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await NamespaceDriverHelpers.SaveEdgesAsync(
            _driver,
            edges,
            batchSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the entity edge (fact).</summary>
    public Task DeleteAsync(EntityEdge edge, CancellationToken cancellationToken = default) =>
        edge.DeleteAsync(_driver, cancellationToken);

    /// <summary>Deletes the entity edges (facts) with the given UUIDs.</summary>
    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteEdgesByUuidsAsync<EntityEdge>(_driver, uuids, cancellationToken);

    /// <summary>Loads a single entity edge (fact) by UUID.</summary>
    public Task<EntityEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) =>
        EntityEdge.GetByUuidAsync(_driver, uuid, cancellationToken);

    /// <summary>Loads the entity edges (facts) with the given UUIDs.</summary>
    public Task<IReadOnlyList<EntityEdge>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        _driver.GetEdgesByUuidsAsync<EntityEdge>(uuids, cancellationToken);

    /// <summary>
    /// Loads entity edges (facts) across the given group partitions, with optional UUID-cursor paging
    /// and optional inclusion of fact embeddings.
    /// </summary>
    public Task<IReadOnlyList<EntityEdge>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default) =>
        _driver.GetEdgesByGroupIdsAsync<EntityEdge>(
            groupIds,
            limit,
            uuidCursor,
            withEmbeddings,
            cancellationToken);

    /// <summary>Loads the entity edges (facts) that directly connect two nodes.</summary>
    public Task<IReadOnlyList<EntityEdge>> GetBetweenNodesAsync(
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default) =>
        EntityEdge.GetBetweenNodesAsync(_driver, sourceNodeUuid, targetNodeUuid, cancellationToken);

    /// <summary>Loads all entity edges (facts) incident to the given node.</summary>
    public Task<IReadOnlyList<EntityEdge>> GetByNodeUuidAsync(
        string nodeUuid,
        CancellationToken cancellationToken = default) =>
        EntityEdge.GetByNodeUuidAsync(_driver, nodeUuid, cancellationToken);

    /// <summary>Populates the edge's fact embedding from storage.</summary>
    public Task LoadEmbeddingsAsync(EntityEdge edge, CancellationToken cancellationToken = default) =>
        edge.LoadFactEmbeddingAsync(_driver, cancellationToken);

    /// <summary>Populates fact embeddings for many edges from storage.</summary>
    public Task LoadEmbeddingsBulkAsync(
        IEnumerable<EntityEdge> edges,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        _ = batchSize;
        return NamespaceDriverHelpers.LoadEntityEdgeEmbeddingsAsync(_driver, edges, cancellationToken);
    }
}
