using Graphiti.Core.Models;
using Graphiti.Core.Models.Nodes;
using Graphiti.Core.Prompts;

namespace Graphiti.Core.Tests.Prompts;

/// <summary>
/// Golden tests pinning the rendered node-deduplication prompt to the Python source
/// (graphiti_core/prompts/dedupe_nodes.py). The expected text is transcribed independently from
/// Python; if a test fails after an edit, reconcile against the Python file, not against the
/// builder.
/// </summary>
public class DedupeNodesPromptsTests
{
    private static EpisodicNode CreateEpisode(string content, DateTime validAt) => new()
    {
        Name = "episode",
        Content = content,
        Source = EpisodeType.Message,
        SourceDescription = "a chat transcript",
        GroupId = "group",
        ValidAt = validAt
    };

    private static EntityNode CreateNode(string name, params string[] labels) => new()
    {
        Name = name,
        GroupId = "group",
        Labels = labels.Length == 0 ? new List<string> { "Entity" } : labels.ToList()
    };

    [Fact]
    public void BuildNodes_RendersPythonParityPrompt()
    {
        var episode = CreateEpisode(
            "Alice: I saw NYC and Java mentioned together.",
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var previous = new[]
        {
            CreateEpisode(
                "Alice previously visited New York City.",
                new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc))
        };
        var extractedNodes = new[]
        {
            CreateNode("NYC", "Entity", "Location"),
            CreateNode("Java")
        };
        var candidates = new[]
        {
            new EntityNode
            {
                Name = "New York City",
                GroupId = "group",
                Labels = new List<string> { "Entity", "Location" },
                Summary = "A city in New York."
            },
            new EntityNode
            {
                Name = "Java",
                GroupId = "group",
                Labels = new List<string> { "Entity", "Location" },
                Summary = "An island in Indonesia."
            }
        };
        var entityTypes = new Dictionary<string, EntityTypeDefinition>
        {
            ["Location"] = new("Location", "A city, region, or other place.")
        };

        var context = DedupeNodesPrompts.BuildContext(
            extractedNodes,
            candidates,
            episode,
            previous,
            entityTypes);
        var messages = DedupeNodesPrompts.BuildNodes(context);

        Assert.Equal(2, messages.Length);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal(
            "You are an entity deduplication assistant. " +
            "NEVER fabricate entity names or mark distinct entities as duplicates.",
            messages[0].Content);
        Assert.Equal("user", messages[1].Role);

        var expected = """

            <PREVIOUS MESSAGES>
            [{"content":"Alice previously visited New York City.","timestamp":"2026-01-01T12:00:00.0000000Z"}]
            </PREVIOUS MESSAGES>

            <CURRENT MESSAGE>
            Alice: I saw NYC and Java mentioned together.
            </CURRENT MESSAGE>

            <ENTITIES>
            [{"id":0,"name":"NYC","entity_type":["Entity","Location"],"entity_type_description":"A city, region, or other place."},{"id":1,"name":"Java","entity_type":["Entity"],"entity_type_description":"Default Entity Type"}]
            </ENTITIES>

            <EXISTING ENTITIES>
            [{"candidate_id":0,"name":"New York City","entity_types":["Entity","Location"],"summary":"A city in New York."},{"candidate_id":1,"name":"Java","entity_types":["Entity","Location"],"summary":"An island in Indonesia."}]
            </EXISTING ENTITIES>

            Each of the above ENTITIES was extracted from the CURRENT MESSAGE.
            For each entity, determine if it is a duplicate of any EXISTING ENTITY.
            Entities should only be considered duplicates if they refer to the *same real-world object or concept*.

            NEVER mark entities as duplicates if:
            - They are related but distinct.
            - They have similar names or purposes but refer to separate instances or concepts.

            Task:
            ENTITIES contains 2 entities with IDs 0 through 1.
            Your response MUST include EXACTLY 2 resolutions with IDs 0 through 1. Do not skip or add IDs.

            For every entity, provide:
            - `id`: integer id from ENTITIES
            - `name`: the best full name for the entity (preserve the original name unless a duplicate has a more complete name)
            - `duplicate_candidate_id`: the `candidate_id` of the EXISTING ENTITY that is the best duplicate match, or -1 if there is no duplicate

            <EXAMPLE>
            ENTITY: "Sam" (Person)
            EXISTING ENTITIES: [{"candidate_id": 0, "name": "Sam", "entity_types": ["Person"], "summary": "Sam enjoys hiking and photography"}]
            Result: duplicate_candidate_id = 0 (same person referenced in conversation)

            ENTITY: "NYC"
            EXISTING ENTITIES: [{"candidate_id": 0, "name": "New York City", "entity_types": ["Location"]}, {"candidate_id": 1, "name": "New York Knicks", "entity_types": ["Organization"]}]
            Result: duplicate_candidate_id = 0 (same location, abbreviated name)

            ENTITY: "Java" (programming language)
            EXISTING ENTITIES: [{"candidate_id": 0, "name": "Java", "entity_types": ["Location"], "summary": "An island in Indonesia"}]
            Result: duplicate_candidate_id = -1 (same name but distinct real-world things)

            ENTITY: "Marco's car"
            EXISTING ENTITIES: [{"candidate_id": 0, "name": "Marco's vehicle", "entity_types": ["Entity"], "summary": "Marco drives a red sedan."}]
            Result: duplicate_candidate_id = 0 (synonym - "car" and "vehicle" refer to the same thing, same possessor)
            </EXAMPLE>
            """;
        Assert.Equal(expected, messages[1].Content);
    }

    [Fact]
    public void BuildNodes_EntityTypeDescription_UsesFirstNonEntityLabelOnly()
    {
        // Python _get_entity_type_description (node_operations.py:184-189) selects ONLY the first
        // label != "Entity", does a SINGLE lookup, and falls back to "Default Entity Type" when
        // that label is absent from entity_types. It NEVER falls through to later labels, even if a
        // later label (here "Location") IS present with a description.
        var episode = CreateEpisode(
            "Alice mentioned an unknown place.",
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var extractedNodes = new[]
        {
            CreateNode("Mystery", "Entity", "Unknown", "Location")
        };
        var candidates = Array.Empty<EntityNode>();
        var entityTypes = new Dictionary<string, EntityTypeDefinition>
        {
            // "Unknown" is intentionally absent; "Location" IS present with a description.
            ["Location"] = new("Location", "A city, region, or other place.")
        };

        var context = DedupeNodesPrompts.BuildContext(
            extractedNodes,
            candidates,
            episode,
            Array.Empty<EpisodicNode>(),
            entityTypes);
        var messages = DedupeNodesPrompts.BuildNodes(context);

        // First non-Entity label is "Unknown" (absent) -> single lookup misses -> default.
        // It must NOT fall through to "Location"'s real description.
        Assert.Contains(
            "\"entity_type_description\":\"Default Entity Type\"",
            messages[1].Content);
        Assert.DoesNotContain(
            "\"entity_type_description\":\"A city, region, or other place.\"",
            messages[1].Content);
    }
}
