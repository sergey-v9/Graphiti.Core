using System.Text.Json.Nodes;

namespace Graphiti.Core.Prompts;

/// <summary>
/// Prompt builders ported from Python <c>graphiti_core/prompts/eval.py</c> (the four eval prompt
/// functions: <c>query_expansion</c>, <c>qa_prompt</c>, <c>eval_prompt</c>, and
/// <c>eval_add_episode_results</c>). The instruction text is transcribed near-verbatim per the prompt
/// parity contract in <c>.agents/notes/decisions.md</c>; golden tests pin the rendered output. Do not
/// reword the prose without a parity reason.
/// </summary>
/// <remarks>
/// These prompts mirror Python f-strings defined at the module-function level, so every content line
/// carries a 4-space leading indent and the bodies begin with a leading newline, exactly as Python
/// renders them. <c>to_prompt_json</c> values are rendered as compact JSON (the allowed JSON divergence
/// from Python's spaced <c>json.dumps</c> output).
/// </remarks>
internal static class EvalPrompts
{
    // Every Python eval f-string body ends with the 4-space indentation that precedes its closing
    // triple-quote (e.g. "...</QUESTION>\n    "). C# raw-string literals strip that closing-delimiter
    // indentation, so the trailing 4 spaces are reattached explicitly to preserve byte parity.
    private const string TrailingIndent = "\n    ";

    /// <summary>
    /// Ports <c>eval.py::query_expansion</c> (eval.py:64-77). Rephrases Bob's question into a
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
    /// Ports <c>eval.py::qa_prompt</c> (eval.py:80-99). Answers the question as Alice using the supplied
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
    /// Ports <c>eval.py::eval_prompt</c> (eval.py:102-124). Judges whether <paramref name="response"/>
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
    /// Ports <c>eval.py::eval_add_episode_results</c> (eval.py:127-156). Judges whether the BASELINE graph
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
