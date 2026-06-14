namespace Graphiti.Core.Search;

/// <summary>
/// Ready-made <see cref="SearchConfig"/> presets mirroring the Python <c>search_config_recipes</c>
/// module. Each recipe selects which result types are searched (edges/nodes/episodes/communities),
/// the retrieval methods (BM25 full-text, cosine-similarity embeddings, and breadth-first traversal),
/// and the reranker (RRF, MMR, graph node-distance, episode-mention count, or cross-encoder). Use
/// these as a starting point and customize the returned config as needed.
/// </summary>
public static class SearchConfigRecipes
{
    /// <summary>
    /// Searches edges, nodes, episodes, and communities with BM25 + cosine similarity, fused with
    /// Reciprocal Rank Fusion. A balanced general-purpose default across all result types.
    /// </summary>
    public static SearchConfig CombinedHybridSearchRrf =>
        new()
        {
            EdgeConfig = new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity },
                Reranker = EdgeReranker.Rrf
            },
            NodeConfig = new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity },
                Reranker = NodeReranker.Rrf
            },
            EpisodeConfig = new EpisodeSearchConfig
            {
                SearchMethods = { EpisodeSearchMethod.Bm25 },
                Reranker = EpisodeReranker.Rrf
            },
            CommunityConfig = new CommunitySearchConfig
            {
                SearchMethods = { CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity },
                Reranker = CommunityReranker.Rrf
            }
        };

    /// <summary>
    /// Like <see cref="CombinedHybridSearchRrf"/> but reranks edges, nodes, and communities with
    /// Maximal Marginal Relevance to favor diversity among results. Episodes still use RRF.
    /// </summary>
    public static SearchConfig CombinedHybridSearchMmr =>
        new()
        {
            EdgeConfig = new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity },
                Reranker = EdgeReranker.Mmr,
                MmrLambda = 1
            },
            NodeConfig = new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity },
                Reranker = NodeReranker.Mmr,
                MmrLambda = 1
            },
            EpisodeConfig = new EpisodeSearchConfig
            {
                SearchMethods = { EpisodeSearchMethod.Bm25 },
                Reranker = EpisodeReranker.Rrf
            },
            CommunityConfig = new CommunitySearchConfig
            {
                SearchMethods = { CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity },
                Reranker = CommunityReranker.Mmr,
                MmrLambda = 1
            }
        };

    /// <summary>
    /// Searches all result types (edges and nodes also via breadth-first traversal) and reranks every
    /// type with the configured cross-encoder for highest relevance at higher latency/cost.
    /// </summary>
    public static SearchConfig CombinedHybridSearchCrossEncoder =>
        new()
        {
            EdgeConfig = new EdgeSearchConfig
            {
                SearchMethods =
                {
                    EdgeSearchMethod.Bm25,
                    EdgeSearchMethod.CosineSimilarity,
                    EdgeSearchMethod.Bfs
                },
                Reranker = EdgeReranker.CrossEncoder
            },
            NodeConfig = new NodeSearchConfig
            {
                SearchMethods =
                {
                    NodeSearchMethod.Bm25,
                    NodeSearchMethod.CosineSimilarity,
                    NodeSearchMethod.Bfs
                },
                Reranker = NodeReranker.CrossEncoder
            },
            EpisodeConfig = new EpisodeSearchConfig
            {
                SearchMethods = { EpisodeSearchMethod.Bm25 },
                Reranker = EpisodeReranker.CrossEncoder
            },
            CommunityConfig = new CommunitySearchConfig
            {
                SearchMethods = { CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity },
                Reranker = CommunityReranker.CrossEncoder
            }
        };

    /// <summary>Edges only: BM25 + cosine similarity fused with Reciprocal Rank Fusion.</summary>
    public static SearchConfig EdgeHybridSearchRrf =>
        new()
        {
            EdgeConfig = new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity },
                Reranker = EdgeReranker.Rrf
            }
        };

    /// <summary>Edges only: BM25 + cosine similarity reranked with Maximal Marginal Relevance for diversity.</summary>
    public static SearchConfig EdgeHybridSearchMmr =>
        new()
        {
            EdgeConfig = new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity },
                Reranker = EdgeReranker.Mmr
            }
        };

    /// <summary>
    /// Edges only: BM25 + cosine similarity reranked by graph distance from a center node, boosting
    /// facts close to it. Requires a center node UUID to be supplied at search time.
    /// </summary>
    public static SearchConfig EdgeHybridSearchNodeDistance =>
        new()
        {
            EdgeConfig = new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity },
                Reranker = EdgeReranker.NodeDistance
            }
        };

    /// <summary>Edges only: BM25 + cosine similarity reranked by how many episodes mention each fact.</summary>
    public static SearchConfig EdgeHybridSearchEpisodeMentions =>
        new()
        {
            EdgeConfig = new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity },
                Reranker = EdgeReranker.EpisodeMentions
            }
        };

    /// <summary>
    /// Edges only: BM25 + cosine similarity + breadth-first traversal, reranked with the cross-encoder
    /// and capped at 10 results.
    /// </summary>
    public static SearchConfig EdgeHybridSearchCrossEncoder =>
        new()
        {
            EdgeConfig = new EdgeSearchConfig
            {
                SearchMethods =
                {
                    EdgeSearchMethod.Bm25,
                    EdgeSearchMethod.CosineSimilarity,
                    EdgeSearchMethod.Bfs
                },
                Reranker = EdgeReranker.CrossEncoder
            },
            Limit = 10
        };

    /// <summary>Nodes only: BM25 + cosine similarity fused with Reciprocal Rank Fusion.</summary>
    public static SearchConfig NodeHybridSearchRrf =>
        new()
        {
            NodeConfig = new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity },
                Reranker = NodeReranker.Rrf
            }
        };

    /// <summary>Nodes only: BM25 + cosine similarity reranked with Maximal Marginal Relevance for diversity.</summary>
    public static SearchConfig NodeHybridSearchMmr =>
        new()
        {
            NodeConfig = new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity },
                Reranker = NodeReranker.Mmr
            }
        };

    /// <summary>
    /// Nodes only: BM25 + cosine similarity reranked by graph distance from a center node. Requires a
    /// center node UUID to be supplied at search time.
    /// </summary>
    public static SearchConfig NodeHybridSearchNodeDistance =>
        new()
        {
            NodeConfig = new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity },
                Reranker = NodeReranker.NodeDistance
            }
        };

    /// <summary>Nodes only: BM25 + cosine similarity reranked by how many episodes mention each entity.</summary>
    public static SearchConfig NodeHybridSearchEpisodeMentions =>
        new()
        {
            NodeConfig = new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity },
                Reranker = NodeReranker.EpisodeMentions
            }
        };

    /// <summary>
    /// Nodes only: BM25 + cosine similarity + breadth-first traversal, reranked with the cross-encoder
    /// and capped at 10 results.
    /// </summary>
    public static SearchConfig NodeHybridSearchCrossEncoder =>
        new()
        {
            NodeConfig = new NodeSearchConfig
            {
                SearchMethods =
                {
                    NodeSearchMethod.Bm25,
                    NodeSearchMethod.CosineSimilarity,
                    NodeSearchMethod.Bfs
                },
                Reranker = NodeReranker.CrossEncoder
            },
            Limit = 10
        };

    /// <summary>Communities only: BM25 + cosine similarity fused with Reciprocal Rank Fusion.</summary>
    public static SearchConfig CommunityHybridSearchRrf =>
        new()
        {
            CommunityConfig = new CommunitySearchConfig
            {
                SearchMethods = { CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity },
                Reranker = CommunityReranker.Rrf
            }
        };

    /// <summary>Communities only: BM25 + cosine similarity reranked with Maximal Marginal Relevance for diversity.</summary>
    public static SearchConfig CommunityHybridSearchMmr =>
        new()
        {
            CommunityConfig = new CommunitySearchConfig
            {
                SearchMethods = { CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity },
                Reranker = CommunityReranker.Mmr
            }
        };

    /// <summary>
    /// Communities only: BM25 + cosine similarity reranked with the cross-encoder and capped at
    /// 3 results.
    /// </summary>
    public static SearchConfig CommunityHybridSearchCrossEncoder =>
        new()
        {
            CommunityConfig = new CommunitySearchConfig
            {
                SearchMethods = { CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity },
                Reranker = CommunityReranker.CrossEncoder
            },
            Limit = 3
        };
}
