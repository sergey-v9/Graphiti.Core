using System.Text.Json.Serialization;

namespace Graphiti.Core.Search;

/// <summary>
/// Top-level search configuration. Enable any combination of edge, node, episode, and community
/// search by supplying the corresponding per-type config; results are limited and score-filtered by
/// <see cref="Limit"/> and <see cref="RerankerMinScore"/>. See <c>SearchConfigRecipes</c> for ready-made
/// presets matching the Python implementation.
/// </summary>
public sealed class SearchConfig
{
    /// <summary>Edge (fact) search settings, or <c>null</c> to skip edge search.</summary>
    [JsonPropertyName("edge_config")]
    public EdgeSearchConfig? EdgeConfig { get; set; }

    /// <summary>Entity-node search settings, or <c>null</c> to skip node search.</summary>
    [JsonPropertyName("node_config")]
    public NodeSearchConfig? NodeConfig { get; set; }

    /// <summary>Episode search settings, or <c>null</c> to skip episode search.</summary>
    [JsonPropertyName("episode_config")]
    public EpisodeSearchConfig? EpisodeConfig { get; set; }

    /// <summary>Community search settings, or <c>null</c> to skip community search.</summary>
    [JsonPropertyName("community_config")]
    public CommunitySearchConfig? CommunityConfig { get; set; }

    /// <summary>Maximum number of results per result type.</summary>
    [JsonPropertyName("limit")]
    public int Limit { get; set; } = SearchConfiguration.DefaultSearchLimit;

    /// <summary>Minimum reranker score a result must reach to be included.</summary>
    [JsonPropertyName("reranker_min_score")]
    public double RerankerMinScore { get; set; }
}
