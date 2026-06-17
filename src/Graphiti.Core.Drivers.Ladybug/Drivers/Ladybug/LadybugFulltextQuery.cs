namespace Graphiti.Core.Drivers.Ladybug;

internal static class LadybugFulltextQuery
{
    internal static string Build(string? query, IReadOnlyList<string>? groupIds)
    {
        GraphitiHelpers.ValidateGroupIds(groupIds);

        // LadybugDB only supports simple full-text queries. The word count is taken by splitting on
        // a SINGLE space character; if it exceeds MaxQueryLength the search is skipped (empty string
        // = no search), otherwise the query is returned VERBATIM with no normalization.
        //
        // Whitespace-only queries are guarded here as "no search": the search entry point already
        // skips blank queries before this point, so returning empty avoids issuing an index query
        // for nothing.
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
    /// Counts the parts produced by splitting on the single space character only: empty strings
    /// between consecutive spaces are kept and nothing is trimmed, so an N-space run yields N+1
    /// parts. The count is always at least one for a non-empty string.
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
