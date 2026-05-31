namespace Graphiti.Core.Search;

public static class SearchConfigRecipes
{
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

    public static SearchConfig EdgeHybridSearchRrf =>
        new()
        {
            EdgeConfig = new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity },
                Reranker = EdgeReranker.Rrf
            }
        };

    public static SearchConfig EdgeHybridSearchMmr =>
        new()
        {
            EdgeConfig = new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity },
                Reranker = EdgeReranker.Mmr
            }
        };

    public static SearchConfig EdgeHybridSearchNodeDistance =>
        new()
        {
            EdgeConfig = new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity },
                Reranker = EdgeReranker.NodeDistance
            }
        };

    public static SearchConfig EdgeHybridSearchEpisodeMentions =>
        new()
        {
            EdgeConfig = new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity },
                Reranker = EdgeReranker.EpisodeMentions
            }
        };

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

    public static SearchConfig NodeHybridSearchRrf =>
        new()
        {
            NodeConfig = new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity },
                Reranker = NodeReranker.Rrf
            }
        };

    public static SearchConfig NodeHybridSearchMmr =>
        new()
        {
            NodeConfig = new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity },
                Reranker = NodeReranker.Mmr
            }
        };

    public static SearchConfig NodeHybridSearchNodeDistance =>
        new()
        {
            NodeConfig = new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity },
                Reranker = NodeReranker.NodeDistance
            }
        };

    public static SearchConfig NodeHybridSearchEpisodeMentions =>
        new()
        {
            NodeConfig = new NodeSearchConfig
            {
                SearchMethods = { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity },
                Reranker = NodeReranker.EpisodeMentions
            }
        };

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

    public static SearchConfig CommunityHybridSearchRrf =>
        new()
        {
            CommunityConfig = new CommunitySearchConfig
            {
                SearchMethods = { CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity },
                Reranker = CommunityReranker.Rrf
            }
        };

    public static SearchConfig CommunityHybridSearchMmr =>
        new()
        {
            CommunityConfig = new CommunitySearchConfig
            {
                SearchMethods = { CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity },
                Reranker = CommunityReranker.Mmr
            }
        };

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
