namespace Graphiti.Core.Search;

/// <summary>
/// Conversions from the search method/reranker enums to the stable string wire values
/// used when serializing search configuration.
/// </summary>
internal static class SearchConfigurationEnumExtensions
{
    /// <summary>Returns the wire string for the edge search method.</summary>
    public static string ToWireValue(this EdgeSearchMethod source) =>
        source switch
        {
            EdgeSearchMethod.CosineSimilarity => "cosine_similarity",
            EdgeSearchMethod.Bm25 => "bm25",
            EdgeSearchMethod.Bfs => "breadth_first_search",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };

    /// <summary>Returns the wire string for the node search method.</summary>
    public static string ToWireValue(this NodeSearchMethod source) =>
        source switch
        {
            NodeSearchMethod.CosineSimilarity => "cosine_similarity",
            NodeSearchMethod.Bm25 => "bm25",
            NodeSearchMethod.Bfs => "breadth_first_search",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };

    /// <summary>Returns the wire string for the episode search method.</summary>
    public static string ToWireValue(this EpisodeSearchMethod source) =>
        source switch
        {
            EpisodeSearchMethod.Bm25 => "bm25",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };

    /// <summary>Returns the wire string for the community search method.</summary>
    public static string ToWireValue(this CommunitySearchMethod source) =>
        source switch
        {
            CommunitySearchMethod.CosineSimilarity => "cosine_similarity",
            CommunitySearchMethod.Bm25 => "bm25",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };

    /// <summary>Returns the wire string for the edge reranker.</summary>
    public static string ToWireValue(this EdgeReranker source) =>
        source switch
        {
            EdgeReranker.Rrf => "reciprocal_rank_fusion",
            EdgeReranker.NodeDistance => "node_distance",
            EdgeReranker.EpisodeMentions => "episode_mentions",
            EdgeReranker.Mmr => "mmr",
            EdgeReranker.CrossEncoder => "cross_encoder",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };

    /// <summary>Returns the wire string for the node reranker.</summary>
    public static string ToWireValue(this NodeReranker source) =>
        source switch
        {
            NodeReranker.Rrf => "reciprocal_rank_fusion",
            NodeReranker.NodeDistance => "node_distance",
            NodeReranker.EpisodeMentions => "episode_mentions",
            NodeReranker.Mmr => "mmr",
            NodeReranker.CrossEncoder => "cross_encoder",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };

    /// <summary>Returns the wire string for the episode reranker.</summary>
    public static string ToWireValue(this EpisodeReranker source) =>
        source switch
        {
            EpisodeReranker.Rrf => "reciprocal_rank_fusion",
            EpisodeReranker.CrossEncoder => "cross_encoder",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };

    /// <summary>Returns the wire string for the community reranker.</summary>
    public static string ToWireValue(this CommunityReranker source) =>
        source switch
        {
            CommunityReranker.Rrf => "reciprocal_rank_fusion",
            CommunityReranker.Mmr => "mmr",
            CommunityReranker.CrossEncoder => "cross_encoder",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
}
