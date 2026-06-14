namespace Graphiti.Core.Models.Edges;

/// <summary>Orders one episode after another within a saga (the <c>NEXT_EPISODE</c> relationship).</summary>
public sealed class NextEpisodeEdge : Edge
{
    /// <summary>Retrieves a single next-episode edge by UUID.</summary>
    public static Task<NextEpisodeEdge> GetByUuidAsync(
        IGraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgeByUuidAsync<NextEpisodeEdge>(uuid, cancellationToken);

    /// <summary>Retrieves the NEXT_EPISODE edges with the given UUIDs.</summary>
    public static Task<IReadOnlyList<NextEpisodeEdge>> GetByUuidsAsync(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgesByUuidsAsync<NextEpisodeEdge>(uuids, cancellationToken);

    /// <summary>Retrieves NEXT_EPISODE edges across the given group partitions, with optional UUID-cursor paging.</summary>
    public static Task<IReadOnlyList<NextEpisodeEdge>> GetByGroupIdsAsync(
        IGraphDriver driver,
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgesByGroupIdsAsync<NextEpisodeEdge>(groupIds, limit, uuidCursor, false, cancellationToken);
}
