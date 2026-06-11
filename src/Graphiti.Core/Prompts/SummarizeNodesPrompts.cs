using System.Text.Json.Nodes;

namespace Graphiti.Core.Prompts;

/// <summary>
/// Prompt builders ported from Python <c>graphiti_core/prompts/summarize_nodes.py</c>. The
/// instruction text is transcribed near-verbatim per the prompt parity contract in
/// <c>.agents/notes/decisions.md</c>; golden tests pin the rendered output.
/// </summary>
internal static class SummarizeNodesPrompts
{
    internal static Message[] BuildSummarizePair(string leftSummary, string rightSummary)
    {
        var userPrompt = $$"""

                    Synthesize the information from the following two summaries into a single information-dense summary.

                    IMPORTANT:
                    - Preserve all materially relevant names, roles, places, dates, counts, and changes over time that are explicitly supported.
                    - Prefer compact factual sentences over vague thematic phrasing.
                    - When the durable fact is the content of what was said, state the content directly instead of narrating that it was said.
                    - Use communication verbs only when the act of speaking, asking, sharing, presenting, or announcing is itself the important fact.
                    - Avoid filler verbs like "mentioned", "described", "stated", "reported", "noted", "discussed", "referenced", and "indicated" unless the communication act itself matters.
                    - SUMMARIES MUST BE LESS THAN {{TextUtilities.MaxSummaryChars}} CHARACTERS.

                    Summaries:
                    {{PromptJson.Serialize(BuildSummaryPairContext(leftSummary, rightSummary))}}
            """;

        return new[]
        {
            new Message(
                "system",
                "You are a helpful assistant that combines summaries into a single dense factual summary."),
            new Message("user", userPrompt)
        };
    }

    internal static Message[] BuildSummaryDescription(string summary)
    {
        var userPrompt = $$"""

                    Create a short one sentence description of the summary that explains what kind of information is summarized.
                    Summaries must be under {{TextUtilities.MaxSummaryChars}} characters.

                    Summary:
                    {{PromptJson.Serialize(JsonValue.Create(summary))}}
            """;

        return new[]
        {
            new Message(
                "system",
                "You are a helpful assistant that describes provided contents in a single sentence."),
            new Message("user", userPrompt)
        };
    }

    private static JsonArray BuildSummaryPairContext(string leftSummary, string rightSummary) =>
        new(
            new JsonObject { ["summary"] = leftSummary },
            new JsonObject { ["summary"] = rightSummary });
}
