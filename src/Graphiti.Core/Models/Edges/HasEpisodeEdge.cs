namespace Graphiti.Core.Models.Edges;

/// <summary>Links a saga to an episode that belongs to it (the <c>HAS_EPISODE</c> relationship).</summary>
public sealed class HasEpisodeEdge : Edge
{
    /// <summary>Retrieves a single has-episode edge by UUID.</summary>
    public static Task<HasEpisodeEdge> GetByUuidAsync(
        IGraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgeByUuidAsync<HasEpisodeEdge>(uuid, cancellationToken);

    public static Task<IReadOnlyList<HasEpisodeEdge>> GetByUuidsAsync(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgesByUuidsAsync<HasEpisodeEdge>(uuids, cancellationToken);

    public static Task<IReadOnlyList<HasEpisodeEdge>> GetByGroupIdsAsync(
        IGraphDriver driver,
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgesByGroupIdsAsync<HasEpisodeEdge>(groupIds, limit, uuidCursor, false, cancellationToken);
}
