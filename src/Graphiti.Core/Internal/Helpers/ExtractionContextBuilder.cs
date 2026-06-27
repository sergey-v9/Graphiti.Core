using System.Text.Json.Nodes;

namespace Graphiti.Core.Internal.Helpers;

internal static class ExtractionContextBuilder
{
    internal static readonly IReadOnlyList<string> DefaultEntityLabels =
        Array.AsReadOnly<string>(["Entity"]);

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
            var jsonSchemaType = NormalizeJsonSchemaType(pair.Value.Type);
            var attributeSchema = new JsonObject
            {
                ["type"] = new JsonArray(
                    JsonValue.Create(jsonSchemaType),
                    JsonValue.Create("null")),
                ["description"] = pair.Value.Description
            };
            if (pair.Value.MaxLength is int maxLength && jsonSchemaType == "string")
            {
                attributeSchema["maxLength"] = maxLength;
            }

            attributeProperties[pair.Key] = attributeSchema;
            if (pair.Value.Required)
            {
                requiredAttributes.Add(JsonValue.Create(pair.Key));
            }
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
            StringComparer.Ordinal.Compare(left.Key, right.Key));
        return attributes;
    }
}
