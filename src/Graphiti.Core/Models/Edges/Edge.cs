namespace Graphiti.Core.Models.Edges;

/// <summary>
/// Base type for all graph edges: a directed relationship from <see cref="SourceNodeUuid"/> to
/// <see cref="TargetNodeUuid"/> within a graph partition. Concrete subtypes include
/// <see cref="EntityEdge"/>, <see cref="EpisodicEdge"/>, <see cref="CommunityEdge"/>,
/// <see cref="HasEpisodeEdge"/>, and <see cref="NextEpisodeEdge"/>.
/// </summary>
public abstract class Edge : IEquatable<Edge>
{
    /// <summary>Unique identifier for the edge. Defaults to a new UUID.</summary>
    public string Uuid { get; set; } = GraphitiHelpers.NewUuid();

    /// <summary>Graph partition this edge belongs to.</summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>UUID of the source (origin) node.</summary>
    public string SourceNodeUuid { get; set; } = string.Empty;

    /// <summary>UUID of the target (destination) node.</summary>
    public string TargetNodeUuid { get; set; } = string.Empty;

    /// <summary>Timestamp at which the edge was created in the graph.</summary>
    public DateTime CreatedAt { get; set; } = GraphitiHelpers.DefaultTimestamp;

    /// <summary>Persists this edge using the supplied driver.</summary>
    public Task SaveAsync(IGraphDriver driver, CancellationToken cancellationToken = default) =>
        driver.SaveEdgeAsync(this, cancellationToken);

    /// <summary>Deletes this edge from the graph.</summary>
    public Task DeleteAsync(IGraphDriver driver, CancellationToken cancellationToken = default) =>
        TypedEdgeDeletion.DeleteEdgeAsync(this, driver, cancellationToken);

    /// <summary>Deletes the edges with the given UUIDs.</summary>
    public static Task DeleteByUuidsAsync(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        TypedEdgeDeletion.DeleteBaseEdgesByUuidsAsync(driver, uuids, cancellationToken);

    /// <summary>Edges do not compare equal to other edges through the typed equality path.</summary>
    public bool Equals(Edge? other)
    {
        _ = other;
        return false;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Node node && Uuid == node.Uuid;

    /// <inheritdoc />
    public override int GetHashCode() => Uuid.GetHashCode(StringComparison.Ordinal);
}
