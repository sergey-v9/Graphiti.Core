using System.Runtime.InteropServices;

namespace Graphiti.Core.Search;

internal static class Bm25TextScorer
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    public static IReadOnlyList<(T Item, float Score)> Rank<T>(
        IEnumerable<T> candidates,
        Func<T, string> textSelector,
        string query,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(textSelector);

        if (limit <= 0)
        {
            return Array.Empty<(T Item, float Score)>();
        }

        var queryTerms = BuildDistinctQueryTerms(query);
        if (queryTerms.Terms.Count == 0)
        {
            return Array.Empty<(T Item, float Score)>();
        }

        var documentFrequencies = new Dictionary<string, int>(StringComparer.Ordinal);
        var documents = new List<Document<T>>();
        var totalDocumentLength = 0;

        foreach (var candidate in candidates)
        {
            AddDocument(
                candidate,
                textSelector,
                queryTerms,
                documentFrequencies,
                documents,
                ref totalDocumentLength);
        }

        return ScoreDocuments(queryTerms, documentFrequencies, documents, totalDocumentLength, limit);
    }

    public static IReadOnlyList<(T Item, float Score)> Rank<T>(
        IReadOnlyList<T> candidates,
        Func<T, bool> candidatePredicate,
        Func<T, string> textSelector,
        string query,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(candidatePredicate);
        ArgumentNullException.ThrowIfNull(textSelector);

        if (limit <= 0)
        {
            return Array.Empty<(T Item, float Score)>();
        }

        var queryTerms = BuildDistinctQueryTerms(query);
        if (queryTerms.Terms.Count == 0)
        {
            return Array.Empty<(T Item, float Score)>();
        }

        var documentFrequencies = new Dictionary<string, int>(StringComparer.Ordinal);
        var documents = new List<Document<T>>();
        var totalDocumentLength = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (!candidatePredicate(candidate))
            {
                continue;
            }

            AddDocument(
                candidate,
                textSelector,
                queryTerms,
                documentFrequencies,
                documents,
                ref totalDocumentLength);
        }

        return ScoreDocuments(queryTerms, documentFrequencies, documents, totalDocumentLength, limit);
    }

    private static IReadOnlyList<(T Item, float Score)> ScoreDocuments<T>(
        QueryTerms queryTerms,
        Dictionary<string, int> documentFrequencies,
        List<Document<T>> documents,
        int totalDocumentLength,
        int limit)
    {
        if (documents.Count == 0 || totalDocumentLength == 0)
        {
            return Array.Empty<(T Item, float Score)>();
        }

        var averageDocumentLength = (double)totalDocumentLength / documents.Count;
        var inverseDocumentFrequencies = BuildInverseDocumentFrequencies(
            queryTerms.Terms,
            documentFrequencies,
            documents.Count);

        return ProjectDocumentScores(SearchUtilities.TopByScore(
            documents,
            document => Score(
                document.Length,
                document.QueryTermFrequencies,
                inverseDocumentFrequencies,
                averageDocumentLength),
            limit,
            minScore: 0,
            includeMinScore: false));
    }

    private static void AddDocument<T>(
        T candidate,
        Func<T, string> textSelector,
        QueryTerms queryTerms,
        Dictionary<string, int> documentFrequencies,
        List<Document<T>> documents,
        ref int totalDocumentLength)
    {
        var length = 0;
        Dictionary<string, int>? termFrequencies = null;
        SearchUtilities.VisitTokens(
            textSelector(candidate),
            queryTerms.TermSet,
            (token, terms) =>
        {
            length++;
            if (!terms.Contains(token))
            {
                return;
            }

            termFrequencies ??= new Dictionary<string, int>(StringComparer.Ordinal);
            ref var frequency = ref CollectionsMarshal.GetValueRefOrAddDefault(
                termFrequencies,
                token,
                out var exists);
            frequency++;
            if (exists)
            {
                return;
            }

            ref var documentFrequency = ref CollectionsMarshal.GetValueRefOrAddDefault(
                documentFrequencies,
                token,
                out _);
            documentFrequency++;
        });

        documents.Add(new Document<T>(candidate, length, termFrequencies));
        totalDocumentLength += length;
    }

    private static QueryTerms BuildDistinctQueryTerms(string query)
    {
        var accumulator = new QueryTermAccumulator();
        SearchUtilities.VisitTokens(
            query,
            accumulator,
            static (token, state) =>
            {
                if (state.TermSet.Add(token))
                {
                    state.Terms.Add(token);
                }
            });

        return new QueryTerms(accumulator.Terms, accumulator.TermSet);
    }

    private static Dictionary<string, double> BuildInverseDocumentFrequencies(
        List<string> queryTerms,
        Dictionary<string, int> documentFrequencies,
        int documentCount)
    {
        var idf = new Dictionary<string, double>(queryTerms.Count, StringComparer.Ordinal);
        for (var i = 0; i < queryTerms.Count; i++)
        {
            var term = queryTerms[i];
            var frequency = documentFrequencies.GetValueOrDefault(term);
            if (frequency == 0)
            {
                idf[term] = 0;
                continue;
            }

            idf[term] = Math.Log(1 + (documentCount - frequency + 0.5) / (frequency + 0.5));
        }

        return idf;
    }

    private static List<(T Item, float Score)> ProjectDocumentScores<T>(
        IReadOnlyList<(Document<T> Item, float Score)> ranked)
    {
        var results = new List<(T Item, float Score)>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            results.Add((ranked[i].Item.Item, ranked[i].Score));
        }

        return results;
    }

    private static float Score(
        int tokenCount,
        Dictionary<string, int>? termFrequencies,
        Dictionary<string, double> inverseDocumentFrequencies,
        double averageDocumentLength)
    {
        if (tokenCount == 0 || termFrequencies is null || averageDocumentLength <= 0)
        {
            return 0;
        }

        var lengthNormalization = K1 * (1 - B + B * tokenCount / averageDocumentLength);
        var score = 0d;
        foreach (var pair in termFrequencies)
        {
            var termFrequency = pair.Value;
            var denominator = termFrequency + lengthNormalization;
            if (denominator <= 0)
            {
                continue;
            }

            score += inverseDocumentFrequencies.GetValueOrDefault(pair.Key)
                     * termFrequency
                     * (K1 + 1)
                     / denominator;
        }

        return double.IsFinite(score) ? (float)score : 0;
    }

    private sealed class QueryTermAccumulator
    {
        public List<string> Terms { get; } = new();

        public HashSet<string> TermSet { get; } = new(StringComparer.Ordinal);
    }

    private sealed record QueryTerms(
        List<string> Terms,
        HashSet<string> TermSet);

    private sealed record Document<T>(
        T Item,
        int Length,
        Dictionary<string, int>? QueryTermFrequencies);
}
