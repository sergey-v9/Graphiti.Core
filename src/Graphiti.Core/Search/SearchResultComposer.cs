using System.Diagnostics;

namespace Graphiti.Core.Search;

internal static class SearchResultComposer
{
    internal static async Task<List<(T Item, float Score)>> ApplyCrossEncoderRerankerAsync<T>(
        ICrossEncoderClient crossEncoder,
        string query,
        IReadOnlyList<(T Item, float Score)> ranked,
        Func<T, string> passageSelector,
        float minScore,
        CancellationToken cancellationToken)
    {
        using var activity = StartRerankerActivity(
            "CrossEncoder",
            "cross_encoder",
            ranked.Count,
            minScore);
        activity?.SetTag("graphiti.query.length", query.Length);

        try
        {
            var passages = new List<string>(ranked.Count);
            for (var i = 0; i < ranked.Count; i++)
            {
                passages.Add(passageSelector(ranked[i].Item));
            }

            var crossRanks = await crossEncoder
                .RankIndexedAsync(query, passages, cancellationToken)
                .ConfigureAwait(false);
            var seen = new bool[ranked.Count];
            var reranked = new List<(T Item, float Score, int Index)>(
                Math.Min(crossRanks.Count, ranked.Count));

            foreach (var rank in crossRanks)
            {
                if ((uint)rank.Index >= (uint)ranked.Count || seen[rank.Index])
                {
                    continue;
                }

                seen[rank.Index] = true;
                if (rank.Score >= minScore)
                {
                    reranked.Add((ranked[rank.Index].Item, rank.Score, rank.Index));
                }
            }

            reranked.Sort(static (left, right) =>
            {
                var scoreComparison = right.Score.CompareTo(left.Score);
                return scoreComparison != 0
                    ? scoreComparison
                    : left.Index.CompareTo(right.Index);
            });
            var results = new List<(T Item, float Score)>(reranked.Count);
            foreach (var item in reranked)
            {
                results.Add((item.Item, item.Score));
            }

            activity?.SetTag("graphiti.result.count", results.Count);
            GraphitiTelemetry.SetOk(activity);
            return results;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    internal static List<(T Item, float Score)> FuseRanks<T>(
        IEnumerable<IReadOnlyList<(T Item, float Score)>> rankedLists,
        Func<T, string> keySelector,
        int limit,
        float minScore = 0)
    {
        var fused = SearchUtilities.ReciprocalRankFusionFromRankedItems(
            rankedLists,
            keySelector,
            limit,
            minScore: minScore);
        return fused;
    }

    internal static List<(T Item, float Score)> FuseRanks<T>(
        IReadOnlyList<(T Item, float Score)> first,
        Func<T, string> keySelector,
        int limit,
        float minScore = 0) =>
        SearchUtilities.ReciprocalRankFusionFromRankedItems(
            first,
            keySelector,
            limit,
            minScore: minScore);

    internal static List<(T Item, float Score)> FuseRanks<T>(
        IReadOnlyList<(T Item, float Score)> first,
        IReadOnlyList<(T Item, float Score)> second,
        Func<T, string> keySelector,
        int limit,
        float minScore = 0) =>
        SearchUtilities.ReciprocalRankFusionFromRankedItems(
            first,
            second,
            keySelector,
            limit,
            minScore: minScore);

    internal static List<(T Item, float Score)> FuseRanks<T>(
        IReadOnlyList<(T Item, float Score)> first,
        IReadOnlyList<(T Item, float Score)> second,
        IReadOnlyList<(T Item, float Score)> third,
        Func<T, string> keySelector,
        int limit,
        float minScore = 0) =>
        SearchUtilities.ReciprocalRankFusionFromRankedItems(
            first,
            second,
            third,
            keySelector,
            limit,
            minScore: minScore);

    internal static List<(T Item, float Score)> MergeRankedCandidates<T>(
        IEnumerable<IReadOnlyList<(T Item, float Score)>> rankedLists,
        Func<T, string> keySelector)
    {
        ArgumentNullException.ThrowIfNull(rankedLists);
        ArgumentNullException.ThrowIfNull(keySelector);

        var merged = new Dictionary<string, (T Item, float Score, int Index)>(StringComparer.Ordinal);
        var index = 0;
        foreach (var rankedList in rankedLists)
        {
            AddMergedCandidates(rankedList, keySelector, merged, ref index);
        }

        return ProjectMergedCandidates(merged);
    }

    internal static List<(T Item, float Score)> MergeRankedCandidates<T>(
        IReadOnlyList<(T Item, float Score)> first,
        IReadOnlyList<(T Item, float Score)> second,
        Func<T, string> keySelector)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(keySelector);

        var merged = new Dictionary<string, (T Item, float Score, int Index)>(StringComparer.Ordinal);
        var index = 0;
        AddMergedCandidates(first, keySelector, merged, ref index);
        AddMergedCandidates(second, keySelector, merged, ref index);
        return ProjectMergedCandidates(merged);
    }

    internal static List<(T Item, float Score)> MergeRankedCandidates<T>(
        IReadOnlyList<(T Item, float Score)> first,
        IReadOnlyList<(T Item, float Score)> second,
        IReadOnlyList<(T Item, float Score)> third,
        Func<T, string> keySelector)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);
        ArgumentNullException.ThrowIfNull(keySelector);

