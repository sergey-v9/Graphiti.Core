namespace Graphiti.Core.Models.Results;

/// <summary>Aggregated results from ingesting a batch of episodes via bulk add.</summary>
public sealed class AddBulkEpisodeResults
{
    /// <summary>The episode nodes that were persisted.</summary>
    public List<EpisodicNode> Episodes { get; set; } = new();

    /// <summary>Episode-to-entity mention edges created across the batch.</summary>
    public List<EpisodicEdge> EpisodicEdges { get; set; } = new();

    /// <summary>Entity nodes extracted (or matched) across the batch.</summary>
    public List<EntityNode> Nodes { get; set; } = new();

    /// <summary>Entity edges (facts) extracted across the batch.</summary>
    public List<EntityEdge> Edges { get; set; } = new();

    /// <summary>Community nodes created or updated across the batch.</summary>
    public List<CommunityNode> Communities { get; set; } = new();

    /// <summary>Community membership edges created or updated.</summary>
    public List<CommunityEdge> CommunityEdges { get; set; } = new();
}
