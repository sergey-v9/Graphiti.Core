using System.Collections.Frozen;
using System.Globalization;
using Neo4j.Driver;

namespace Graphiti.Core.Drivers;

internal static class Neo4jRecordMapper
{
    internal static Node MapNode(IRecord record, string key)
    {
        var value = record[key];
        return value is INode node
            ? MapNode(node)
            : MapProjectedNode(GetRecordMap(value));
    }

    internal static Node MapNode(INode node) =>
        MapNode(node.Properties, node.Labels.ToList());

    internal static Node MapNode(IReadOnlyDictionary<string, object> props, IReadOnlyList<string> labels)
    {
        var labelList = labels.ToList();
        if (labelList.Contains("Entity"))
        {
            return new EntityNode
            {
                Uuid = Get<string>(props, "uuid"),
                Name = Get<string>(props, "name"),
                GroupId = Get<string>(props, "group_id"),
                CreatedAt = GetDate(props, "created_at") ?? GraphitiHelpers.DefaultTimestamp,
                Labels = labelList,
                Summary = Get<string?>(props, "summary") ?? string.Empty,
                NameEmbedding = GetList<float>(props, "name_embedding"),
                Attributes = props
                    .Where(pair => !EntityNodeReserved.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal)
            };
        }

        if (labelList.Contains("Episodic"))
        {
            return new EpisodicNode
            {
                Uuid = Get<string>(props, "uuid"),
                Name = Get<string>(props, "name"),
                GroupId = Get<string>(props, "group_id"),
                CreatedAt = GetDate(props, "created_at") ?? GraphitiHelpers.DefaultTimestamp,
                Source = EpisodeTypeExtensions.FromWireValue(Get<string>(props, "source")),
                SourceDescription = Get<string>(props, "source_description"),
                Content = Get<string?>(props, "content") ?? string.Empty,
                ValidAt = GetDate(props, "valid_at") ?? GraphitiHelpers.DefaultTimestamp,
                EntityEdges = GetList<string>(props, "entity_edges") ?? new List<string>()
            };
        }

        if (labelList.Contains("Community"))
        {
            return new CommunityNode
            {
                Uuid = Get<string>(props, "uuid"),
                Name = Get<string>(props, "name"),
                GroupId = Get<string>(props, "group_id"),
                CreatedAt = GetDate(props, "created_at") ?? GraphitiHelpers.DefaultTimestamp,
                Summary = Get<string?>(props, "summary") ?? string.Empty,
                NameEmbedding = GetList<float>(props, "name_embedding")
            };
        }

        return new SagaNode
        {
            Uuid = Get<string>(props, "uuid"),
            Name = Get<string>(props, "name"),
            GroupId = Get<string>(props, "group_id"),
            CreatedAt = GetDate(props, "created_at") ?? GraphitiHelpers.DefaultTimestamp,
            Summary = Get<string?>(props, "summary") ?? string.Empty,
            FirstEpisodeUuid = Get<string?>(props, "first_episode_uuid"),
            LastEpisodeUuid = Get<string?>(props, "last_episode_uuid"),
            LastSummarizedAt = GetDate(props, "last_summarized_at"),
            LastSummarizedEpisodeValidAt = GetDate(props, "last_summarized_episode_valid_at")
        };
    }

    internal static Edge MapEdge(
        IRecord record,
        string key,
        string sourceUuid,
        string targetUuid,
        Type targetType)
    {
        var value = record[key];
        return value is IRelationship relationship
            ? MapEdge(relationship, sourceUuid, targetUuid, targetType)
            : MapEdge(GetRecordMap(value), sourceUuid, targetUuid, targetType);
    }

    internal static Edge MapEdge(
        IRelationship relationship,
        string sourceUuid,
        string targetUuid,
        Type targetType) =>
        MapEdge(relationship.Properties, sourceUuid, targetUuid, targetType);