        var merged = new Dictionary<string, (T Item, float Score, int Index)>(StringComparer.Ordinal);
        var index = 0;
        AddMergedCandidates(first, keySelector, merged, ref index);
        AddMergedCandidates(second, keySelector, merged, ref index);
        AddMergedCandidates(third, keySelector, merged, ref index);
        return ProjectMergedCandidates(merged);
    }

    private static void AddMergedCandidates<T>(
        IReadOnlyList<(T Item, float Score)> rankedList,
        Func<T, string> keySelector,
        Dictionary<string, (T Item, float Score, int Index)> merged,
        ref int index)
    {
        for (var i = 0; i < rankedList.Count; i++)
        {
            var item = rankedList[i];
            var key = keySelector(item.Item);
            if (merged.TryGetValue(key, out var existing))
            {
                merged[key] = (existing.Item, Math.Max(existing.Score, item.Score), existing.Index);
                continue;
            }

            merged[key] = (item.Item, item.Score, index++);
        }
    }

    private static List<(T Item, float Score)> ProjectMergedCandidates<T>(
        Dictionary<string, (T Item, float Score, int Index)> merged)
    {
        var ordered = new List<(T Item, float Score, int Index)>(merged.Values);
        ordered.Sort(static (left, right) =>
        {
            var scoreComparison = right.Score.CompareTo(left.Score);
            return scoreComparison != 0
                ? scoreComparison
                : left.Index.CompareTo(right.Index);
        });

        var results = new List<(T Item, float Score)>(ordered.Count);
        foreach (var item in ordered)
        {
            results.Add((item.Item, item.Score));
        }

        return results;
    }

    internal static List<(T Item, float Score)> ToRankedList<T>(IReadOnlyList<SearchHit<T>> hits)
    {
        var ranked = new List<(T Item, float Score)>(hits.Count);
        for (var i = 0; i < hits.Count; i++)
        {
            ranked.Add((hits[i].Item, hits[i].Score));
        }

        return ranked;
    }

    internal static List<(T Item, float Score)> LimitRanked<T>(
        List<(T Item, float Score)> ranked,
        int limit)
    {
        if (limit <= 0)
        {
            ranked.Clear();
            return ranked;
        }

        if (ranked.Count > limit)
        {
            ranked.RemoveRange(limit, ranked.Count - limit);
        }

        return ranked;
    }

    internal static List<(T Item, float Score)> ApplyMmrReranker<T>(
        IReadOnlyList<(T Item, float Score)> ranked,
        IReadOnlyList<float> queryVector,
        Func<T, IReadOnlyList<float>?> vectorSelector,
        int limit,
        float lambda,
        float minScore) =>
        SearchUtilities.MaximalMarginalRelevanceWithScoresFromRankedItems(
            ranked,
            queryVector,
            vectorSelector,
            limit,
            lambda,
            minScore);

    internal static List<(EntityEdge Item, float Score)> SortByEpisodeMentions(
        IReadOnlyList<(EntityEdge Item, float Score)> ranked)
    {
        var ordered = new List<(EntityEdge Item, float Score, int Index)>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            ordered.Add((ranked[i].Item, ranked[i].Score, i));
        }

        ordered.Sort(static (left, right) =>
        {
            var episodeComparison = right.Item.Episodes.Count.CompareTo(left.Item.Episodes.Count);
            return episodeComparison != 0
                ? episodeComparison
                : left.Index.CompareTo(right.Index);
        });

        var results = new List<(EntityEdge Item, float Score)>(ordered.Count);
        foreach (var item in ordered)
        {
            results.Add((item.Item, item.Score));
        }

        return results;
    }

    internal static (List<T> Items, List<double> Scores) SplitRankedResults<T>(
        IReadOnlyList<(T Item, float Score)> ranked)
    {
        var items = new List<T>(ranked.Count);
        var scores = new List<double>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            items.Add(ranked[i].Item);
            scores.Add(ranked[i].Score);
        }

        return (items, scores);
    }

    internal static async Task<List<(EntityEdge Item, float Score)>> ApplyEdgeNodeDistanceRerankerAsync(
        ISearchGraphDriver searchDriver,
        IReadOnlyList<(EntityEdge Item, float Score)> ranked,
        string centerNodeUuid,
        float minScore,
        CancellationToken cancellationToken)
    {
        using var activity = StartRerankerActivity(
            "NodeDistance",
            "node_distance",
            ranked.Count,
            minScore);
        activity?.SetTag("graphiti.center_node.uuid", centerNodeUuid);

        try
        {
            var sourceNodeUuids = new List<string>();
            var sourceToEdges = new Dictionary<string, List<EntityEdge>>(StringComparer.Ordinal);
            foreach (var item in ranked)
            {
                if (!sourceToEdges.TryGetValue(item.Item.SourceNodeUuid, out var edges))
                {
                    edges = new List<EntityEdge>();
                    sourceToEdges[item.Item.SourceNodeUuid] = edges;
                    sourceNodeUuids.Add(item.Item.SourceNodeUuid);
                }

                edges.Add(item.Item);
            }

            var ranks = await searchDriver.RankNodeDistanceAsync(
                sourceNodeUuids,
                centerNodeUuid,
                minScore,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var results = new List<(EntityEdge Item, float Score)>();
            foreach (var rank in ranks)
            {
                if (!sourceToEdges.TryGetValue(rank.Uuid, out var edges))
                {
                    continue;
                }

                foreach (var edge in edges)
                {
                    results.Add((edge, rank.Score));
                }
            }

            activity?.SetTag("graphiti.result.count", results.Count);
            GraphitiTelemetry.SetOk(activity);
            return results;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    internal static async Task<List<(EntityNode Item, float Score)>> ApplyNodeDistanceRerankerAsync(
        ISearchGraphDriver searchDriver,
        IReadOnlyList<(EntityNode Item, float Score)> ranked,
        string centerNodeUuid,
        float minScore,
        CancellationToken cancellationToken)
    {
        using var activity = StartRerankerActivity(
            "NodeDistance",
            "node_distance",
            ranked.Count,
            minScore);
        activity?.SetTag("graphiti.center_node.uuid", centerNodeUuid);

        try
        {
            var nodeUuids = new List<string>(ranked.Count);
            var nodeByUuid = new Dictionary<string, EntityNode>(ranked.Count, StringComparer.Ordinal);
            foreach (var item in ranked)
            {
                if (nodeByUuid.TryAdd(item.Item.Uuid, item.Item))
                {
                    nodeUuids.Add(item.Item.Uuid);
                }
            }

            var ranks = await searchDriver.RankNodeDistanceAsync(
                nodeUuids,
                centerNodeUuid,
                minScore,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var results = new List<(EntityNode Item, float Score)>(Math.Min(ranks.Count, nodeByUuid.Count));
            foreach (var rank in ranks)
            {
                if (nodeByUuid.TryGetValue(rank.Uuid, out var node))
                {
                    results.Add((node, rank.Score));
                }
            }

            activity?.SetTag("graphiti.result.count", results.Count);
            GraphitiTelemetry.SetOk(activity);
            return results;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    internal static async Task<List<(EntityNode Item, float Score)>> ApplyNodeEpisodeMentionsRerankerAsync(
        ISearchGraphDriver searchDriver,
        IReadOnlyList<(EntityNode Item, float Score)> ranked,
        float minScore,
        CancellationToken cancellationToken)
    {
        using var activity = StartRerankerActivity(
            "EpisodeMentions",
            "episode_mentions",
            ranked.Count,
            minScore);

        try
        {
            var nodeUuids = new List<string>(ranked.Count);
            var nodeByUuid = new Dictionary<string, EntityNode>(ranked.Count, StringComparer.Ordinal);
            foreach (var item in ranked)
            {
                if (nodeByUuid.TryAdd(item.Item.Uuid, item.Item))
                {
                    nodeUuids.Add(item.Item.Uuid);
                }
            }

            var ranks = await searchDriver.RankNodeEpisodeMentionsAsync(
                nodeUuids,
                minScore,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var results = new List<(EntityNode Item, float Score)>(Math.Min(ranks.Count, nodeByUuid.Count));
            foreach (var rank in ranks)
            {
                if (nodeByUuid.TryGetValue(rank.Uuid, out var node))
                {
                    results.Add((node, rank.Score));
                }
            }

            activity?.SetTag("graphiti.result.count", results.Count);
            GraphitiTelemetry.SetOk(activity);
            return results;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private static Activity? StartRerankerActivity(
        string operation,
        string reranker,
        int candidateCount,
        float minScore)
    {
        var activity = GraphitiTelemetry.StartActivity($"SearchEngine.Rerank.{operation}");
        activity?.SetTag("graphiti.search.reranker", reranker);
        activity?.SetTag("graphiti.candidate.count", candidateCount);
        activity?.SetTag("graphiti.reranker.min_score", minScore);
        return activity;
    }
}
