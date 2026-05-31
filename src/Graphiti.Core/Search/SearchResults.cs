using System.Text.Json.Serialization;

namespace Graphiti.Core.Search;

/// <summary>
/// The outcome of a search: the matched edges, nodes, episodes, and communities, each paired with a
/// parallel list of reranker scores (same order and length as the corresponding result list).
/// </summary>
public sealed class SearchResults
{
    /// <summary>Matched facts (entity edges), ordered by relevance.</summary>
    [JsonPropertyName("edges")]
    public List<EntityEdge> Edges { get; set; } = new();

    /// <summary>Reranker scores aligned with <see cref="Edges"/>.</summary>
    [JsonPropertyName("edge_reranker_scores")]
    public List<double> EdgeRerankerScores { get; set; } = new();

    /// <summary>Matched entity nodes, ordered by relevance.</summary>
    [JsonPropertyName("nodes")]
    public List<EntityNode> Nodes { get; set; } = new();

    /// <summary>Reranker scores aligned with <see cref="Nodes"/>.</summary>
    [JsonPropertyName("node_reranker_scores")]
    public List<double> NodeRerankerScores { get; set; } = new();

    /// <summary>Matched episodes, ordered by relevance.</summary>
    [JsonPropertyName("episodes")]
    public List<EpisodicNode> Episodes { get; set; } = new();

    /// <summary>Reranker scores aligned with <see cref="Episodes"/>.</summary>
    [JsonPropertyName("episode_reranker_scores")]
    public List<double> EpisodeRerankerScores { get; set; } = new();

    /// <summary>Matched communities, ordered by relevance.</summary>
    [JsonPropertyName("communities")]
    public List<CommunityNode> Communities { get; set; } = new();

    /// <summary>Reranker scores aligned with <see cref="Communities"/>.</summary>
    [JsonPropertyName("community_reranker_scores")]
    public List<double> CommunityRerankerScores { get; set; } = new();

    /// <summary>Combines several result sets into one, concatenating each result type and its scores.</summary>
    public static SearchResults Merge(IEnumerable<SearchResults>? resultsList)
    {
        var merged = new SearchResults();
        if (resultsList is null)
        {
            return merged;
        }

        foreach (var result in resultsList)
        {
            merged.Edges.AddRange(result.Edges);
            merged.EdgeRerankerScores.AddRange(result.EdgeRerankerScores);
            merged.Nodes.AddRange(result.Nodes);
            merged.NodeRerankerScores.AddRange(result.NodeRerankerScores);
            merged.Episodes.AddRange(result.Episodes);
            merged.EpisodeRerankerScores.AddRange(result.EpisodeRerankerScores);
            merged.Communities.AddRange(result.Communities);
            merged.CommunityRerankerScores.AddRange(result.CommunityRerankerScores);
        }

        return merged;
    }
}
