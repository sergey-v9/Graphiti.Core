using System.Diagnostics;

namespace Graphiti.Core.Search;

internal static class SearchRetrievalRunner
{
    internal static async Task<List<(EntityEdge Item, float Score)>> GetEdgeFulltextRankedAsync(
        ISearchGraphDriver searchDriver,
        string query,
        IReadOnlyList<string>? groupIds,
        SearchFilters searchFilter,
        int limit,
        CancellationToken cancellationToken)
    {
        using var activity = StartRetrievalActivity(
            "EdgeFulltext",
            "edge",
            EdgeSearchMethod.Bm25.ToWireValue(),
            groupIds,
            limit,
            queryLength: query.Length);
        return await RunRetrievalAsync(
            activity,
            () => searchDriver.SearchEntityEdgesFulltextAsync(
                query,
                searchFilter,
                groupIds,
                limit,
                cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<List<(EntityEdge Item, float Score)>> GetEdgeVectorRankedAsync(
        ISearchGraphDriver searchDriver,
        IReadOnlyList<float> queryVector,
        IReadOnlyList<string>? groupIds,
        SearchFilters searchFilter,
        int limit,
        float minScore,
        CancellationToken cancellationToken)
    {
        using var activity = StartRetrievalActivity(
            "EdgeVector",
            "edge",
            EdgeSearchMethod.CosineSimilarity.ToWireValue(),
            groupIds,
            limit,
            minScore,
            queryVectorDimension: queryVector.Count);
        return await RunRetrievalAsync(
            activity,
            () => searchDriver.SearchEntityEdgesByEmbeddingAsync(
                queryVector,
                searchFilter,
                groupIds,
                limit,
                minScore,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<List<(EntityNode Item, float Score)>> GetNodeFulltextRankedAsync(
        ISearchGraphDriver searchDriver,
        string query,
        IReadOnlyList<string>? groupIds,
        SearchFilters searchFilter,
        int limit,
        CancellationToken cancellationToken)
    {
        using var activity = StartRetrievalActivity(
            "NodeFulltext",
            "node",
            NodeSearchMethod.Bm25.ToWireValue(),
            groupIds,
            limit,
            queryLength: query.Length);
        return await RunRetrievalAsync(
            activity,
            () => searchDriver.SearchEntityNodesFulltextAsync(
                query,
                searchFilter,
                groupIds,
                limit,
                cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<List<(EntityNode Item, float Score)>> GetNodeVectorRankedAsync(
        ISearchGraphDriver searchDriver,
        IReadOnlyList<float> queryVector,
        IReadOnlyList<string>? groupIds,
        SearchFilters searchFilter,
        int limit,
        float minScore,
        CancellationToken cancellationToken)
    {
        using var activity = StartRetrievalActivity(
            "NodeVector",
            "node",
            NodeSearchMethod.CosineSimilarity.ToWireValue(),
            groupIds,
            limit,
            minScore,
            queryVectorDimension: queryVector.Count);
        return await RunRetrievalAsync(
            activity,
            () => searchDriver.SearchEntityNodesByEmbeddingAsync(
                queryVector,
                searchFilter,
                groupIds,
                limit,
                minScore,
                cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<List<(EpisodicNode Item, float Score)>> GetEpisodeFulltextRankedAsync(
        ISearchGraphDriver searchDriver,
        string query,
        IReadOnlyList<string>? groupIds,
        SearchFilters searchFilter,
        int limit,
        CancellationToken cancellationToken)
    {
        using var activity = StartRetrievalActivity(
            "EpisodeFulltext",
            "episode",
            EpisodeSearchMethod.Bm25.ToWireValue(),
            groupIds,
            limit,
            queryLength: query.Length);
        return await RunRetrievalAsync(
            activity,
            () => searchDriver.SearchEpisodesFulltextAsync(
                query,
                searchFilter,
                groupIds,
                limit,
                cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<List<(CommunityNode Item, float Score)>> GetCommunityFulltextRankedAsync(
        ISearchGraphDriver searchDriver,
        string query,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken)
    {
        using var activity = StartRetrievalActivity(
            "CommunityFulltext",
            "community",
            CommunitySearchMethod.Bm25.ToWireValue(),
            groupIds,
            limit,
            queryLength: query.Length);
        return await RunRetrievalAsync(
            activity,
            () => searchDriver.SearchCommunitiesFulltextAsync(
                query,
                groupIds,
                limit,
                cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<List<(CommunityNode Item, float Score)>> GetCommunityVectorRankedAsync(
        ISearchGraphDriver searchDriver,
        IReadOnlyList<float> queryVector,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        CancellationToken cancellationToken)
    {
        using var activity = StartRetrievalActivity(
            "CommunityVector",
            "community",
            CommunitySearchMethod.CosineSimilarity.ToWireValue(),
            groupIds,
            limit,
            minScore,
            queryVectorDimension: queryVector.Count);
        return await RunRetrievalAsync(
            activity,
            () => searchDriver.SearchCommunitiesByEmbeddingAsync(
                queryVector,
                groupIds,
                limit,
                minScore,
                cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<List<(EntityEdge Item, float Score)>> EdgeBfsSearchAsync(
        ISearchGraphDriver searchDriver,
        IReadOnlyList<string>? originNodeUuids,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        SearchFilters searchFilter,
        int limit,
        CancellationToken cancellationToken)
    {
        if (originNodeUuids is null || originNodeUuids.Count == 0 || maxDepth < 1)
        {
            return new List<(EntityEdge Item, float Score)>();
        }

        using var activity = StartRetrievalActivity(
            "EdgeBfs",
            "edge",
            EdgeSearchMethod.Bfs.ToWireValue(),
            groupIds,
            limit,
            originCount: originNodeUuids.Count,
            maxDepth: maxDepth);
        return await RunRetrievalAsync(
            activity,
            () => searchDriver.SearchEntityEdgesBfsAsync(
                originNodeUuids,
                searchFilter,
                maxDepth,
                groupIds,
                limit,
                cancellationToken)).ConfigureAwait(false);
    }

    internal static async Task<List<(EntityNode Item, float Score)>> NodeBfsSearchAsync(
        ISearchGraphDriver searchDriver,
        IReadOnlyList<string>? originNodeUuids,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        SearchFilters searchFilter,
        int limit,
        CancellationToken cancellationToken)
    {
        if (originNodeUuids is null || originNodeUuids.Count == 0 || maxDepth < 1)
        {
            return new List<(EntityNode Item, float Score)>();
        }

        using var activity = StartRetrievalActivity(
            "NodeBfs",
            "node",
            NodeSearchMethod.Bfs.ToWireValue(),
            groupIds,
            limit,
            originCount: originNodeUuids.Count,
            maxDepth: maxDepth);
        return await RunRetrievalAsync(
            activity,
            () => searchDriver.SearchEntityNodesBfsAsync(
                originNodeUuids,
                searchFilter,
                maxDepth,
                groupIds,
                limit,
                cancellationToken)).ConfigureAwait(false);
    }

    private static Activity? StartRetrievalActivity(
        string operation,
        string scope,
        string method,
        IReadOnlyList<string>? groupIds,
        int limit,
        float? minScore = null,
        int? queryLength = null,
        int? queryVectorDimension = null,
        int? originCount = null,
        int? maxDepth = null)
    {
        var activity = GraphitiTelemetry.StartActivity($"SearchEngine.Retrieve.{operation}");
        activity?.SetTag("graphiti.search.scope", scope);
        activity?.SetTag("graphiti.search.method", method);
        activity?.SetTag("graphiti.limit", limit);
        if (minScore is not null)
        {
            activity?.SetTag("graphiti.search.min_score", minScore.Value);
        }

        if (queryLength is not null)
        {
            activity?.SetTag("graphiti.query.length", queryLength.Value);
        }

        if (queryVectorDimension is not null)
        {
            activity?.SetTag("graphiti.query_vector.dimension", queryVectorDimension.Value);
        }

        if (originCount is not null)
        {
            activity?.SetTag("graphiti.search.origin_count", originCount.Value);
        }

        if (maxDepth is not null)
        {
            activity?.SetTag("graphiti.search.bfs_max_depth", maxDepth.Value);
        }

        GraphitiTelemetry.SetGroupIds(activity, groupIds);
        return activity;
    }

    private static async Task<List<(T Item, float Score)>> RunRetrievalAsync<T>(
        Activity? activity,
        Func<Task<IReadOnlyList<SearchHit<T>>>> search)
    {
        try
        {
            var hits = await search().ConfigureAwait(false);
            return CompleteRetrieval(activity, hits);
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private static List<(T Item, float Score)> CompleteRetrieval<T>(
        Activity? activity,
        IReadOnlyList<SearchHit<T>> hits)
    {
        var ranked = SearchResultComposer.ToRankedList(hits);
        activity?.SetTag("graphiti.result.count", ranked.Count);
        GraphitiTelemetry.SetOk(activity);
        return ranked;
    }

}
