using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graphiti.Core.Drivers.Ladybug;

internal static class LadybugRecordMapper
{
    internal static EpisodicNode MapEpisodicNode(IReadOnlyDictionary<string, object?> record) =>
        new()
        {
            Uuid = Get<string>(record, "uuid") ?? string.Empty,
            Name = Get<string>(record, "name") ?? string.Empty,
            GroupId = Get<string>(record, "group_id") ?? string.Empty,
            CreatedAt = GetDate(record, "created_at") ?? GraphitiHelpers.DefaultTimestamp,
            Source = EpisodeTypeExtensions.FromWireValue(Get<string>(record, "source") ?? "message"),
            SourceDescription = Get<string>(record, "source_description") ?? string.Empty,
            Content = Get<string>(record, "content") ?? string.Empty,
            ValidAt = GetDate(record, "valid_at") ?? GraphitiHelpers.DefaultTimestamp,
            EntityEdges = GetList<string>(record, "entity_edges") ?? new List<string>()
        };

    internal static EntityNode MapEntityNode(IReadOnlyDictionary<string, object?> record) =>
        new()
        {
            Uuid = Get<string>(record, "uuid") ?? string.Empty,
            Name = Get<string>(record, "name") ?? string.Empty,
            GroupId = Get<string>(record, "group_id") ?? string.Empty,
            Labels = GetList<string>(record, "labels") ?? new List<string> { "Entity" },
            CreatedAt = GetDate(record, "created_at") ?? GraphitiHelpers.DefaultTimestamp,
            Summary = Get<string>(record, "summary") ?? string.Empty,
            NameEmbedding = GetList<float>(record, "name_embedding"),
            Attributes = ParseAttributes(GetValueOrNull(record, "attributes"))
        };

    internal static CommunityNode MapCommunityNode(IReadOnlyDictionary<string, object?> record) =>
        new()
        {
            Uuid = Get<string>(record, "uuid") ?? string.Empty,
            Name = Get<string>(record, "name") ?? string.Empty,
            GroupId = Get<string>(record, "group_id") ?? string.Empty,
            CreatedAt = GetDate(record, "created_at") ?? GraphitiHelpers.DefaultTimestamp,
            Summary = Get<string>(record, "summary") ?? string.Empty,
            NameEmbedding = GetList<float>(record, "name_embedding")
        };

    internal static SagaNode MapSagaNode(IReadOnlyDictionary<string, object?> record) =>
        new()
        {
            Uuid = Get<string>(record, "uuid") ?? string.Empty,
            Name = Get<string>(record, "name") ?? string.Empty,
            GroupId = Get<string>(record, "group_id") ?? string.Empty,
            CreatedAt = GetDate(record, "created_at") ?? GraphitiHelpers.DefaultTimestamp,
            Summary = Get<string>(record, "summary") ?? string.Empty,
            FirstEpisodeUuid = Get<string>(record, "first_episode_uuid"),
            LastEpisodeUuid = Get<string>(record, "last_episode_uuid"),
            LastSummarizedAt = GetDate(record, "last_summarized_at"),
            LastSummarizedEpisodeValidAt = GetDate(record, "last_summarized_episode_valid_at")
        };

    internal static EntityEdge MapEntityEdge(IReadOnlyDictionary<string, object?> record) =>
        new()
        {
            Uuid = Get<string>(record, "uuid") ?? string.Empty,
            SourceNodeUuid = Get<string>(record, "source_node_uuid") ?? string.Empty,
            TargetNodeUuid = Get<string>(record, "target_node_uuid") ?? string.Empty,
            GroupId = Get<string>(record, "group_id") ?? string.Empty,
            CreatedAt = GetDate(record, "created_at") ?? GraphitiHelpers.DefaultTimestamp,
            Name = Get<string>(record, "name") ?? string.Empty,
            Fact = Get<string>(record, "fact") ?? string.Empty,
            FactEmbedding = GetList<float>(record, "fact_embedding"),
            Episodes = GetList<string>(record, "episodes") ?? new List<string>(),
            ExpiredAt = GetDate(record, "expired_at"),
            ValidAt = GetDate(record, "valid_at"),
            InvalidAt = GetDate(record, "invalid_at"),
            ReferenceTime = GetDate(record, "reference_time"),
            Attributes = ParseAttributes(GetValueOrNull(record, "attributes"))
        };

