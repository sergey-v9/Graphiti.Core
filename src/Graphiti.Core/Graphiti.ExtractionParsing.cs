using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Graphiti.Core;

public sealed partial class Graphiti
{
    private static readonly string[] EntityExtractionArrayKeys = ["extracted_entities", "entities"];
    private static readonly string[] DefaultEntityTypeNamesById = ["Entity"];

    internal static List<(string Name, string Type)> ExtractEntityNames(
        JsonObject response,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        var entities = ExtractEntities(response, entityTypes);
        var results = new List<(string Name, string Type)>(entities.Count);
        for (var i = 0; i < entities.Count; i++)
        {
            results.Add((entities[i].Name, entities[i].Type));
        }

        return results;
    }

    internal static List<ExtractedEntity> ExtractEntities(
        JsonObject response,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        var results = new List<ExtractedEntity>();
        IReadOnlyList<string>? entityTypeNamesById = null;
        foreach (var key in EntityExtractionArrayKeys)
        {
            if (response.TryGetPropertyValue(key, out var node) && node is JsonArray array)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is not JsonObject item)
                    {
                        continue;
                    }

                    var name = ReadString(item, "name")
                               ?? ReadString(item, "entity")
                               ?? ReadString(item, "entity_name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var type = ReadString(item, "entity_type")
                               ?? ReadString(item, "type")
                               ?? ReadEntityTypeById(item, entityTypes, ref entityTypeNamesById)
                               ?? "Entity";
                    results.Add(new ExtractedEntity(name, type, ReadEpisodeIndices(item)));
                }
            }
        }

        return results;
    }

    private static IReadOnlyList<string> BuildEntityTypeNamesById(
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        if (entityTypes is null || entityTypes.Count == 0)
        {
            return DefaultEntityTypeNamesById;
        }

        var names = new List<string>(entityTypes.Count + 1) { "Entity" };
        foreach (var pair in entityTypes)
        {
            names.Add(pair.Key);
        }

        return names;
    }

    private static string? ReadEntityTypeById(
        JsonObject item,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes,
        ref IReadOnlyList<string>? entityTypeNamesById)
    {
        if (!item.TryGetPropertyValue("entity_type_id", out var node) || node is null)
        {
            throw new JsonException("Extracted entity is missing required entity_type_id.");
        }

        if (TryReadInt(node, out var id))
        {
            entityTypeNamesById ??= BuildEntityTypeNamesById(entityTypes);
            return id >= 0 && id < entityTypeNamesById.Count ? entityTypeNamesById[id] : null;
        }

        throw new JsonException("Extracted entity entity_type_id must be an integer.");
    }

    private static bool TryReadInt(JsonNode node, out int id)
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
        if (!response.TryGetPropertyValue("edges", out var node) || node is not JsonArray array)
        {
            return [];
        }

        var results = new List<ExtractedEdge>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject item)
            {
                continue;
            }

            var source = ReadString(item, "source_entity_name") ?? ReadString(item, "source") ?? string.Empty;
            var target = ReadString(item, "target_entity_name") ?? ReadString(item, "target") ?? string.Empty;
            // relation_type is a required field on both the edge and combined-fact response shapes; a
            // response whose edge lacks one is rejected. Never fabricate a relation name: skip the
            // edge when neither relation_type nor the lenient `name` alias is present (matches the
            // no-fabrication parity contract).
            var relation = ReadString(item, "relation_type") ?? ReadString(item, "name");
            var fact = ReadString(item, "fact") ?? string.Empty;
            var validAt = ParseOptionalDate(ReadString(item, "valid_at"));
            var invalidAt = ParseOptionalDate(ReadString(item, "invalid_at"));
            if (!string.IsNullOrWhiteSpace(source)
                && !string.IsNullOrWhiteSpace(target)
                && !string.IsNullOrWhiteSpace(relation))
            {
                results.Add(new ExtractedEdge(
                    source,
                    target,
                    relation,
                    fact,
                    validAt,
                    invalidAt,
                    ReadEpisodeIndices(item)));
            }
        }

        return results;
    }

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

    private static IReadOnlyList<int> ReadEpisodeIndices(JsonObject item)
    {
        if (!item.TryGetPropertyValue("episode_indices", out var node) || node is not JsonArray array)
        {
            return EpisodeAttribution.FirstEpisodeIndex;
        }

        var indices = new List<int>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not null && TryReadInt(array[i]!, out var index))
            {
                indices.Add(index);
            }
        }

        return indices;
    }

    internal sealed record ExtractedEntity(
        string Name,
        string Type,
        IReadOnlyList<int> EpisodeIndices);

    internal sealed record ExtractedEdge
    {
        public ExtractedEdge(
            string sourceName,
            string targetName,
            string relationType,
            string fact,
            DateTime? validAt,
            DateTime? invalidAt,
            IReadOnlyList<int>? episodeIndices = null,
            DateTime? referenceTime = null,
            bool allowSelfEdge = false)
        {
            SourceName = sourceName;
            TargetName = targetName;
            RelationType = relationType;
            Fact = fact;
            ValidAt = validAt;
            InvalidAt = invalidAt;
            EpisodeIndices = episodeIndices ?? EpisodeAttribution.FirstEpisodeIndex;
            ReferenceTime = referenceTime;
            AllowSelfEdge = allowSelfEdge;
        }

        public string SourceName { get; init; }

        public string TargetName { get; init; }

        public string RelationType { get; init; }

        public string Fact { get; init; }

        public DateTime? ValidAt { get; init; }

        public DateTime? InvalidAt { get; init; }

        public IReadOnlyList<int> EpisodeIndices { get; init; }

        public DateTime? ReferenceTime { get; init; }

        public bool AllowSelfEdge { get; init; }
    }

    internal sealed class EpisodeNodeExtractionResponse
    {
        [JsonRequired]
        public List<EpisodeGraphExtractedEntityResponse> ExtractedEntities { get; set; } = new();
    }

    internal sealed class EpisodeEdgeExtractionResponse
    {
        [JsonRequired]
        public List<EpisodeGraphExtractedEdgeResponse> Edges { get; set; } = new();
    }

    internal sealed class EpisodeGraphExtractedEntityResponse
    {
        [JsonRequired]
        public string Name { get; set; } = string.Empty;

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        [JsonRequired]
        public int EntityTypeId { get; set; }

        public List<int>? EpisodeIndices { get; set; }
    }

    internal sealed class EpisodeGraphExtractedEdgeResponse
    {
        [JsonRequired]
        public string SourceEntityName { get; set; } = string.Empty;

        [JsonRequired]
        public string TargetEntityName { get; set; } = string.Empty;

        [JsonRequired]
        public string RelationType { get; set; } = string.Empty;

        [JsonRequired]
        public string Fact { get; set; } = string.Empty;

        public string? ValidAt { get; set; }

        public string? InvalidAt { get; set; }

        public List<int>? EpisodeIndices { get; set; }
    }

    internal sealed class SagaSummaryResponse
    {
        [JsonRequired]
        public string Summary { get; set; } = string.Empty;
    }

    internal sealed class CommunitySummaryResponse
    {
        [JsonRequired]
        public string Summary { get; set; } = string.Empty;
    }

    internal sealed class CommunityNameResponse
    {
        [JsonRequired]
        public string Description { get; set; } = string.Empty;
    }

    internal sealed class SummarizedEntitiesResponse
    {
        [JsonRequired]
        public List<SummarizedEntityResponse> Summaries { get; set; } = new();
    }

    internal sealed record SummarizedEntityResponse(
        [property: JsonRequired]
        string Name,
        [property: JsonRequired]
        string Summary);

    internal sealed class NodeResolutionsResponse
    {
        [JsonRequired]
        public List<NodeDuplicateResponse> EntityResolutions { get; set; } = new();
    }

    internal sealed record NodeDuplicateResponse(
        [property: JsonRequired]
        int Id,
        [property: JsonRequired]
        string Name,
        [property: JsonRequired]
        int DuplicateCandidateId);

    internal sealed class EdgeResolutionResponse
    {
        [JsonRequired]
        public List<int> DuplicateFacts { get; set; } = new();

        [JsonRequired]
        public List<int> ContradictedFacts { get; set; } = new();
    }

    internal sealed class EdgeTimestampResponse
    {
        public string? ValidAt { get; set; }

        public string? InvalidAt { get; set; }
    }

    internal sealed class BatchEdgeTimestampsResponse
    {
        [JsonRequired]
        public List<EdgeTimestampResponse> Timestamps { get; set; } = new();
    }

    internal sealed class CombinedExtractionResponse
    {
        [JsonRequired]
        public List<CombinedExtractedEntityResponse> ExtractedEntities { get; set; } = new();

        [JsonRequired]
        public List<CombinedExtractedEdgeResponse> Edges { get; set; } = new();
    }

    internal sealed class CombinedExtractedEntityResponse
    {
        [JsonRequired]
        public string Name { get; set; } = string.Empty;

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        [JsonRequired]
        public int EntityTypeId { get; set; }
    }

    internal sealed class CombinedExtractedEdgeResponse
    {
        [JsonRequired]
        public string SourceEntityName { get; set; } = string.Empty;

        [JsonRequired]
        public string TargetEntityName { get; set; } = string.Empty;

        [JsonRequired]
        public string RelationType { get; set; } = string.Empty;

        [JsonRequired]
        public string Fact { get; set; } = string.Empty;

        public List<int>? EpisodeIndices { get; set; }
    }
}