    internal static Edge MapEdge(
        IReadOnlyDictionary<string, object> props,
        string sourceUuid,
        string targetUuid,
        Type targetType)
    {
        if (targetType == typeof(EntityEdge))
        {
            return new EntityEdge
            {
                Uuid = Get<string>(props, "uuid"),
                GroupId = Get<string>(props, "group_id"),
                SourceNodeUuid = sourceUuid,
                TargetNodeUuid = targetUuid,
                CreatedAt = GetDate(props, "created_at") ?? GraphitiHelpers.DefaultTimestamp,
                Name = Get<string?>(props, "name") ?? string.Empty,
                Fact = Get<string?>(props, "fact") ?? string.Empty,
                FactEmbedding = GetList<float>(props, "fact_embedding"),
                Episodes = GetList<string>(props, "episodes") ?? new List<string>(),
                ExpiredAt = GetDate(props, "expired_at"),
                ValidAt = GetDate(props, "valid_at"),
                InvalidAt = GetDate(props, "invalid_at"),
                ReferenceTime = GetDate(props, "reference_time"),
                Attributes = props
                    .Where(pair => !EntityEdgeReserved.Contains(pair.Key))
                    .ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal)
            };
        }

        Edge edge = targetType == typeof(EpisodicEdge) ? new EpisodicEdge() :
            targetType == typeof(CommunityEdge) ? new CommunityEdge() :
            targetType == typeof(HasEpisodeEdge) ? new HasEpisodeEdge() :
            new NextEpisodeEdge();
        edge.Uuid = Get<string>(props, "uuid");
        edge.GroupId = Get<string>(props, "group_id");
        edge.SourceNodeUuid = sourceUuid;
        edge.TargetNodeUuid = targetUuid;
        edge.CreatedAt = GetDate(props, "created_at") ?? GraphitiHelpers.DefaultTimestamp;
        return edge;
    }

    internal static TNode ProjectNodeEmbedding<TNode>(TNode node, bool withEmbeddings)
        where TNode : Node
    {
        if (withEmbeddings)
        {
            return node;
        }

        if (node is EntityNode entity)
        {
            entity.NameEmbedding = null;
        }

        return node;
    }

    internal static TEdge ProjectEdgeEmbedding<TEdge>(TEdge edge, bool withEmbeddings)
        where TEdge : Edge
    {
        if (withEmbeddings)
        {
            return edge;
        }

        if (edge is EntityEdge entity)
        {
            entity.FactEmbedding = null;
        }

        return edge;
    }

    private static Node MapProjectedNode(IReadOnlyDictionary<string, object> props) =>
        MapNode(props, GetList<string>(props, "labels") ?? new List<string>());

    private static IReadOnlyDictionary<string, object> GetRecordMap(object value)
    {
        if (value is IReadOnlyDictionary<string, object> readOnly)
        {
            return readOnly;
        }

        if (value is IDictionary<string, object> dictionary)
        {
            return new Dictionary<string, object>(dictionary, StringComparer.Ordinal);
        }

        return value.As<IReadOnlyDictionary<string, object>>();
    }

    private static readonly FrozenSet<string> EntityNodeReserved = new[]
    {
        "uuid", "name", "group_id", "name_embedding", "summary", "created_at", "labels"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> EntityEdgeReserved = new[]
    {
        "uuid", "source_node_uuid", "target_node_uuid", "fact", "fact_embedding", "name",
        "group_id", "episodes", "created_at", "expired_at", "valid_at", "invalid_at", "reference_time"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static T Get<T>(IReadOnlyDictionary<string, object> props, string key)
    {
        if (!props.TryGetValue(key, out var value) || value is null)
        {
            return default!;
        }

        if (value is T typed)
        {
            return typed;
        }

        return (T)Convert.ChangeType(
            value,
            Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T),
            CultureInfo.InvariantCulture);
    }

    private static DateTime? GetDate(IReadOnlyDictionary<string, object> props, string key) =>
        props.TryGetValue(key, out var value) ? GraphitiHelpers.ParseDbDate(value) : null;

    private static List<T>? GetList<T>(IReadOnlyDictionary<string, object> props, string key)
    {
        if (!props.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is IEnumerable<T> typed)
        {
            return typed.ToList();
        }

        if (value is IEnumerable<object> objects)
        {
            return objects
                .Select(item => (T)Convert.ChangeType(item, typeof(T), CultureInfo.InvariantCulture))
                .ToList();
        }

        return null;
    }
}
