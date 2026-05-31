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

        var builder = new StringBuilder(EstimateConcatenateCapacity(episodes));
        for (var i = 0; i < episodes.Count; i++)
        {
            AppendEpisode(builder, i, episodes[i].Content, episodes[i].ValidAt);
        }

        return builder.ToString();
    }

    /// <summary>Tuple-based overload of <see cref="ConcatenateEpisodes(IReadOnlyList{SagaEpisodeContent})"/>.</summary>
    public static string ConcatenateEpisodes(IReadOnlyList<(string Content, DateTime? ValidAt)> episodes)
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

        var builder = new StringBuilder(EstimateConcatenateCapacity(episodes));
        for (var i = 0; i < episodes.Count; i++)
        {
            AppendEpisode(builder, i, episodes[i].Content, episodes[i].ValidAt);
        }

        return builder.ToString();
    }

    private static void AppendEpisode(
        StringBuilder builder,
        int index,
        string? content,
        DateTime? validAt)
    {
        if (index > 0)
        {
            builder.Append("\n\n");
        }

        builder.Append("[Episode ");
        builder.Append(index.ToString(CultureInfo.InvariantCulture));
        builder.Append("] (timestamp: ");
        builder.Append(validAt is null ? "unknown" : FormatTimestamp(validAt.Value));
        builder.Append(")\n");
        builder.Append(content);
    }

    private static int EstimateConcatenateCapacity(IReadOnlyList<SagaEpisodeContent> episodes)
    {
        var capacity = Math.Max(episodes.Count - 1, 0) * 2;
        for (var i = 0; i < episodes.Count; i++)
        {
            capacity += EstimateEpisodeCapacity(i, episodes[i].Content, episodes[i].ValidAt);
        }

        return capacity;
    }

    private static int EstimateConcatenateCapacity(IReadOnlyList<(string Content, DateTime? ValidAt)> episodes)
    {
        var capacity = Math.Max(episodes.Count - 1, 0) * 2;
        for (var i = 0; i < episodes.Count; i++)
        {
            capacity += EstimateEpisodeCapacity(i, episodes[i].Content, episodes[i].ValidAt);
        }

        return capacity;
    }

    private static int EstimateEpisodeCapacity(int index, string? content, DateTime? validAt)
    {
        const int fixedHeaderLength = 25; // "[Episode " + "] (timestamp: " + ")\n"
        var timestampLength = validAt is null ? "unknown".Length : 33;
        return fixedHeaderLength
               + CountDecimalDigits(index)
               + timestampLength
               + (content?.Length ?? 0);
    }

    private static int CountDecimalDigits(int value)
    {
        var digits = 1;
        while (value >= 10)
        {
            value /= 10;
            digits++;
        }

        return digits;
    }

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
