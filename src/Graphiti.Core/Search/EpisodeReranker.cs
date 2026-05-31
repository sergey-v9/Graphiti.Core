namespace Graphiti.Core.Search;

/// <summary>Strategies for reranking episode search candidates.</summary>
public enum EpisodeReranker
{
    /// <summary>Reciprocal rank fusion across the per-method result lists.</summary>
    Rrf,

    /// <summary>Rerank using a cross-encoder model.</summary>
    CrossEncoder
}
