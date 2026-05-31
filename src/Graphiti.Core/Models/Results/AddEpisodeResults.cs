namespace Graphiti.Core.Models.Results;

/// <summary>
/// Everything created or touched while ingesting a single episode: the stored episode, its
/// mention edges, the resolved entities and facts, and any communities affected.
/// </summary>
public sealed class AddEpisodeResults
{
    /// <summary>The episode node that was persisted.</summary>
    public EpisodicNode Episode { get; set; } = new();

    /// <summary>Episode-to-entity mention edges created for the episode.</summary>
    public List<EpisodicEdge> EpisodicEdges { get; set; } = new();

    /// <summary>Entity nodes extracted (or matched) for the episode.</summary>
    public List<EntityNode> Nodes { get; set; } = new();

    /// <summary>Entity edges (facts) extracted for the episode.</summary>
    public List<EntityEdge> Edges { get; set; } = new();

    /// <summary>Community nodes created or updated as a result of the episode.</summary>
    public List<CommunityNode> Communities { get; set; } = new();

    /// <summary>Community membership edges created or updated.</summary>
    public List<CommunityEdge> CommunityEdges { get; set; } = new();
}
