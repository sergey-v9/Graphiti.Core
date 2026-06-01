namespace Graphiti.Core.Search;

internal sealed class MaterializingSearchGraphDriver(
    IGraphDriver driver,
    IReadOnlyList<string>? rankGroupIds = null,
    bool materializeEmbeddingsForFulltext = false) : ISearchGraphDriver
{
    public async Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var compiledFilter = CompiledSearchFilter.Compile(searchFilter);
        var candidates = await SearchFallbackGraph.GetAllEntityNodesAsync(
            driver,
            groupIds,
            materializeEmbeddingsForFulltext,
            cancellationToken).ConfigureAwait(false);
        return ToSearchHits(Bm25TextScorer.Rank(
            candidates,
            node => SearchFilterMatcher.NodeMatches(node, compiledFilter),
            EntityNodeFulltextText,
            query,
            limit));
    }

    public async Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        CancellationToken cancellationToken = default)
    {
        var compiledFilter = CompiledSearchFilter.Compile(searchFilter);
        var candidates = await SearchFallbackGraph.GetAllEntityNodesAsync(
            driver,
            groupIds,
            withEmbeddings: true,
            cancellationToken).ConfigureAwait(false);
        var scorer = SearchUtilities.CreateCosineSimilarityScorer(searchVector);
        return ToSearchHits(SearchUtilities.TopByScore(
            candidates,
            node => SearchFilterMatcher.NodeMatches(node, compiledFilter),
            node => scorer.Score(node.NameEmbedding),
            limit,
            minScore,
            includeMinScore: false));
    }

    public async Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var compiledFilter = CompiledSearchFilter.Compile(searchFilter);
        var candidates = await SearchFallbackGraph.GetAllEntityEdgesAsync(
            driver,
            groupIds,
            materializeEmbeddingsForFulltext,
            cancellationToken).ConfigureAwait(false);
        var nodesByUuid = await SearchFallbackGraph.LoadEdgeEndpointNodeLookupAsync(
            driver,
            candidates,
            compiledFilter,
            cancellationToken).ConfigureAwait(false);
        return ToSearchHits(Bm25TextScorer.Rank(
            candidates,
            edge => SearchFilterMatcher.EdgeMatches(edge, compiledFilter, nodesByUuid),
            EntityEdgeFulltextText,
            query,
            limit));
    }

    public async Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        string? sourceNodeUuid = null,
        string? targetNodeUuid = null,
        CancellationToken cancellationToken = default)
    {
        var compiledFilter = CompiledSearchFilter.Compile(searchFilter);
        var candidates = FilterEdgesByEndpoint(
            await SearchFallbackGraph.GetAllEntityEdgesAsync(
                driver,
                groupIds,
                withEmbeddings: true,
                cancellationToken).ConfigureAwait(false),
            sourceNodeUuid,
            targetNodeUuid);
        var nodesByUuid = await SearchFallbackGraph.LoadEdgeEndpointNodeLookupAsync(
            driver,
            candidates,
            compiledFilter,
            cancellationToken).ConfigureAwait(false);
        var scorer = SearchUtilities.CreateCosineSimilarityScorer(searchVector);
        return ToSearchHits(SearchUtilities.TopByScore(
            candidates,
            edge => SearchFilterMatcher.EdgeMatches(edge, compiledFilter, nodesByUuid),
            edge => scorer.Score(edge.FactEmbedding),
            limit,
            minScore,
            includeMinScore: false));
    }

    public async Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesBfsAsync(
        IReadOnlyList<string>? originNodeUuids,
        SearchFilters searchFilter,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (originNodeUuids is null || originNodeUuids.Count == 0 || maxDepth < 1)
        {
            return Array.Empty<SearchHit<EntityNode>>();
        }

        var compiledFilter = CompiledSearchFilter.Compile(searchFilter);
        var candidates = await SearchFallbackGraph.GetAllEntityNodesAsync(
            driver,
            groupIds,
            withEmbeddings: false,
            cancellationToken).ConfigureAwait(false);
        var candidateByUuid = BuildNodeCandidateLookup(candidates, compiledFilter);
        var graph = await SearchFallbackGraph.LoadTraversalGraphAsync(driver, groupIds, cancellationToken).ConfigureAwait(false);
        return BuildNodeBfsHits(originNodeUuids, maxDepth, limit, graph, candidateByUuid);
    }

    public async Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesBfsAsync(
        IReadOnlyList<string>? originNodeUuids,
        SearchFilters searchFilter,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (originNodeUuids is null || originNodeUuids.Count == 0 || maxDepth < 1)
        {
            return Array.Empty<SearchHit<EntityEdge>>();
        }

        var compiledFilter = CompiledSearchFilter.Compile(searchFilter);
        var candidates = await SearchFallbackGraph.GetAllEntityEdgesAsync(
            driver,
            groupIds,
            withEmbeddings: false,
            cancellationToken).ConfigureAwait(false);
        var nodesByUuid = await SearchFallbackGraph.LoadEdgeEndpointNodeLookupAsync(
            driver,
            candidates,
            compiledFilter,
            cancellationToken).ConfigureAwait(false);
        var candidateByUuid = BuildEdgeCandidateLookup(candidates, compiledFilter, nodesByUuid);
        var graph = await SearchFallbackGraph.LoadTraversalGraphAsync(driver, groupIds, cancellationToken).ConfigureAwait(false);
        return BuildEdgeBfsHits(originNodeUuids, maxDepth, limit, graph, candidateByUuid);
    }

    public async Task<IReadOnlyList<SearchHit<EpisodicNode>>> SearchEpisodesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        _ = searchFilter;
        var candidates = await SearchFallbackGraph.GetAllEpisodesAsync(
            driver,
            groupIds,
            cancellationToken).ConfigureAwait(false);
        return ToSearchHits(Bm25TextScorer.Rank(
            candidates,
            EpisodeFulltextText,
            query,
            limit));
    }

    public async Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesFulltextAsync(
        string query,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var candidates = await SearchFallbackGraph.GetAllCommunityNodesAsync(
            driver,
            groupIds,
            materializeEmbeddingsForFulltext,
            cancellationToken).ConfigureAwait(false);
        return ToSearchHits(Bm25TextScorer.Rank(
            candidates,
            CommunityFulltextText,
            query,
            limit));
    }

    public async Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        CancellationToken cancellationToken = default)
    {
        var candidates = await SearchFallbackGraph.GetAllCommunityNodesAsync(
            driver,
            groupIds,
            withEmbeddings: true,
            cancellationToken).ConfigureAwait(false);
        var scorer = SearchUtilities.CreateCosineSimilarityScorer(searchVector);
        return ToSearchHits(SearchUtilities.TopByScore(
            candidates,
            node => scorer.Score(node.NameEmbedding),
            limit,
            minScore,
            includeMinScore: false));
    }

    public async Task<IReadOnlyList<SearchRank>> RankNodeDistanceAsync(
        IReadOnlyList<string> nodeUuids,
        string centerNodeUuid,
        float minScore = 0,
        CancellationToken cancellationToken = default)
    {
        var graphEdges = await SearchFallbackGraph.GetAllEntityEdgesAsync(
            driver,
            rankGroupIds,
            withEmbeddings: false,
            cancellationToken).ConfigureAwait(false);
        var adjacentNodeUuids = BuildAdjacentNodeLookup(graphEdges, centerNodeUuid);
        var centerRanks = new List<SearchRank>(1);
        var adjacentRanks = new List<SearchRank>(nodeUuids.Count);
        var distantRanks = new List<SearchRank>(nodeUuids.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < nodeUuids.Count; i++)
        {
            var uuid = nodeUuids[i];
            if (!seen.Add(uuid))
            {
                continue;
            }

            var score = NodeDistanceScore(adjacentNodeUuids, uuid, centerNodeUuid);
            if (score >= minScore)
            {
                AddNodeDistanceRank(centerRanks, adjacentRanks, distantRanks, uuid, score);
            }
        }

        return CombineNodeDistanceRanks(centerRanks, adjacentRanks, distantRanks);
    }

    public async Task<IReadOnlyList<SearchRank>> RankNodeEpisodeMentionsAsync(
        IReadOnlyList<string> nodeUuids,
        float minScore = 0,
        CancellationToken cancellationToken = default)
    {
        var episodicEdges = await SearchFallbackGraph.GetAllEpisodicEdgesAsync(
            driver,
            rankGroupIds,
            cancellationToken).ConfigureAwait(false);
        var mentionsByNodeUuid = BuildMentionCountLookup(episodicEdges);
        var ranks = new List<(SearchRank Rank, int Index)>(nodeUuids.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < nodeUuids.Count; i++)
        {
            var uuid = nodeUuids[i];
            if (!seen.Add(uuid))
            {
                continue;
            }

            var mentions = mentionsByNodeUuid.GetValueOrDefault(uuid);
            var score = mentions > 0 ? mentions : float.PositiveInfinity;
            if (score >= minScore)
            {
                ranks.Add((new SearchRank(uuid, score), i));
            }
        }

        ranks.Sort(static (left, right) =>
        {
            var scoreComparison = left.Rank.Score.CompareTo(right.Rank.Score);
            return scoreComparison != 0
                ? scoreComparison
                : left.Index.CompareTo(right.Index);
        });
        return ToSearchRanks(ranks);
    }

    private static List<SearchHit<T>> ToSearchHits<T>(IReadOnlyList<(T Item, float Score)> ranked)
    {
        var results = new List<SearchHit<T>>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            results.Add(new SearchHit<T>(ranked[i].Item, ranked[i].Score));
        }

        return results;
    }

    private static List<EntityEdge> FilterEdgesByEndpoint(
        IReadOnlyList<EntityEdge> candidates,
        string? sourceNodeUuid,
        string? targetNodeUuid)
    {
        var results = new List<EntityEdge>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            var edge = candidates[i];
            if ((sourceNodeUuid is null || edge.SourceNodeUuid == sourceNodeUuid)
                && (targetNodeUuid is null || edge.TargetNodeUuid == targetNodeUuid))
            {
                results.Add(edge);
            }
        }

        return results;
    }

    private static Dictionary<string, EntityNode> BuildNodeCandidateLookup(
        IReadOnlyList<EntityNode> candidates,
        CompiledSearchFilter compiledFilter)
    {
        var candidateByUuid = new Dictionary<string, EntityNode>(candidates.Count, StringComparer.Ordinal);
        for (var i = 0; i < candidates.Count; i++)
        {
            var node = candidates[i];
            if (SearchFilterMatcher.NodeMatches(node, compiledFilter))
            {
                candidateByUuid.Add(node.Uuid, node);
            }
        }

        return candidateByUuid;
    }

    private static Dictionary<string, EntityEdge> BuildEdgeCandidateLookup(
        IReadOnlyList<EntityEdge> candidates,
        CompiledSearchFilter compiledFilter,
        IReadOnlyDictionary<string, EntityNode> nodesByUuid)
    {
        var candidateByUuid = new Dictionary<string, EntityEdge>(candidates.Count, StringComparer.Ordinal);
        for (var i = 0; i < candidates.Count; i++)
        {
            var edge = candidates[i];
            if (SearchFilterMatcher.EdgeMatches(edge, compiledFilter, nodesByUuid))
            {
                candidateByUuid.Add(edge.Uuid, edge);
            }
        }

        return candidateByUuid;
    }

    private static List<SearchHit<EntityNode>> BuildNodeBfsHits(
        IReadOnlyList<string> originNodeUuids,
        int maxDepth,
        int limit,
        SearchFallbackGraph.TraversalGraph graph,
        Dictionary<string, EntityNode> candidateByUuid)
    {
        var results = new List<SearchHit<EntityNode>>(ResultCapacity(limit, candidateByUuid.Count));
        if (limit <= 0)
        {
            return results;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in SearchFallbackGraph.TraverseBreadthFirst(originNodeUuids, maxDepth, graph))
        {
            if (step.TargetNodeUuid is null
                || !step.TargetMatchesOriginGroup
                || !candidateByUuid.TryGetValue(step.TargetNodeUuid, out var node)
                || !seen.Add(step.TargetNodeUuid))
            {
                continue;
            }

            results.Add(new SearchHit<EntityNode>(node, 1f / step.Depth));
            if (results.Count >= limit)
            {
                break;
            }
        }

        return results;
    }

    private static List<SearchHit<EntityEdge>> BuildEdgeBfsHits(
        IReadOnlyList<string> originNodeUuids,
        int maxDepth,
        int limit,
        SearchFallbackGraph.TraversalGraph graph,
        Dictionary<string, EntityEdge> candidateByUuid)
    {
        var results = new List<SearchHit<EntityEdge>>(ResultCapacity(limit, candidateByUuid.Count));
        if (limit <= 0)
        {
            return results;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in SearchFallbackGraph.TraverseBreadthFirst(originNodeUuids, maxDepth, graph))
        {
            if (step.Edge is null
                || !candidateByUuid.TryGetValue(step.Edge.Uuid, out var edge)
                || !seen.Add(step.Edge.Uuid))
            {
                continue;
            }

            results.Add(new SearchHit<EntityEdge>(edge, 1f / step.Depth));
            if (results.Count >= limit)
            {
                break;
            }
        }

        return results;
    }

    private static int ResultCapacity(int limit, int maximum) =>
        limit <= 0 ? 0 : Math.Min(limit, maximum);

    private static HashSet<string> BuildAdjacentNodeLookup(
        IReadOnlyList<EntityEdge> graphEdges,
        string centerNodeUuid)
    {
        var adjacentNodeUuids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < graphEdges.Count; i++)
        {
            var edge = graphEdges[i];
            if (edge.SourceNodeUuid == centerNodeUuid)
            {
                adjacentNodeUuids.Add(edge.TargetNodeUuid);
            }
            else if (edge.TargetNodeUuid == centerNodeUuid)
            {
                adjacentNodeUuids.Add(edge.SourceNodeUuid);
            }
        }

        return adjacentNodeUuids;
    }

    private static float NodeDistanceScore(
        HashSet<string> adjacentNodeUuids,
        string nodeUuid,
        string centerNodeUuid)
    {
        if (nodeUuid == centerNodeUuid)
        {
            return 10;
        }

        return adjacentNodeUuids.Contains(nodeUuid) ? 1 : 0;
    }

    private static void AddNodeDistanceRank(
        List<SearchRank> centerRanks,
        List<SearchRank> adjacentRanks,
        List<SearchRank> distantRanks,
        string uuid,
        float score)
    {
        var rank = new SearchRank(uuid, score);
        if (score >= 10)
        {
            centerRanks.Add(rank);
        }
        else if (score >= 1)
        {
            adjacentRanks.Add(rank);
        }
        else
        {
            distantRanks.Add(rank);
        }
    }

    private static List<SearchRank> CombineNodeDistanceRanks(
        List<SearchRank> centerRanks,
        List<SearchRank> adjacentRanks,
        List<SearchRank> distantRanks)
    {
        var results = new List<SearchRank>(
            centerRanks.Count + adjacentRanks.Count + distantRanks.Count);
        results.AddRange(centerRanks);
        results.AddRange(adjacentRanks);
        results.AddRange(distantRanks);
        return results;
    }

    private static Dictionary<string, int> BuildMentionCountLookup(IReadOnlyList<EpisodicEdge> episodicEdges)
    {
        var mentionsByNodeUuid = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < episodicEdges.Count; i++)
        {
            var targetNodeUuid = episodicEdges[i].TargetNodeUuid;
            mentionsByNodeUuid[targetNodeUuid] = mentionsByNodeUuid.GetValueOrDefault(targetNodeUuid) + 1;
        }

        return mentionsByNodeUuid;
    }

    private static List<SearchRank> ToSearchRanks(List<(SearchRank Rank, int Index)> ranks)
    {
        var results = new List<SearchRank>(ranks.Count);
        for (var i = 0; i < ranks.Count; i++)
        {
            results.Add(ranks[i].Rank);
        }

        return results;
    }

    private static string EntityNodeFulltextText(EntityNode node) =>
        $"{node.Name} {node.Summary} {node.GroupId}";

    private static string EntityEdgeFulltextText(EntityEdge edge) =>
        $"{edge.Name} {edge.Fact} {edge.GroupId}";

    private static string EpisodeFulltextText(EpisodicNode episode) =>
        $"{episode.Content} {episode.Source.ToWireValue()} {episode.SourceDescription} {episode.GroupId}";

    private static string CommunityFulltextText(CommunityNode community) =>
        $"{community.Name} {community.GroupId}";
}
