namespace Graphiti.Core.Search;

/// <summary>Candidate-retrieval methods available when searching edges (facts).</summary>
public enum EdgeSearchMethod
{
    /// <summary>Semantic search over fact embeddings.</summary>
    CosineSimilarity,

    /// <summary>BM25-style lexical keyword search.</summary>
    Bm25,

    /// <summary>Breadth-first graph traversal from origin nodes.</summary>
    Bfs
}
