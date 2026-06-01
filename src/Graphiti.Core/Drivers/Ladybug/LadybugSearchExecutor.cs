using System.Globalization;
using System.Text.Json;

namespace Graphiti.Core.Drivers.Ladybug;

internal sealed class LadybugSearchExecutor
{
    private readonly ILadybugQueryExecutor _executor;

    internal LadybugSearchExecutor(ILadybugQueryExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
    }

    internal async Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var fulltextQuery = SearchUtilities.FulltextQuery(query, groupIds, GraphProvider.Kuzu);
        if (fulltextQuery.Length == 0)
        {
            return Array.Empty<SearchHit<EntityNode>>();
        }

        return await QueryHitsAsync(
            LadybugSearchStatementBuilder.BuildEntityNodeFulltextSearchStatement(
                fulltextQuery,
                searchFilter,
                groupIds,
                limit),
            LadybugRecordMapper.MapEntityNode,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        CancellationToken cancellationToken = default) =>
        await QueryHitsAsync(
            LadybugSearchStatementBuilder.BuildEntityNodeEmbeddingSearchStatement(
                searchVector,
                searchFilter,
                groupIds,
                limit,
                minScore),
            LadybugRecordMapper.MapEntityNode,
            cancellationToken).ConfigureAwait(false);

    internal async Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var fulltextQuery = SearchUtilities.FulltextQuery(query, groupIds, GraphProvider.Kuzu);
        if (fulltextQuery.Length == 0)
        {
            return Array.Empty<SearchHit<EntityEdge>>();
        }

        return await QueryHitsAsync(
            LadybugSearchStatementBuilder.BuildEntityEdgeFulltextSearchStatement(
                fulltextQuery,
                searchFilter,
                groupIds,
                limit),
            LadybugRecordMapper.MapEntityEdge,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        string? sourceNodeUuid = null,
        string? targetNodeUuid = null,
        CancellationToken cancellationToken = default) =>
        await QueryHitsAsync(
            LadybugSearchStatementBuilder.BuildEntityEdgeEmbeddingSearchStatement(
                searchVector,
                searchFilter,
                groupIds,
                limit,
                minScore,
                sourceNodeUuid,
                targetNodeUuid),
            LadybugRecordMapper.MapEntityEdge,
            cancellationToken).ConfigureAwait(false);

    internal async Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesBfsAsync(
        IReadOnlyList<string>? originNodeUuids,
        SearchFilters searchFilter,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default) =>
        await QueryDistinctHitsAsync(
            LadybugSearchStatementBuilder.BuildEntityNodeBfsSearchStatements(
                originNodeUuids,
                searchFilter,
                maxDepth,
                groupIds,
                limit),
            LadybugRecordMapper.MapEntityNode,
            node => node.Uuid,
            limit,
            cancellationToken).ConfigureAwait(false);

    internal async Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesBfsAsync(
        IReadOnlyList<string>? originNodeUuids,
        SearchFilters searchFilter,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default) =>
        await QueryDistinctHitsAsync(
            LadybugSearchStatementBuilder.BuildEntityEdgeBfsSearchStatements(
                originNodeUuids,
                searchFilter,
                maxDepth,
                groupIds,
                limit),
            LadybugRecordMapper.MapEntityEdge,
            edge => edge.Uuid,
            limit,
            cancellationToken).ConfigureAwait(false);

    internal async Task<IReadOnlyList<SearchHit<EpisodicNode>>> SearchEpisodesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var fulltextQuery = SearchUtilities.FulltextQuery(query, groupIds, GraphProvider.Kuzu);
        if (fulltextQuery.Length == 0)
        {
            return Array.Empty<SearchHit<EpisodicNode>>();
        }

        return await QueryHitsAsync(
            LadybugSearchStatementBuilder.BuildEpisodeFulltextSearchStatement(
                fulltextQuery,
                searchFilter,
                groupIds,
                limit),
            LadybugRecordMapper.MapEpisodicNode,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesFulltextAsync(
        string query,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var fulltextQuery = SearchUtilities.FulltextQuery(query, groupIds, GraphProvider.Kuzu);
        if (fulltextQuery.Length == 0)
        {
            return Array.Empty<SearchHit<CommunityNode>>();
        }

        return await QueryHitsAsync(
            LadybugSearchStatementBuilder.BuildCommunityFulltextSearchStatement(
                fulltextQuery,
                groupIds,
                limit),
            LadybugRecordMapper.MapCommunityNode,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        CancellationToken cancellationToken = default) =>
        await QueryHitsAsync(
            LadybugSearchStatementBuilder.BuildCommunityEmbeddingSearchStatement(
                searchVector,
                groupIds,
                limit,
                minScore),
            LadybugRecordMapper.MapCommunityNode,
            cancellationToken).ConfigureAwait(false);

    internal async Task<IReadOnlyList<SearchRank>> RankNodeDistanceAsync(
        IReadOnlyList<string> nodeUuids,
        string centerNodeUuid,
        float minScore = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodeUuids);
        if (nodeUuids.Count == 0)
        {
            return Array.Empty<SearchRank>();
        }

        var scores = BuildScoreMap(nodeUuids, defaultScore: 0);

        foreach (var statement in LadybugSearchStatementBuilder.BuildNodeDistanceRankStatements(
                     nodeUuids,
                     centerNodeUuid))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var records = await _executor.QueryAsync(statement, cancellationToken).ConfigureAwait(false);
            foreach (var record in records)
            {
                if (GetString(record, "uuid") is { Length: > 0 } uuid)
                {
                    var score = GetScore(record, defaultScore: 0);
                    scores[uuid] = scores.TryGetValue(uuid, out var existing)
                        ? (score, existing.Index)
                        : (score, int.MaxValue);
                }
            }
        }

        if (scores.TryGetValue(centerNodeUuid, out var center))
        {
            scores[centerNodeUuid] = (10f, center.Index);
        }

        return SortRanksByDescendingScore(scores, minScore);
    }