    internal static EpisodicEdge MapEpisodicEdge(IReadOnlyDictionary<string, object?> record) =>
        MapSimpleEdge<EpisodicEdge>(record);

    internal static CommunityEdge MapCommunityEdge(IReadOnlyDictionary<string, object?> record) =>
        MapSimpleEdge<CommunityEdge>(record);

    internal static HasEpisodeEdge MapHasEpisodeEdge(IReadOnlyDictionary<string, object?> record) =>
        MapSimpleEdge<HasEpisodeEdge>(record);

    internal static NextEpisodeEdge MapNextEpisodeEdge(IReadOnlyDictionary<string, object?> record) =>
        MapSimpleEdge<NextEpisodeEdge>(record);

    internal static Dictionary<string, object?> ParseAttributes(object? value)
    {
        if (value is null)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        if (value is string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new Dictionary<string, object?>(StringComparer.Ordinal);
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    text,
                    GraphitiJsonSerializer.Options);
                return parsed?.ToDictionary(
                        pair => pair.Key,
                        pair => (object?)pair.Value.Clone(),
                        StringComparer.Ordinal)
                    ?? new Dictionary<string, object?>(StringComparer.Ordinal);
            }
            catch (JsonException)
            {
                return new Dictionary<string, object?>(StringComparer.Ordinal);
            }
        }

        if (value is JsonObject jsonObject)
        {
            return jsonObject.ToDictionary(
                pair => pair.Key,
                pair => (object?)pair.Value?.DeepClone(),
                StringComparer.Ordinal);
        }

        if (value is IReadOnlyDictionary<string, object?> nullableDictionary)
        {
            return new Dictionary<string, object?>(nullableDictionary, StringComparer.Ordinal);
        }

        if (value is IReadOnlyDictionary<string, object> dictionary)
        {
            return dictionary.ToDictionary(
                pair => pair.Key,
                pair => (object?)pair.Value,
                StringComparer.Ordinal);
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    private static T? Get<T>(IReadOnlyDictionary<string, object?> record, string key)
    {
        if (!record.TryGetValue(key, out var value) || value is null)
        {
            return default;
        }

        if (value is T typed)
        {
            return typed;
        }

        if (value is JsonElement element)
        {
            value = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when typeof(T) == typeof(int) => element.GetInt32(),
                JsonValueKind.Number when typeof(T) == typeof(long) => element.GetInt64(),
                JsonValueKind.Number when typeof(T) == typeof(float) => element.GetSingle(),
                JsonValueKind.Number when typeof(T) == typeof(double) => element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => value
            };
        }

        return (T?)Convert.ChangeType(
            value,
            Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T),
            CultureInfo.InvariantCulture);
    }

    private static DateTime? GetDate(IReadOnlyDictionary<string, object?> record, string key) =>
        record.TryGetValue(key, out var value) ? GraphitiHelpers.ParseDbDate(value) : null;

    private static List<T>? GetList<T>(IReadOnlyDictionary<string, object?> record, string key)
    {
        if (!record.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is IEnumerable<T> typed)
        {
            return typed.ToList();
        }

        if (value is JsonArray jsonArray)
        {
            return jsonArray
                .Select(item => item is null ? default : item.Deserialize<T>(GraphitiJsonSerializer.Options))
                .OfType<T>()
                .ToList();
        }

        if (value is JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            return element.Deserialize<List<T>>(GraphitiJsonSerializer.Options);
        }

        if (value is not string && value is IEnumerable<object> objects)
        {
            return objects
                .Select(item => (T)Convert.ChangeType(item, typeof(T), CultureInfo.InvariantCulture))
                .ToList();
        }

        return null;
    }

    private static object? GetValueOrNull(IReadOnlyDictionary<string, object?> record, string key) =>
        record.TryGetValue(key, out var value) ? value : null;

    private static TEdge MapSimpleEdge<TEdge>(IReadOnlyDictionary<string, object?> record)
        where TEdge : Edge, new() =>
        new()
        {
            Uuid = Get<string>(record, "uuid") ?? string.Empty,
            GroupId = Get<string>(record, "group_id") ?? string.Empty,
            SourceNodeUuid = Get<string>(record, "source_node_uuid") ?? string.Empty,
            TargetNodeUuid = Get<string>(record, "target_node_uuid") ?? string.Empty,
            CreatedAt = GetDate(record, "created_at") ?? GraphitiHelpers.DefaultTimestamp
        };
}
