using Graphiti.Core.Drivers;
using Graphiti.Core.Models.Nodes;
using Graphiti.Core.Prompts;

namespace Graphiti.Core.Tests.Prompts;

/// <summary>
/// Golden tests pinning the rendered saga summary prompt to the Python source
/// (graphiti_core/prompts/summarize_sagas.py). The expected text is transcribed independently from
/// Python; if a test fails after an edit, reconcile against the Python file, not against the
/// builder.
/// </summary>
public class SummarizeSagasPromptsTests
{
    [Fact]
    public void BuildSummarizeSaga_RendersPythonParityPrompt()
    {
        var saga = new SagaNode
        {
            Name = "launch",
            GroupId = "group",
            Summary = "Prior launch decision"
        };
        var episodes = new[]
        {
            new SagaEpisodeContent("Launch moved to March 15.", new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)),
            new SagaEpisodeContent("Marketing owns the checklist.", new DateTime(2026, 1, 2, 4, 5, 6, DateTimeKind.Utc))
        };

        var messages = SummarizeSagasPrompts.BuildSummarizeSaga(saga, episodes);

        Assert.Equal(2, messages.Length);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(
            "You extract durable knowledge from message threads. " +
            "Output a factual knowledge brief - facts, decisions, preferences, plans, " +
            "entities, and relationships - that stands alone without reference to the original messages. " +
            "Stay under 1000 characters.",
            messages[0].Content);
        Assert.Equal("user", messages[1].Role);