    internal async Task<IReadOnlyList<SearchRank>> RankNodeEpisodeMentionsAsync(
        IReadOnlyList<string> nodeUuids,
        float minScore = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodeUuids);
        if (nodeUuids.Count == 0)
        {
            return Array.Empty<SearchRank>();
        }

        var scores = BuildScoreMap(nodeUuids, defaultScore: float.PositiveInfinity);

        foreach (var statement in LadybugSearchStatementBuilder.BuildNodeEpisodeMentionsRankStatements(nodeUuids))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var records = await _executor.QueryAsync(statement, cancellationToken).ConfigureAwait(false);
            foreach (var record in records)
            {
                if (GetString(record, "uuid") is { Length: > 0 } uuid)
                {
                    var score = GetScore(record, defaultScore: float.PositiveInfinity);
                    scores[uuid] = scores.TryGetValue(uuid, out var existing)
                        ? (score, existing.Index)
                        : (score, int.MaxValue);
                }
            }
        }

        return SortRanksByAscendingScore(scores, minScore);
    }

    private async Task<IReadOnlyList<SearchHit<T>>> QueryHitsAsync<T>(
        LadybugStatement statement,
        Func<IReadOnlyDictionary<string, object?>, T> mapper,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var records = await _executor.QueryAsync(statement, cancellationToken).ConfigureAwait(false);
        var hits = new List<SearchHit<T>>(records.Count);
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            hits.Add(new SearchHit<T>(mapper(record), GetScore(record)));
        }

        return hits;
    }

    private async Task<IReadOnlyList<SearchHit<T>>> QueryDistinctHitsAsync<T>(
        IReadOnlyList<LadybugStatement> statements,
        Func<IReadOnlyDictionary<string, object?>, T> mapper,
        Func<T, string> keySelector,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0 || statements.Count == 0)
        {
            return Array.Empty<SearchHit<T>>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hits = new List<SearchHit<T>>(limit);
        foreach (var statement in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var records = await _executor.QueryAsync(statement, cancellationToken).ConfigureAwait(false);
            foreach (var record in records)
            {
                var item = mapper(record);
                if (!seen.Add(keySelector(item)))
                {
                    continue;
                }

                hits.Add(new SearchHit<T>(item, GetScore(record, defaultScore: 1)));
                if (hits.Count >= limit)
                {
                    return hits;
                }
            }
        }

        return hits;
    }

    private static Dictionary<string, (float Score, int Index)> BuildScoreMap(
        IReadOnlyList<string> nodeUuids,
        float defaultScore)
    {
        var scores = new Dictionary<string, (float Score, int Index)>(
            nodeUuids.Count,
            StringComparer.Ordinal);
        for (var i = 0; i < nodeUuids.Count; i++)
        {
            scores.TryAdd(nodeUuids[i], (defaultScore, i));
        }

        return scores;
    }

    private static List<SearchRank> SortRanksByDescendingScore(
        IReadOnlyDictionary<string, (float Score, int Index)> scores,
        float minScore)
    {
        var ranked = FilterRanks(scores, minScore);
        ranked.Sort(static (left, right) =>
        {
            var scoreComparison = right.Score.CompareTo(left.Score);
            return scoreComparison != 0
                ? scoreComparison
                : left.Index.CompareTo(right.Index);
        });
        return ProjectRanks(ranked);
    }

    private static List<SearchRank> SortRanksByAscendingScore(
        IReadOnlyDictionary<string, (float Score, int Index)> scores,
        float minScore)
    {
        var ranked = FilterRanks(scores, minScore);
        ranked.Sort(static (left, right) =>
        {
            var scoreComparison = left.Score.CompareTo(right.Score);
            return scoreComparison != 0
                ? scoreComparison
                : left.Index.CompareTo(right.Index);
        });
        return ProjectRanks(ranked);
    }

    private static List<(string Uuid, float Score, int Index)> FilterRanks(
        IReadOnlyDictionary<string, (float Score, int Index)> scores,
        float minScore)
    {
        var ranked = new List<(string Uuid, float Score, int Index)>(scores.Count);
        foreach (var (uuid, rank) in scores)
        {
            if (rank.Score >= minScore)
            {
                ranked.Add((uuid, rank.Score, rank.Index));
            }
        }

        return ranked;
    }

    private static List<SearchRank> ProjectRanks(
        List<(string Uuid, float Score, int Index)> ranked)
    {
        var results = new List<SearchRank>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            results.Add(new SearchRank(ranked[i].Uuid, ranked[i].Score));
        }

        return results;
    }

    private static float GetScore(
        IReadOnlyDictionary<string, object?> record,
        float defaultScore = 0)
    {
        if (!record.TryGetValue("score", out var value) || value is null)
        {
            return defaultScore;
        }

        if (value is JsonElement element)
        {
            value = element.ValueKind switch
            {
                JsonValueKind.Number when element.TryGetSingle(out var single) => single,
                JsonValueKind.String => element.GetString(),
                _ => value
            };
        }

        return value switch
        {
            float typed => typed,
            double typed => (float)typed,
            decimal typed => (float)typed,
            int typed => typed,
            long typed => typed,
            string text when float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => Convert.ToSingle(value, CultureInfo.InvariantCulture)
        };
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> record, string key)
    {
        if (!record.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value is string text ? text : Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}
