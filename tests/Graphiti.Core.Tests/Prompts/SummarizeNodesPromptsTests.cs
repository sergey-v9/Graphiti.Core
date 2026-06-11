using Graphiti.Core.Prompts;

namespace Graphiti.Core.Tests.Prompts;

/// <summary>
/// Golden tests pinning the rendered community/node summary prompts to the Python source
/// (graphiti_core/prompts/summarize_nodes.py). The expected text is transcribed independently from
/// Python; if a test fails after an edit, reconcile against the Python file, not against the
/// builder.
/// </summary>
public class SummarizeNodesPromptsTests
{
    [Fact]
    public void BuildSummarizePair_RendersPythonParityPrompt()
    {
        var messages = SummarizeNodesPrompts.BuildSummarizePair(
            "Alice manages the Acme launch.",
            "Bob coordinates release operations.");

        Assert.Equal(2, messages.Length);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(
            "You are a helpful assistant that combines summaries into a single dense factual summary.",
            messages[0].Content);
        Assert.Equal("user", messages[1].Role);

        var expected = """

                    Synthesize the information from the following two summaries into a single information-dense summary.

                    IMPORTANT:
                    - Preserve all materially relevant names, roles, places, dates, counts, and changes over time that are explicitly supported.
                    - Prefer compact factual sentences over vague thematic phrasing.
                    - When the durable fact is the content of what was said, state the content directly instead of narrating that it was said.
                    - Use communication verbs only when the act of speaking, asking, sharing, presenting, or announcing is itself the important fact.
                    - Avoid filler verbs like "mentioned", "described", "stated", "reported", "noted", "discussed", "referenced", and "indicated" unless the communication act itself matters.
                    - SUMMARIES MUST BE LESS THAN 1000 CHARACTERS.

                    Summaries:
                    [{"summary":"Alice manages the Acme launch."},{"summary":"Bob coordinates release operations."}]
            """;
        Assert.Equal(expected, messages[1].Content);
    }

    [Fact]
    public void BuildSummaryDescription_RendersPythonParityPrompt()
    {
        var messages = SummarizeNodesPrompts.BuildSummaryDescription(
            "Alice and Bob coordinate the Acme launch.");

        Assert.Equal(2, messages.Length);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(
            "You are a helpful assistant that describes provided contents in a single sentence.",
            messages[0].Content);
        Assert.Equal("user", messages[1].Role);

        var expected = """

                    Create a short one sentence description of the summary that explains what kind of information is summarized.
                    Summaries must be under 1000 characters.

                    Summary:
                    "Alice and Bob coordinate the Acme launch."
            """;
        Assert.Equal(expected, messages[1].Content);
    }
}
