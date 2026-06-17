using Graphiti.Core;

namespace Graphiti.Core.Tests.Text;

public class TextUtilityTests
{
    [Fact]
    public void TruncateAtSentence_ReturnsShortTextUnchanged()
    {
        const string text = "This is a short sentence.";

        var result = TextUtilities.TruncateAtSentence(text, 100);

        Assert.Equal(text, result);
    }

    [Fact]
    public void TruncateAtSentence_HandlesEmptyAndNull()
    {
        Assert.Equal(string.Empty, TextUtilities.TruncateAtSentence(string.Empty, 100));
        Assert.Null(TextUtilities.TruncateAtSentence(null, 100));
    }

    [Theory]
    [InlineData("Alpha beta", -20, "")]
    [InlineData("Alpha beta", -6, "Alph")]
    [InlineData("Alpha beta", -1, "Alpha bet")]
    [InlineData("Alpha. Beta gamma", -6, "Alpha.")]
    [InlineData("Alpha. Beta gamma", -1, "Alpha.")]
    [InlineData("", -1, "")]
    public void TruncateAtSentence_UsesPythonSliceSemanticsForNegativeMaxChars(
        string text,
        int maxChars,
        string expected)
    {
        var result = TextUtilities.TruncateAtSentence(text, maxChars);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void TruncateAtSentence_UsesLastSentenceBoundaryBeforeLimit()
    {
        const string text = "First sentence. Second sentence. Third sentence. Fourth sentence.";

        var result = TextUtilities.TruncateAtSentence(text, 40);

        Assert.Equal("First sentence. Second sentence.", result);
        Assert.True(result!.Length <= 40);
    }

    [Fact]
    public void TruncateAtSentence_FallsBackToCharacterLimit()
    {
        const string text = "This is a very long sentence without any punctuation marks near the beginning";

        var result = TextUtilities.TruncateAtSentence(text, 30);

        Assert.True(result!.Length <= 30);
        Assert.StartsWith("This is a very long sentence", result);
    }

    [Fact]
    public void TruncateAtSentence_StripsTrailingWhitespace()
    {
        const string text = "First sentence.   Second sentence.";

        var result = TextUtilities.TruncateAtSentence(text, 20);

        Assert.Equal("First sentence.", result);
        Assert.False(result!.EndsWith(' '));
    }

    [Fact]
    public void MaxSummaryChars_MatchesPythonConstant()
    {
        Assert.Equal(1000, TextUtilities.MaxSummaryChars);
    }

    [Fact]
    public void ConcatenateEpisodes_ReturnsSingleEpisodeContentAsIs()
    {
        var episode = new SagaEpisodeContent("Hello world", new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        var result = TextUtilities.ConcatenateEpisodes(new[] { episode });

        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void ConcatenateEpisodes_AddsHeadersWithTimestamps()
    {
        var episodes = new[]
        {
            new SagaEpisodeContent("First", new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc)),
            new SagaEpisodeContent("Second", new DateTime(2025, 1, 1, 10, 5, 0, DateTimeKind.Utc)),
            new SagaEpisodeContent("Third", new DateTime(2025, 1, 1, 10, 10, 0, DateTimeKind.Utc))
        };

        var result = TextUtilities.ConcatenateEpisodes(episodes);

        Assert.Equal(
            "[Episode 0] (timestamp: 2025-01-01T10:00:00+00:00)\nFirst\n\n" +
            "[Episode 1] (timestamp: 2025-01-01T10:05:00+00:00)\nSecond\n\n" +
            "[Episode 2] (timestamp: 2025-01-01T10:10:00+00:00)\nThird",
            result);
    }

    [Fact]
    public void ConcatenateEpisodes_FormatsTimestampsLikePythonIsoformat()
    {
        var naiveTimestamp = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Unspecified)
            .AddTicks(123_456 * 10);
        var utcTimestamp = new DateTime(2025, 1, 1, 10, 5, 0, DateTimeKind.Utc)
            .AddTicks(10);
        var localTimestamp = DateTime.SpecifyKind(
            new DateTime(2025, 1, 1, 10, 10, 0),
            DateTimeKind.Local);
        var tickTimestamp = new DateTime(2025, 1, 1, 10, 15, 0, DateTimeKind.Utc)
            .AddTicks(1);
        var episodes = new[]
        {
            new SagaEpisodeContent("Naive", naiveTimestamp),
            new SagaEpisodeContent("Utc", utcTimestamp),
            new SagaEpisodeContent("Local", localTimestamp),
            new SagaEpisodeContent("Tick", tickTimestamp)
        };

        var result = TextUtilities.ConcatenateEpisodes(episodes);
        var localOffset = FormatExpectedOffset(TimeZoneInfo.Local.GetUtcOffset(localTimestamp));

        Assert.Equal(
            "[Episode 0] (timestamp: 2025-01-01T10:00:00.123456)\nNaive\n\n" +
            "[Episode 1] (timestamp: 2025-01-01T10:05:00.000001+00:00)\nUtc\n\n" +
            $"[Episode 2] (timestamp: 2025-01-01T10:10:00{localOffset})\nLocal\n\n" +
            "[Episode 3] (timestamp: 2025-01-01T10:15:00.0000001+00:00)\nTick",
            result);
    }

    [Fact]
    public void ConcatenateEpisodes_UsesBlankLineBetweenMultipleEpisodes()
    {
        var timestamp = new DateTime(2025, 6, 15, 8, 0, 0, DateTimeKind.Utc);
        var episodes = new[] { new SagaEpisodeContent("A", timestamp), new SagaEpisodeContent("B", timestamp) };

        var result = TextUtilities.ConcatenateEpisodes(episodes);

        Assert.Equal(
            "[Episode 0] (timestamp: 2025-06-15T08:00:00+00:00)\nA\n\n" +
            "[Episode 1] (timestamp: 2025-06-15T08:00:00+00:00)\nB",
            result);
    }

    [Fact]
    public void ConcatenateEpisodes_UsesUnknownTimestampForNullValidAt()
    {
        var timestamp = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var episodes = new[] { new SagaEpisodeContent("A", timestamp), new SagaEpisodeContent("B", null) };

        var result = TextUtilities.ConcatenateEpisodes(episodes);

        Assert.Equal(
            "[Episode 0] (timestamp: 2025-01-01T10:00:00+00:00)\nA\n\n" +
            "[Episode 1] (timestamp: unknown)\nB",
            result);
    }

    [Fact]
    public void ConcatenateEpisodes_TupleOverloadMatchesSagaEpisodeContentForMultipleEpisodes()
    {
        var timestamp = new DateTime(2025, 6, 15, 8, 0, 0, DateTimeKind.Utc);
        var episodes = new[] { new SagaEpisodeContent("A", timestamp), new SagaEpisodeContent("B", null) };
        (string Content, DateTime? ValidAt)[] tupleEpisodes =
        [
            ("A", timestamp),
            ("B", null)
        ];

        Assert.Equal(
            TextUtilities.ConcatenateEpisodes(episodes),
            TextUtilities.ConcatenateEpisodes(tupleEpisodes));
    }

    private static string FormatExpectedOffset(TimeSpan offset)
    {
        var absolute = offset.Duration();
        return $"{(offset < TimeSpan.Zero ? "-" : "+")}{(int)absolute.TotalHours:00}:{absolute.Minutes:00}";
    }
}
