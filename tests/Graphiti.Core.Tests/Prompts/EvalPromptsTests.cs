using System.Text.Json.Nodes;
using Graphiti.Core.Prompts;

namespace Graphiti.Core.Tests.Prompts;

/// <summary>
/// Golden tests pin the rendered eval prompt; reconcile against parity.md.
/// </summary>
/// <remarks>
/// Every expected string is assembled from explicit line parts joined by <c>"\n"</c> so the exact
/// whitespace each prompt produces is visible and verifiable. Every content line carries a 4-space
/// leading indent (<see cref="Indent"/>), bodies begin with a leading newline, and bodies end with a
/// bare 4-space line (the indentation that precedes the closing triple-quote). Interpolated collections
/// render as compact JSON, the accepted rendering divergence.
/// </remarks>
public class EvalPromptsTests
{
    private const string Indent = "    ";

    [Fact]
    public void BuildQueryExpansion_RendersExpectedPrompt()
    {
        var messages = EvalPrompts.BuildQueryExpansion("Who does Bob report to?");

        Assert.Equal(2, messages.Length);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(
            "You are an expert at rephrasing questions into queries used in a database retrieval system",
            messages[0].Content);
        Assert.Equal("user", messages[1].Role);

        var expected = string.Join(
            "\n",
            string.Empty,
            Indent + "Bob is asking Alice a question, are you able to rephrase the question into a simpler one about Alice in the third person",
            Indent + "that maintains the relevant context?",
            Indent + "<QUESTION>",
            Indent + "\"Who does Bob report to?\"",
            Indent + "</QUESTION>",
            Indent);

        Assert.Equal(expected, messages[1].Content);
    }

    [Fact]
    public void BuildQa_RendersExpectedPrompt()
    {
        var summaries = new JsonArray("Alice manages the Acme launch.");
        var facts = new JsonArray("Alice reports to Carol.");
        var messages = EvalPrompts.BuildQa(summaries, facts, "Who does Alice report to?");

        Assert.Equal(2, messages.Length);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(
            "You are Alice and should respond to all questions from the first person perspective of Alice",
            messages[0].Content);
        Assert.Equal("user", messages[1].Role);

        var expected = string.Join(
            "\n",
            string.Empty,
            Indent + "Your task is to briefly answer the question in the way that you think Alice would answer the question.",
            Indent + "You are given the following entity summaries and facts to help you determine the answer to your question.",
            Indent + "<ENTITY_SUMMARIES>",
            Indent + "[\"Alice manages the Acme launch.\"]",
            Indent + "</ENTITY_SUMMARIES>",
            Indent + "<FACTS>",
            Indent + "[\"Alice reports to Carol.\"]",
            Indent + "</FACTS>",
            Indent + "<QUESTION>",
            Indent + "Who does Alice report to?",
            Indent + "</QUESTION>",
            Indent);

        Assert.Equal(expected, messages[1].Content);
    }

    [Fact]
    public void BuildEval_RendersExpectedPrompt()
    {
        var messages = EvalPrompts.BuildEval(
            "Who owns the Atlas rollout?",
            "Leo Chen",
            "Leo Chen from Operations owns the Atlas deployment.");

        Assert.Equal(2, messages.Length);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(
            "You are a judge that determines if answers to questions match a gold standard answer",
            messages[0].Content);
        Assert.Equal("user", messages[1].Role);

        var expected = string.Join(
            "\n",
            string.Empty,
            Indent + "Given the QUESTION and the gold standard ANSWER determine if the RESPONSE to the question is correct or incorrect.",
            Indent + "Although the RESPONSE may be more verbose, mark it as correct as long as it references the same topic ",
            Indent + "as the gold standard ANSWER. Also include your reasoning for the grade.",
            Indent + "<QUESTION>",
            Indent + "Who owns the Atlas rollout?",
            Indent + "</QUESTION>",
            Indent + "<ANSWER>",
            Indent + "Leo Chen",
            Indent + "</ANSWER>",
            Indent + "<RESPONSE>",
            Indent + "Leo Chen from Operations owns the Atlas deployment.",
            Indent + "</RESPONSE>",
            Indent);

        Assert.Equal(expected, messages[1].Content);
    }

    [Fact]
    public void BuildEvalAddEpisodeResults_RendersExpectedPrompt()
    {
        var messages = EvalPrompts.BuildEvalAddEpisodeResults(
            "User: Leo owns Atlas.",
            "User: Atlas moved to March 29.",
            "baseline-graph",
            "candidate-graph");

        Assert.Equal(2, messages.Length);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(
            "You are a judge that determines whether a baseline graph building result from a list of messages is better\n"
            + "        than a candidate graph building result based on the same messages.",
            messages[0].Content);
        Assert.Equal("user", messages[1].Role);

        var expected = string.Join(
            "\n",
            string.Empty,
            Indent + "Given the following PREVIOUS MESSAGES and MESSAGE, determine if the BASELINE graph data extracted from the ",
            Indent + "conversation is higher quality than the CANDIDATE graph data extracted from the conversation.",
            Indent,
            Indent + "Return False if the BASELINE extraction is better, and True otherwise. If the CANDIDATE extraction and",
            Indent + "BASELINE extraction are nearly identical in quality, return True. Add your reasoning for your decision to the reasoning field",
            Indent,
            Indent + "<PREVIOUS MESSAGES>",
            Indent + "User: Leo owns Atlas.",
            Indent + "</PREVIOUS MESSAGES>",
            Indent + "<MESSAGE>",
            Indent + "User: Atlas moved to March 29.",
            Indent + "</MESSAGE>",
            Indent,
            Indent + "<BASELINE>",
            Indent + "baseline-graph",
            Indent + "</BASELINE>",
            Indent,
            Indent + "<CANDIDATE>",
            Indent + "candidate-graph",
            Indent + "</CANDIDATE>",
            Indent);

        Assert.Equal(expected, messages[1].Content);
    }
}
