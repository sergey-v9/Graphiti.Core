using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Graphiti.Core;

public sealed partial class Graphiti
{
    internal static List<(string Name, string Type)> ExtractEntityNames(
        JsonObject response,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        var results = new List<(string Name, string Type)>();
        var entityTypeNamesById = BuildEntityTypeNamesById(entityTypes);
        foreach (var key in new[] { "extracted_entities", "entities" })
        {
            if (response.TryGetPropertyValue(key, out var node) && node is JsonArray array)
            {
                foreach (var item in array.OfType<JsonObject>())
                {
                    var name = ReadString(item, "name")
                               ?? ReadString(item, "entity")
                               ?? ReadString(item, "entity_name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var type = ReadString(item, "entity_type")
                               ?? ReadString(item, "type")
                               ?? ReadEntityTypeById(item, entityTypeNamesById)
                               ?? "Entity";
                    results.Add((name, type));
                }
            }
        }

        return results;
    }

    private static List<string> BuildEntityTypeNamesById(
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        var names = new List<string> { "Entity" };
        if (entityTypes is null)
        {
            return names;
        }

        names.AddRange(entityTypes.Select(pair => pair.Value.Name));
        return names;
    }

    private static string? ReadEntityTypeById(JsonObject item, List<string> entityTypeNamesById)
    {
        if (!item.TryGetPropertyValue("entity_type_id", out var node) || node is null)
        {
            return null;
        }

        if (TryReadEntityTypeId(node, out var id))
        {
            return id >= 0 && id < entityTypeNamesById.Count ? entityTypeNamesById[id] : null;
        }

        return null;
    }

    private static bool TryReadEntityTypeId(JsonNode node, out int id)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out id))
            {
                return true;
            }

            if (value.TryGetValue<string>(out var text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            {
                return true;
            }
        }

        id = 0;
        return false;
    }

    internal static List<ExtractedEdge> ExtractEdges(JsonObject response)
    {
        var results = new List<ExtractedEdge>();
        if (!response.TryGetPropertyValue("edges", out var node) || node is not JsonArray array)
        {
            return results;
        }

        foreach (var item in array.OfType<JsonObject>())
        {
            var source = ReadString(item, "source_entity_name") ?? ReadString(item, "source") ?? string.Empty;
            var target = ReadString(item, "target_entity_name") ?? ReadString(item, "target") ?? string.Empty;
            var relation = ReadString(item, "relation_type") ?? ReadString(item, "name") ?? "RELATES_TO";
            var fact = ReadString(item, "fact") ?? string.Empty;
            var validAt = ParseOptionalDate(ReadString(item, "valid_at"));
            var invalidAt = ParseOptionalDate(ReadString(item, "invalid_at"));
            if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target))
            {
                results.Add(new ExtractedEdge(source, target, relation, fact, validAt, invalidAt));
            }
        }

        return results;
    }

    internal static List<(string Name, string Type)> HeuristicEntityNames(string content)
    {
        var source = content ?? string.Empty;
        var sourceSpan = source.AsSpan();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<(string Name, string Type)>();

        foreach (var match in HeuristicEntityRegex().EnumerateMatches(sourceSpan))
        {
            var name = sourceSpan.Slice(match.Index, match.Length).ToString();
            if (IsIgnoredHeuristicEntityName(name) || !seen.Add(name))
            {
                continue;
            }

            results.Add((name, "Entity"));
            if (results.Count == 20)
            {
                break;
            }
        }

        return results;
    }

    private static bool IsIgnoredHeuristicEntityName(string name) =>
        name is "I" or "The" or "A" or "An";

    [GeneratedRegex("\\b[A-Z][a-zA-Z0-9_'-]*\\b", RegexOptions.CultureInvariant)]
    private static partial Regex HeuristicEntityRegex();

    private static string? ReadString(JsonObject item, string key)
    {
        if (!item.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return node.ToJsonString(GraphitiJsonSerializer.Options);
    }

    private static DateTime? ParseOptionalDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return GraphitiHelpers.TryParseDbDate(value, out var parsed) ? parsed : null;
    }

    internal sealed record ExtractedEdge(
        string SourceName,
        string TargetName,
        string RelationType,
        string Fact,
        DateTime? ValidAt,
        DateTime? InvalidAt);

    internal sealed class EpisodeNodeExtractionResponse
    {
        public List<EpisodeGraphExtractedEntityResponse>? ExtractedEntities { get; set; }

        public List<EpisodeGraphExtractedEntityResponse>? Entities { get; set; }
    }

    internal sealed class EpisodeEdgeExtractionResponse
    {
        public List<EpisodeGraphExtractedEdgeResponse>? Edges { get; set; }
    }

    internal sealed class EpisodeGraphExtractedEntityResponse
    {
        public string? Name { get; set; }

        public string? Entity { get; set; }

        public string? EntityName { get; set; }

        public string? EntityType { get; set; }

        public string? Type { get; set; }

        public int? EntityTypeId { get; set; }

        public List<int>? EpisodeIndices { get; set; }
    }

    internal sealed class EpisodeGraphExtractedEdgeResponse
    {
        public string? SourceEntityName { get; set; }

        public string? Source { get; set; }

        public string? TargetEntityName { get; set; }

        public string? Target { get; set; }

        public string? RelationType { get; set; }

        public string? Name { get; set; }

        public string? Fact { get; set; }

        public string? ValidAt { get; set; }

        public string? InvalidAt { get; set; }

        public List<int>? EpisodeIndices { get; set; }
    }

    internal sealed class SagaSummaryResponse
    {
        public string? Summary { get; set; }
    }

    internal sealed class CommunitySummaryResponse
    {
        public string? Summary { get; set; }
    }

    internal sealed class CommunityNameResponse
    {
        public string? Description { get; set; }
    }

    internal sealed class NodeResolutionsResponse
    {
        public List<NodeDuplicateResponse> EntityResolutions { get; set; } = new();
    }

    internal sealed record NodeDuplicateResponse(
        int Id,
        string Name,
        int DuplicateCandidateId);

    internal sealed class EdgeResolutionResponse
    {
        public List<int> DuplicateFacts { get; set; } = new();

        public List<int> ContradictedFacts { get; set; } = new();
    }

    internal sealed class EdgeTimestampResponse
    {
        public string? ValidAt { get; set; }

        public string? InvalidAt { get; set; }
    }
}
