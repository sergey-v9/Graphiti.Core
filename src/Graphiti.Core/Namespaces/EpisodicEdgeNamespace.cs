namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="EpisodicEdge"/> (mentions).</summary>
public sealed class EpisodicEdgeNamespace
{
    private readonly IGraphDriver _driver;

    /// <summary>Creates the namespace bound to a driver.</summary>
    public EpisodicEdgeNamespace(IGraphDriver driver) => _driver = driver;

    public async Task<EpisodicEdge> SaveAsync(EpisodicEdge edge, CancellationToken cancellationToken = default)
    {
        await edge.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return edge;
    }

    public async Task SaveBulkAsync(
        IEnumerable<EpisodicEdge> edges,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        await NamespaceDriverHelpers.SaveEdgesAsync(
            _driver,
            edges,
            batchSize,
            cancellationToken).ConfigureAwait(false);

    public Task DeleteAsync(EpisodicEdge edge, CancellationToken cancellationToken = default) =>
        edge.DeleteAsync(_driver, cancellationToken);

    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteEdgesByUuidsAsync<EpisodicEdge>(_driver, uuids, cancellationToken);

    public Task<EpisodicEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) => EpisodicEdge.GetByUuidAsync(_driver, uuid, cancellationToken);

    public Task<IReadOnlyList<EpisodicEdge>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        EpisodicEdge.GetByUuidsAsync(_driver, uuids, cancellationToken);

    public Task<IReadOnlyList<EpisodicEdge>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        EpisodicEdge.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
}
