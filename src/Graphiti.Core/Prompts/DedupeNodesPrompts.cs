using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graphiti.Core.Prompts;

/// <summary>
/// Prompt builders for node deduplication, with the context shaped by LLM node resolution. The
/// instruction text follows the prompt parity contract in <c>.agents/notes/decisions.md</c>; golden
/// tests pin the rendered output.
/// </summary>
internal static class DedupeNodesPrompts
{
    internal readonly record struct NodeDeduplicationContext(
        string PreviousEpisodesJson,
        string EpisodeContent,
        string ExtractedNodesJson,
        string ExistingNodesJson,
        int ExtractedNodeCount);

    internal static NodeDeduplicationContext BuildContext(
        IReadOnlyList<EntityNode> extractedNodes,
        IReadOnlyList<EntityNode> candidates,
        EpisodicNode? episode,
        IReadOnlyList<EpisodicNode>? previousEpisodes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        return new NodeDeduplicationContext(
            PromptJson.Serialize(ExtractNodesPrompts.BuildPreviousEpisodesContext(
                previousEpisodes ?? Array.Empty<EpisodicNode>())),
            episode?.Content ?? string.Empty,
            PromptJson.Serialize(BuildExtractedNodesContext(extractedNodes, entityTypes)),
            PromptJson.Serialize(BuildExistingNodesContext(candidates)),
            extractedNodes.Count);
    }

    internal static Message[] BuildNodes(in NodeDeduplicationContext context)
    {
        var lastExtractedNodeId = context.ExtractedNodeCount - 1;
        var userPrompt = $$"""

            <PREVIOUS MESSAGES>
            {{context.PreviousEpisodesJson}}
            </PREVIOUS MESSAGES>

            <CURRENT MESSAGE>
            {{context.EpisodeContent}}
            </CURRENT MESSAGE>

            <ENTITIES>
            {{context.ExtractedNodesJson}}
            </ENTITIES>

            <EXISTING ENTITIES>
            {{context.ExistingNodesJson}}
            </EXISTING ENTITIES>

            Each of the above ENTITIES was extracted from the CURRENT MESSAGE.
            For each entity, determine if it is a duplicate of any EXISTING ENTITY.
            Entities should only be considered duplicates if they refer to the *same real-world object or concept*.

            NEVER mark entities as duplicates if:
            - They are related but distinct.
            - They have similar names or purposes but refer to separate instances or concepts.

            Task:
            ENTITIES contains {{context.ExtractedNodeCount}} entities with IDs 0 through {{lastExtractedNodeId}}.
            Your response MUST include EXACTLY {{context.ExtractedNodeCount}} resolutions with IDs 0 through {{lastExtractedNodeId}}. Do not skip or add IDs.

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

        return new[]
        {
            new Message(
                "system",
                "You are an entity deduplication assistant. " +
                "NEVER fabricate entity names or mark distinct entities as duplicates."),
            new Message("user", userPrompt)
        };
    }

    private static JsonArray BuildExtractedNodesContext(
        IReadOnlyList<EntityNode> extractedNodes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        var context = new JsonArray();
        for (var i = 0; i < extractedNodes.Count; i++)
        {
            var node = extractedNodes[i];
            context.Add(new JsonObject
            {
                ["id"] = i,
                ["name"] = node.Name,
                ["entity_type"] = ExtractionContextBuilder.BuildStringArray(node.Labels),
                ["entity_type_description"] = EntityTypeDescription(node, entityTypes)
            });
        }

        return context;
    }

    private static JsonArray BuildExistingNodesContext(IReadOnlyList<EntityNode> candidates)
    {
        var context = new JsonArray();
        for (var i = 0; i < candidates.Count; i++)
        {
            context.Add(BuildExistingNodeContext(candidates[i], i));
        }

        return context;
    }

    private static JsonObject BuildExistingNodeContext(EntityNode candidate, int candidateId)
    {
        var context = JsonSerializer.SerializeToNode(candidate.Attributes, GraphitiJsonSerializer.Options) as JsonObject
                      ?? new JsonObject();
        context["candidate_id"] = candidateId;
        context["name"] = candidate.Name;
        context["entity_types"] = ExtractionContextBuilder.BuildStringArray(candidate.Labels);
        context["summary"] = string.IsNullOrEmpty(candidate.Summary)
            ? string.Empty
            : candidate.Summary.Length > 120
                ? candidate.Summary[..120]
                : candidate.Summary;
        return context;
    }

    private static string EntityTypeDescription(
        EntityNode node,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        // Select ONLY the first label != "Entity" (default ""), do a SINGLE lookup, and return its
        // description if non-empty else "Default Entity Type". Never fall through to later labels.
        var typeName = string.Empty;
        for (var i = 0; i < node.Labels.Count; i++)
        {
            if (!string.Equals(node.Labels[i], "Entity", StringComparison.Ordinal))
            {
                typeName = node.Labels[i];
                break;
            }
        }

        if (entityTypes is not null
            && entityTypes.TryGetValue(typeName, out var definition)
            && !string.IsNullOrWhiteSpace(definition.Description))
        {
            return definition.Description;
        }

        return "Default Entity Type";
    }
}
