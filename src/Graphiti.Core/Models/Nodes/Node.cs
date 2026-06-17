namespace Graphiti.Core.Models.Nodes;

/// <summary>
/// Base type for all graph nodes. A node has a stable <see cref="Uuid"/>, a human-readable
/// <see cref="Name"/>, and belongs to a graph partition identified by <see cref="GroupId"/>.
/// Nodes are compared by UUID. Concrete subtypes are <see cref="EntityNode"/>,
/// <see cref="EpisodicNode"/>, <see cref="CommunityNode"/>, and <see cref="SagaNode"/>.
/// </summary>
public abstract class Node : IEquatable<Node>
{
    private List<string> _labels = new();

    /// <summary>Unique identifier for the node. Defaults to a new UUID.</summary>
    public string Uuid { get; set; } = GraphitiHelpers.NewUuid();

    /// <summary>Display name of the node.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Graph partition this node belongs to.</summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// Additional graph labels applied to the node. Assigning a value validates each label
    /// and throws <see cref="NodeLabelValidationException"/> for invalid names.
    /// </summary>
    public List<string> Labels
    {
        get => _labels;
        set
        {
            GraphitiHelpers.ValidateNodeLabels(value);
            _labels = value ?? new List<string>();
        }
    }

    /// <summary>Timestamp at which the node was created in the graph.</summary>
    public DateTime CreatedAt { get; set; } = GraphitiHelpers.DefaultTimestamp;

    /// <summary>Persists this node using the supplied driver.</summary>
    public Task SaveAsync(IGraphDriver driver, CancellationToken cancellationToken = default) =>
        driver.SaveNodeAsync(this, cancellationToken);

    /// <summary>Deletes this node (and its attached edges) from the graph.</summary>
    public Task DeleteAsync(IGraphDriver driver, CancellationToken cancellationToken = default) =>
        TypedNodeDeletion.DeleteNodeAsync(this, driver, cancellationToken);

    /// <summary>Deletes all nodes in the given group partition, in batches.</summary>
    public static Task DeleteByGroupIdAsync(
        IGraphDriver driver,
        string groupId,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        driver.DeleteNodesByGroupIdAsync(groupId, batchSize, cancellationToken);

    /// <summary>Deletes the nodes with the given UUIDs, in batches.</summary>
    public static Task DeleteByUuidsAsync(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        driver.DeleteNodesByUuidsAsync(uuids, batchSize, cancellationToken);

    /// <summary>Two nodes are equal when they share the same <see cref="Uuid"/>.</summary>
    public bool Equals(Node? other) => other is not null && Uuid == other.Uuid;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Node node && Equals(node);

    /// <inheritdoc />
    public override int GetHashCode() => Uuid.GetHashCode(StringComparison.Ordinal);
}
