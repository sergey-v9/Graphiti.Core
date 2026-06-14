namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="HasEpisodeEdge"/> (saga-to-episode links).</summary>
public sealed class HasEpisodeEdgeNamespace
{
    private readonly IGraphDriver _driver;

    /// <summary>Creates the namespace bound to a driver.</summary>
    public HasEpisodeEdgeNamespace(IGraphDriver driver) => _driver = driver;

    /// <summary>Persists the HAS_EPISODE edge and returns it.</summary>
    public async Task<HasEpisodeEdge> SaveAsync(HasEpisodeEdge edge, CancellationToken cancellationToken = default)
    {
        await edge.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return edge;
    }

    /// <summary>Persists many HAS_EPISODE edges in batches.</summary>
    public async Task SaveBulkAsync(
        IEnumerable<HasEpisodeEdge> edges,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        await NamespaceDriverHelpers.SaveEdgesAsync(
            _driver,
            edges,
            batchSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Deletes the HAS_EPISODE edge.</summary>
    public Task DeleteAsync(HasEpisodeEdge edge, CancellationToken cancellationToken = default) =>
        edge.DeleteAsync(_driver, cancellationToken);

    /// <summary>Deletes the HAS_EPISODE edges with the given UUIDs.</summary>
    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteEdgesByUuidsAsync<HasEpisodeEdge>(_driver, uuids, cancellationToken);

    /// <summary>Loads a single HAS_EPISODE edge by UUID.</summary>
    public Task<HasEpisodeEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) => HasEpisodeEdge.GetByUuidAsync(_driver, uuid, cancellationToken);

    /// <summary>Loads the HAS_EPISODE edges with the given UUIDs.</summary>
    public Task<IReadOnlyList<HasEpisodeEdge>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        HasEpisodeEdge.GetByUuidsAsync(_driver, uuids, cancellationToken);

    /// <summary>Loads HAS_EPISODE edges across the given group partitions, with optional UUID-cursor paging.</summary>
    public Task<IReadOnlyList<HasEpisodeEdge>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        HasEpisodeEdge.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
}
