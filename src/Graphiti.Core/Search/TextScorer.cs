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

        var textTermCount = 0;
        var matchCount = 0;
        HashSet<string>? distinctMatches = null;
        SearchUtilities.VisitTokens(
            text,
            _queryTerms,
            (term, queryTerms) =>
        {
            textTermCount++;
            if (!queryTerms.Contains(term))
            {
                return;
            }

            matchCount++;
            distinctMatches ??= new HashSet<string>(StringComparer.Ordinal);
            distinctMatches.Add(term);
        });

        if (textTermCount == 0)
        {
            return 0;
        }

        return (float)((matchCount + (distinctMatches?.Count ?? 0))
                       / (double)(textTermCount + _queryTerms.Count));
    }
}
