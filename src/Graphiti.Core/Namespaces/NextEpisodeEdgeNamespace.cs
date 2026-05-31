namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="NextEpisodeEdge"/> (episode ordering).</summary>
public sealed class NextEpisodeEdgeNamespace
{
    private readonly IGraphDriver _driver;

    /// <summary>Creates the namespace bound to a driver.</summary>
    public NextEpisodeEdgeNamespace(IGraphDriver driver) => _driver = driver;

    public async Task<NextEpisodeEdge> SaveAsync(NextEpisodeEdge edge, CancellationToken cancellationToken = default)
    {
        await edge.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return edge;
    }

    public async Task SaveBulkAsync(
        IEnumerable<NextEpisodeEdge> edges,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        await NamespaceDriverHelpers.SaveEdgesAsync(
            _driver,
            edges,
            batchSize,
            cancellationToken).ConfigureAwait(false);

    public Task DeleteAsync(NextEpisodeEdge edge, CancellationToken cancellationToken = default) =>
        edge.DeleteAsync(_driver, cancellationToken);

    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteEdgesByUuidsAsync<NextEpisodeEdge>(_driver, uuids, cancellationToken);

    public Task<NextEpisodeEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) => NextEpisodeEdge.GetByUuidAsync(_driver, uuid, cancellationToken);

    public Task<IReadOnlyList<NextEpisodeEdge>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        NextEpisodeEdge.GetByUuidsAsync(_driver, uuids, cancellationToken);

    public Task<IReadOnlyList<NextEpisodeEdge>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        NextEpisodeEdge.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
}
