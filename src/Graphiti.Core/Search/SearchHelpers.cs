using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Graphiti.Core.Search;

/// <summary>
/// Public formatting helpers for turning search results into LLM-ready context, mirroring
/// <c>graphiti_core.search.search_helpers</c>.
/// </summary>
public static class SearchHelpers
{
    /// <summary>
    /// Formats an entity edge's validity window as
    /// <c>"valid_at - invalid_at"</c>, using Python's fallback labels for missing dates.
    /// </summary>
    public static string FormatEdgeDateRange(EntityEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return $"{FormatDateTime(edge.ValidAt, "date unknown")} - {FormatDateTime(edge.InvalidAt, "present")}";
    }

    /// <summary>
    /// Reformats a <see cref="SearchResults"/> object into the same sectioned context string that
    /// Python's <c>search_results_to_context_string</c> returns for direct use in LLM prompts.
    /// </summary>
    public static string SearchResultsToContextString(SearchResults searchResults)
    {
        ArgumentNullException.ThrowIfNull(searchResults);

        var factJson = new JsonArray();
        foreach (var edge in searchResults.Edges)
        {
            factJson.Add(new JsonObject
            {
                ["fact"] = edge.Fact,
                ["valid_at"] = FormatDateTime(edge.ValidAt, "None"),
                ["invalid_at"] = FormatDateTime(edge.InvalidAt, "Present")
            });
        }

        var entityJson = new JsonArray();
        foreach (var node in searchResults.Nodes)
        {
            entityJson.Add(new JsonObject
            {
                ["entity_name"] = node.Name,
                ["summary"] = node.Summary
            });
        }

        var episodeJson = new JsonArray();
        foreach (var episode in searchResults.Episodes)
        {
            episodeJson.Add(new JsonObject
            {
                ["source_description"] = episode.SourceDescription,
                ["content"] = episode.Content
            });
        }

        var communityJson = new JsonArray();
        foreach (var community in searchResults.Communities)
        {
            communityJson.Add(new JsonObject
            {
                ["community_name"] = community.Name,
                ["summary"] = community.Summary
            });
        }

        return $"""

            FACTS and ENTITIES represent relevant context to the current conversation.
            COMMUNITIES represent a cluster of closely related entities.

            These are the most relevant facts and their valid and invalid dates. Facts are considered valid
            between their valid_at and invalid_at dates. Facts with an invalid_at date of "Present" are considered valid.
            <FACTS>
                    {PromptJson.Serialize(factJson)}
            </FACTS>
            <ENTITIES>
                    {PromptJson.Serialize(entityJson)}
            </ENTITIES>
            <EPISODES>
                    {PromptJson.Serialize(episodeJson)}
            </EPISODES>
            <COMMUNITIES>
                    {PromptJson.Serialize(communityJson)}
            </COMMUNITIES>

        """;
    }

    private static string FormatDateTime(DateTime? value, string nullFallback)
    {
        if (value is null)
        {
            return nullFallback;
        }

        var timestamp = value.Value;
        var formatted = FormatLocalDateTime(timestamp);
        return timestamp.Kind switch
        {
            DateTimeKind.Utc => formatted + "+00:00",
            DateTimeKind.Local => formatted + FormatOffset(TimeZoneInfo.Local.GetUtcOffset(timestamp)),
            _ => formatted
        };
    }

    private static string FormatLocalDateTime(DateTime timestamp)
    {
        var formatted = timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var subsecondTicks = timestamp.Ticks % TimeSpan.TicksPerSecond;
        if (subsecondTicks == 0)
        {
            return formatted;
        }

        var fraction = subsecondTicks % 10 == 0
            ? (subsecondTicks / 10).ToString("D6", CultureInfo.InvariantCulture)
            : subsecondTicks.ToString("D7", CultureInfo.InvariantCulture);
        return formatted + "." + fraction;
    }

    private static string FormatOffset(TimeSpan offset)
    {
        var absolute = offset.Duration();
        var builder = new StringBuilder(6);
        builder.Append(offset < TimeSpan.Zero ? '-' : '+');
        builder.Append(((int)absolute.TotalHours).ToString("D2", CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(absolute.Minutes.ToString("D2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }
}
