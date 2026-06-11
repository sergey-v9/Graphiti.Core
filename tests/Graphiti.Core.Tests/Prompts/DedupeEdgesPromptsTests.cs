using Graphiti.Core.Models.Edges;
using Graphiti.Core.Prompts;

namespace Graphiti.Core.Tests.Prompts;

/// <summary>
/// Golden tests pinning the rendered edge-deduplication prompt to the Python source
/// (graphiti_core/prompts/dedupe_edges.py). The expected text is transcribed independently from
/// Python; if a test fails after an edit, reconcile against the Python file, not against the
/// builder.
/// </summary>
public class DedupeEdgesPromptsTests
{
    private static EntityEdge CreateEdge(string fact) => new()
    {
        Fact = fact,
        Name = "RELATES_TO",
        GroupId = "group"
    };

    [Fact]
    public void BuildResolveEdge_RendersPythonParityPrompt()
    {
        var newEdge = CreateEdge("Alice works at Acme Corp as a senior engineer.");
        var relatedEdges = new[]
        {
            CreateEdge("Alice works at Acme Corp as a software engineer.")
        };
        var invalidationCandidates = new[]
        {
            CreateEdge("Alice joined Acme Corp in 2020.")
        };

        var context = DedupeEdgesPrompts.BuildContext(newEdge, relatedEdges, invalidationCandidates);
        var messages = DedupeEdgesPrompts.BuildResolveEdge(context);

        Assert.Equal(2, messages.Length);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(
            "You are a fact deduplication assistant. " +
            "NEVER mark facts with key differences as duplicates.",
            messages[0].Content);
        Assert.Equal("user", messages[1].Role);

        var expected = """

            NEVER mark facts as duplicates if they have key differences, particularly around numeric values, dates, or key qualifiers.

            IMPORTANT constraints:
            - duplicate_facts: ONLY idx values from EXISTING FACTS (NEVER include FACT INVALIDATION CANDIDATES)
            - contradicted_facts: idx values from EITHER list (EXISTING FACTS or FACT INVALIDATION CANDIDATES)
            - The idx values are continuous across both lists (INVALIDATION CANDIDATES start where EXISTING FACTS end)

            <EXISTING FACTS>
            [{"idx":0,"fact":"Alice works at Acme Corp as a software engineer."}]
            </EXISTING FACTS>

            <FACT INVALIDATION CANDIDATES>
            [{"idx":1,"fact":"Alice joined Acme Corp in 2020."}]
            </FACT INVALIDATION CANDIDATES>

            <NEW FACT>
            Alice works at Acme Corp as a senior engineer.
            </NEW FACT>

            You will receive TWO lists of facts with CONTINUOUS idx numbering across both lists.
            EXISTING FACTS are indexed first, followed by FACT INVALIDATION CANDIDATES.

            1. DUPLICATE DETECTION:
               - If the NEW FACT represents identical factual information as any fact in EXISTING FACTS, return those idx values in duplicate_facts.
               - If no duplicates, return an empty list for duplicate_facts.

            2. CONTRADICTION DETECTION:
               - Determine which facts the NEW FACT contradicts from either list.
               - A fact from EXISTING FACTS can be both a duplicate AND contradicted (e.g., semantically the same but the new fact updates/supersedes it).
               - Return all contradicted idx values in contradicted_facts.
               - If no contradictions, return an empty list for contradicted_facts.

            <EXAMPLE>
            EXISTING FACT: idx=0, "Alice joined Acme Corp in 2020"
            NEW FACT: "Alice joined Acme Corp in 2020"
            Result: duplicate_facts=[0], contradicted_facts=[] (identical factual information)

            EXISTING FACT: idx=1, "Alice works at Acme Corp as a software engineer"
            NEW FACT: "Alice works at Acme Corp as a senior engineer"
            Result: duplicate_facts=[], contradicted_facts=[1] (same relationship but updated title - contradiction, NOT a duplicate)

            EXISTING FACT: idx=2, "Bob ran 5 miles on Tuesday"
            NEW FACT: "Bob ran 3 miles on Wednesday"
            Result: duplicate_facts=[], contradicted_facts=[] (different events on different days - neither duplicate nor contradiction)
            </EXAMPLE>
            """;
        Assert.Equal(expected, messages[1].Content);
    }
}
