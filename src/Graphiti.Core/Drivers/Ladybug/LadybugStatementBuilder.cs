using System.Text.Json;

namespace Graphiti.Core.Drivers.Ladybug;

internal static class LadybugStatementBuilder
{
    internal static LadybugStatement BuildNodeSave(Node node) =>
        node switch
        {
            EntityNode entity => BuildEntityNodeSave(entity),
            EpisodicNode episode => BuildEpisodicNodeSave(episode),
            CommunityNode community => BuildCommunityNodeSave(community),
            SagaNode saga => BuildSagaNodeSave(saga),
            _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType().Name)
        };

    internal static LadybugStatement BuildEdgeSave(Edge edge) =>
        edge switch
        {
            EntityEdge entity => BuildEntityEdgeSave(entity),
            EpisodicEdge episodic => BuildEpisodicEdgeSave(episodic),
            CommunityEdge community => BuildCommunityEdgeSave(community),
            HasEpisodeEdge hasEpisode => BuildHasEpisodeEdgeSave(hasEpisode),
            NextEpisodeEdge nextEpisode => BuildNextEpisodeEdgeSave(nextEpisode),
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge.GetType().Name)
        };

    internal static IReadOnlyList<LadybugStatement> BuildBulkSaveStatements(
        IReadOnlyList<EpisodicNode> episodicNodes,
        IReadOnlyList<EpisodicEdge> episodicEdges,
        IReadOnlyList<EntityNode> entityNodes,
        IReadOnlyList<EntityEdge> entityEdges)
    {
        var statements = new List<LadybugStatement>(
            episodicNodes.Count + episodicEdges.Count + entityNodes.Count + entityEdges.Count);
        for (var i = 0; i < episodicNodes.Count; i++)
        {
            statements.Add(BuildEpisodicNodeSave(episodicNodes[i]));
        }

        for (var i = 0; i < entityNodes.Count; i++)
        {
            statements.Add(BuildEntityNodeSave(entityNodes[i]));
        }

        for (var i = 0; i < episodicEdges.Count; i++)
        {
            statements.Add(BuildEpisodicEdgeSave(episodicEdges[i]));
        }

        for (var i = 0; i < entityEdges.Count; i++)
        {
            statements.Add(BuildEntityEdgeSave(entityEdges[i]));
        }

        return statements;
    }

    internal static LadybugStatement BuildNodeGetByUuid<TNode>(
        string uuid,
        bool withEmbeddings = false)
        where TNode : Node
    {
        var variable = NodeVariable<TNode>();
        return new LadybugStatement(
            $$"""
            MATCH ({{variable}}:{{NodeLabel<TNode>()}} {uuid: $uuid})
            RETURN
            {{NodeReturnClause<TNode>(variable, withEmbeddings)}}
            """,
            Parameters(("uuid", uuid)));
    }

    internal static LadybugStatement BuildNodesGetByUuids<TNode>(
        IEnumerable<string> uuids,
        bool withEmbeddings = false)
        where TNode : Node
    {
        ArgumentNullException.ThrowIfNull(uuids);
        var variable = NodeVariable<TNode>();
        return new LadybugStatement(
            $"""
            MATCH ({variable}:{NodeLabel<TNode>()})
            WHERE {variable}.uuid IN $uuids
            RETURN DISTINCT
            {NodeReturnClause<TNode>(variable, withEmbeddings)}
            """,
            Parameters(("uuids", SnapshotList(uuids))));
    }

    internal static LadybugStatement BuildNodesGetByGroupIds<TNode>(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false)
        where TNode : Node
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        var variable = NodeVariable<TNode>();
        var cursorClause = uuidCursor is null ? string.Empty : $"AND {variable}.uuid < $uuid";
        var limitClause = limit is null ? string.Empty : "LIMIT $limit";
        var parameters = Parameters(("group_ids", SnapshotList(groupIds)));
        if (uuidCursor is not null)
        {
            parameters["uuid"] = uuidCursor;
        }

        if (limit is not null)
        {
            parameters["limit"] = limit.Value;
        }

        return new LadybugStatement(
            $"""
            MATCH ({variable}:{NodeLabel<TNode>()})
            WHERE {variable}.group_id IN $group_ids
            {cursorClause}
            RETURN DISTINCT
            {NodeReturnClause<TNode>(variable, withEmbeddings)}
            ORDER BY uuid DESC
            {limitClause}
            """,
            parameters);
    }

    internal static IReadOnlyList<LadybugStatement> BuildNodeDeleteByUuidStatements<TNode>(
        string uuid)
        where TNode : Node =>
        BuildNodeDeleteStatements<TNode>(NodeUuidDeleteMatch<TNode>(), Parameters(("uuid", uuid)));

    internal static IReadOnlyList<LadybugStatement> BuildNodesDeleteByGroupIdStatements<TNode>(
        string groupId)
        where TNode : Node =>
        BuildNodeDeleteStatements<TNode>(
            NodeGroupDeleteMatch<TNode>(),
            Parameters(("group_id", groupId)));

    internal static IReadOnlyList<LadybugStatement> BuildNodesDeleteByUuidsStatements<TNode>(
        IEnumerable<string> uuids)
        where TNode : Node
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return BuildNodeDeleteStatements<TNode>(
            NodeUuidsDeleteMatch<TNode>(),
            Parameters(("uuids", SnapshotList(uuids))));
    }

    internal static LadybugStatement BuildNodeLoadEmbedding<TNode>(string uuid)
        where TNode : Node
    {
        var (label, variable) = typeof(TNode) == typeof(EntityNode)
            ? ("Entity", "n")
            : typeof(TNode) == typeof(CommunityNode)
                ? ("Community", "c")
                : throw new ArgumentOutOfRangeException(typeof(TNode).Name);

        return new LadybugStatement(
            $$"""
            MATCH ({{variable}}:{{label}} {uuid: $uuid})
            RETURN {{variable}}.name_embedding AS name_embedding
            """,
            Parameters(("uuid", uuid)));
    }

    internal static LadybugStatement BuildNodesLoadEmbeddings<TNode>(IEnumerable<string> uuids)
        where TNode : Node
    {
        ArgumentNullException.ThrowIfNull(uuids);
        var (label, variable) = typeof(TNode) == typeof(EntityNode)
            ? ("Entity", "n")
            : typeof(TNode) == typeof(CommunityNode)
                ? ("Community", "c")
                : throw new ArgumentOutOfRangeException(typeof(TNode).Name);

        return new LadybugStatement(
            $"""
            MATCH ({variable}:{label})
            WHERE {variable}.uuid IN $uuids
            RETURN DISTINCT {variable}.uuid AS uuid, {variable}.name_embedding AS name_embedding
            """,
            Parameters(("uuids", SnapshotList(uuids))));
    }

    internal static LadybugStatement BuildEdgeGetByUuid<TEdge>(
        string uuid,
        bool withEmbeddings = false)
        where TEdge : Edge =>
        new(
            $"""
            {EdgeMatchPattern<TEdge>(" {uuid: $uuid}")}
            RETURN
            {EdgeReturnClause<TEdge>(withEmbeddings)}
            """,
            Parameters(("uuid", uuid)));

    internal static LadybugStatement BuildEdgesGetByUuids<TEdge>(
        IEnumerable<string> uuids,
        bool withEmbeddings = false)
        where TEdge : Edge
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return new LadybugStatement(
            $"""
            {EdgeMatchPattern<TEdge>()}
            WHERE e.uuid IN $uuids
            RETURN
            {EdgeReturnClause<TEdge>(withEmbeddings)}
            """,
            Parameters(("uuids", SnapshotList(uuids))));
    }

    internal static LadybugStatement BuildEdgesGetByGroupIds<TEdge>(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false)
        where TEdge : Edge
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        var cursorClause = uuidCursor is null ? string.Empty : "AND e.uuid < $uuid";
        var limitClause = limit is null ? string.Empty : "LIMIT $limit";
        var parameters = Parameters(("group_ids", SnapshotList(groupIds)));
        if (uuidCursor is not null)
        {
            parameters["uuid"] = uuidCursor;
        }

        if (limit is not null)
        {
            parameters["limit"] = limit.Value;
        }

        return new LadybugStatement(
            $"""
            {EdgeMatchPattern<TEdge>()}
            WHERE e.group_id IN $group_ids
            {cursorClause}
            RETURN
            {EdgeReturnClause<TEdge>(withEmbeddings)}
            ORDER BY uuid DESC
            {limitClause}
            """,
            parameters);
    }

    internal static LadybugStatement BuildEntityEdgesBetweenNodesGet(
        string sourceNodeUuid,
        string targetNodeUuid) =>
        new(
            """
            MATCH (n:Entity {uuid: $source_node_uuid})-[:RELATES_TO]->(e:RelatesToNode_)-[:RELATES_TO]->(m:Entity {uuid: $target_node_uuid})
            RETURN
            """ + EdgeReturnClause<EntityEdge>(),
            Parameters(
                ("source_node_uuid", sourceNodeUuid),
                ("target_node_uuid", targetNodeUuid)));

    internal static LadybugStatement BuildEntityEdgesByNodeUuidGet(string nodeUuid) =>
        new(
            """
            MATCH (n:Entity {uuid: $node_uuid})-[:RELATES_TO]->(e:RelatesToNode_)-[:RELATES_TO]->(m:Entity)
            RETURN
            """ + EdgeReturnClause<EntityEdge>(),
            Parameters(("node_uuid", nodeUuid)));

    internal static LadybugStatement BuildEntityEdgeLoadEmbedding(string uuid) =>
        new(
            """
            MATCH (n:Entity)-[:RELATES_TO]->(e:RelatesToNode_ {uuid: $uuid})-[:RELATES_TO]->(m:Entity)
            RETURN e.fact_embedding AS fact_embedding
            """,
            Parameters(("uuid", uuid)));

    internal static LadybugStatement BuildEntityEdgesLoadEmbeddings(IEnumerable<string> uuids)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return new LadybugStatement(
            """
            MATCH (n:Entity)-[:RELATES_TO]->(e:RelatesToNode_)-[:RELATES_TO]->(m:Entity)
            WHERE e.uuid IN $edge_uuids
            RETURN DISTINCT e.uuid AS uuid, e.fact_embedding AS fact_embedding
            """,
            Parameters(("edge_uuids", SnapshotList(uuids))));
    }

    internal static LadybugStatement BuildRetrieveEpisodes(
        DateTime referenceTime,
        int lastN,
        IReadOnlyList<string>? groupIds = null,
        EpisodeType? source = null,
        string? saga = null)
    {
        var parameters = Parameters(
            ("reference_time", referenceTime),
            ("num_episodes", lastN));
        var sourceClause = source is null ? string.Empty : "AND e.source = $source";
        if (source is not null)
        {
            parameters["source"] = source.Value.ToWireValue();
        }

        if (saga is not null && groupIds is { Count: > 0 })
        {
            parameters["saga_name"] = saga;
            parameters["group_id"] = groupIds[0];
            return new LadybugStatement(
                $$"""
                MATCH (s:Saga {name: $saga_name, group_id: $group_id})-[:HAS_EPISODE]->(e:Episodic)
                WHERE e.valid_at <= $reference_time
                {{sourceClause}}
                RETURN
                {{NodeReturnClause<EpisodicNode>("e")}}
                ORDER BY valid_at DESC
                LIMIT $num_episodes
                """,
                parameters);
        }

        var groupClause = groupIds is { Count: > 0 } ? "AND e.group_id IN $group_ids" : string.Empty;
        if (groupIds is { Count: > 0 })
        {
            parameters["group_ids"] = SnapshotList(groupIds);
        }

        return new LadybugStatement(
            $"""
            MATCH (e:Episodic)
            WHERE e.valid_at <= $reference_time
            {groupClause}
            {sourceClause}
            RETURN
            {NodeReturnClause<EpisodicNode>("e")}
            ORDER BY valid_at DESC
            LIMIT $num_episodes
            """,
            parameters);
    }

    internal static LadybugStatement BuildEpisodesByEntityNodeUuidGet(string entityNodeUuid) =>
        new(
            """
            MATCH (e:Episodic)-[r:MENTIONS]->(n:Entity {uuid: $entity_node_uuid})
            RETURN DISTINCT
            """ + NodeReturnClause<EpisodicNode>("e"),
            Parameters(("entity_node_uuid", entityNodeUuid)));

    internal static LadybugStatement BuildMentionedNodesGet(IEnumerable<string> episodeUuids)
    {
        ArgumentNullException.ThrowIfNull(episodeUuids);
        return new LadybugStatement(
            """
            MATCH (episode:Episodic)-[:MENTIONS]->(n:Entity)
            WHERE episode.uuid IN $uuids
            RETURN DISTINCT
            """ + NodeReturnClause<EntityNode>("n"),
            Parameters(("uuids", SnapshotList(episodeUuids))));
    }

    internal static LadybugStatement BuildCommunitiesByNodesGet(IEnumerable<string> nodeUuids)
    {
        ArgumentNullException.ThrowIfNull(nodeUuids);
        return new LadybugStatement(
            """
            MATCH (c:Community)-[:HAS_MEMBER]->(n:Entity)
            WHERE n.uuid IN $uuids
            RETURN DISTINCT
            """ + NodeReturnClause<CommunityNode>("c"),
            Parameters(("uuids", SnapshotList(nodeUuids))));
    }

    internal static LadybugStatement BuildSagaByNameGet(string name, string groupId) =>
        new(
            """
            MATCH (s:Saga {name: $name, group_id: $group_id})
            RETURN
            """ + NodeReturnClause<SagaNode>("s") + """

            ORDER BY uuid ASC
            LIMIT 1
            """,
            Parameters(("name", name), ("group_id", groupId)));

    internal static LadybugStatement BuildSagaPreviousEpisodeUuidGet(
        string sagaUuid,
        string currentEpisodeUuid) =>
        new(
            """
            MATCH (:Saga {uuid: $saga_uuid})-[:HAS_EPISODE]->(e:Episodic)
            WHERE e.uuid <> $current_episode_uuid
            RETURN e.uuid AS uuid, e.valid_at AS valid_at, e.created_at AS created_at
            ORDER BY valid_at DESC, created_at DESC
            LIMIT 1
            """,
            Parameters(
                ("saga_uuid", sagaUuid),
                ("current_episode_uuid", currentEpisodeUuid)));

    internal static LadybugStatement BuildSagaEpisodeContentsGet(
        string sagaUuid,
        DateTime? since = null,
        int limit = 200)
    {
        var sinceFilter = since is null ? string.Empty : "AND e.created_at > $since";
        var orderBy = since is null
            ? "ORDER BY valid_at DESC, created_at DESC"
            : "ORDER BY valid_at ASC, created_at ASC";
        return new LadybugStatement(
            $$"""
            MATCH (:Saga {uuid: $saga_uuid})-[:HAS_EPISODE]->(e:Episodic)
            WHERE true {{sinceFilter}}
            RETURN e.content AS content, e.valid_at AS valid_at, e.created_at AS created_at
            {{orderBy}}
            LIMIT $limit
            """,
            Parameters(
                ("saga_uuid", sagaUuid),
                ("since", since),
                ("limit", limit)));
    }

    internal static LadybugStatement BuildNodeGroupIdsGet<TNode>()
        where TNode : Node
    {
        var variable = NodeVariable<TNode>();
        return new LadybugStatement(
            $"""
            MATCH ({variable}:{NodeLabel<TNode>()})
            RETURN DISTINCT {variable}.group_id AS group_id
            ORDER BY group_id ASC
            """,
            Parameters());
    }

    internal static LadybugStatement BuildEdgeDeleteByUuid<TEdge>(string uuid)
        where TEdge : Edge =>
        new(
            $"""
            {EdgeMatchPattern<TEdge>(" {uuid: $uuid}")}
            {EdgeDeleteVerb<TEdge>()} e
            """,
            Parameters(("uuid", uuid)));

    internal static LadybugStatement BuildEdgesDeleteByUuids<TEdge>(IEnumerable<string> uuids)
        where TEdge : Edge
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return new LadybugStatement(
            $"""
            {EdgeMatchPattern<TEdge>()}
            WHERE e.uuid IN $uuids
            {EdgeDeleteVerb<TEdge>()} e
            """,
            Parameters(("uuids", SnapshotList(uuids))));
    }

    internal static LadybugStatement BuildEpisodicNodeSave(EpisodicNode node) =>
        new(
            """
            MERGE (n:Episodic {uuid: $uuid})
            SET
                n.name = $name,
                n.group_id = $group_id,
                n.created_at = $created_at,
                n.source = $source,
                n.source_description = $source_description,
                n.content = $content,
                n.valid_at = $valid_at,
                n.entity_edges = $entity_edges
            RETURN n.uuid AS uuid
            """,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["uuid"] = node.Uuid,
                ["name"] = node.Name,
                ["group_id"] = node.GroupId,
                ["source_description"] = node.SourceDescription,
                ["content"] = node.Content,
                ["entity_edges"] = node.EntityEdges,
                ["created_at"] = node.CreatedAt,
                ["valid_at"] = node.ValidAt,
                ["source"] = node.Source.ToWireValue()
            });

    internal static LadybugStatement BuildEntityNodeSave(EntityNode node) =>
        new(
            """
            MERGE (n:Entity {uuid: $uuid})
            SET
                n.name = $name,
                n.group_id = $group_id,
                n.labels = $labels,
                n.created_at = $created_at,
                n.name_embedding = $name_embedding,
                n.summary = $summary,
                n.attributes = $attributes
            WITH n
            RETURN n.uuid AS uuid
            """,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["uuid"] = node.Uuid,
                ["name"] = node.Name,
                ["name_embedding"] = node.NameEmbedding,
                ["group_id"] = node.GroupId,
                ["summary"] = node.Summary,
                ["created_at"] = node.CreatedAt,
                ["labels"] = EntityLabels(node),
                ["attributes"] = SerializeAttributes(node.Attributes)
            });

    internal static LadybugStatement BuildCommunityNodeSave(CommunityNode node) =>
        new(
            """
            MERGE (n:Community {uuid: $uuid})
            SET
                n.name = $name,
                n.group_id = $group_id,
                n.created_at = $created_at,
                n.name_embedding = $name_embedding,
                n.summary = $summary
            RETURN n.uuid AS uuid
            """,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["uuid"] = node.Uuid,
                ["name"] = node.Name,
                ["group_id"] = node.GroupId,
                ["summary"] = node.Summary,
                ["name_embedding"] = node.NameEmbedding,
                ["created_at"] = node.CreatedAt
            });

    internal static LadybugStatement BuildSagaNodeSave(SagaNode node) =>
        new(
            """
            MERGE (n:Saga {uuid: $uuid})
            SET
                n.name = $name,
                n.group_id = $group_id,
                n.created_at = $created_at,
                n.summary = $summary,
                n.first_episode_uuid = $first_episode_uuid,
                n.last_episode_uuid = $last_episode_uuid,
                n.last_summarized_at = $last_summarized_at,
                n.last_summarized_episode_valid_at = $last_summarized_episode_valid_at
            RETURN n.uuid AS uuid
            """,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["uuid"] = node.Uuid,
                ["name"] = node.Name,
                ["group_id"] = node.GroupId,
                ["created_at"] = node.CreatedAt,
                ["summary"] = node.Summary,
                ["first_episode_uuid"] = node.FirstEpisodeUuid,
                ["last_episode_uuid"] = node.LastEpisodeUuid,
                ["last_summarized_at"] = node.LastSummarizedAt,
                ["last_summarized_episode_valid_at"] = node.LastSummarizedEpisodeValidAt
            });

    internal static LadybugStatement BuildEntityEdgeSave(EntityEdge edge) =>
        new(
            """
            MATCH (source:Entity {uuid: $source_uuid})
            MATCH (target:Entity {uuid: $target_uuid})
            MERGE (source)-[:RELATES_TO]->(e:RelatesToNode_ {uuid: $uuid})-[:RELATES_TO]->(target)
            SET
                e.group_id = $group_id,
                e.created_at = $created_at,
                e.name = $name,
                e.fact = $fact,
                e.fact_embedding = $fact_embedding,
                e.episodes = $episodes,
                e.expired_at = $expired_at,
                e.valid_at = $valid_at,
                e.invalid_at = $invalid_at,
                e.reference_time = $reference_time,
                e.attributes = $attributes
            RETURN e.uuid AS uuid
            """,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["uuid"] = edge.Uuid,
                ["source_uuid"] = edge.SourceNodeUuid,
                ["target_uuid"] = edge.TargetNodeUuid,
                ["name"] = edge.Name,
                ["fact"] = edge.Fact,
                ["fact_embedding"] = edge.FactEmbedding,
                ["group_id"] = edge.GroupId,
                ["episodes"] = edge.Episodes,
                ["created_at"] = edge.CreatedAt,
                ["expired_at"] = edge.ExpiredAt,
                ["valid_at"] = edge.ValidAt,
                ["invalid_at"] = edge.InvalidAt,
                ["reference_time"] = edge.ReferenceTime,
                ["attributes"] = SerializeAttributes(edge.Attributes)
            });

    internal static LadybugStatement BuildEpisodicEdgeSave(EpisodicEdge edge) =>
        SimpleEdge(
            """
            MATCH (episode:Episodic {uuid: $episode_uuid})
            MATCH (node:Entity {uuid: $entity_uuid})
            MERGE (episode)-[e:MENTIONS {uuid: $uuid}]->(node)
            SET
                e.group_id = $group_id,
                e.created_at = $created_at
            RETURN e.uuid AS uuid
            """,
            edge,
            ("episode_uuid", edge.SourceNodeUuid),
            ("entity_uuid", edge.TargetNodeUuid));

    internal static LadybugStatement BuildCommunityEdgeSave(CommunityEdge edge) =>
        SimpleEdge(
            """
            MATCH (community:Community {uuid: $community_uuid})
            MATCH (node:Entity {uuid: $entity_uuid})
            MERGE (community)-[e:HAS_MEMBER {uuid: $uuid}]->(node)
            SET
                e.group_id = $group_id,
                e.created_at = $created_at
            RETURN e.uuid AS uuid
            UNION
            MATCH (community:Community {uuid: $community_uuid})
            MATCH (node:Community {uuid: $entity_uuid})
            MERGE (community)-[e:HAS_MEMBER {uuid: $uuid}]->(node)
            SET
                e.group_id = $group_id,
                e.created_at = $created_at
            RETURN e.uuid AS uuid
            """,
            edge,
            ("community_uuid", edge.SourceNodeUuid),
            ("entity_uuid", edge.TargetNodeUuid));

    internal static LadybugStatement BuildHasEpisodeEdgeSave(HasEpisodeEdge edge) =>
        SimpleEdge(
            """
            MATCH (saga:Saga {uuid: $saga_uuid})
            MATCH (episode:Episodic {uuid: $episode_uuid})
            MERGE (saga)-[e:HAS_EPISODE {uuid: $uuid}]->(episode)
            SET
                e.group_id = $group_id,
                e.created_at = $created_at
            RETURN e.uuid AS uuid
            """,
            edge,
            ("saga_uuid", edge.SourceNodeUuid),
            ("episode_uuid", edge.TargetNodeUuid));

    internal static LadybugStatement BuildNextEpisodeEdgeSave(NextEpisodeEdge edge) =>
        SimpleEdge(
            """
            MATCH (source_episode:Episodic {uuid: $source_episode_uuid})
            MATCH (target_episode:Episodic {uuid: $target_episode_uuid})
            MERGE (source_episode)-[e:NEXT_EPISODE {uuid: $uuid}]->(target_episode)
            SET
                e.group_id = $group_id,
                e.created_at = $created_at
            RETURN e.uuid AS uuid
            """,
            edge,
            ("source_episode_uuid", edge.SourceNodeUuid),
            ("target_episode_uuid", edge.TargetNodeUuid));

    private static LadybugStatement SimpleEdge(
        string query,
        Edge edge,
        (string Name, string Value) source,
        (string Name, string Value) target)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["uuid"] = edge.Uuid,
            [source.Name] = source.Value,
            [target.Name] = target.Value,
            ["group_id"] = edge.GroupId,
            ["created_at"] = edge.CreatedAt
        };
        return new LadybugStatement(query, parameters);
    }

    private static LadybugStatement[] BuildNodeDeleteStatements<TNode>(
        string deleteMatch,
        Dictionary<string, object?> parameters)
        where TNode : Node
    {
        if (typeof(TNode) != typeof(EntityNode))
        {
            return new[]
            {
                new LadybugStatement(
                    $"""
                    {deleteMatch}
                    DETACH DELETE n
                    """,
                    parameters)
            };
        }

        return new[]
        {
            new LadybugStatement(
                $"""
                {EntityRelatesToCleanupMatch(deleteMatch)}
                DETACH DELETE r
                """,
                parameters),
            new LadybugStatement(
                $"""
                {deleteMatch}
                DETACH DELETE n
                """,
                parameters)
        };
    }

    private static string EntityRelatesToCleanupMatch(string entityMatch) =>
        entityMatch.Contains("WHERE", StringComparison.Ordinal)
            ? entityMatch + "\nWITH n\nMATCH (n)-[:RELATES_TO]->(r:RelatesToNode_)"
            : entityMatch + "-[:RELATES_TO]->(r:RelatesToNode_)";

    private static string NodeUuidDeleteMatch<TNode>()
        where TNode : Node =>
        $"MATCH (n:{NodeLabel<TNode>()} {{uuid: $uuid}})";

    private static string NodeGroupDeleteMatch<TNode>()
        where TNode : Node =>
        $"MATCH (n:{NodeLabel<TNode>()} {{group_id: $group_id}})";

    private static string NodeUuidsDeleteMatch<TNode>()
        where TNode : Node =>
        $"""
        MATCH (n:{NodeLabel<TNode>()})
        WHERE n.uuid IN $uuids
        """;

    private static string NodeLabel<TNode>()
        where TNode : Node =>
        typeof(TNode) == typeof(EntityNode) ? "Entity" :
        typeof(TNode) == typeof(EpisodicNode) ? "Episodic" :
        typeof(TNode) == typeof(CommunityNode) ? "Community" :
        typeof(TNode) == typeof(SagaNode) ? "Saga" :
        throw new ArgumentOutOfRangeException(typeof(TNode).Name);

    private static string NodeVariable<TNode>()
        where TNode : Node =>
        typeof(TNode) == typeof(EntityNode) ? "n" :
        typeof(TNode) == typeof(EpisodicNode) ? "e" :
        typeof(TNode) == typeof(CommunityNode) ? "c" :
        typeof(TNode) == typeof(SagaNode) ? "s" :
        throw new ArgumentOutOfRangeException(typeof(TNode).Name);

    private static string NodeReturnClause<TNode>(string variable, bool withEmbeddings = false)
        where TNode : Node
    {
        if (typeof(TNode) == typeof(EpisodicNode))
        {
            return $"""
                {variable}.uuid AS uuid,
                {variable}.name AS name,
                {variable}.group_id AS group_id,
                {variable}.created_at AS created_at,
                {variable}.source AS source,
                {variable}.source_description AS source_description,
                {variable}.content AS content,
                {variable}.valid_at AS valid_at,
                {variable}.entity_edges AS entity_edges
            """;
        }

        if (typeof(TNode) == typeof(EntityNode))
        {
            var embeddingProjection = withEmbeddings ? $",\n    {variable}.name_embedding AS name_embedding" : string.Empty;
            return $"""
                {variable}.uuid AS uuid,
                {variable}.name AS name,
                {variable}.group_id AS group_id,
                {variable}.labels AS labels,
                {variable}.created_at AS created_at,
                {variable}.summary AS summary,
                {variable}.attributes AS attributes{embeddingProjection}
            """;
        }

        if (typeof(TNode) == typeof(CommunityNode))
        {
            return $"""
                {variable}.uuid AS uuid,
                {variable}.name AS name,
                {variable}.group_id AS group_id,
                {variable}.created_at AS created_at,
                {variable}.name_embedding AS name_embedding,
                {variable}.summary AS summary
            """;
        }

        if (typeof(TNode) == typeof(SagaNode))
        {
            return $"""
                {variable}.uuid AS uuid,
                {variable}.name AS name,
                {variable}.group_id AS group_id,
                {variable}.created_at AS created_at,
                {variable}.summary AS summary,
                {variable}.first_episode_uuid AS first_episode_uuid,
                {variable}.last_episode_uuid AS last_episode_uuid,
                {variable}.last_summarized_at AS last_summarized_at,
                {variable}.last_summarized_episode_valid_at AS last_summarized_episode_valid_at
            """;
        }

        throw new ArgumentOutOfRangeException(typeof(TNode).Name);
    }

    private static string EdgeMatchPattern<TEdge>(string edgePredicate = "")
        where TEdge : Edge =>
        typeof(TEdge) == typeof(EntityEdge)
            ? $"MATCH (n:Entity)-[:RELATES_TO]->(e:RelatesToNode_{edgePredicate})-[:RELATES_TO]->(m:Entity)"
            : typeof(TEdge) == typeof(EpisodicEdge)
                ? $"MATCH (n:Episodic)-[e:MENTIONS{edgePredicate}]->(m:Entity)"
                : typeof(TEdge) == typeof(CommunityEdge)
                    ? $"MATCH (n:Community)-[e:HAS_MEMBER{edgePredicate}]->(m)"
                    : typeof(TEdge) == typeof(HasEpisodeEdge)
                        ? $"MATCH (n:Saga)-[e:HAS_EPISODE{edgePredicate}]->(m:Episodic)"
                        : typeof(TEdge) == typeof(NextEpisodeEdge)
                            ? $"MATCH (n:Episodic)-[e:NEXT_EPISODE{edgePredicate}]->(m:Episodic)"
                            : throw new ArgumentOutOfRangeException(typeof(TEdge).Name);

    private static string EdgeReturnClause<TEdge>(bool withEmbeddings = false)
        where TEdge : Edge
    {
        if (typeof(TEdge) != typeof(EntityEdge))
        {
            return """
                e.uuid AS uuid,
                e.group_id AS group_id,
                n.uuid AS source_node_uuid,
                m.uuid AS target_node_uuid,
                e.created_at AS created_at
            """;
        }

        var embeddingProjection = withEmbeddings ? ",\n    e.fact_embedding AS fact_embedding" : string.Empty;
        return $"""
            e.uuid AS uuid,
            n.uuid AS source_node_uuid,
            m.uuid AS target_node_uuid,
            e.group_id AS group_id,
            e.created_at AS created_at,
            e.name AS name,
            e.fact AS fact,
            e.episodes AS episodes,
            e.expired_at AS expired_at,
            e.valid_at AS valid_at,
            e.invalid_at AS invalid_at,
            e.reference_time AS reference_time,
            e.attributes AS attributes{embeddingProjection}
        """;
    }

    private static string EdgeDeleteVerb<TEdge>()
        where TEdge : Edge =>
        typeof(TEdge) == typeof(EntityEdge) ? "DETACH DELETE" : "DELETE";

    private static Dictionary<string, object?> Parameters(params (string Name, object? Value)[] parameters)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in parameters)
        {
            dictionary[name] = value;
        }

        return dictionary;
    }

    private static List<T> SnapshotList<T>(IEnumerable<T> values)
    {
        if (values is IReadOnlyList<T> list)
        {
            return SnapshotList(list);
        }

        var snapshot = values.TryGetNonEnumeratedCount(out var count)
            ? new List<T>(count)
            : new List<T>();
        foreach (var value in values)
        {
            snapshot.Add(value);
        }

        return snapshot;
    }

    private static List<T> SnapshotList<T>(IReadOnlyList<T> values)
    {
        var snapshot = new List<T>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            snapshot.Add(values[i]);
        }

        return snapshot;
    }

    private static List<string> EntityLabels(EntityNode node)
    {
        var labels = new List<string>(node.Labels.Count + 1);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var label in node.Labels)
        {
            if (seen.Add(label))
            {
                labels.Add(label);
            }
        }

        if (seen.Add("Entity"))
        {
            labels.Add("Entity");
        }

        return labels;
    }

    private static string SerializeAttributes(Dictionary<string, object?> attributes) =>
        JsonSerializer.Serialize(attributes, GraphitiJsonSerializer.Options);
}
