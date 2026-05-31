namespace Graphiti.Core.Models.Results;

/// <summary>Results from manually adding a single entity-edge-entity triplet to the graph.</summary>
public sealed class AddTripletResults
{
    /// <summary>The source and target entity nodes involved in the triplet.</summary>
    public List<EntityNode> Nodes { get; set; } = new();

    /// <summary>The entity edge (fact) connecting the two nodes.</summary>
    public List<EntityEdge> Edges { get; set; } = new();
}
