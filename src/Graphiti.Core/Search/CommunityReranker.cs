namespace Graphiti.Core.Search;

/// <summary>Strategies for reranking community search candidates.</summary>
public enum CommunityReranker
{
    /// <summary>Reciprocal rank fusion across the per-method result lists.</summary>
    Rrf,

    /// <summary>Maximal marginal relevance, balancing relevance and diversity.</summary>
    Mmr,

    /// <summary>Rerank using a cross-encoder model.</summary>
    CrossEncoder
}
