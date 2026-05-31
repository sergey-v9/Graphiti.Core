namespace Graphiti.Core.Drivers;

internal static partial class Neo4jStatementBuilder
{
    internal static (string Query, Dictionary<string, object?> Parameters) BuildNodeSave(Node node) =>
        node switch
        {
            EntityNode entity => BuildEntityNodeSave(entity),
            EpisodicNode episode => BuildEpisodeNodeSave(episode),
            CommunityNode community => BuildCommunityNodeSave(community),
            SagaNode saga => BuildSagaNodeSave(saga),
            _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType().Name)
        };

    internal static (string Query, Dictionary<string, object?> Parameters) BuildEdgeSave(Edge edge) =>
        edge switch
        {
            EntityEdge entity => BuildEntityEdgeSave(entity),
            EpisodicEdge episodic => BuildSimpleEdgeSave(episodic, "Episodic", "Entity", "MENTIONS"),
            CommunityEdge community => BuildCommunityEdgeSave(community),
            HasEpisodeEdge hasEpisode => BuildSimpleEdgeSave(hasEpisode, "Saga", "Episodic", "HAS_EPISODE"),
            NextEpisodeEdge nextEpisode => BuildSimpleEdgeSave(nextEpisode, "Episodic", "Episodic", "NEXT_EPISODE"),
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge.GetType().Name)
        };

    internal static IReadOnlyList<Neo4jStatement> BuildBulkSaveStatements(
        IReadOnlyList<EpisodicNode> episodicNodes,
        IReadOnlyList<EpisodicEdge> episodicEdges,
        IReadOnlyList<EntityNode> entityNodes,
        IReadOnlyList<EntityEdge> entityEdges)
    {
        var statements = new List<Neo4jStatement>(4);
        if (episodicNodes.Count > 0)
        {
            statements.Add(BuildEpisodeNodeBulkSave(episodicNodes));
        }

        if (entityNodes.Count > 0)
        {
            statements.Add(BuildEntityNodeBulkSave(entityNodes));
        }

        if (episodicEdges.Count > 0)
        {
            statements.Add(BuildEpisodicEdgeBulkSave(episodicEdges));
        }

        if (entityEdges.Count > 0)
        {
            statements.Add(BuildEntityEdgeBulkSave(entityEdges));
        }

        return statements;
    }

    internal static Neo4jStatement BuildDeleteNodesByGroupIdStatement(
        string groupId,
        int batchSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        return new Neo4jStatement(
            """
            MATCH (n)
            WHERE n.group_id = $group_id
            WITH n LIMIT $batch_size
            DETACH DELETE n
            RETURN count(*) AS deleted
            """,
            new Dictionary<string, object?>
            {
                ["group_id"] = groupId,
                ["batch_size"] = batchSize
            });
    }

    internal static IReadOnlyList<Neo4jStatement> BuildDeleteNodesByUuidsStatements(
        IEnumerable<string> uuids,
        int batchSize)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var statements = new List<Neo4jStatement>();
        foreach (var batch in uuids.ToList().Chunk(batchSize))
        {
            statements.Add(new Neo4jStatement(
                """
                UNWIND $uuids AS uuid
                MATCH (n {uuid: uuid})
                DETACH DELETE n
                """,
                new Dictionary<string, object?>
                {
                    ["uuids"] = batch.ToList()
                }));
        }

        return statements;
    }

    internal static Neo4jStatement BuildRetrieveEpisodesStatement(
        DateTime referenceTime,
        int lastN,
        IReadOnlyList<string>? groupIds = null,
        EpisodeType? source = null,
        string? saga = null)
    {
        var groupIdList = groupIds?.ToList();
        var hasGroupIds = groupIdList is { Count: > 0 };
        var parameters = new Dictionary<string, object?>
        {
            ["reference_time"] = referenceTime,
            ["group_ids"] = groupIdList,
            ["source"] = source?.ToWireValue(),
            ["saga"] = saga,
            ["saga_group_id"] = hasGroupIds ? groupIdList![0] : null,
            ["limit"] = lastN
        };

        var sagaMatch = saga is null
            ? "MATCH (e:Episodic)"
            : hasGroupIds
                ? "MATCH (s:Saga {name: $saga, group_id: $saga_group_id})-[:HAS_EPISODE]->(e:Episodic)"
                : "MATCH (s:Saga {name: $saga})-[:HAS_EPISODE]->(e:Episodic)";
        var groupFilter = hasGroupIds ? "AND e.group_id IN $group_ids" : "";
        var sourceFilter = source is null ? "" : "AND e.source = $source";

        return new Neo4jStatement(
            $"""
            {sagaMatch}
            WHERE e.valid_at <= $reference_time {groupFilter} {sourceFilter}
            RETURN e AS n
            ORDER BY e.valid_at DESC
            LIMIT $limit
            """,
            parameters);
    }

    internal static string BuildNodeBfsSearchQuery(int maxDepth, string filterQuery) =>
        $$"""
        UNWIND $bfs_origin_node_uuids AS origin_uuid
        MATCH path = (origin {uuid: origin_uuid})-[:RELATES_TO|MENTIONS*1..{{maxDepth}}]->(n:Entity)
        WHERE n.group_id = origin.group_id
        {{filterQuery}}
        WITH n, min(length(path)) AS depth
        RETURN n, 1.0 / depth AS score
        ORDER BY depth ASC, n.uuid ASC
        LIMIT $limit
        """;

    internal static string BuildEdgeBfsSearchQuery(int maxDepth, string pathFilter, string filterQuery) =>
        $$"""
        UNWIND $bfs_origin_node_uuids AS origin_uuid
        MATCH path = (origin {uuid: origin_uuid})-[:RELATES_TO|MENTIONS*1..{{maxDepth}}]->(:Entity)
        {{pathFilter}}
        UNWIND relationships(path) AS rel
        WITH rel, length(path) AS depth
        MATCH (n:Entity)-[e:RELATES_TO {uuid: rel.uuid}]->(m:Entity)
        {{filterQuery}}
        WITH e, n, m, min(depth) AS depth
        RETURN e, n.uuid AS source_uuid, m.uuid AS target_uuid, 1.0 / depth AS score
        ORDER BY depth ASC, e.uuid ASC
        LIMIT $limit
        """;

    internal static (string Query, Dictionary<string, object?> Parameters) BuildEntityNodeSave(EntityNode node)
    {
        var labels = string.Join("", EntityLabels(node).Select(label => $":{label}"));
        var props = EntityNodeProperties(node);

        return ($"MERGE (n:Entity {{uuid: $props.uuid}}) SET n{labels} SET n = $props RETURN n", new Dictionary<string, object?> { ["props"] = props });
    }

    internal static (string Query, Dictionary<string, object?> Parameters) BuildEntityEdgeSave(EntityEdge edge)
    {
        var props = EntityEdgeProperties(edge);

        return (
            """
            MATCH (source:Entity {uuid: $source_uuid})
            MATCH (target:Entity {uuid: $target_uuid})
            MERGE (source)-[e:RELATES_TO {uuid: $props.uuid}]->(target)
            SET e = $props
            RETURN e
            """,
            new Dictionary<string, object?>
            {
                ["source_uuid"] = edge.SourceNodeUuid,
                ["target_uuid"] = edge.TargetNodeUuid,
                ["props"] = props
            });
    }

    internal static (string Query, Dictionary<string, object?> Parameters) BuildCommunityEdgeSave(CommunityEdge edge)
    {
        var props = BaseEdgeProperties(edge);

        return (
            """
            MATCH (source:Community {uuid: $source_uuid})
            MATCH (target:Entity|Community {uuid: $target_uuid})
            MERGE (source)-[e:HAS_MEMBER {uuid: $props.uuid}]->(target)
            SET e = $props
            RETURN e
            """,
            new Dictionary<string, object?>
            {
                ["source_uuid"] = edge.SourceNodeUuid,
                ["target_uuid"] = edge.TargetNodeUuid,
                ["props"] = props
            });
    }

    internal static string LabelFor<TNode>() where TNode : Node =>
        typeof(TNode) == typeof(EntityNode) ? "Entity" :
        typeof(TNode) == typeof(EpisodicNode) ? "Episodic" :
        typeof(TNode) == typeof(CommunityNode) ? "Community" :
        typeof(TNode) == typeof(SagaNode) ? "Saga" :
        throw new ArgumentOutOfRangeException(typeof(TNode).Name);

    internal static (string RelationshipType, string SourceLabel, string TargetLabel) EdgeShapeFor<T>() where T : Edge =>
        typeof(T) == typeof(EntityEdge) ? ("RELATES_TO", "Entity", "Entity") :
        typeof(T) == typeof(EpisodicEdge) ? ("MENTIONS", "Episodic", "Entity") :
        typeof(T) == typeof(CommunityEdge) ? ("HAS_MEMBER", "Community", "Entity|Community") :
        typeof(T) == typeof(HasEpisodeEdge) ? ("HAS_EPISODE", "Saga", "Episodic") :
        typeof(T) == typeof(NextEpisodeEdge) ? ("NEXT_EPISODE", "Episodic", "Episodic") :
        throw new ArgumentOutOfRangeException(typeof(T).Name);

    internal static string BuildEdgeMatchPattern<T>() where T : Edge
    {
        var (relationshipType, sourceLabel, targetLabel) = EdgeShapeFor<T>();
        return BuildEdgeMatchPattern(relationshipType, sourceLabel, targetLabel);
    }

    internal static string BuildEdgeMatchPattern(
        string relationshipType,
        string sourceLabel,
        string targetLabel) =>
        $"MATCH (source:{sourceLabel})-[e:{relationshipType}]->(target:{targetLabel})";

    internal static string BuildNodeGroupReturnClause<TNode>(bool withEmbeddings)
        where TNode : Node =>
        !withEmbeddings && typeof(TNode) == typeof(EntityNode)
            ? "RETURN n {.*, name_embedding: null, labels: labels(n)} AS n"
            : "RETURN n";

    internal static string BuildEdgeReturnClause<T>(bool withEmbeddings)
        where T : Edge
    {
        var relationshipProjection = !withEmbeddings && typeof(T) == typeof(EntityEdge)
            ? "e {.*, fact_embedding: null} AS e"
            : "e";

        return $"RETURN {relationshipProjection}, source.uuid AS source_uuid, target.uuid AS target_uuid";
    }

    private static Neo4jStatement BuildEpisodeNodeBulkSave(IReadOnlyList<EpisodicNode> nodes) =>
        new(
            """
            UNWIND $episodes AS episode
            MERGE (n:Episodic {uuid: episode.uuid})
            SET n = {
                uuid: episode.uuid,
                name: episode.name,
                group_id: episode.group_id,
                source_description: episode.source_description,
                source: episode.source,
                content: episode.content,
                entity_edges: episode.entity_edges,
                created_at: episode.created_at,
                valid_at: episode.valid_at
            }
            RETURN n.uuid AS uuid
            """,
            new Dictionary<string, object?>
            {
                ["episodes"] = nodes.Select(EpisodeNodeProperties).ToList()
            });

    private static Neo4jStatement BuildEntityNodeBulkSave(IReadOnlyList<EntityNode> nodes) =>
        new(
            """
            UNWIND $nodes AS node
            MERGE (n:Entity {uuid: node.uuid})
            SET n:$(node.labels)
            SET n = node
            RETURN n.uuid AS uuid
            """,
            new Dictionary<string, object?>
            {
                ["nodes"] = nodes.Select(EntityNodeBulkProperties).ToList()
            });

    private static Neo4jStatement BuildEpisodicEdgeBulkSave(IReadOnlyList<EpisodicEdge> edges) =>
        new(
            """
            UNWIND $episodic_edges AS edge
            MATCH (episode:Episodic {uuid: edge.source_node_uuid})
            MATCH (node:Entity {uuid: edge.target_node_uuid})
            MERGE (episode)-[e:MENTIONS {uuid: edge.uuid}]->(node)
            SET
                e.group_id = edge.group_id,
                e.created_at = edge.created_at
            RETURN e.uuid AS uuid
            """,
            new Dictionary<string, object?>
            {
                ["episodic_edges"] = edges.Select(EdgeBulkProperties).ToList()
            });

    private static Neo4jStatement BuildEntityEdgeBulkSave(IReadOnlyList<EntityEdge> edges) =>
        new(
            """
            UNWIND $entity_edges AS edge
            MATCH (source:Entity {uuid: edge.source_node_uuid})
            MATCH (target:Entity {uuid: edge.target_node_uuid})
            MERGE (source)-[e:RELATES_TO {uuid: edge.uuid}]->(target)
            SET e = edge
            RETURN edge.uuid AS uuid
            """,
            new Dictionary<string, object?>
            {
                ["entity_edges"] = edges.Select(EntityEdgeBulkProperties).ToList()
            });

    private static (string Query, Dictionary<string, object?> Parameters) BuildEpisodeNodeSave(EpisodicNode node)
    {
        var props = EpisodeNodeProperties(node);
        return ("MERGE (n:Episodic {uuid: $props.uuid}) SET n = $props RETURN n", new Dictionary<string, object?> { ["props"] = props });
    }

    private static (string Query, Dictionary<string, object?> Parameters) BuildCommunityNodeSave(CommunityNode node)
    {
        var props = BaseNodeProperties(node);
        props["summary"] = node.Summary;
        props["name_embedding"] = node.NameEmbedding;
        return ("MERGE (n:Community {uuid: $props.uuid}) SET n = $props RETURN n", new Dictionary<string, object?> { ["props"] = props });
    }

    private static (string Query, Dictionary<string, object?> Parameters) BuildSagaNodeSave(SagaNode node)
    {
        var props = BaseNodeProperties(node);
        props["summary"] = node.Summary;
        props["first_episode_uuid"] = node.FirstEpisodeUuid;
        props["last_episode_uuid"] = node.LastEpisodeUuid;
        props["last_summarized_at"] = node.LastSummarizedAt;
        props["last_summarized_episode_valid_at"] = node.LastSummarizedEpisodeValidAt;
        return ("MERGE (n:Saga {uuid: $props.uuid}) SET n = $props RETURN n", new Dictionary<string, object?> { ["props"] = props });
    }

    private static (string Query, Dictionary<string, object?> Parameters) BuildSimpleEdgeSave(
        Edge edge,
        string sourceLabel,
        string targetLabel,
        string relationshipType)
    {
        return (
            $$"""
            MATCH (source:{{sourceLabel}} {uuid: $source_uuid})
            MATCH (target:{{targetLabel}} {uuid: $target_uuid})
            MERGE (source)-[e:{{relationshipType}} {uuid: $props.uuid}]->(target)
            SET e += $props
            RETURN e
            """,
            new Dictionary<string, object?>
            {
                ["source_uuid"] = edge.SourceNodeUuid,
                ["target_uuid"] = edge.TargetNodeUuid,
                ["props"] = BaseEdgeProperties(edge)
            });
    }

    private static Dictionary<string, object?> EpisodeNodeProperties(EpisodicNode node)
    {
        var props = BaseNodeProperties(node);
        props["source"] = node.Source.ToWireValue();
        props["source_description"] = node.SourceDescription;
        props["content"] = node.Content;
        props["valid_at"] = node.ValidAt;
        props["entity_edges"] = node.EntityEdges;
        return props;
    }

    private static List<string> EntityLabels(EntityNode node)
    {
        GraphitiHelpers.ValidateNodeLabels(node.Labels);
        return node.Labels.Append("Entity").Distinct(StringComparer.Ordinal).ToList();
    }

    private static Dictionary<string, object?> EntityNodeProperties(EntityNode node)
    {
        var props = BaseNodeProperties(node);
        props["summary"] = node.Summary;
        props["name_embedding"] = node.NameEmbedding;
        foreach (var pair in node.Attributes)
        {
            props.TryAdd(pair.Key, pair.Value);
        }

        return props;
    }

    private static Dictionary<string, object?> EntityNodeBulkProperties(EntityNode node)
    {
        var props = EntityNodeProperties(node);
        props["labels"] = EntityLabels(node);
        return props;
    }

    private static Dictionary<string, object?> EdgeBulkProperties(Edge edge)
    {
        var props = BaseEdgeProperties(edge);
        props["source_node_uuid"] = edge.SourceNodeUuid;
        props["target_node_uuid"] = edge.TargetNodeUuid;
        return props;
    }

    private static Dictionary<string, object?> EntityEdgeProperties(EntityEdge edge)
    {
        var props = BaseEdgeProperties(edge);
        props["name"] = edge.Name;
        props["fact"] = edge.Fact;
        props["fact_embedding"] = edge.FactEmbedding;
        props["episodes"] = edge.Episodes;
        props["expired_at"] = edge.ExpiredAt;
        props["valid_at"] = edge.ValidAt;
        props["invalid_at"] = edge.InvalidAt;
        props["reference_time"] = edge.ReferenceTime;
        foreach (var pair in edge.Attributes)
        {
            props.TryAdd(pair.Key, pair.Value);
        }

        return props;
    }

    private static Dictionary<string, object?> EntityEdgeBulkProperties(EntityEdge edge)
    {
        var props = EntityEdgeProperties(edge);
        props["source_node_uuid"] = edge.SourceNodeUuid;
        props["target_node_uuid"] = edge.TargetNodeUuid;
        return props;
    }

    private static Dictionary<string, object?> BaseNodeProperties(Node node) =>
        new(StringComparer.Ordinal)
        {
            ["uuid"] = node.Uuid,
            ["name"] = node.Name,
            ["group_id"] = node.GroupId,
            ["created_at"] = node.CreatedAt
        };

    private static Dictionary<string, object?> BaseEdgeProperties(Edge edge) =>
        new(StringComparer.Ordinal)
        {
            ["uuid"] = edge.Uuid,
            ["group_id"] = edge.GroupId,
            ["created_at"] = edge.CreatedAt
        };
}
