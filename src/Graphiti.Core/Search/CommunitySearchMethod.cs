namespace Graphiti.Core.Search;

/// <summary>Candidate-retrieval methods available when searching communities.</summary>
public enum CommunitySearchMethod
{
    /// <summary>Semantic search over community name embeddings.</summary>
    CosineSimilarity,

    /// <summary>BM25-style lexical keyword search.</summary>
    Bm25
}