        var expected = """
NEVER use meta-language verbs: "mentioned", "discussed", "noted", "stated", "described", "referenced", "indicated", "reported", "talked about", "brought up" - these describe conversational dynamics, not knowledge. State facts directly instead.
NEVER refer to the messages, conversation, thread, or participants' communicative acts. The output must read as if no conversation happened - only the facts matter.
NEVER begin with "This conversation", "The thread", "In this thread", or "The discussion".
NEVER infer preferences or habits from a single passing mention. When a person explicitly states a preference ("I prefer X", "I love X", "I always do X"), capture it as a stated preference attributed to that person.

Your task: extract all durable knowledge from the MESSAGES below and produce a factual knowledge brief for the topic "launch".

Capture explicitly stated:
- Facts and concrete details (names, dates, numbers, locations)
- Decisions and their outcomes
- Preferences and requirements (when a person explicitly claims them)
- Plans, next steps, and commitments
- Relationships between entities (who works where, who owns what)
- State changes (what was X, now is Y)

Write 2-6 dense sentences. Use third person. Preserve all names, dates, counts, and temporal qualifiers. Lead with the most important fact or decision.

<EXISTING_KNOWLEDGE>
Prior launch decision
</EXISTING_KNOWLEDGE>
The EXISTING_KNOWLEDGE contains previously extracted facts. Merge any new facts from MESSAGES into it. When newer messages contradict older facts, prefer the newer fact. If MESSAGES add no new durable facts, return the existing knowledge unchanged.

<MESSAGES>
Launch moved to March 15.
---
Marketing owns the checklist.
</MESSAGES>

<EXAMPLES>
MESSAGES: "Jordan: We decided to move the deployment to March 15 instead of March 8. The staging environment isn't ready.\n---\nPriya: Agreed. I'll update the client timeline. We also need to switch from PostgreSQL to CockroachDB for the multi-region requirement."
GOOD: "Deployment moved from March 8 to March 15 because the staging environment is not ready. Priya owns updating the client timeline. The database is switching from PostgreSQL to CockroachDB to support the multi-region requirement."
BAD: "Jordan mentioned moving the deployment date. Priya discussed updating the timeline and talked about switching databases. The team noted staging issues."
</EXAMPLES>

<EXAMPLES>
MESSAGES: "Alex: I tried the new Thai place on Elm Street last night - the pad see ew was incredible. Definitely going back.\n---\nMina: Oh nice, I've been wanting to try that. Is it the one next to the bookstore?\n---\nAlex: Yeah, Siam Kitchen. They're open until 11 PM on weekends."
GOOD: "Siam Kitchen is a Thai restaurant on Elm Street, next to a bookstore, open until 11 PM on weekends. Alex considers the pad see ew excellent."
BAD: "Alex mentioned trying a new Thai place and discussed the pad see ew. Mina asked about the location. Alex noted it was Siam Kitchen and stated the weekend hours."
</EXAMPLES>

<EXAMPLES>
MESSAGES: "Sam: I really prefer working in the mornings - I'm way more productive before noon.\n---\nDana: Same. I've been blocking 9-11 AM for deep work. Also, I can't stand Jira - can we move the tracker to Linear?\n---\nSam: Fine by me. I'll set up the workspace."
GOOD: "Sam prefers morning work and reports higher productivity before noon. Dana blocks 9-11 AM for deep work. Dana prefers Linear over Jira for issue tracking. Sam is setting up the Linear workspace."
BAD: "Sam and Dana discussed their work preferences. They talked about morning productivity and mentioned switching from Jira to Linear."
</EXAMPLES>
""";
        Assert.Equal(expected, messages[1].Content);
    }

    [Fact]
    public void BuildSummarizeSaga_RendersWhitespaceExistingSummaryLikePython()
    {
        // Python summarize_sagas.py checks `if existing_summary`, so whitespace-only summaries are
        // still rendered as existing knowledge. Do not use whitespace-trimming truthiness here.
        var saga = new SagaNode
        {
            Name = "launch",
            GroupId = "group",
            Summary = "   "
        };
        var episodes = new[]
        {
            new SagaEpisodeContent("Launch moved to March 15.", null)
        };

        var messages = SummarizeSagasPrompts.BuildSummarizeSaga(saga, episodes);

        Assert.Equal(2, messages.Length);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(
            "You extract durable knowledge from message threads. " +
            "Output a factual knowledge brief - facts, decisions, preferences, plans, " +
            "entities, and relationships - that stands alone without reference to the original messages. " +
            "Stay under 1000 characters.",
            messages[0].Content);
        Assert.Equal("user", messages[1].Role);

        var expected = """
NEVER use meta-language verbs: "mentioned", "discussed", "noted", "stated", "described", "referenced", "indicated", "reported", "talked about", "brought up" - these describe conversational dynamics, not knowledge. State facts directly instead.
NEVER refer to the messages, conversation, thread, or participants' communicative acts. The output must read as if no conversation happened - only the facts matter.
NEVER begin with "This conversation", "The thread", "In this thread", or "The discussion".
NEVER infer preferences or habits from a single passing mention. When a person explicitly states a preference ("I prefer X", "I love X", "I always do X"), capture it as a stated preference attributed to that person.

Your task: extract all durable knowledge from the MESSAGES below and produce a factual knowledge brief for the topic "launch".

Capture explicitly stated:
- Facts and concrete details (names, dates, numbers, locations)
- Decisions and their outcomes
- Preferences and requirements (when a person explicitly claims them)
- Plans, next steps, and commitments
- Relationships between entities (who works where, who owns what)
- State changes (what was X, now is Y)

Write 2-6 dense sentences. Use third person. Preserve all names, dates, counts, and temporal qualifiers. Lead with the most important fact or decision.
""" + "\n\n<EXISTING_KNOWLEDGE>\n   \n</EXISTING_KNOWLEDGE>\n" + """
The EXISTING_KNOWLEDGE contains previously extracted facts. Merge any new facts from MESSAGES into it. When newer messages contradict older facts, prefer the newer fact. If MESSAGES add no new durable facts, return the existing knowledge unchanged.

<MESSAGES>
Launch moved to March 15.
</MESSAGES>

<EXAMPLES>
MESSAGES: "Jordan: We decided to move the deployment to March 15 instead of March 8. The staging environment isn't ready.\n---\nPriya: Agreed. I'll update the client timeline. We also need to switch from PostgreSQL to CockroachDB for the multi-region requirement."
GOOD: "Deployment moved from March 8 to March 15 because the staging environment is not ready. Priya owns updating the client timeline. The database is switching from PostgreSQL to CockroachDB to support the multi-region requirement."
BAD: "Jordan mentioned moving the deployment date. Priya discussed updating the timeline and talked about switching databases. The team noted staging issues."
</EXAMPLES>

<EXAMPLES>
MESSAGES: "Alex: I tried the new Thai place on Elm Street last night - the pad see ew was incredible. Definitely going back.\n---\nMina: Oh nice, I've been wanting to try that. Is it the one next to the bookstore?\n---\nAlex: Yeah, Siam Kitchen. They're open until 11 PM on weekends."
GOOD: "Siam Kitchen is a Thai restaurant on Elm Street, next to a bookstore, open until 11 PM on weekends. Alex considers the pad see ew excellent."
BAD: "Alex mentioned trying a new Thai place and discussed the pad see ew. Mina asked about the location. Alex noted it was Siam Kitchen and stated the weekend hours."
</EXAMPLES>

<EXAMPLES>
MESSAGES: "Sam: I really prefer working in the mornings - I'm way more productive before noon.\n---\nDana: Same. I've been blocking 9-11 AM for deep work. Also, I can't stand Jira - can we move the tracker to Linear?\n---\nSam: Fine by me. I'll set up the workspace."
GOOD: "Sam prefers morning work and reports higher productivity before noon. Dana blocks 9-11 AM for deep work. Dana prefers Linear over Jira for issue tracking. Sam is setting up the Linear workspace."
BAD: "Sam and Dana discussed their work preferences. They talked about morning productivity and mentioned switching from Jira to Linear."
</EXAMPLES>
""";

        Assert.Equal(expected, messages[1].Content);
    }
}
