using System.Collections.Frozen;

namespace Graphiti.Core.Search;

/// <summary>
/// A lightweight term-overlap scorer built from a query. Tokenizes text and scores it by the fraction
/// of overlapping terms with the query, used as a cheap relevance heuristic in fallback search paths.
/// </summary>
public sealed class TextScorer
{
    private readonly FrozenSet<string> _queryTerms;

    internal TextScorer(string query)
    {
        var queryTerms = new HashSet<string>(StringComparer.Ordinal);
        SearchUtilities.VisitTokens(
            query,
            queryTerms,
            static (term, terms) => terms.Add(term));
        _queryTerms = queryTerms.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>Scores the given text against the query terms; higher means more overlap.</summary>
    public float Score(string text)
    {
        if (_queryTerms.Count == 0)
        {
            return 0;
        }

        var state = new ScoreState(_queryTerms);
        SearchUtilities.VisitTokens<ScoreState, ScoreTokenVisitor>(
            text,
            ref state);

        if (state.TextTermCount == 0)
        {
            return 0;
        }

        return (float)((state.MatchCount + (state.DistinctMatches?.Count ?? 0))
                       / (double)(state.TextTermCount + _queryTerms.Count));
    }

    private record struct ScoreState(FrozenSet<string> QueryTerms)
    {
        public int TextTermCount { get; set; }

        public int MatchCount { get; set; }

        public HashSet<string>? DistinctMatches { get; set; }
    }

    private readonly record struct ScoreTokenVisitor
        : SearchUtilities.ITokenVisitor<ScoreState>
    {
        public static void Visit(string term, ref ScoreState state)
        {
            state.TextTermCount++;
            if (!state.QueryTerms.Contains(term))
            {
                return;
            }

            state.MatchCount++;
            state.DistinctMatches ??= new HashSet<string>(StringComparer.Ordinal);
            state.DistinctMatches.Add(term);
        }
    }
}
