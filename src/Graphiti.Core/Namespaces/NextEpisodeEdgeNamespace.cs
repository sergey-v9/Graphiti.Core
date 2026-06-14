namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="NextEpisodeEdge"/> (episode ordering).</summary>
public sealed class NextEpisodeEdgeNamespace
{
    private readonly IGraphDriver _driver;

    /// <summary>Creates the namespace bound to a driver.</summary>
    public NextEpisodeEdgeNamespace(IGraphDriver driver) => _driver = driver;

    /// <summary>Persists the NEXT_EPISODE edge and returns it.</summary>
    public async Task<NextEpisodeEdge> SaveAsync(NextEpisodeEdge edge, CancellationToken cancellationToken = default)
    {
        await edge.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return edge;
    }

    /// <summary>Persists many NEXT_EPISODE edges in batches.</summary>
    public async Task SaveBulkAsync(
        IEnumerable<NextEpisodeEdge> edges,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        await NamespaceDriverHelpers.SaveEdgesAsync(
            _driver,
            edges,
            batchSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Deletes the NEXT_EPISODE edge.</summary>
    public Task DeleteAsync(NextEpisodeEdge edge, CancellationToken cancellationToken = default) =>
        edge.DeleteAsync(_driver, cancellationToken);

    /// <summary>Deletes the NEXT_EPISODE edges with the given UUIDs.</summary>
    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteEdgesByUuidsAsync<NextEpisodeEdge>(_driver, uuids, cancellationToken);

    /// <summary>Loads a single NEXT_EPISODE edge by UUID.</summary>
    public Task<NextEpisodeEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) => NextEpisodeEdge.GetByUuidAsync(_driver, uuid, cancellationToken);

    /// <summary>Loads the NEXT_EPISODE edges with the given UUIDs.</summary>
    public Task<IReadOnlyList<NextEpisodeEdge>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        NextEpisodeEdge.GetByUuidsAsync(_driver, uuids, cancellationToken);

    /// <summary>Loads NEXT_EPISODE edges across the given group partitions, with optional UUID-cursor paging.</summary>
    public Task<IReadOnlyList<NextEpisodeEdge>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        NextEpisodeEdge.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
}
