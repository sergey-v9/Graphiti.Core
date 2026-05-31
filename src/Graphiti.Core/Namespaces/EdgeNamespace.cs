namespace Graphiti.Core.Namespaces;

/// <summary>
/// Convenience facade grouping the edge-type accessors (<see cref="Entity"/>, <see cref="Episodic"/>,
/// <see cref="Community"/>, <see cref="HasEpisode"/>, <see cref="NextEpisode"/>). Exposed as
/// <c>Graphiti.Edges</c>.
/// </summary>
public sealed class EdgeNamespace
{
    /// <summary>Creates the edge namespace bound to a driver and embedder.</summary>
    public EdgeNamespace(IGraphDriver driver, IEmbedderClient embedder)
    {
        Entity = new EntityEdgeNamespace(driver, embedder);
        Episodic = new EpisodicEdgeNamespace(driver);
        Community = new CommunityEdgeNamespace(driver);
        HasEpisode = new HasEpisodeEdgeNamespace(driver);
        NextEpisode = new NextEpisodeEdgeNamespace(driver);
    }

    /// <summary>Accessor for entity edges (facts).</summary>
    public EntityEdgeNamespace Entity { get; }

    /// <summary>Accessor for episodic (mention) edges.</summary>
    public EpisodicEdgeNamespace Episodic { get; }

    /// <summary>Alias for <see cref="Episodic"/>.</summary>
    public EpisodicEdgeNamespace Episode => Episodic;

    /// <summary>Accessor for community membership edges.</summary>
    public CommunityEdgeNamespace Community { get; }

    /// <summary>Accessor for saga has-episode edges.</summary>
    public HasEpisodeEdgeNamespace HasEpisode { get; }

    /// <summary>Accessor for saga episode-ordering edges.</summary>
    public NextEpisodeEdgeNamespace NextEpisode { get; }
}
