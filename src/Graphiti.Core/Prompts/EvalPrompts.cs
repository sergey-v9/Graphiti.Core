using System.Text.Json.Nodes;

namespace Graphiti.Core.Prompts;

/// <summary>
/// Prompt builders for the four eval prompts: query expansion, QA, answer evaluation, and
/// add-episode-results evaluation. The instruction text follows the prompt parity contract in
/// <c>.agents/notes/decisions.md</c>; golden tests pin the rendered output. Do not reword the prose
/// without a parity reason.
/// </summary>
/// <remarks>
/// Every content line carries a 4-space leading indent and the bodies begin with a leading newline,
/// which the golden tests pin exactly. Interpolated JSON values are rendered as compact JSON (the
/// allowed JSON rendering divergence).
/// </remarks>
internal static class EvalPrompts
{
    // Each eval prompt body ends with 4 spaces of indentation before its closing delimiter (e.g.
    // "...</QUESTION>\n    "). C# raw-string literals strip that closing-delimiter indentation, so the
    // trailing 4 spaces are reattached explicitly to preserve the pinned output bytes.
    private const string TrailingIndent = "\n    ";

    /// <summary>
    /// Builds the query-expansion prompt. Rephrases Bob's question into a
    /// third-person query about Alice for database retrieval. <paramref name="query"/> is rendered via
    /// <c>to_prompt_json</c>.
    /// </summary>
    internal static Message[] BuildQueryExpansion(string query)
    {
        var userPrompt = $"""

                Bob is asking Alice a question, are you able to rephrase the question into a simpler one about Alice in the third person
                that maintains the relevant context?
                <QUESTION>
                {PromptJson.Serialize(JsonValue.Create(query))}
                </QUESTION>
            """ + TrailingIndent;

        return new[]
        {
            new Message(
                "system",
                "You are an expert at rephrasing questions into queries used in a database retrieval system"),
            new Message("user", userPrompt)
        };
    }

    /// <summary>
    /// Builds the QA prompt. Answers the question as Alice using the supplied
    /// entity summaries and facts. <paramref name="entitySummaries"/> and <paramref name="facts"/> are
    /// rendered via <c>to_prompt_json</c>; <paramref name="query"/> is interpolated verbatim.
    /// </summary>
    internal static Message[] BuildQa(JsonNode? entitySummaries, JsonNode? facts, string query)
    {
        var userPrompt = $"""

                Your task is to briefly answer the question in the way that you think Alice would answer the question.
                You are given the following entity summaries and facts to help you determine the answer to your question.
                <ENTITY_SUMMARIES>
                {PromptJson.Serialize(entitySummaries)}
                </ENTITY_SUMMARIES>
                <FACTS>
                {PromptJson.Serialize(facts)}
                </FACTS>
                <QUESTION>
                {query}
                </QUESTION>
            """ + TrailingIndent;

        return new[]
        {
            new Message(
                "system",
                "You are Alice and should respond to all questions from the first person perspective of Alice"),
            new Message("user", userPrompt)
        };
    }

    /// <summary>
    /// Builds the answer-evaluation prompt. Judges whether <paramref name="response"/>
    /// matches the gold standard <paramref name="answer"/> for <paramref name="query"/>. All three values
    /// are interpolated verbatim.
    /// </summary>
    internal static Message[] BuildEval(string query, string answer, string response)
    {
        var userPrompt = $"""

                Given the QUESTION and the gold standard ANSWER determine if the RESPONSE to the question is correct or incorrect.
                Although the RESPONSE may be more verbose, mark it as correct as long as it references the same topic{" "}
                as the gold standard ANSWER. Also include your reasoning for the grade.
                <QUESTION>
                {query}
                </QUESTION>
                <ANSWER>
                {answer}
                </ANSWER>
                <RESPONSE>
                {response}
                </RESPONSE>
            """ + TrailingIndent;

        return new[]
        {
            new Message(
                "system",
                "You are a judge that determines if answers to questions match a gold standard answer"),
            new Message("user", userPrompt)
        };
    }

    /// <summary>
    /// Builds the add-episode-results evaluation prompt. Judges whether the BASELINE graph
    /// extraction is higher quality than the CANDIDATE for the same message. Returns <c>candidate_is_worse
    /// = False</c> when the baseline is better and <c>True</c> otherwise (including near-identical quality).
    /// All four values are interpolated verbatim.
    /// </summary>
    internal static Message[] BuildEvalAddEpisodeResults(
        string previousMessages,
        string message,
        string baseline,
        string candidate)
    {
        var userPrompt = $"""

                Given the following PREVIOUS MESSAGES and MESSAGE, determine if the BASELINE graph data extracted from the{" "}
                conversation is higher quality than the CANDIDATE graph data extracted from the conversation.
                {""}
                Return False if the BASELINE extraction is better, and True otherwise. If the CANDIDATE extraction and
                BASELINE extraction are nearly identical in quality, return True. Add your reasoning for your decision to the reasoning field
                {""}
                <PREVIOUS MESSAGES>
                {previousMessages}
                </PREVIOUS MESSAGES>
                <MESSAGE>
                {message}
                </MESSAGE>
                {""}
                <BASELINE>
                {baseline}
                </BASELINE>
                {""}
                <CANDIDATE>
                {candidate}
                </CANDIDATE>
            """ + TrailingIndent;

        return new[]
        {
            new Message(
                "system",
                "You are a judge that determines whether a baseline graph building result from a list of messages is better\n        than a candidate graph building result based on the same messages."),
            new Message("user", userPrompt)
        };
    }
}
