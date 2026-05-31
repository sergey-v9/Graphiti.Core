namespace Graphiti.Core.Search;

/// <summary>Candidate-retrieval methods available when searching entity nodes.</summary>
public enum NodeSearchMethod
{
    /// <summary>Semantic search over name embeddings.</summary>
    CosineSimilarity,

    /// <summary>BM25-style lexical keyword search.</summary>
    Bm25,

    /// <summary>Breadth-first graph traversal from origin nodes.</summary>
    Bfs
}
