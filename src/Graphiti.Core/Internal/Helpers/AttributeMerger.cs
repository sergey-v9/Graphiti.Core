using System.Text.Json.Nodes;

namespace Graphiti.Core.Internal.Helpers;

internal static class AttributeMerger
{
    private const int DefaultAttributeMaxLength = 250;
    private const int AttributeListTotalLengthMultiplier = 8;
    private const string AttributeMaxLengthEnvironmentVariable = "GRAPHITI_ATTRIBUTE_MAX_LENGTH";

    internal static Dictionary<string, object?> ReplaceExtractedAttributes(
        Dictionary<string, object?> priorAttributes,
        EntityTypeDefinition entityType,
        JsonObject response) =>
        MergeExtractedAttributes(
            priorAttributes,
            entityType,
            ExtractDeclaredAttributes(entityType, response),
            AttributeMergeMode.Replace);

    internal static void OverlayExtractedAttributes(
        EntityNode node,
        EntityTypeDefinition entityType,
        JsonObject response)
    {
        var merged = MergeExtractedAttributes(
            node.Attributes,
            entityType,
            ExtractDeclaredAttributes(entityType, response),
            AttributeMergeMode.Overlay);
        node.Attributes.Clear();
        foreach (var pair in merged)
        {
            node.Attributes[pair.Key] = pair.Value;
        }
    }

    private static Dictionary<string, object?> ExtractDeclaredAttributes(
        EntityTypeDefinition entityType,
        JsonObject response)
    {
        var source = response.TryGetPropertyValue("attributes", out var attributesNode) && attributesNode is JsonObject attributes
            ? attributes
            : response;
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        var valuesByName = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            valuesByName[pair.Key] = pair.Value;
        }

        var attributeNames = SortedAttributeNames(entityType);
        foreach (var attributeName in attributeNames)
        {
            if (valuesByName.TryGetValue(attributeName, out var value))
            {
                result[attributeName] = JsonNodeToObject(value);
            }
        }

        return result;
    }

    private static List<string> SortedAttributeNames(EntityTypeDefinition entityType)
    {
        var attributeNames = new List<string>(entityType.Attributes.Count);
        foreach (var attributeName in entityType.Attributes.Keys)
        {
            attributeNames.Add(attributeName);
        }

        attributeNames.Sort(StringComparer.Ordinal);
        return attributeNames;
    }

    private static Dictionary<string, object?> MergeExtractedAttributes(
        Dictionary<string, object?> priorAttributes,
        EntityTypeDefinition entityType,
        Dictionary<string, object?> extractedAttributes,
        AttributeMergeMode mergeMode)
    {
        var capped = CapExtractedAttributes(entityType, extractedAttributes);
        if (mergeMode == AttributeMergeMode.Overlay)
        {
            var merged = new Dictionary<string, object?>(priorAttributes, StringComparer.Ordinal);
            foreach (var pair in capped.Kept)
            {
                merged[pair.Key] = pair.Value;
            }

            return merged;
        }

        var replaced = new Dictionary<string, object?>(capped.Kept, StringComparer.Ordinal);
        if (capped.Dropped is null)
        {
            return replaced;
        }

        foreach (var droppedField in capped.Dropped)
        {
            if (priorAttributes.TryGetValue(droppedField, out var priorValue))
            {
                replaced[droppedField] = priorValue;
            }
        }

        return replaced;
    }

    private static AttributeCapResult CapExtractedAttributes(
        EntityTypeDefinition entityType,
        Dictionary<string, object?> attributes)
    {
        var defaultMaxLength = ResolveAttributeMaxLength();
        var kept = new Dictionary<string, object?>(StringComparer.Ordinal);
        HashSet<string>? dropped = null;
        foreach (var pair in attributes)
        {
            var definition = entityType.Attributes[pair.Key];
            var maxLength = definition.MaxLength ?? defaultMaxLength;
            if (AttributeExceedsCap(pair.Value, maxLength))
            {
                if (definition.Required)
                {
                    kept[pair.Key] = pair.Value;
                    continue;
                }

                (dropped ??= new HashSet<string>(StringComparer.Ordinal)).Add(pair.Key);
                continue;
            }

            kept[pair.Key] = pair.Value;
        }

        return new AttributeCapResult(kept, dropped);
    }

    private static int ResolveAttributeMaxLength()
    {
        var raw = Environment.GetEnvironmentVariable(AttributeMaxLengthEnvironmentVariable);
        return int.TryParse(raw, out var parsed) && parsed > 0
            ? parsed
            : DefaultAttributeMaxLength;
    }

    private static bool AttributeExceedsCap(object? value, int maxLength)
    {
        if (value is string text)
        {
            return text.Length > maxLength;
        }

        if (value is not System.Collections.IEnumerable values
            || value is System.Collections.IDictionary)
        {
            return false;
        }

        var maxItemLength = 0;
        long totalLength = 0;
        foreach (var item in values)
        {
            if (item is not string itemText)
            {
                continue;
            }

            maxItemLength = Math.Max(maxItemLength, itemText.Length);
            totalLength += itemText.Length;
        }

        return maxItemLength > maxLength
               || totalLength > (long)maxLength * AttributeListTotalLengthMultiplier;
    }

    private static object? JsonNodeToObject(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject jsonObject)
        {
            return JsonObjectToDictionary(jsonObject);
        }

        if (node is JsonArray jsonArray)
        {
            return JsonArrayToList(jsonArray);
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var stringValue))
            {
                return stringValue;
            }

            if (value.TryGetValue<bool>(out var boolValue))
            {
                return boolValue;
            }

            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }

            if (value.TryGetValue<long>(out var longValue))
            {
                return longValue;
            }

            if (value.TryGetValue<double>(out var doubleValue))
            {
                return doubleValue;
            }

            if (value.TryGetValue<decimal>(out var decimalValue))
            {
                return decimalValue;
            }
        }

        return node.ToJsonString(GraphitiJsonSerializer.Options);
    }

    private static Dictionary<string, object?> JsonObjectToDictionary(JsonObject jsonObject)
    {
        var result = new Dictionary<string, object?>(jsonObject.Count, StringComparer.Ordinal);
        foreach (var pair in jsonObject)
        {
            result[pair.Key] = JsonNodeToObject(pair.Value);
        }

        return result;
    }

    private static List<object?> JsonArrayToList(JsonArray jsonArray)
    {
        var result = new List<object?>(jsonArray.Count);
        foreach (var item in jsonArray)
        {
            result.Add(JsonNodeToObject(item));
        }

        return result;
    }

    private enum AttributeMergeMode
    {
        Overlay,
        Replace
    }

    private sealed record AttributeCapResult(
        Dictionary<string, object?> Kept,
        HashSet<string>? Dropped);
}
