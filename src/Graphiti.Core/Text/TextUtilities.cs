using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Graphiti.Core.Text;

/// <summary>
/// Text helpers for episode handling: truncating summaries at sentence boundaries and concatenating
/// episode contents into a single timestamped block (used when summarizing sagas).
/// </summary>
public static partial class TextUtilities
{
    /// <summary>Default maximum length, in characters, for generated summaries.</summary>
    public const int MaxSummaryChars = 1000;

    /// <summary>
    /// Truncates <paramref name="text"/> to at most <paramref name="maxChars"/> characters, preferring
    /// to cut at the last sentence boundary within the limit; falls back to a hard trim if none exists.
    /// </summary>
    public static string? TruncateAtSentence(string? text, int maxChars)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxChars);

        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
        {
            return text;
        }

        var lastBoundaryEnd = -1;
        foreach (var match in SentenceBoundaryRegex().EnumerateMatches(text.AsSpan(0, maxChars)))
        {
            lastBoundaryEnd = match.Index + match.Length;
        }

        if (lastBoundaryEnd < 0)
        {
            return text.AsSpan(0, maxChars).TrimEnd().ToString();
        }

        return text.AsSpan(0, lastBoundaryEnd).TrimEnd().ToString();
    }

    /// <summary>
    /// Joins episode contents into one string, prefixing each with its index and validity timestamp.
    /// </summary>
    public static string ConcatenateEpisodes(IReadOnlyList<SagaEpisodeContent> episodes)
    {
        ArgumentNullException.ThrowIfNull(episodes);

        if (episodes.Count == 0)
        {
            return string.Empty;
        }

        if (episodes.Count == 1)
        {
            return episodes[0].Content;
        }

        return string.Join(
            "\n\n",
            episodes.Select((episode, index) =>
            {
                var timestamp = episode.ValidAt is null
                    ? "unknown"
                    : FormatTimestamp(episode.ValidAt.Value);
                return $"[Episode {index}] (timestamp: {timestamp})\n{episode.Content}";
            }));
    }

    /// <summary>Tuple-based overload of <see cref="ConcatenateEpisodes(IReadOnlyList{SagaEpisodeContent})"/>.</summary>
    public static string ConcatenateEpisodes(
        IReadOnlyList<(string Content, DateTime? ValidAt)> episodes) =>
        ConcatenateEpisodes(episodes.Select(episode => new SagaEpisodeContent(episode.Content, episode.ValidAt)).ToList());

    private static string FormatTimestamp(DateTime timestamp)
    {
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
        var formatted = timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
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

    [GeneratedRegex("[.!?](?:\\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBoundaryRegex();
}
