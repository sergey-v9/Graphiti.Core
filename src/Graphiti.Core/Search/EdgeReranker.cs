namespace Graphiti.Core.Search;

/// <summary>Strategies for reranking edge (fact) search candidates into a final order.</summary>
public enum EdgeReranker
{
    /// <summary>Reciprocal rank fusion across the per-method result lists.</summary>
    Rrf,

    /// <summary>Rerank by graph distance from a center node.</summary>
    NodeDistance,

    /// <summary>Rerank by how many episodes mention the candidate.</summary>
    EpisodeMentions,

    /// <summary>Maximal marginal relevance, balancing relevance and diversity.</summary>
    Mmr,

    /// <summary>Rerank using a cross-encoder model.</summary>
    CrossEncoder
}
