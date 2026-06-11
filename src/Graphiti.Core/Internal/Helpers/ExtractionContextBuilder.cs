using System.Text.Json.Nodes;

namespace Graphiti.Core.Internal.Helpers;

internal static class ExtractionContextBuilder
{
    internal static readonly IReadOnlyList<string> DefaultEntityLabels =
        Array.AsReadOnly(new[] { "Entity" });

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
            ["excluded_entity_types"] = BuildStringArray(excludedEntityTypes ?? Array.Empty<string>()),
            ["edge_types"] = BuildEdgeTypeContext(edgeTypes, edgeTypeMap),
            ["custom_extraction_instructions"] = customExtractionInstructions ?? string.Empty
        };

        return context;
    }

    internal static JsonObject BuildAttributeSchema(EntityTypeDefinition typeDefinition)
    {
        var schema = new JsonObject();
        var attributes = GetSortedAttributes(typeDefinition);
        for (var i = 0; i < attributes.Count; i++)
        {
            var pair = attributes[i];
            schema[pair.Key] = new JsonObject
            {
                ["type"] = pair.Value.Type,
                ["description"] = pair.Value.Description
            };
        }

        return schema;
    }

    internal static JsonArray BuildStringArray(IReadOnlyList<string> values)
    {
        var result = new JsonArray();
        for (var i = 0; i < values.Count; i++)
        {
            result.Add(JsonValue.Create(values[i]));
        }

        return result;
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
                ["entity_types"] = BuildStringArray(node.Labels)
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

        var sortedEdgeTypes = GetSortedTypeDefinitions(edgeTypes);
        for (var i = 0; i < sortedEdgeTypes.Count; i++)
        {
            var pair = sortedEdgeTypes[i];
            var signatures = BuildEdgeTypeSignatures(pair.Key, edgeTypeMap);
            result.Add(new JsonObject
            {
                ["fact_type_name"] = pair.Value.Name,
                ["fact_type_description"] = pair.Value.Description,
                ["fact_type_signatures"] = BuildEdgeTypeSignatureContext(signatures),
                ["name"] = pair.Value.Name,
                ["description"] = pair.Value.Description,
                ["signatures"] = BuildEdgeTypeSignatureContext(signatures),
                ["attributes"] = BuildAttributeSchema(pair.Value)
            });
        }

        return result;
    }

    private static List<EdgeTypeSignature> BuildEdgeTypeSignatures(
        string edgeTypeName,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap)
    {
        if (edgeTypeMap is null || edgeTypeMap.Count == 0)
        {
            return new List<EdgeTypeSignature>
            {
                new("Entity", "Entity")
            };
        }

        var matchingSignatures =
            new List<KeyValuePair<(string SourceType, string TargetType), IReadOnlyList<string>>>(edgeTypeMap.Count);
        foreach (var pair in edgeTypeMap)
        {
            if (ContainsEdgeTypeName(pair.Value, edgeTypeName))
            {
                matchingSignatures.Add(pair);
            }
        }

        matchingSignatures.Sort(static (left, right) =>
        {
            var sourceComparison = StringComparer.OrdinalIgnoreCase.Compare(
                left.Key.SourceType,
                right.Key.SourceType);
            return sourceComparison != 0
                ? sourceComparison
                : StringComparer.OrdinalIgnoreCase.Compare(left.Key.TargetType, right.Key.TargetType);
        });

        var signatures = new List<EdgeTypeSignature>(matchingSignatures.Count);
        for (var i = 0; i < matchingSignatures.Count; i++)
        {
            var pair = matchingSignatures[i];
            signatures.Add(new EdgeTypeSignature(pair.Key.SourceType, pair.Key.TargetType));
        }

        return signatures;
    }

    private static JsonArray BuildEdgeTypeSignatureContext(IReadOnlyList<EdgeTypeSignature> signatures)
    {
        var result = new JsonArray();
        for (var i = 0; i < signatures.Count; i++)
        {
            var signature = signatures[i];
            result.Add(new JsonObject
            {
                ["source"] = signature.Source,
                ["target"] = signature.Target
            });
        }

        return result;
    }

    private static bool ContainsEdgeTypeName(IReadOnlyList<string> edgeTypeNames, string edgeTypeName)
    {
        for (var i = 0; i < edgeTypeNames.Count; i++)
        {
            if (string.Equals(edgeTypeNames[i], edgeTypeName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static StructuredResponseSchema BuildAttributeResponseSchema(
        EntityTypeDefinition typeDefinition,
        string name)
    {
        var attributeProperties = new JsonObject();
        var requiredAttributes = new JsonArray();
        var attributes = GetSortedAttributes(typeDefinition);
        for (var i = 0; i < attributes.Count; i++)
        {
            var pair = attributes[i];
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

    private static List<KeyValuePair<string, EntityAttributeDefinition>> GetSortedAttributes(
        EntityTypeDefinition typeDefinition)
    {
        var attributes = new List<KeyValuePair<string, EntityAttributeDefinition>>(
            typeDefinition.Attributes.Count);
        foreach (var pair in typeDefinition.Attributes)
        {
            attributes.Add(pair);
        }

        attributes.Sort(static (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.Key, right.Key));
        return attributes;
    }

    private static List<KeyValuePair<string, EntityTypeDefinition>> GetSortedTypeDefinitions(
        IReadOnlyDictionary<string, EntityTypeDefinition> typeDefinitions)
    {
        var sorted = new List<KeyValuePair<string, EntityTypeDefinition>>(typeDefinitions.Count);
        foreach (var pair in typeDefinitions)
        {
            sorted.Add(pair);
        }

        sorted.Sort(static (left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.Key, right.Key));
        return sorted;
    }

    private readonly record struct EdgeTypeSignature(string Source, string Target);
}
