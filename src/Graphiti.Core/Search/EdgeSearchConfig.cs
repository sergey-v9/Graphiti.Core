using System.Text.Json.Serialization;

namespace Graphiti.Core.Search;

/// <summary>How edges (facts) are retrieved and reranked within a <see cref="SearchConfig"/>.</summary>
public sealed class EdgeSearchConfig
{
    /// <summary>Retrieval methods to run; their results are combined by the reranker.</summary>
    [JsonPropertyName("search_methods")]
    public List<EdgeSearchMethod> SearchMethods { get; set; } = new();

    [JsonPropertyName("reranker")]
    public EdgeReranker Reranker { get; set; } = EdgeReranker.Rrf;

    [JsonPropertyName("sim_min_score")]
    public double SimMinScore { get; set; } = SearchConfiguration.DefaultMinScore;

    [JsonPropertyName("mmr_lambda")]
    public double MmrLambda { get; set; } = SearchConfiguration.DefaultMmrLambda;

    [JsonPropertyName("bfs_max_depth")]
    public int BfsMaxDepth { get; set; } = SearchConfiguration.MaxSearchDepth;
}
