using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graphiti.Core.Drivers;

/// <summary>
/// Deep-clone helpers that produce snapshot-isolated copies of in-memory nodes, edges, and their
/// metadata so that values handed out by <see cref="InMemoryGraphDriver"/> cannot be mutated through
/// the caller's reference. Pure, stateless, and free of driver instance state.
/// </summary>
internal static class InMemorySnapshotCloner
{
    public static Node CloneNode(Node node) =>
        node switch
        {
            EntityNode entity => new EntityNode
            {
                Uuid = entity.Uuid,
                Name = entity.Name,
                GroupId = entity.GroupId,
                Labels = CopyList(entity.Labels),
                CreatedAt = entity.CreatedAt,
                NameEmbedding = CopyNullableList(entity.NameEmbedding),
                Summary = entity.Summary,
                Attributes = CloneDictionary(entity.Attributes)
            },
            EpisodicNode episode => new EpisodicNode
            {
                Uuid = episode.Uuid,
                Name = episode.Name,
                GroupId = episode.GroupId,
                Labels = CopyList(episode.Labels),
                CreatedAt = episode.CreatedAt,
                Source = episode.Source,
                SourceDescription = episode.SourceDescription,
                Content = episode.Content,
                ValidAt = episode.ValidAt,
                EntityEdges = CopyList(episode.EntityEdges)
            },
            CommunityNode community => new CommunityNode
            {
                Uuid = community.Uuid,
                Name = community.Name,
                GroupId = community.GroupId,
                Labels = CopyList(community.Labels),
                CreatedAt = community.CreatedAt,
                NameEmbedding = CopyNullableList(community.NameEmbedding),
                Summary = community.Summary
            },
            SagaNode saga => new SagaNode
            {
                Uuid = saga.Uuid,
                Name = saga.Name,
                GroupId = saga.GroupId,
                Labels = CopyList(saga.Labels),
                CreatedAt = saga.CreatedAt,
                Summary = saga.Summary,
                FirstEpisodeUuid = saga.FirstEpisodeUuid,
                LastEpisodeUuid = saga.LastEpisodeUuid,
                LastSummarizedAt = saga.LastSummarizedAt,
                LastSummarizedEpisodeValidAt = saga.LastSummarizedEpisodeValidAt
            },
            _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType().Name)
        };

    public static Edge CloneEdge(Edge edge) =>
        edge switch
        {
            EntityEdge entity => new EntityEdge
            {
                Uuid = entity.Uuid,
                GroupId = entity.GroupId,
                SourceNodeUuid = entity.SourceNodeUuid,
                TargetNodeUuid = entity.TargetNodeUuid,
                CreatedAt = entity.CreatedAt,
                Name = entity.Name,
                Fact = entity.Fact,
                FactEmbedding = CopyNullableList(entity.FactEmbedding),
                Episodes = CopyList(entity.Episodes),
                ExpiredAt = entity.ExpiredAt,
                ValidAt = entity.ValidAt,
                InvalidAt = entity.InvalidAt,
                ReferenceTime = entity.ReferenceTime,
                Attributes = CloneDictionary(entity.Attributes)
            },
            EpisodicEdge episodic => CopyBase(new EpisodicEdge(), episodic),
            CommunityEdge community => CopyBase(new CommunityEdge(), community),
            HasEpisodeEdge hasEpisode => CopyBase(new HasEpisodeEdge(), hasEpisode),
            NextEpisodeEdge nextEpisode => CopyBase(new NextEpisodeEdge(), nextEpisode),
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge.GetType().Name)
        };

    private static T CopyBase<T>(T target, Edge source) where T : Edge
    {
        target.Uuid = source.Uuid;
        target.GroupId = source.GroupId;
        target.SourceNodeUuid = source.SourceNodeUuid;
        target.TargetNodeUuid = source.TargetNodeUuid;
        target.CreatedAt = source.CreatedAt;
        return target;
    }

    private static List<T> CopyList<T>(IReadOnlyList<T> source)
    {
        var copy = new List<T>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            copy.Add(source[i]);
        }

        return copy;
    }

    public static List<T>? CopyNullableList<T>(IReadOnlyList<T>? source) =>
        source is null ? null : CopyList(source);

    private static Dictionary<string, object?> CloneDictionary(IDictionary<string, object?> source)
    {
        var clone = new Dictionary<string, object?>(source.Count, StringComparer.Ordinal);
        foreach (var pair in source)
        {
            clone[pair.Key] = CloneMetadataValue(pair.Value);
        }

        return clone;
    }

    private static object? CloneMetadataValue(object? value)
    {
        if (value is null || IsImmutableScalar(value))
        {
            return value;
        }

        return value switch
        {
            JsonNode node => node.DeepClone(),
            JsonElement element => element.Clone(),
            IDictionary<string, object?> dictionary => CloneDictionary(dictionary),
            IEnumerable<object?> values => CloneMetadataValues(values),
            _ => CloneJsonCompatibleValue(value)
        };
    }

    private static List<object?> CloneMetadataValues(IEnumerable<object?> values)
    {
        var clone = values is ICollection<object?> collection
            ? new List<object?>(collection.Count)
            : [];

        foreach (var value in values)
        {
            clone.Add(CloneMetadataValue(value));
        }

        return clone;
    }

    private static object? CloneJsonCompatibleValue(object value)
    {
        var node = JsonSerializer.SerializeToNode(value, GraphitiJsonSerializer.Options);
        return ConvertJsonNode(node);
    }

    private static object? ConvertJsonNode(JsonNode? node) =>
        node switch
        {
            null => null,
            JsonObject jsonObject => ConvertJsonObject(jsonObject),
            JsonArray jsonArray => ConvertJsonArray(jsonArray),
            JsonValue jsonValue => ConvertJsonValue(jsonValue),
            _ => node.ToJsonString(GraphitiJsonSerializer.Options)
        };

    private static Dictionary<string, object?> ConvertJsonObject(JsonObject jsonObject)
    {
        var dictionary = new Dictionary<string, object?>(jsonObject.Count, StringComparer.Ordinal);
        foreach (var pair in jsonObject)
        {
            dictionary[pair.Key] = ConvertJsonNode(pair.Value);
        }

        return dictionary;
    }

    private static List<object?> ConvertJsonArray(JsonArray jsonArray)
    {
        var values = new List<object?>(jsonArray.Count);
        foreach (var item in jsonArray)
        {
            values.Add(ConvertJsonNode(item));
        }

        return values;
    }

    private static object? ConvertJsonValue(JsonValue value)
    {
        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }

        if (value.TryGetValue<long>(out var integer))
        {
            return integer;
        }

        if (value.TryGetValue<decimal>(out var decimalValue))
        {
            return decimalValue;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        return value.DeepClone();
    }

    private static bool IsImmutableScalar(object value)
    {
        var type = value.GetType();
        return type.IsEnum
            || value is string
                or bool
                or char
                or byte
                or sbyte
                or short
                or ushort
                or int
                or uint
                or long
                or ulong
                or float
                or double
                or decimal
                or DateTime
                or DateTimeOffset
                or Guid
                or TimeSpan;
    }
}
