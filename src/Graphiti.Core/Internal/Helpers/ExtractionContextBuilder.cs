using System.Text.Json.Nodes;

namespace Graphiti.Core.Internal.Helpers;

internal static class ExtractionContextBuilder
{
    private const string DefaultEntityTypeDescription =
        "A specific, identifiable entity that does not fit any of the other listed types. " +
        "Must still be a concrete, meaningful thing - specific enough to be uniquely identifiable. " +
        "GOOD: a named entity not covered by the other types. " +
        "BAD: \"luck\", \"ideas\", \"tomorrow\", \"things\", \"them\", \"everybody\", " +
        "\"a sense of wonder\", \"great times\". " +
        "When in doubt, do not extract the entity.";

    internal static JsonObject BuildExtractionContext(
        EpisodicNode episode,
        IReadOnlyList<EpisodicNode> previousEpisodes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes,
        IReadOnlyList<string>? excludedEntityTypes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap,
        string? customExtractionInstructions,
        IReadOnlyList<EntityNode>? nodes = null)
    {
        var previous = new JsonArray();
        foreach (var previousEpisode in previousEpisodes)
        {
            previous.Add(new JsonObject
            {
                ["uuid"] = previousEpisode.Uuid,
                ["content"] = previousEpisode.Content,
                ["source"] = previousEpisode.Source.ToWireValue(),
                ["valid_at"] = GraphitiHelpers.EnsureUtc(previousEpisode.ValidAt).ToString("O")
            });
        }

        var context = new JsonObject
        {
            ["episode_content"] = episode.Content,
            ["episode_source"] = episode.Source.ToWireValue(),
            ["episode_name"] = episode.Name,
            ["source_description"] = episode.SourceDescription,
            ["reference_time"] = GraphitiHelpers.EnsureUtc(episode.ValidAt).ToString("O"),
            ["previous_episodes"] = previous,
            ["nodes"] = BuildExtractedNodeContext(nodes),
            ["entity_types"] = BuildTypeContext(entityTypes),
            ["excluded_entity_types"] = new JsonArray((excludedEntityTypes ?? Array.Empty<string>())
                .Select(type => JsonValue.Create(type))
                .ToArray()),
            ["edge_types"] = BuildEdgeTypeContext(edgeTypes, edgeTypeMap),
            ["custom_extraction_instructions"] = customExtractionInstructions ?? string.Empty
        };

        return context;
    }

    internal static JsonObject BuildAttributeSchema(EntityTypeDefinition typeDefinition)
    {
        var schema = new JsonObject();
        foreach (var pair in typeDefinition.Attributes.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            schema[pair.Key] = new JsonObject
            {
                ["type"] = pair.Value.Type,
                ["description"] = pair.Value.Description
            };
        }

        return schema;
    }

    internal static Dictionary<EntityTypeDefinition, StructuredResponseSchema> CreateAttributeResponseSchemas(
        IEnumerable<EntityTypeDefinition> typeDefinitions,
        string name)
    {
        var schemas = new Dictionary<EntityTypeDefinition, StructuredResponseSchema>();
        foreach (var typeDefinition in typeDefinitions.Distinct())
        {
            schemas[typeDefinition] = BuildAttributeResponseSchema(typeDefinition, name);
        }

        return schemas;
    }

    private static JsonArray BuildExtractedNodeContext(IReadOnlyList<EntityNode>? nodes)
    {
        var result = new JsonArray();
        if (nodes is null)
        {
            return result;
        }

        foreach (var node in nodes)
        {
            result.Add(new JsonObject
            {
                ["name"] = node.Name,
                ["entity_types"] = new JsonArray(node.Labels.Select(label => JsonValue.Create(label)).ToArray())
            });
        }

        return result;
    }

    private static JsonArray BuildTypeContext(IReadOnlyDictionary<string, EntityTypeDefinition>? typeDefinitions)
    {
        var result = new JsonArray
        {
            new JsonObject
            {
                ["entity_type_id"] = 0,
                ["entity_type_name"] = "Entity",
                ["entity_type_description"] = DefaultEntityTypeDescription,
                ["name"] = "Entity",
                ["description"] = DefaultEntityTypeDescription,
                ["attributes"] = new JsonObject()
            }
        };
        if (typeDefinitions is null)
        {
            return result;
        }

        var index = 1;
        foreach (var pair in typeDefinitions)
        {
            result.Add(new JsonObject
            {
                ["entity_type_id"] = index++,
                ["entity_type_name"] = pair.Value.Name,
                ["entity_type_description"] = pair.Value.Description,
                ["name"] = pair.Value.Name,
                ["description"] = pair.Value.Description,
                ["attributes"] = BuildAttributeSchema(pair.Value)
            });
        }

        return result;
    }

    private static JsonArray BuildEdgeTypeContext(
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap)
    {
        var result = new JsonArray();
        if (edgeTypes is null)
        {
            return result;
        }

        foreach (var pair in edgeTypes.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(new JsonObject
            {
                ["fact_type_name"] = pair.Value.Name,
                ["fact_type_description"] = pair.Value.Description,
                ["fact_type_signatures"] = BuildEdgeTypeSignatures(pair.Key, edgeTypeMap),
                ["name"] = pair.Value.Name,
                ["description"] = pair.Value.Description,
                ["signatures"] = BuildEdgeTypeSignatures(pair.Key, edgeTypeMap),
                ["attributes"] = BuildAttributeSchema(pair.Value)
            });
        }

        return result;
    }

    private static JsonArray BuildEdgeTypeSignatures(
        string edgeTypeName,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap)
    {
        var signatures = new JsonArray();
        if (edgeTypeMap is null || edgeTypeMap.Count == 0)
        {
            signatures.Add(new JsonObject
            {
                ["source"] = "Entity",
                ["target"] = "Entity"
            });
            return signatures;
        }

        foreach (var pair in edgeTypeMap
                     .Where(pair => pair.Value.Any(name => string.Equals(name, edgeTypeName, StringComparison.OrdinalIgnoreCase)))
                     .OrderBy(pair => pair.Key.SourceType, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(pair => pair.Key.TargetType, StringComparer.OrdinalIgnoreCase))
        {
            signatures.Add(new JsonObject
            {
                ["source"] = pair.Key.SourceType,
                ["target"] = pair.Key.TargetType
            });
        }

        return signatures;
    }

    private static StructuredResponseSchema BuildAttributeResponseSchema(
        EntityTypeDefinition typeDefinition,
        string name)
    {
        var attributeProperties = new JsonObject();
        var requiredAttributes = new JsonArray();
        foreach (var pair in typeDefinition.Attributes.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            attributeProperties[pair.Key] = new JsonObject
            {
                ["type"] = new JsonArray(
                    JsonValue.Create(NormalizeJsonSchemaType(pair.Value.Type)),
                    JsonValue.Create("null")),
                ["description"] = pair.Value.Description
            };
            requiredAttributes.Add(JsonValue.Create(pair.Key));
        }

        return new StructuredResponseSchema(
            name,
            new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JsonArray(JsonValue.Create("attributes")),
                ["properties"] = new JsonObject
                {
                    ["attributes"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = requiredAttributes,
                        ["properties"] = attributeProperties
                    }
                }
            });
    }

    private static string NormalizeJsonSchemaType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return "string";
        }

        var normalized = type.Trim().ToLowerInvariant();
        return normalized switch
        {
            "string" or "number" or "integer" or "boolean" or "object" or "array" => normalized,
            "bool" => "boolean",
            "double" or "float" or "decimal" => "number",
            "int" or "long" or "short" => "integer",
            _ => "string"
        };
    }
}
