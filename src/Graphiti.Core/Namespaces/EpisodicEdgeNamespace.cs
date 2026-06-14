namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="EpisodicEdge"/> (mentions).</summary>
public sealed class EpisodicEdgeNamespace
{
    private readonly IGraphDriver _driver;

    /// <summary>Creates the namespace bound to a driver.</summary>
    public EpisodicEdgeNamespace(IGraphDriver driver) => _driver = driver;

    /// <summary>Persists the episodic edge and returns it.</summary>
    public async Task<EpisodicEdge> SaveAsync(EpisodicEdge edge, CancellationToken cancellationToken = default)
    {
        await edge.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return edge;
    }

    /// <summary>Persists many episodic edges in batches.</summary>
    public async Task SaveBulkAsync(
        IEnumerable<EpisodicEdge> edges,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        await NamespaceDriverHelpers.SaveEdgesAsync(
            _driver,
            edges,
            batchSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Deletes the episodic edge.</summary>
    public Task DeleteAsync(EpisodicEdge edge, CancellationToken cancellationToken = default) =>
        edge.DeleteAsync(_driver, cancellationToken);

    /// <summary>Deletes the episodic edges with the given UUIDs.</summary>
    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteEdgesByUuidsAsync<EpisodicEdge>(_driver, uuids, cancellationToken);

    /// <summary>Loads a single episodic edge by UUID.</summary>
    public Task<EpisodicEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) => EpisodicEdge.GetByUuidAsync(_driver, uuid, cancellationToken);

    /// <summary>Loads the episodic edges with the given UUIDs.</summary>
    public Task<IReadOnlyList<EpisodicEdge>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        EpisodicEdge.GetByUuidsAsync(_driver, uuids, cancellationToken);

    /// <summary>Loads episodic edges across the given group partitions, with optional UUID-cursor paging.</summary>
    public Task<IReadOnlyList<EpisodicEdge>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        EpisodicEdge.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
}
