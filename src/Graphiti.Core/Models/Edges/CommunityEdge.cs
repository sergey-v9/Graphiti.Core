namespace Graphiti.Core.Models.Edges;

/// <summary>Connects a community to one of its member entities (the <c>HAS_MEMBER</c> relationship).</summary>
public sealed class CommunityEdge : Edge
{
    /// <summary>Retrieves a single community edge by UUID.</summary>
    public static Task<CommunityEdge> GetByUuidAsync(
        IGraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgeByUuidAsync<CommunityEdge>(uuid, cancellationToken);

    public static Task<IReadOnlyList<CommunityEdge>> GetByUuidsAsync(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgesByUuidsAsync<CommunityEdge>(uuids, cancellationToken);

    public static Task<IReadOnlyList<CommunityEdge>> GetByGroupIdsAsync(
        IGraphDriver driver,
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgesByGroupIdsAsync<CommunityEdge>(groupIds, limit, uuidCursor, false, cancellationToken);
}
