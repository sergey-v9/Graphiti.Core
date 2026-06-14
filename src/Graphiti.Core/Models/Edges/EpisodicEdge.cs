namespace Graphiti.Core.Models.Edges;

/// <summary>Connects an episode to an entity it mentions (the <c>MENTIONS</c> relationship).</summary>
public sealed class EpisodicEdge : Edge
{
    /// <summary>Retrieves a single episodic edge by UUID.</summary>
    public static Task<EpisodicEdge> GetByUuidAsync(
        IGraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgeByUuidAsync<EpisodicEdge>(uuid, cancellationToken);

    /// <summary>Retrieves the episodic edges with the given UUIDs.</summary>
    public static Task<IReadOnlyList<EpisodicEdge>> GetByUuidsAsync(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgesByUuidsAsync<EpisodicEdge>(uuids, cancellationToken);

    /// <summary>Retrieves episodic edges across the given group partitions, with optional UUID-cursor paging.</summary>
    public static Task<IReadOnlyList<EpisodicEdge>> GetByGroupIdsAsync(
        IGraphDriver driver,
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgesByGroupIdsAsync<EpisodicEdge>(groupIds, limit, uuidCursor, false, cancellationToken);
}
