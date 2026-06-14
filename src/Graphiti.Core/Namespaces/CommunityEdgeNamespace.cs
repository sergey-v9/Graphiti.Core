namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="CommunityEdge"/> (community membership).</summary>
public sealed class CommunityEdgeNamespace
{
    private readonly IGraphDriver _driver;

    /// <summary>Creates the namespace bound to a driver.</summary>
    public CommunityEdgeNamespace(IGraphDriver driver) => _driver = driver;

    /// <summary>Persists the community edge and returns it.</summary>
    public async Task<CommunityEdge> SaveAsync(CommunityEdge edge, CancellationToken cancellationToken = default)
    {
        await edge.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return edge;
    }

    /// <summary>Persists many community edges in batches.</summary>
    public async Task SaveBulkAsync(
        IEnumerable<CommunityEdge> edges,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        await NamespaceDriverHelpers.SaveEdgesAsync(
            _driver,
            edges,
            batchSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Deletes the community edge.</summary>
    public Task DeleteAsync(CommunityEdge edge, CancellationToken cancellationToken = default) =>
        edge.DeleteAsync(_driver, cancellationToken);

    /// <summary>Deletes the community edges with the given UUIDs.</summary>
    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteEdgesByUuidsAsync<CommunityEdge>(_driver, uuids, cancellationToken);

    /// <summary>Loads a single community edge by UUID.</summary>
    public Task<CommunityEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) => CommunityEdge.GetByUuidAsync(_driver, uuid, cancellationToken);

    /// <summary>Loads the community edges with the given UUIDs.</summary>
    public Task<IReadOnlyList<CommunityEdge>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        CommunityEdge.GetByUuidsAsync(_driver, uuids, cancellationToken);

    /// <summary>Loads community edges across the given group partitions, with optional UUID-cursor paging.</summary>
    public Task<IReadOnlyList<CommunityEdge>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        CommunityEdge.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
}
