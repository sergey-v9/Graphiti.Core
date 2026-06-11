using Graphiti.Core.Models;
using Graphiti.Core.Models.Nodes;
using Graphiti.Core.Prompts;

namespace Graphiti.Core.Tests.Prompts;

public class ExtractNodesAndEdgesPromptsTests
{
    [Fact]
    public void BuildExtractMessage_RendersPythonParityPromptSections()
    {
        var episode = new EpisodicNode
        {
            Name = "episode",
            Content = "Alice: I met Bob at Acme.",
            Source = EpisodeType.Message,
            GroupId = "group",
            ValidAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };
        var previousEpisode = new EpisodicNode
        {
            Content = "Alice previously mentioned Acme.",
            ValidAt = new DateTime(2026, 1, 1, 3, 4, 5, DateTimeKind.Utc)
        };
        var edgeTypes = new Dictionary<string, EntityTypeDefinition>
        {
            ["WORKS_AT"] = new("WORKS_AT", "Employment relationship")
        };
        var edgeTypeMap = new Dictionary<(string SourceType, string TargetType), IReadOnlyList<string>>
        {
            [("Person", "Organization")] = new[] { "WORKS_AT" }
        };

        var context = ExtractNodesAndEdgesPrompts.BuildContext(
            episode,
            new[] { previousEpisode },
            new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new("Person", "A named person")
            },
            edgeTypes,
            edgeTypeMap,
            "Prefer durable workplace facts.");
        var messages = ExtractNodesAndEdgesPrompts.BuildExtractMessage(context);

        Assert.Equal(2, messages.Length);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(
            "You are an expert knowledge graph extraction specialist for an AI agent memory system. " +
            "You extract both entity nodes and relationship facts from conversations in a single pass. " +
            "The extracted graph will be searched later by an AI agent to answer questions, personalize " +
            "responses, and maintain long-term memory. The original conversation will NOT be available " +
            "at retrieval time - only the entities and facts you extract will survive.",
            messages[0].Content);

        var prompt = messages[1].Content;
        Assert.Contains("ENTITY RULES:", prompt, StringComparison.Ordinal);
        Assert.Contains("FACT RULES:", prompt, StringComparison.Ordinal);
        Assert.Contains("Self-referencing facts are still common and valuable - do NOT skip them", prompt, StringComparison.Ordinal);
        Assert.Contains("Process each episode's CURRENT_MESSAGE independently. Set `episode_indices`", prompt, StringComparison.Ordinal);
        Assert.Contains("OUTPUT DISCIPLINE:", prompt, StringComparison.Ordinal);
        Assert.Contains("<NEGATIVE EXAMPLES>", prompt, StringComparison.Ordinal);
        Assert.Contains("<ENTITY TYPES>", prompt, StringComparison.Ordinal);
        Assert.Contains(
            """{"entity_type_id":1,"entity_type_name":"Person","entity_type_description":"A named person"}""",
            prompt,
            StringComparison.Ordinal);
        Assert.Contains(
            "<FACT TYPES>\n" +
            """[{"fact_type_name":"WORKS_AT","fact_type_signatures":[["Person","Organization"]],"fact_type_description":"Employment relationship"}]""" +
            "\n</FACT TYPES>",
            prompt,
            StringComparison.Ordinal);
        Assert.Contains(
            "<PREVIOUS MESSAGES>\n" +
            "[{\"content\":\"Alice previously mentioned Acme.\",\"timestamp\":\"2026-01-01T03:04:05.0000000Z\"}]\n" +
            "</PREVIOUS MESSAGES>",
            prompt,
            StringComparison.Ordinal);
        Assert.Contains(
            "<CURRENT MESSAGES>\nAlice: I met Bob at Acme.\n</CURRENT MESSAGES>",
            prompt,
            StringComparison.Ordinal);
        Assert.EndsWith("\nPrefer durable workplace facts.", prompt.TrimEnd(), StringComparison.Ordinal);
    }
}
