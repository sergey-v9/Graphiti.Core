namespace Graphiti.Core.Namespaces;

/// <summary>
/// Convenience facade grouping the node-type accessors (<see cref="Entity"/>, <see cref="Episodic"/>,
/// <see cref="Community"/>, <see cref="Saga"/>) so callers can work with nodes without dealing with
/// the driver directly. Exposed as <c>Graphiti.Nodes</c>.
/// </summary>
public sealed class NodeNamespace
{
    /// <summary>Creates the node namespace bound to a driver and embedder.</summary>
    public NodeNamespace(IGraphDriver driver, IEmbedderClient embedder)
    {
        Entity = new EntityNodeNamespace(driver, embedder);
        Episodic = new EpisodicNodeNamespace(driver);
        Community = new CommunityNodeNamespace(driver, embedder);
        Saga = new SagaNodeNamespace(driver);
    }

    /// <summary>Accessor for entity nodes.</summary>
    public EntityNodeNamespace Entity { get; }

    /// <summary>Accessor for episodic nodes.</summary>
    public EpisodicNodeNamespace Episodic { get; }

    /// <summary>Alias for <see cref="Episodic"/>.</summary>
    public EpisodicNodeNamespace Episode => Episodic;

    /// <summary>Accessor for community nodes.</summary>
    public CommunityNodeNamespace Community { get; }

    /// <summary>Accessor for saga nodes.</summary>
    public SagaNodeNamespace Saga { get; }
}
