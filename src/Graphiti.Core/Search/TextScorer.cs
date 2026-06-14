using System.Collections.Frozen;

namespace Graphiti.Core.Search;

/// <summary>
/// A lightweight term-overlap scorer built from a query. Tokenizes text and scores it by the fraction
/// of overlapping terms with the query, used as a cheap relevance heuristic in fallback search paths.
/// </summary>
internal sealed class TextScorer
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

        var state = new ScoreState(_queryTerms.GetAlternateLookup<ReadOnlySpan<char>>());
        SearchUtilities.VisitTokenSpans<ScoreState, ScoreTokenVisitor>(
            text,
            ref state);

        if (state.TextTermCount == 0)
        {
            return 0;
        }

        return (float)((state.MatchCount + (state.DistinctMatches?.Count ?? 0))
                       / (double)(state.TextTermCount + _queryTerms.Count));
    }

    private struct ScoreState(FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> queryTerms)
    {
        public FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> QueryTerms { get; } = queryTerms;

        public int TextTermCount { get; set; }

        public int MatchCount { get; set; }

        public HashSet<string>? DistinctMatches { get; set; }

        public HashSet<string>.AlternateLookup<ReadOnlySpan<char>> DistinctMatchesLookup { get; set; }
    }

    private readonly record struct ScoreTokenVisitor
        : SearchUtilities.ISpanTokenVisitor<ScoreState>
    {
        public static void Visit(ReadOnlySpan<char> term, ref ScoreState state)
        {
            state.TextTermCount++;
            if (!state.QueryTerms.Contains(term))
            {
                return;
            }

            state.MatchCount++;
            if (state.DistinctMatches is null)
            {
                var distinct = new HashSet<string>(StringComparer.Ordinal);
                state.DistinctMatches = distinct;
                state.DistinctMatchesLookup = distinct.GetAlternateLookup<ReadOnlySpan<char>>();
            }

            // Materializes the string key only on first insertion of a distinct match; repeated
            // matches probe by span and allocate nothing.
            state.DistinctMatchesLookup.Add(term);
        }
    }
}
