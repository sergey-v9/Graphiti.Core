namespace Graphiti.Core.Drivers.Ladybug;

internal static class LadybugFulltextQuery
{
    internal static string Build(string? query, IReadOnlyList<string>? groupIds)
    {
        GraphitiHelpers.ValidateGroupIds(groupIds);

        // Mirrors graphiti_core/search/search_utils.py:88-92 (KUZU branch of fulltext_query):
        // KUZU only supports simple queries. The word count is taken by splitting on a SINGLE
        // space character; if it exceeds MAX_QUERY_LENGTH the search is skipped (empty string =
        // no search), otherwise the query is returned VERBATIM with no normalization.
        //
        // Python's search() entry point (search.py:117) already short-circuits whitespace-only
        // queries before fulltext_query is reached, so guarding the blank case here as "no search"
        // is faithful to the overall behavior and avoids issuing an index query for nothing.
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var wordCount = CountSpaceSeparatedParts(query);
        if (wordCount > SearchUtilities.MaxQueryLength)
        {
            return string.Empty;
        }

        return query;
    }

    /// <summary>
    /// Counts the parts produced by Python's <c>query.split(' ')</c>: a split on the single space
    /// character only, which (unlike <c>str.split()</c>) keeps empty strings and never trims, so an
    /// N-space run yields N+1 parts. The count is always at least one for a non-empty string.
    /// </summary>
    private static int CountSpaceSeparatedParts(string query)
    {
        var count = 1;
        foreach (var ch in query)
        {
            if (ch == ' ')
            {
                count++;
            }
        }

        return count;
    }
}
