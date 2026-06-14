using System.Text.Json.Serialization;

namespace Graphiti.Core.Search;

/// <summary>How entity nodes are retrieved and reranked within a <see cref="SearchConfig"/>.</summary>
public sealed class NodeSearchConfig
{
    /// <summary>Retrieval methods to run; their results are combined by the reranker.</summary>
    [JsonPropertyName("search_methods")]
    public List<NodeSearchMethod> SearchMethods { get; set; } = new();

    /// <summary>Strategy used to combine and reorder the results from the configured search methods.</summary>
    [JsonPropertyName("reranker")]
    public NodeReranker Reranker { get; set; } = NodeReranker.Rrf;

    /// <summary>Minimum cosine-similarity score an embedding match must reach to be retained.</summary>
    [JsonPropertyName("sim_min_score")]
    public double SimMinScore { get; set; } = SearchConfiguration.DefaultMinScore;

    /// <summary>Trade-off (0..1) between relevance and diversity when the MMR reranker is used; higher favors relevance.</summary>
    [JsonPropertyName("mmr_lambda")]
    public double MmrLambda { get; set; } = SearchConfiguration.DefaultMmrLambda;

    /// <summary>Maximum traversal depth for breadth-first search methods.</summary>
    [JsonPropertyName("bfs_max_depth")]
    public int BfsMaxDepth { get; set; } = SearchConfiguration.MaxSearchDepth;
}
