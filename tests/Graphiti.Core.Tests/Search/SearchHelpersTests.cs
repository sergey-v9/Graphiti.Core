namespace Graphiti.Core.Tests.Search;

public sealed class SearchHelpersTests
{
    [Fact]
    public void FormatEdgeDateRange_UsesFallbackLabels()
    {
        var edge = new EntityEdge();

        Assert.Equal("date unknown - present", SearchHelpers.FormatEdgeDateRange(edge));
    }

    [Fact]
    public void FormatEdgeDateRange_RendersInvariantDateTimes()
    {
        var edge = new EntityEdge
        {
            ValidAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            InvalidAt = new DateTime(2026, 1, 3, 4, 5, 6, 123, DateTimeKind.Unspecified)
        };

        Assert.Equal(
            "2026-01-02 03:04:05+00:00 - 2026-01-03 04:05:06.123000",
            SearchHelpers.FormatEdgeDateRange(edge));
    }

    [Fact]
    public void SearchResultsToContextString_RendersSearchHelperSections()
    {
        var results = new SearchResults
        {
            Edges =
            {
                new EntityEdge
                {
                    Fact = "Alice founded Graphiti",
                    ValidAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
                },
                new EntityEdge
                {
                    Fact = "Alice left Zep",
                    InvalidAt = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc)
                }
            },
            Nodes =
            {
                new EntityNode { Name = "Alice", Summary = "Founder" }
            },
            Episodes =
            {
                new EpisodicNode
                {
                    SourceDescription = "chat",
                    Content = "Alice mentioned Graphiti."
                }
            },
            Communities =
            {
                new CommunityNode { Name = "Graphiti 日本", Summary = "関連 entities" }
            }
        };

        var context = SearchHelpers.SearchResultsToContextString(results);

        Assert.Contains("FACTS and ENTITIES represent relevant context", context);
        Assert.Contains("<FACTS>", context);
        Assert.Contains("</COMMUNITIES>", context);
        Assert.Contains(
            """{"fact":"Alice founded Graphiti","valid_at":"2026-01-02 03:04:05+00:00","invalid_at":"Present"}""",
            context);
        Assert.Contains(
            """{"fact":"Alice left Zep","valid_at":"None","invalid_at":"2026-02-03 04:05:06+00:00"}""",
            context);
        Assert.Contains("""{"entity_name":"Alice","summary":"Founder"}""", context);
        Assert.Contains("""{"source_description":"chat","content":"Alice mentioned Graphiti."}""", context);
        Assert.Contains("""{"community_name":"Graphiti 日本","summary":"関連 entities"}""", context);
    }
}
