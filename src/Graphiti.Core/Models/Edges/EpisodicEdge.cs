namespace Graphiti.Core.Models.Edges;

/// <summary>Connects an episode to an entity it mentions (the <c>MENTIONS</c> relationship).</summary>
public sealed class EpisodicEdge : Edge
{
    public static Task<EpisodicEdge> GetByUuidAsync(
        IGraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgeByUuidAsync<EpisodicEdge>(uuid, cancellationToken);

    public static Task<IReadOnlyList<EpisodicEdge>> GetByUuidsAsync(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgesByUuidsAsync<EpisodicEdge>(uuids, cancellationToken);

    public static Task<IReadOnlyList<EpisodicEdge>> GetByGroupIdsAsync(
        IGraphDriver driver,
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgesByGroupIdsAsync<EpisodicEdge>(groupIds, limit, uuidCursor, false, cancellationToken);
}
