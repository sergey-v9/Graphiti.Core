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
    public static async Task<IReadOnlyList<EpisodicEdge>> GetByUuidsAsync(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default)
    {
        var requestedUuids = uuids as IReadOnlyList<string> ?? uuids.ToArray();
        var edges = await driver.GetEdgesByUuidsAsync<EpisodicEdge>(requestedUuids, cancellationToken)
            .ConfigureAwait(false);
        if (requestedUuids.Count > 0 && edges.Count == 0)
        {
            throw new EdgeNotFoundException(requestedUuids[0]);
        }

        return edges;
    }

    /// <summary>Retrieves episodic edges across the given group partitions, with optional UUID-cursor paging.</summary>
    public static Task<IReadOnlyList<EpisodicEdge>> GetByGroupIdsAsync(
        IGraphDriver driver,
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgesByGroupIdsAsync<EpisodicEdge>(groupIds, limit, uuidCursor, false, cancellationToken);
}
