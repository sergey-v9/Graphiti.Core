using System.Buffers;
using System.Collections.Frozen;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Graphiti.Core.Search;

/// <summary>
/// Low-level helpers shared by the search pipeline: vector math (cosine similarity, normalization),
/// reranking primitives (RRF, MMR, score combination), tokenization, and full-text query construction
/// and sanitization for the various graph backends.
/// </summary>
public static partial class SearchUtilities
{
    /// <summary>Default maximum number of schema-relevant items considered during search.</summary>
    public const int RelevantSchemaLimit = 10;

    /// <summary>Default minimum similarity score for a candidate to be retained.</summary>
    public const float DefaultMinScore = 0.6f;

    /// <summary>Default MMR trade-off between relevance and diversity (0 = diversity, 1 = relevance).</summary>
    public const float DefaultMmrLambda = 0.5f;

    /// <summary>Default maximum breadth-first traversal depth.</summary>
    public const int MaxSearchDepth = 3;

    /// <summary>Maximum backend full-text query term/part count used when constructing queries.</summary>
    public const int MaxQueryLength = 128;

    private static readonly SearchValues<char> FalkorFulltextSeparators =
        SearchValues.Create(",.<>{}[]\"':;!@#$%^&*()-+=~?|/\\");

    private static readonly FrozenSet<string> FalkorStopwords = new[]
    {
        "a",
        "is",
        "the",
        "an",
        "and",
        "are",
        "as",
        "at",
        "be",
        "but",
        "by",
        "for",
        "if",
        "in",
        "into",
        "it",
        "no",
        "not",
        "of",
        "on",
        "or",
        "such",
        "that",
        "their",
        "then",
        "there",
        "these",
        "they",
        "this",
        "to",
        "was",
        "will",
        "with"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Computes cosine similarity of two vectors; returns 0 if either is null/empty and throws when
    /// non-empty vectors have different dimensions.
    /// </summary>
    public static float CalculateCosineSimilarity(IReadOnlyList<float>? vector1, IReadOnlyList<float>? vector2)
    {
        if (vector1 is null || vector2 is null || vector1.Count == 0 || vector2.Count == 0)
        {
            return 0;
        }

        EnsureSameVectorDimension(vector1.Count, vector2.Count);

        var leftRented = TryGetSpan(vector1, vector1.Count, out var left);
        var rightRented = TryGetSpan(vector2, vector2.Count, out var right);
        try
        {
            return CosineSimilarity(left, right);
        }
        finally
        {
            if (leftRented is not null)
            {
                ArrayPool<float>.Shared.Return(leftRented);
            }

            if (rightRented is not null)
            {
                ArrayPool<float>.Shared.Return(rightRented);
            }
        }
    }

    /// <summary>Creates a reusable scorer that compares candidate vectors against a fixed query vector.</summary>
    public static CosineSimilarityScorer CreateCosineSimilarityScorer(IReadOnlyList<float>? queryVector) =>
        new(queryVector);

    internal static IReadOnlyList<(T Item, float Score)> TopByScore<T>(
        IEnumerable<T> candidates,
        Func<T, float> scoreSelector,
        int limit,
        float minScore = 0,
        bool includeMinScore = true)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(scoreSelector);

        if (limit <= 0)
        {
            return Array.Empty<(T Item, float Score)>();
        }

        return TopByScoreCore(
            candidates,
            scoreSelector,
            static (_, index) => index,
            limit,
            minScore,
            includeMinScore);
    }

