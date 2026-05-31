namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="CommunityEdge"/> (community membership).</summary>
public sealed class CommunityEdgeNamespace
{
    private readonly IGraphDriver _driver;

    /// <summary>Creates the namespace bound to a driver.</summary>
    public CommunityEdgeNamespace(IGraphDriver driver) => _driver = driver;

    public async Task<CommunityEdge> SaveAsync(CommunityEdge edge, CancellationToken cancellationToken = default)
    {
        await edge.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return edge;
    }

    public async Task SaveBulkAsync(
        IEnumerable<CommunityEdge> edges,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        await NamespaceDriverHelpers.SaveEdgesAsync(
            _driver,
            edges,
            batchSize,
            cancellationToken).ConfigureAwait(false);

    public Task DeleteAsync(CommunityEdge edge, CancellationToken cancellationToken = default) =>
        edge.DeleteAsync(_driver, cancellationToken);

    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteEdgesByUuidsAsync<CommunityEdge>(_driver, uuids, cancellationToken);

    public Task<CommunityEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) => CommunityEdge.GetByUuidAsync(_driver, uuid, cancellationToken);

    public Task<IReadOnlyList<CommunityEdge>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        CommunityEdge.GetByUuidsAsync(_driver, uuids, cancellationToken);

    public Task<IReadOnlyList<CommunityEdge>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        CommunityEdge.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
}
