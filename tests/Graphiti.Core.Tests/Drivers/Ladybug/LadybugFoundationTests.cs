using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.Core.Drivers.Ladybug;

namespace Graphiti.Core.Tests.Drivers.Ladybug;

public class LadybugFoundationTests
{
    [Fact]
    public void LadybugSchema_PortsPythonKuzuTablesWithoutClaimingFulltext()
    {
        foreach (var table in LadybugSchema.NodeTables)
        {
            Assert.Contains(
                $"CREATE NODE TABLE IF NOT EXISTS {table}",
                LadybugSchema.SchemaQueries,
                StringComparison.Ordinal);
        }

        foreach (var table in LadybugSchema.RelationshipTables)
        {
            Assert.Contains(
                $"CREATE REL TABLE IF NOT EXISTS {table}",
                LadybugSchema.SchemaQueries,
                StringComparison.Ordinal);
        }

        Assert.Contains("CREATE NODE TABLE IF NOT EXISTS RelatesToNode_", LadybugSchema.SchemaQueries, StringComparison.Ordinal);
        Assert.Contains("FROM Entity TO RelatesToNode_", LadybugSchema.SchemaQueries, StringComparison.Ordinal);
        Assert.Contains("FROM RelatesToNode_ TO Entity", LadybugSchema.SchemaQueries, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE_FTS_INDEX", LadybugSchema.SchemaQueries, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("QUERY_FTS_INDEX", LadybugSchema.SchemaQueries, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildEntityNodeSave_UsesLabelArrayAndJsonAttributes()
    {
        var node = new EntityNode
        {
            Uuid = "entity-1",
            Name = "Alice",
            GroupId = "tenant",
            Labels = ["Person", "Person"],
            CreatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            NameEmbedding = [0.1f, 0.2f],
            Summary = "Alice summary",
            Attributes = new Dictionary<string, object?>
            {
                ["age"] = 42,
                ["role"] = "engineer"
            }
        };

        var statement = LadybugStatementBuilder.BuildNodeSave(node);

        Assert.Contains("MERGE (n:Entity {uuid: $uuid})", statement.Query, StringComparison.Ordinal);
        Assert.Contains("n.labels = $labels", statement.Query, StringComparison.Ordinal);
        Assert.Contains("n.attributes = $attributes", statement.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("SET n:", statement.Query, StringComparison.Ordinal);
        Assert.Equal("entity-1", statement.Parameters["uuid"]);
        Assert.Equal(new[] { "Person", "Entity" }, Assert.IsType<List<string>>(statement.Parameters["labels"]));
        var attributes = JsonNode.Parse(Assert.IsType<string>(statement.Parameters["attributes"]))!.AsObject();
        Assert.Equal(42, attributes["age"]!.GetValue<int>());
        Assert.Equal("engineer", attributes["role"]!.GetValue<string>());
    }

    [Fact]
    public void BuildEntityEdgeSave_UsesRelatesToNodeAndJsonAttributes()
    {
        var referenceTime = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        var edge = new EntityEdge
        {
            Uuid = "edge-1",
            SourceNodeUuid = "source-1",
            TargetNodeUuid = "target-1",
            GroupId = "tenant",
            Name = "KNOWS",
            Fact = "Alice knows Bob",
            FactEmbedding = [0.3f, 0.4f],
            Episodes = ["episode-1"],
            CreatedAt = referenceTime.AddMinutes(-1),
            ExpiredAt = null,
            ValidAt = referenceTime.AddDays(-1),
            InvalidAt = null,
            ReferenceTime = referenceTime,
            Attributes = new Dictionary<string, object?>
            {
                ["confidence"] = 0.9,
                ["source"] = "message"
            }
        };

        var statement = LadybugStatementBuilder.BuildEdgeSave(edge);

        Assert.Contains(
            "MERGE (source)-[:RELATES_TO]->(e:RelatesToNode_ {uuid: $uuid})-[:RELATES_TO]->(target)",
            statement.Query,
            StringComparison.Ordinal);
        Assert.DoesNotContain("-[e:RELATES_TO {uuid:", statement.Query, StringComparison.Ordinal);
        Assert.Contains("e.reference_time = $reference_time", statement.Query, StringComparison.Ordinal);
        Assert.Contains("e.attributes = $attributes", statement.Query, StringComparison.Ordinal);
        Assert.Equal("source-1", statement.Parameters["source_uuid"]);
        Assert.Equal("target-1", statement.Parameters["target_uuid"]);
        Assert.Equal(referenceTime, statement.Parameters["reference_time"]);
        var attributes = JsonNode.Parse(Assert.IsType<string>(statement.Parameters["attributes"]))!.AsObject();
        Assert.Equal(0.9, attributes["confidence"]!.GetValue<double>());
        Assert.Equal("message", attributes["source"]!.GetValue<string>());
    }

    [Fact]
    public void BuildCommunityEdgeSave_UsesKuzuUnionTargetShape()
    {
        var edge = new CommunityEdge
        {
            Uuid = "membership-1",
            SourceNodeUuid = "community-1",
            TargetNodeUuid = "target-1",
            GroupId = "tenant"
        };

        var statement = LadybugStatementBuilder.BuildEdgeSave(edge);

        Assert.Contains("MATCH (node:Entity {uuid: $entity_uuid})", statement.Query, StringComparison.Ordinal);
        Assert.Contains("UNION", statement.Query, StringComparison.Ordinal);
        Assert.Contains("MATCH (node:Community {uuid: $entity_uuid})", statement.Query, StringComparison.Ordinal);
        Assert.Equal("community-1", statement.Parameters["community_uuid"]);
        Assert.Equal("target-1", statement.Parameters["entity_uuid"]);
    }

    [Fact]
    public void BuildBulkSaveStatements_UsesIndividualKuzuStatements()
    {
        var statements = LadybugStatementBuilder.BuildBulkSaveStatements(
            [new EpisodicNode { Uuid = "episode-1" }],
            [new EpisodicEdge { Uuid = "mention-1" }],
            [new EntityNode { Uuid = "entity-1" }],
            [new EntityEdge { Uuid = "edge-1" }]);

        Assert.Equal(4, statements.Count);
        Assert.All(statements, statement => Assert.DoesNotContain("UNWIND", statement.Query, StringComparison.Ordinal));
        Assert.Contains("MERGE (n:Episodic {uuid: $uuid})", statements[0].Query, StringComparison.Ordinal);
        Assert.Contains("MERGE (n:Entity {uuid: $uuid})", statements[1].Query, StringComparison.Ordinal);
        Assert.Contains("MERGE (episode)-[e:MENTIONS {uuid: $uuid}]->(node)", statements[2].Query, StringComparison.Ordinal);
        Assert.Contains("MERGE (source)-[:RELATES_TO]->(e:RelatesToNode_ {uuid: $uuid})", statements[3].Query, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCollectionParameterStatements_SnapshotInputsWithIndexAccess()
    {
        var entityUuids = new IndexOnlyReadOnlyList<string>("entity-1", "entity-2");
        var edgeUuids = new IndexOnlyReadOnlyList<string>("edge-1", "edge-2");
        var groupIds = new IndexOnlyReadOnlyList<string>("tenant", "archive");
        var referenceTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var nodesByUuid = LadybugStatementBuilder.BuildNodesGetByUuids<EntityNode>(entityUuids);
        var deleteNodes = LadybugStatementBuilder.BuildNodesDeleteByUuidsStatements<EntityNode>(entityUuids);
        var nodeEmbeddings = LadybugStatementBuilder.BuildNodesLoadEmbeddings<EntityNode>(entityUuids);
        var edgesByUuid = LadybugStatementBuilder.BuildEdgesGetByUuids<EntityEdge>(edgeUuids);
        var edgeEmbeddings = LadybugStatementBuilder.BuildEntityEdgesLoadEmbeddings(edgeUuids);
        var nodesByGroup = LadybugStatementBuilder.BuildNodesGetByGroupIds<EntityNode>(groupIds);
        var edgesByGroup = LadybugStatementBuilder.BuildEdgesGetByGroupIds<EntityEdge>(groupIds);
        var retrieve = LadybugStatementBuilder.BuildRetrieveEpisodes(referenceTime, 3, groupIds);

        Assert.Equal(new[] { "entity-1", "entity-2" }, Assert.IsType<List<string>>(nodesByUuid.Parameters["uuids"]));
        Assert.Equal(new[] { "entity-1", "entity-2" }, Assert.IsType<List<string>>(deleteNodes[0].Parameters["uuids"]));
        Assert.Equal(new[] { "entity-1", "entity-2" }, Assert.IsType<List<string>>(nodeEmbeddings.Parameters["uuids"]));
        Assert.Equal(new[] { "edge-1", "edge-2" }, Assert.IsType<List<string>>(edgesByUuid.Parameters["uuids"]));
        Assert.Equal(new[] { "edge-1", "edge-2" }, Assert.IsType<List<string>>(edgeEmbeddings.Parameters["edge_uuids"]));
        Assert.Equal(new[] { "tenant", "archive" }, Assert.IsType<List<string>>(nodesByGroup.Parameters["group_ids"]));
        Assert.Equal(new[] { "tenant", "archive" }, Assert.IsType<List<string>>(edgesByGroup.Parameters["group_ids"]));
        Assert.Equal(new[] { "tenant", "archive" }, Assert.IsType<List<string>>(retrieve.Parameters["group_ids"]));
    }

    [Fact]
    public void BuildSagaNodeSave_UsesFullSagaModelShapeForRuntimeWiring()
    {
        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var summarizedAt = createdAt.AddHours(2);
        var summarizedValidAt = createdAt.AddHours(1);
        var node = new SagaNode
        {
            Uuid = "saga-1",
            Name = "checkout",
            GroupId = "tenant",
            CreatedAt = createdAt,
            Summary = "summary",
            FirstEpisodeUuid = "episode-1",
            LastEpisodeUuid = "episode-2",
            LastSummarizedAt = summarizedAt,
            LastSummarizedEpisodeValidAt = summarizedValidAt
        };

        var statement = LadybugStatementBuilder.BuildNodeSave(node);
        var get = LadybugStatementBuilder.BuildNodeGetByUuid<SagaNode>("saga-1");
        var sagaSchemaStart = LadybugSchema.SchemaQueries.IndexOf(
            "CREATE NODE TABLE IF NOT EXISTS Saga",
            StringComparison.Ordinal);
        var sagaSchemaEnd = LadybugSchema.SchemaQueries.IndexOf(");", sagaSchemaStart, StringComparison.Ordinal);
        var sagaSchema = LadybugSchema.SchemaQueries[sagaSchemaStart..sagaSchemaEnd];

        Assert.Contains("CREATE NODE TABLE IF NOT EXISTS Saga", sagaSchema, StringComparison.Ordinal);
        Assert.Contains("summary STRING", sagaSchema, StringComparison.Ordinal);
        Assert.Contains("first_episode_uuid STRING", sagaSchema, StringComparison.Ordinal);
        Assert.Contains("last_episode_uuid STRING", sagaSchema, StringComparison.Ordinal);
        Assert.Contains("last_summarized_at TIMESTAMP", sagaSchema, StringComparison.Ordinal);
        Assert.Contains("last_summarized_episode_valid_at TIMESTAMP", sagaSchema, StringComparison.Ordinal);
        Assert.Contains("MERGE (n:Saga {uuid: $uuid})", statement.Query, StringComparison.Ordinal);
        Assert.Contains("n.summary = $summary", statement.Query, StringComparison.Ordinal);
        Assert.Contains("n.first_episode_uuid = $first_episode_uuid", statement.Query, StringComparison.Ordinal);
        Assert.Contains(
            "n.last_summarized_episode_valid_at = $last_summarized_episode_valid_at",
            statement.Query,
            StringComparison.Ordinal);
        Assert.Equal("summary", statement.Parameters["summary"]);
        Assert.Equal("episode-1", statement.Parameters["first_episode_uuid"]);
        Assert.Equal("episode-2", statement.Parameters["last_episode_uuid"]);
        Assert.Equal(summarizedAt, statement.Parameters["last_summarized_at"]);
        Assert.Equal(summarizedValidAt, statement.Parameters["last_summarized_episode_valid_at"]);
        Assert.Contains("s.summary AS summary", get.Query, StringComparison.Ordinal);
        Assert.Contains("s.first_episode_uuid AS first_episode_uuid", get.Query, StringComparison.Ordinal);
        Assert.Contains(
            "s.last_summarized_episode_valid_at AS last_summarized_episode_valid_at",
            get.Query,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEntityNodeDelete_CleansRelatesToNodeIntermediatesFirst()
    {
        var byUuid = LadybugStatementBuilder.BuildNodeDeleteByUuidStatements<EntityNode>("entity-1");
        var byGroup = LadybugStatementBuilder.BuildNodesDeleteByGroupIdStatements<EntityNode>("tenant");
        var byUuids = LadybugStatementBuilder.BuildNodesDeleteByUuidsStatements<EntityNode>(["entity-1", "entity-2"]);

        Assert.Equal(2, byUuid.Count);
        Assert.Contains(
            "MATCH (n:Entity {uuid: $uuid})-[:RELATES_TO]->(r:RelatesToNode_)",
            byUuid[0].Query,
            StringComparison.Ordinal);
        Assert.Contains("DETACH DELETE r", byUuid[0].Query, StringComparison.Ordinal);
        Assert.Contains("MATCH (n:Entity {uuid: $uuid})", byUuid[1].Query, StringComparison.Ordinal);
        Assert.Contains("DETACH DELETE n", byUuid[1].Query, StringComparison.Ordinal);
        Assert.Equal("entity-1", byUuid[0].Parameters["uuid"]);

        Assert.Contains(
            "MATCH (n:Entity {group_id: $group_id})-[:RELATES_TO]->(r:RelatesToNode_)",
            byGroup[0].Query,
            StringComparison.Ordinal);
        Assert.Equal("tenant", byGroup[0].Parameters["group_id"]);

        Assert.Contains("WHERE n.uuid IN $uuids", byUuids[0].Query, StringComparison.Ordinal);
        Assert.Contains("MATCH (n)-[:RELATES_TO]->(r:RelatesToNode_)", byUuids[0].Query, StringComparison.Ordinal);
        Assert.Equal(new[] { "entity-1", "entity-2" }, Assert.IsType<List<string>>(byUuids[0].Parameters["uuids"]));
    }

    [Fact]
    public void BuildNodeGetAndEmbeddingLoad_PinsKuzuEmbeddingProjectionSplit()
    {
        var defaultGet = LadybugStatementBuilder.BuildNodeGetByUuid<EntityNode>("entity-1");
        var embeddingGet = LadybugStatementBuilder.BuildNodeGetByUuid<EntityNode>("entity-1", withEmbeddings: true);
        var loadOne = LadybugStatementBuilder.BuildNodeLoadEmbedding<EntityNode>("entity-1");
        var loadMany = LadybugStatementBuilder.BuildNodesLoadEmbeddings<EntityNode>(["entity-1", "entity-2"]);

        Assert.Contains("MATCH (n:Entity {uuid: $uuid})", defaultGet.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("name_embedding AS name_embedding", defaultGet.Query, StringComparison.Ordinal);
        Assert.Contains("n.attributes AS attributes", defaultGet.Query, StringComparison.Ordinal);
        Assert.Contains("n.name_embedding AS name_embedding", embeddingGet.Query, StringComparison.Ordinal);
        Assert.Contains("RETURN n.name_embedding AS name_embedding", loadOne.Query, StringComparison.Ordinal);
        Assert.Contains("RETURN DISTINCT n.uuid AS uuid, n.name_embedding AS name_embedding", loadMany.Query, StringComparison.Ordinal);
        Assert.Equal(new[] { "entity-1", "entity-2" }, Assert.IsType<List<string>>(loadMany.Parameters["uuids"]));
    }

    [Fact]
    public void BuildEntityEdgeGetDeleteAndEmbeddingLoad_UseRelatesToNode()
    {
        var get = LadybugStatementBuilder.BuildEdgeGetByUuid<EntityEdge>("edge-1");
        var getWithEmbedding = LadybugStatementBuilder.BuildEdgeGetByUuid<EntityEdge>("edge-1", withEmbeddings: true);
        var delete = LadybugStatementBuilder.BuildEdgeDeleteByUuid<EntityEdge>("edge-1");
        var byUuids = LadybugStatementBuilder.BuildEdgesGetByUuids<EntityEdge>(["edge-1", "edge-2"]);
        var byGroup = LadybugStatementBuilder.BuildEdgesGetByGroupIds<EntityEdge>(["tenant"], limit: 10, uuidCursor: "edge-9");
        var between = LadybugStatementBuilder.BuildEntityEdgesBetweenNodesGet("source-1", "target-1");
        var byNode = LadybugStatementBuilder.BuildEntityEdgesByNodeUuidGet("source-1");
        var loadOne = LadybugStatementBuilder.BuildEntityEdgeLoadEmbedding("edge-1");
        var loadMany = LadybugStatementBuilder.BuildEntityEdgesLoadEmbeddings(["edge-1", "edge-2"]);

        Assert.Contains(
            "MATCH (n:Entity)-[:RELATES_TO]->(e:RelatesToNode_ {uuid: $uuid})-[:RELATES_TO]->(m:Entity)",
            get.Query,
            StringComparison.Ordinal);
        Assert.DoesNotContain("fact_embedding AS fact_embedding", get.Query, StringComparison.Ordinal);
        Assert.Contains("e.reference_time AS reference_time", get.Query, StringComparison.Ordinal);
        Assert.Contains("e.fact_embedding AS fact_embedding", getWithEmbedding.Query, StringComparison.Ordinal);
        Assert.Contains("DETACH DELETE e", delete.Query, StringComparison.Ordinal);
        Assert.Contains("WHERE e.uuid IN $uuids", byUuids.Query, StringComparison.Ordinal);
        Assert.Contains("WHERE e.group_id IN $group_ids", byGroup.Query, StringComparison.Ordinal);
        Assert.Contains("AND e.uuid < $uuid", byGroup.Query, StringComparison.Ordinal);
        Assert.Contains("LIMIT $limit", byGroup.Query, StringComparison.Ordinal);
        Assert.Contains("m:Entity {uuid: $target_node_uuid}", between.Query, StringComparison.Ordinal);
        Assert.Contains("n:Entity {uuid: $node_uuid}", byNode.Query, StringComparison.Ordinal);
        Assert.Contains("RETURN e.fact_embedding AS fact_embedding", loadOne.Query, StringComparison.Ordinal);
        Assert.Contains("WHERE e.uuid IN $edge_uuids", loadMany.Query, StringComparison.Ordinal);
        Assert.Equal(new[] { "edge-1", "edge-2" }, Assert.IsType<List<string>>(loadMany.Parameters["edge_uuids"]));
    }

    [Fact]
    public void BuildSimpleEdgeGetAndDelete_UseDirectRelationships()
    {
        var mentionGet = LadybugStatementBuilder.BuildEdgeGetByUuid<EpisodicEdge>("mention-1");
        var communityGet = LadybugStatementBuilder.BuildEdgeGetByUuid<CommunityEdge>("member-1");
        var hasEpisodeGet = LadybugStatementBuilder.BuildEdgeGetByUuid<HasEpisodeEdge>("has-episode-1");
        var nextEpisodeGet = LadybugStatementBuilder.BuildEdgeGetByUuid<NextEpisodeEdge>("next-1");
        var mentionDelete = LadybugStatementBuilder.BuildEdgeDeleteByUuid<EpisodicEdge>("mention-1");

        Assert.Contains("MATCH (n:Episodic)-[e:MENTIONS {uuid: $uuid}]->(m:Entity)", mentionGet.Query, StringComparison.Ordinal);
        Assert.Contains("MATCH (n:Community)-[e:HAS_MEMBER {uuid: $uuid}]->(m)", communityGet.Query, StringComparison.Ordinal);
        Assert.Contains("MATCH (n:Saga)-[e:HAS_EPISODE {uuid: $uuid}]->(m:Episodic)", hasEpisodeGet.Query, StringComparison.Ordinal);
        Assert.Contains("MATCH (n:Episodic)-[e:NEXT_EPISODE {uuid: $uuid}]->(m:Episodic)", nextEpisodeGet.Query, StringComparison.Ordinal);
        Assert.Contains("DELETE e", mentionDelete.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("DETACH DELETE", mentionDelete.Query, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRetrieveEpisodes_PortsKuzuReferenceTimeGroupSourceAndSagaFilters()
    {
        var referenceTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var direct = LadybugStatementBuilder.BuildRetrieveEpisodes(
            referenceTime,
            3,
            ["tenant"],
            EpisodeType.Message);
        var saga = LadybugStatementBuilder.BuildRetrieveEpisodes(
            referenceTime,
            2,
            ["tenant"],
            EpisodeType.Json,
            saga: "checkout");

        Assert.Contains("MATCH (e:Episodic)", direct.Query, StringComparison.Ordinal);
        Assert.Contains("e.valid_at <= $reference_time", direct.Query, StringComparison.Ordinal);
        Assert.Contains("AND e.group_id IN $group_ids", direct.Query, StringComparison.Ordinal);
        Assert.Contains("AND e.source = $source", direct.Query, StringComparison.Ordinal);
        Assert.Equal(referenceTime, direct.Parameters["reference_time"]);
        Assert.Equal("message", direct.Parameters["source"]);

        Assert.Contains(
            "MATCH (s:Saga {name: $saga_name, group_id: $group_id})-[:HAS_EPISODE]->(e:Episodic)",
            saga.Query,
            StringComparison.Ordinal);
        Assert.Equal("checkout", saga.Parameters["saga_name"]);
        Assert.Equal("tenant", saga.Parameters["group_id"]);
        Assert.Equal("json", saga.Parameters["source"]);
        Assert.Equal(2, saga.Parameters["num_episodes"]);
    }

    [Fact]
    public void RecordMapper_DeserializesAttributesLikePythonKuzuParser()
    {
        var entity = LadybugRecordMapper.MapEntityNode(new Dictionary<string, object?>
        {
            ["uuid"] = "entity-1",
            ["name"] = "Alice",
            ["group_id"] = "tenant",
            ["labels"] = new[] { "Entity", "Person" },
            ["created_at"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ["summary"] = "summary",
            ["attributes"] = """{"age":42,"role":"engineer"}"""
        });
        var referenceTime = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        var edge = LadybugRecordMapper.MapEntityEdge(new Dictionary<string, object?>
        {
            ["uuid"] = "edge-1",
            ["source_node_uuid"] = "source-1",
            ["target_node_uuid"] = "target-1",
            ["group_id"] = "tenant",
            ["name"] = "KNOWS",
            ["fact"] = "Alice knows Bob",
            ["episodes"] = new[] { "episode-1" },
            ["created_at"] = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            ["reference_time"] = referenceTime,
            ["attributes"] = """{"confidence":0.9}"""
        });

        Assert.Equal("Alice", entity.Name);
        Assert.Equal(new[] { "Entity", "Person" }, entity.Labels);
        Assert.Equal(42, Assert.IsType<JsonElement>(entity.Attributes["age"]).GetInt32());
        Assert.Equal("engineer", Assert.IsType<JsonElement>(entity.Attributes["role"]).GetString());
        Assert.Equal("source-1", edge.SourceNodeUuid);
        Assert.Equal("target-1", edge.TargetNodeUuid);
        Assert.Equal(referenceTime, edge.ReferenceTime);
        Assert.Equal(0.9, Assert.IsType<JsonElement>(edge.Attributes["confidence"]).GetDouble());
        Assert.Empty(LadybugRecordMapper.ParseAttributes(null));
        Assert.Empty(LadybugRecordMapper.ParseAttributes("{not-json"));
        Assert.Empty(LadybugRecordMapper.ParseAttributes(""));
    }

    [Fact]
    public void RecordMapper_HandlesJsonNodeAttributesAndArrayValues()
    {
        var jsonAttributes = new JsonObject
        {
            ["nested"] = new JsonObject { ["value"] = 1 },
            ["text"] = "original"
        };
        var parsedAttributes = LadybugRecordMapper.ParseAttributes(jsonAttributes);
        jsonAttributes["nested"]!["value"] = 2;
        jsonAttributes["text"] = "mutated";

        var nested = Assert.IsType<JsonObject>(parsedAttributes["nested"]);
        Assert.Equal(1, nested["value"]!.GetValue<int>());
        Assert.Equal("original", Assert.IsAssignableFrom<JsonValue>(parsedAttributes["text"]).GetValue<string>());
        Assert.Same(StringComparer.Ordinal, parsedAttributes.Comparer);

        var episode = LadybugRecordMapper.MapEpisodicNode(new Dictionary<string, object?>
        {
            ["uuid"] = "episode-1",
            ["source"] = "message",
            ["entity_edges"] = new JsonArray("edge-1", null, "edge-2")
        });
        var communityFromJsonArray = LadybugRecordMapper.MapCommunityNode(new Dictionary<string, object?>
        {
            ["uuid"] = "community-1",
            ["name_embedding"] = new JsonArray(0.1, null, 0.3)
        });
        var element = JsonSerializer.Deserialize<JsonElement>("[\"Entity\",\"Person\"]");
        var entity = LadybugRecordMapper.MapEntityNode(new Dictionary<string, object?>
        {
            ["uuid"] = "entity-1",
            ["labels"] = element,
            ["name_embedding"] = new object[] { 1, "2.5" },
            ["attributes"] = new Dictionary<string, object> { ["role"] = "engineer" }
        });

        Assert.Equal(new[] { "edge-1", "edge-2" }, episode.EntityEdges);
        Assert.Equal(new[] { 0.1f, 0f, 0.3f }, communityFromJsonArray.NameEmbedding);
        Assert.Equal(new[] { "Entity", "Person" }, entity.Labels);
        Assert.Equal(new[] { 1f, 2.5f }, entity.NameEmbedding);
        Assert.Equal("engineer", entity.Attributes["role"]);
        Assert.Same(StringComparer.Ordinal, entity.Attributes.Comparer);

        var marker = new object();
        IReadOnlyDictionary<string, object?> nullableAttributes = new Dictionary<string, object?>
        {
            ["marker"] = marker
        };
        var copiedNullableAttributes = LadybugRecordMapper.ParseAttributes(nullableAttributes);

        Assert.Same(StringComparer.Ordinal, copiedNullableAttributes.Comparer);
        Assert.Same(marker, copiedNullableAttributes["marker"]);
        Assert.Empty(LadybugRecordMapper.ParseAttributes("null"));
    }

    [Fact]
    public void RecordMapper_MapsRemainingNodeAndSimpleEdgeProjectionTypes()
    {
        var createdAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var validAt = new DateTime(2026, 1, 3, 3, 4, 5, DateTimeKind.Utc);
        var episode = LadybugRecordMapper.MapEpisodicNode(new Dictionary<string, object?>
        {
            ["uuid"] = "episode-1",
            ["name"] = "episode",
            ["group_id"] = "tenant",
            ["created_at"] = createdAt,
            ["source"] = "json",
            ["source_description"] = "api",
            ["content"] = "{}",
            ["valid_at"] = validAt,
            ["entity_edges"] = new[] { "edge-1" }
        });
        var community = LadybugRecordMapper.MapCommunityNode(new Dictionary<string, object?>
        {
            ["uuid"] = "community-1",
            ["name"] = "community",
            ["group_id"] = "tenant",
            ["created_at"] = createdAt,
            ["summary"] = "summary",
            ["name_embedding"] = new[] { 0.1f, 0.2f }
        });
        var saga = LadybugRecordMapper.MapSagaNode(new Dictionary<string, object?>
        {
            ["uuid"] = "saga-1",
            ["name"] = "checkout",
            ["group_id"] = "tenant",
            ["created_at"] = createdAt,
            ["summary"] = "saga summary",
            ["first_episode_uuid"] = "episode-1",
            ["last_episode_uuid"] = "episode-2",
            ["last_summarized_at"] = createdAt.AddHours(2),
            ["last_summarized_episode_valid_at"] = validAt
        });
        var mention = LadybugRecordMapper.MapEpisodicEdge(SimpleEdgeRecord("mention-1", createdAt));
        var membership = LadybugRecordMapper.MapCommunityEdge(SimpleEdgeRecord("member-1", createdAt));
        var hasEpisode = LadybugRecordMapper.MapHasEpisodeEdge(SimpleEdgeRecord("has-1", createdAt));
        var nextEpisode = LadybugRecordMapper.MapNextEpisodeEdge(SimpleEdgeRecord("next-1", createdAt));

        Assert.Equal(EpisodeType.Json, episode.Source);
        Assert.Equal(validAt, episode.ValidAt);
        Assert.Equal(new[] { "edge-1" }, episode.EntityEdges);
        Assert.Equal(new[] { 0.1f, 0.2f }, community.NameEmbedding);
        Assert.Equal("summary", community.Summary);
        Assert.Equal("checkout", saga.Name);
        Assert.Equal("saga summary", saga.Summary);
        Assert.Equal("episode-1", saga.FirstEpisodeUuid);
        Assert.Equal("episode-2", saga.LastEpisodeUuid);
        Assert.Equal(createdAt.AddHours(2), saga.LastSummarizedAt);
        Assert.Equal(validAt, saga.LastSummarizedEpisodeValidAt);
        Assert.IsType<EpisodicEdge>(mention);
        Assert.IsType<CommunityEdge>(membership);
        Assert.IsType<HasEpisodeEdge>(hasEpisode);
        Assert.IsType<NextEpisodeEdge>(nextEpisode);
        Assert.Equal("source-1", nextEpisode.SourceNodeUuid);
        Assert.Equal("target-1", nextEpisode.TargetNodeUuid);
        Assert.Equal(createdAt, nextEpisode.CreatedAt);
    }

    private static Dictionary<string, object?> SimpleEdgeRecord(string uuid, DateTime createdAt) =>
        new(StringComparer.Ordinal)
        {
            ["uuid"] = uuid,
            ["group_id"] = "tenant",
            ["source_node_uuid"] = "source-1",
            ["target_node_uuid"] = "target-1",
            ["created_at"] = createdAt
        };

    private sealed class IndexOnlyReadOnlyList<T> : IReadOnlyList<T>
    {
        private readonly T[] _values;

        internal IndexOnlyReadOnlyList(params T[] values) => _values = values;

        public int Count => _values.Length;

        public T this[int index] => _values[index];

        public IEnumerator<T> GetEnumerator() =>
            throw new InvalidOperationException("Statement builders should copy IReadOnlyList values by index.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
