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
    public void FormatEdgeDateRange_RejectsNullEdge()
    {
        Assert.Throws<ArgumentNullException>(() => SearchHelpers.FormatEdgeDateRange(null!));
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

        const string expected = """

            FACTS and ENTITIES represent relevant context to the current conversation.
            COMMUNITIES represent a cluster of closely related entities.

            These are the most relevant facts and their valid and invalid dates. Facts are considered valid
            between their valid_at and invalid_at dates. Facts with an invalid_at date of "Present" are considered valid.
            <FACTS>
                    [{"fact":"Alice founded Graphiti","valid_at":"2026-01-02 03:04:05+00:00","invalid_at":"Present"},{"fact":"Alice left Zep","valid_at":"None","invalid_at":"2026-02-03 04:05:06+00:00"}]
            </FACTS>
            <ENTITIES>
                    [{"entity_name":"Alice","summary":"Founder"}]
            </ENTITIES>
            <EPISODES>
                    [{"source_description":"chat","content":"Alice mentioned Graphiti."}]
            </EPISODES>
            <COMMUNITIES>
                    [{"community_name":"Graphiti 日本","summary":"関連 entities"}]
            </COMMUNITIES>

        """;

        Assert.Equal(expected, context);
    }

    [Fact]
    public void SearchResultsToContextString_RejectsNullResults()
    {
        Assert.Throws<ArgumentNullException>(() => SearchHelpers.SearchResultsToContextString(null!));
    }
}