    private static IReadOnlyList<(T Item, float Score)> TopByScoreCore<T>(
        IEnumerable<T> candidates,
        Func<T, float> scoreSelector,
        Func<T, int, int> stableIndexSelector,
        int limit,
        float minScore,
        bool includeMinScore)
    {
        if (limit <= 0)
        {
            return Array.Empty<(T Item, float Score)>();
        }

        var comparer = StableScorePriorityComparer.Instance;
        var queue = new PriorityQueue<ScoredCandidate<T>, StableScorePriority>(comparer);
        var index = 0;
        foreach (var candidate in candidates)
        {
            var score = scoreSelector(candidate);
            var meetsMinScore = includeMinScore
                ? score >= minScore
                : score > minScore;
            if (!meetsMinScore)
            {
                index++;
                continue;
            }

            var stableIndex = stableIndexSelector(candidate, index);
            var scored = new ScoredCandidate<T>(candidate, score, stableIndex);
            var priority = new StableScorePriority(score, stableIndex);
            if (queue.Count < limit)
            {
                queue.Enqueue(scored, priority);
            }
            else
            {
                queue.TryPeek(out _, out var worstPriority);
                if (comparer.Compare(priority, worstPriority) > 0)
                {
                    queue.Dequeue();
                    queue.Enqueue(scored, priority);
                }
            }

            index++;
        }

        var ordered = new List<ScoredCandidate<T>>(queue.Count);
        foreach (var item in queue.UnorderedItems)
        {
            ordered.Add(item.Element);
        }

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

    /// <summary>
    /// Builds a backend-specific full-text query string from a natural-language query and optional
    /// group-id scope, applying the sanitization and stopword rules appropriate to the driver's provider.
    /// </summary>
    public static string FulltextQuery(string query, IReadOnlyList<string>? groupIds, IGraphDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        return FulltextQuery(query, groupIds, driver.Provider);
    }

    internal static string FulltextQuery(string query, IReadOnlyList<string>? groupIds, GraphProvider provider)
    {
        GraphitiHelpers.ValidateGroupIds(groupIds);

        return provider switch
        {
            // NOTE: LadybugDB is the primary provider target; Kuzu remains the Python-parity
            // compatibility value. Preserve this interim behavior until LadybugDB owns the query.
            GraphProvider.Kuzu => BuildKuzuFulltextQuery(query),
            GraphProvider.FalkorDb => BuildFalkorFulltextQuery(query, groupIds),
            _ => BuildLuceneFulltextQuery(query, groupIds)
        };
    }

    private static string BuildLuceneFulltextQuery(string query, IReadOnlyList<string>? groupIds)
    {
        var sanitized = GraphitiHelpers.LuceneSanitize(query ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return string.Empty;
        }

        if (CountPythonSpaceSplitParts(sanitized) + (groupIds?.Count ?? 0) >= MaxQueryLength)
        {
            return string.Empty;
        }

        if (groupIds is null || groupIds.Count == 0)
        {
            return $"({sanitized})";
        }

        var groupFilter = string.Join(" OR ", groupIds.Select(groupId => $"group_id:\"{groupId}\""));
        return $"({groupFilter}) AND ({sanitized})";
    }

    // NOTE: Preserve current Kuzu-parity query semantics until the LadybugDB provider lands.
    private static string BuildKuzuFulltextQuery(string query)
    {
        return JoinFirstWhitespaceTerms(query, MaxQueryLength);
    }

    private static string BuildFalkorFulltextQuery(string query, IReadOnlyList<string>? groupIds)
    {
        var groupFilter = groupIds is { Count: > 0 }
            ? $"(@group_id:{string.Join("|", groupIds.Select(groupId => $"\"{groupId}\""))})"
            : string.Empty;

        var queryPart = BuildFalkorQueryPart(
            SanitizeFalkorFulltextQuery(query ?? string.Empty),
            out var queryPartSplitCount);

        if (queryPartSplitCount + (groupIds?.Count ?? 0) >= MaxQueryLength)
        {
            return string.Empty;
        }

        return $"{groupFilter} ({queryPart})";
    }

    private static string SanitizeFalkorFulltextQuery(string query)
    {
        var builder = new StringBuilder(query.Length);
        var pendingSpace = false;
        foreach (var character in query)
        {
            var normalized = FalkorFulltextSeparators.Contains(character) ? ' ' : character;
            if (char.IsWhiteSpace(normalized))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(normalized);
        }

        return builder.ToString();
    }

    private static int CountPythonSpaceSplitParts(string value)
    {
        var count = 1;
        foreach (var character in value)
        {
            if (character == ' ')
            {
                count++;
            }
        }

        return count;
    }

    private static string JoinFirstWhitespaceTerms(string? query, int maxTerms)
    {
        if (string.IsNullOrWhiteSpace(query) || maxTerms <= 0)
        {
            return string.Empty;
        }

        var source = query.AsSpan();
        var builder = new StringBuilder(query.Length);
        var termCount = 0;
        var index = 0;
        while (index < source.Length && termCount < maxTerms)
        {
            while (index < source.Length && char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            if (index >= source.Length)
            {
                break;
            }

            var start = index;
            while (index < source.Length && !char.IsWhiteSpace(source[index]))
            {
                index++;
            }

            if (termCount > 0)
            {
                builder.Append(' ');
            }

            builder.Append(source[start..index]);
            termCount++;
        }

        return builder.ToString();
    }

    private static string BuildFalkorQueryPart(string sanitizedQuery, out int queryPartSplitCount)
    {
        var source = sanitizedQuery.AsSpan();
        var builder = new StringBuilder(sanitizedQuery.Length);
        var filteredTermCount = 0;
        var index = 0;
        while (index < source.Length)
        {
            while (index < source.Length && source[index] == ' ')
            {
                index++;
            }

            if (index >= source.Length)
            {
                break;
            }

            var start = index;
            while (index < source.Length && source[index] != ' ')
            {
                index++;
            }

            var term = sanitizedQuery[start..index];
            if (FalkorStopwords.Contains(term))
            {
                continue;
            }

            if (filteredTermCount > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(term);
            filteredTermCount++;
        }

        queryPartSplitCount = filteredTermCount == 0 ? 1 : (filteredTermCount * 2) - 1;
        return builder.ToString();
    }

    /// <summary>Convenience one-shot term-overlap score of <paramref name="text"/> against a query.</summary>
    public static float TextScore(string query, string text)
    {
        var scorer = CreateTextScorer(query);
        return scorer.Score(text);
    }

    /// <summary>Creates a reusable <see cref="TextScorer"/> for the given query.</summary>
    public static TextScorer CreateTextScorer(string query) =>
        new(query);

    /// <summary>Returns the items with distinct UUIDs, preserving first-seen order.</summary>
    public static IReadOnlyList<T> DeduplicateByUuid<T>(IEnumerable<T> items) where T : class
    {
        ArgumentNullException.ThrowIfNull(items);

        var seenGraphUuids = new HashSet<string>(StringComparer.Ordinal);
        HashSet<T>? seenItems = null;
        var result = new List<T>();
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);

            if (item is Node node)
            {
                if (seenGraphUuids.Add(node.Uuid))
                {
                    result.Add(item);
                }

                continue;
            }

            if (item is Edge edge)
            {
                if (seenGraphUuids.Add(edge.Uuid))
                {
                    result.Add(item);
                }

                continue;
            }

            seenItems ??= new HashSet<T>();
            if (seenItems.Add(item))
            {
                result.Add(item);
            }
        }

        return result;
    }

    /// <summary>
    /// Combines several ranked result lists into one using Reciprocal Rank Fusion: each item's score is
    /// the sum over lists of <c>1 / (rank + rankConstant)</c>. Returns up to <paramref name="limit"/>
    /// items scoring at least <paramref name="minScore"/>, ordered by combined score.
    /// </summary>
    public static IReadOnlyList<(T Item, float Score)> ReciprocalRankFusion<T>(
        IEnumerable<IReadOnlyList<T>> rankedLists,
        Func<T, string> keySelector,
        int limit,
        int rankConstant = 1,
        float minScore = 0)
    {
        ArgumentNullException.ThrowIfNull(rankedLists);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rankConstant);

        if (limit <= 0)
        {
            return Array.Empty<(T Item, float Score)>();
        }

        var scores = new Dictionary<string, (T Item, float Score, int Index)>(StringComparer.Ordinal);
        var nextIndex = 0;
        foreach (var rankedList in rankedLists)
        {
            for (var i = 0; i < rankedList.Count; i++)
            {
                var item = rankedList[i];
                var key = keySelector(item);
                var score = (float)(1.0 / (i + rankConstant));
                if (scores.TryGetValue(key, out var existing))
                {
                    scores[key] = (existing.Item, existing.Score + score, existing.Index);
                }
                else
                {
                    scores[key] = (item, score, nextIndex++);
                }
            }
        }

        return ProjectIndexedScores(
            TopByScoreCore(
                scores.Values,
                item => item.Score,
                static (item, _) => item.Index,
                limit,
                minScore,
                includeMinScore: true));
    }

    internal static List<(T Item, float Score)> ReciprocalRankFusionFromRankedItems<T>(
        IEnumerable<IReadOnlyList<(T Item, float Score)>> rankedLists,
        Func<T, string> keySelector,
        int limit,
        int rankConstant = 1,
        float minScore = 0)
    {
        ArgumentNullException.ThrowIfNull(rankedLists);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rankConstant);

        if (limit <= 0)
        {
            return [];
        }

        var scores = new Dictionary<string, (T Item, float Score, int Index)>(StringComparer.Ordinal);
        var nextIndex = 0;
        foreach (var rankedList in rankedLists)
        {
            for (var i = 0; i < rankedList.Count; i++)
            {
                var item = rankedList[i].Item;
                var key = keySelector(item);
                var score = (float)(1.0 / (i + rankConstant));
                if (scores.TryGetValue(key, out var existing))
                {
                    scores[key] = (existing.Item, existing.Score + score, existing.Index);
                }
                else
                {
                    scores[key] = (item, score, nextIndex++);
                }
            }
        }

        return ProjectIndexedScores(
            TopByScoreCore(
                scores.Values,
                item => item.Score,
                static (item, _) => item.Index,
                limit,
                minScore,
                includeMinScore: true));
    }

    private static List<(T Item, float Score)> ProjectIndexedScores<T>(
        IReadOnlyList<((T Item, float Score, int Index) Item, float Score)> ranked)
    {
        var results = new List<(T Item, float Score)>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            var scored = ranked[i];
            results.Add((scored.Item.Item, scored.Score));
        }

        return results;
    }

    /// <summary>
    /// Reranks candidates with Maximal Marginal Relevance, trading off relevance to the query against
    /// diversity among selected items (controlled by <paramref name="lambda"/>). Returns the items only.
    /// </summary>
    public static IReadOnlyList<T> MaximalMarginalRelevance<T>(
        IReadOnlyList<T> candidates,
        IReadOnlyList<float> queryVector,
        Func<T, IReadOnlyList<float>?> vectorSelector,
        int limit,
        float lambda = DefaultMmrLambda,
        float minScore = -2.0f) =>
        ProjectItems(
            MaximalMarginalRelevanceWithScores(
                candidates,
                queryVector,
                vectorSelector,
                limit,
                lambda,
                minScore));

    /// <summary>
    /// Same as <see cref="MaximalMarginalRelevance{T}"/> but also returns each selected item's MMR score.
    /// </summary>
    public static IReadOnlyList<(T Item, float Score)> MaximalMarginalRelevanceWithScores<T>(
        IReadOnlyList<T> candidates,
        IReadOnlyList<float> queryVector,
        Func<T, IReadOnlyList<float>?> vectorSelector,
        int limit,
        float lambda = DefaultMmrLambda,
        float minScore = -2.0f)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(queryVector);
        ArgumentNullException.ThrowIfNull(vectorSelector);

        if (limit <= 0 || candidates.Count == 0)
        {
            return Array.Empty<(T Item, float Score)>();
        }

        var records = new MmrCandidate<T>[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            records[i] = new MmrCandidate<T>(
                candidates[i],
                NormalizeCandidateVector(vectorSelector(candidates[i])),
                i);
        }

        return MaximalMarginalRelevanceWithScoresCore(
            records,
            queryVector,
            limit,
            lambda,
            minScore);
    }

    internal static List<(T Item, float Score)> MaximalMarginalRelevanceWithScoresFromRankedItems<T>(
        IReadOnlyList<(T Item, float Score)> candidates,
        IReadOnlyList<float> queryVector,
        Func<T, IReadOnlyList<float>?> vectorSelector,
        int limit,
        float lambda = DefaultMmrLambda,
        float minScore = -2.0f)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(queryVector);
        ArgumentNullException.ThrowIfNull(vectorSelector);

        if (limit <= 0 || candidates.Count == 0)
        {
            return [];
        }

        var records = new MmrCandidate<T>[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            var item = candidates[i].Item;
            records[i] = new MmrCandidate<T>(
                item,
                NormalizeCandidateVector(vectorSelector(item)),
                i);
        }

        return MaximalMarginalRelevanceWithScoresCore(
            records,
            queryVector,
            limit,
            lambda,
            minScore);
    }

    private static List<(T Item, float Score)> MaximalMarginalRelevanceWithScoresCore<T>(
        MmrCandidate<T>[] records,
        IReadOnlyList<float> queryVector,
        int limit,
        float lambda,
        float minScore)
    {
        var maxSimilarities = new float[records.Length];
        for (var i = 0; i < records.Length; i++)
        {
            var left = records[i].Vector;
            for (var j = 0; j < i; j++)
            {
                var similarity = DotSameDimension(left, records[j].Vector);
                if (similarity > maxSimilarities[i])
                {
                    maxSimilarities[i] = similarity;
                }

                if (similarity > maxSimilarities[j])
                {
                    maxSimilarities[j] = similarity;
                }
            }
        }

        var queryRented = TryGetSpan(queryVector, queryVector.Count, out var query);
        try
        {
            var scored = new List<(T Item, float Score, int Index)>(records.Length);
            for (var i = 0; i < records.Length; i++)
            {
                var record = records[i];
                var relevance = DotSameDimension(query, record.Vector);
                var score = lambda * relevance + (lambda - 1) * maxSimilarities[i];
                if (score >= minScore)
                {
                    scored.Add((record.Item, score, record.Index));
                }
            }

            return ProjectIndexedScores(
                TopByScoreCore(
                    scored,
                    item => item.Score,
                    static (item, _) => item.Index,
                    limit,
                    minScore: float.NegativeInfinity,
                    includeMinScore: true));
        }
        finally
        {
            if (queryRented is not null)
            {
                ArrayPool<float>.Shared.Return(queryRented);
            }
        }
    }

    internal static List<string> Tokenize(string text)
    {
        var source = text ?? string.Empty;
        var tokens = new List<string>();
        VisitTokens(source, tokens, static (token, state) => state.Add(token));

        return tokens;
    }

    internal static void VisitTokens<TState>(
        string? text,
        TState state,
        Action<string, TState> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        var source = text ?? string.Empty;
        foreach (var match in SearchTokenRegex().EnumerateMatches(source.AsSpan()))
        {
            visitor(
                string.Create(
                    match.Length,
                    (Text: source, match.Index),
                    static (destination, state) =>
                    {
                        _ = state.Text.AsSpan(state.Index, destination.Length).ToLowerInvariant(destination);
                    }),
                state);
        }
    }

    private static List<T> ProjectItems<T>(IReadOnlyList<(T Item, float Score)> ranked)
    {
        var results = new List<T>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            results.Add(ranked[i].Item);
        }

        return results;
    }

    private static float[]? TryGetSpan(
        IReadOnlyList<float> vector,
        int length,
        out ReadOnlySpan<float> span)
    {
        if (vector is float[] array)
        {
            span = array.AsSpan(0, length);
            return null;
        }

        if (vector is List<float> list)
        {
            span = CollectionsMarshal.AsSpan(list).Slice(0, length);
            return null;
        }

        var rented = ArrayPool<float>.Shared.Rent(length);
        for (var i = 0; i < length; i++)
        {
            rented[i] = vector[i];
        }

        span = rented.AsSpan(0, length);
        return rented;
    }

    private static float CosineSimilarity(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        EnsureSameVectorDimension(left.Length, right.Length);
        return SanitizeCosineSimilarity(TensorPrimitives.CosineSimilarity(left, right));
    }

    private static float CosineSimilarity(
        ReadOnlySpan<float> left,
        ReadOnlySpan<float> right,
        float leftNorm)
    {
        if (left.Length == 0 || right.Length == 0 || leftNorm <= 0)
        {
            return 0;
        }

        EnsureSameVectorDimension(left.Length, right.Length);

        var rightNorm = NormOrZero(right);
        if (rightNorm <= 0)
        {
            return 0;
        }

        var denominator = leftNorm * rightNorm;
        if (!float.IsFinite(denominator) || denominator <= 0)
        {
            return 0;
        }

        return SanitizeCosineSimilarity(TensorPrimitives.Dot(left, right) / denominator);
    }

    private static float SanitizeCosineSimilarity(float score) =>
        float.IsFinite(score) ? score : 0;

    private static float NormOrZero(ReadOnlySpan<float> vector)
    {
        var norm = TensorPrimitives.Norm(vector);
        return float.IsFinite(norm) && norm > 0 ? norm : 0;
    }

    private static float[] NormalizeCandidateVector(IReadOnlyList<float>? embedding) =>
        embedding is null || embedding.Count == 0
            ? Array.Empty<float>()
            : GraphitiHelpers.NormalizeL2(embedding);

    private static float DotSameDimension(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 0;
        }

        EnsureSameVectorDimension(left.Length, right.Length);
        return TensorPrimitives.Dot(left, right);
    }

    private static void EnsureSameVectorDimension(int leftLength, int rightLength)
    {
        if (leftLength != rightLength)
        {
            throw new ArgumentException(
                $"Vector dimensions must match. Left vector has dimension {leftLength}; right vector has dimension {rightLength}.");
        }
    }

    private sealed record MmrCandidate<T>(T Item, float[] Vector, int Index);

    private readonly record struct ScoredCandidate<T>(T Item, float Score, int Index);

    private readonly record struct StableScorePriority(float Score, int Index);

    private sealed class StableScorePriorityComparer : IComparer<StableScorePriority>
    {
        public static readonly StableScorePriorityComparer Instance = new();

        public int Compare(StableScorePriority x, StableScorePriority y)
        {
            var scoreComparison = x.Score.CompareTo(y.Score);
            return scoreComparison != 0
                ? scoreComparison
                : y.Index.CompareTo(x.Index);
        }
    }

    public sealed class CosineSimilarityScorer
    {
        private readonly float[] _queryVector;
        private readonly float _queryNorm;

        internal CosineSimilarityScorer(IReadOnlyList<float>? queryVector)
        {
            if (queryVector is null || queryVector.Count == 0)
            {
                _queryVector = Array.Empty<float>();
                _queryNorm = 0;
                return;
            }

            _queryVector = queryVector.ToArray();
            _queryNorm = NormOrZero(_queryVector);
        }

        public float Score(IReadOnlyList<float>? vector)
        {
            if (_queryNorm <= 0 || vector is null || vector.Count == 0)
            {
                return 0;
            }

            EnsureSameVectorDimension(_queryVector.Length, vector.Count);
            var rightRented = TryGetSpan(vector, vector.Count, out var right);
            try
            {
                return CosineSimilarity(_queryVector, right, _queryNorm);
            }
            finally
            {
                if (rightRented is not null)
                {
                    ArrayPool<float>.Shared.Return(rightRented);
                }
            }
        }
    }

    [GeneratedRegex("[\\p{L}\\p{N}_]+", RegexOptions.CultureInvariant)]
    private static partial Regex SearchTokenRegex();
}
