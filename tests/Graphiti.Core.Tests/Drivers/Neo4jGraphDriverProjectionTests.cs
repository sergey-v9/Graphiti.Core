using Graphiti.Core;

namespace Graphiti.Core.Tests.Drivers;

public class Neo4jGraphDriverProjectionTests
{
    [Fact]
    public void BuildNodeGroupReturnClause_DropsEntityEmbeddingWhenNotRequested()
    {
        var clause = Neo4jStatementBuilder.BuildNodeGroupReturnClause<EntityNode>(withEmbeddings: false);

        Assert.Equal("RETURN n {.*, name_embedding: null, labels: labels(n)} AS n", clause);
    }

    [Fact]
    public void BuildNodeGroupReturnClause_KeepsNativeNodeWhenEmbeddingsAreRequested()
    {
        var clause = Neo4jStatementBuilder.BuildNodeGroupReturnClause<EntityNode>(withEmbeddings: true);

        Assert.Equal("RETURN n", clause);
    }

    [Fact]
    public void BuildNodeGroupReturnClause_KeepsNonEntityNodesUnprojected()
    {
        var clause = Neo4jStatementBuilder.BuildNodeGroupReturnClause<CommunityNode>(withEmbeddings: false);

        Assert.Equal("RETURN n", clause);
    }

    [Fact]
    public void BuildEdgeReturnClause_DropsEntityFactEmbeddingWhenNotRequested()
    {
        var clause = Neo4jStatementBuilder.BuildEdgeReturnClause<EntityEdge>(withEmbeddings: false);

        Assert.Equal(
            "RETURN e {.*, fact_embedding: null} AS e, source.uuid AS source_uuid, target.uuid AS target_uuid",
            clause);
    }

    [Fact]
    public void BuildEdgeReturnClause_KeepsNativeRelationshipWhenEmbeddingsAreRequested()
    {
        var clause = Neo4jStatementBuilder.BuildEdgeReturnClause<EntityEdge>(withEmbeddings: true);

        Assert.Equal("RETURN e, source.uuid AS source_uuid, target.uuid AS target_uuid", clause);
    }

    [Fact]
    public void BuildEdgeReturnClause_KeepsNonEntityEdgesUnprojected()
    {
        var clause = Neo4jStatementBuilder.BuildEdgeReturnClause<EpisodicEdge>(withEmbeddings: false);

        Assert.Equal("RETURN e, source.uuid AS source_uuid, target.uuid AS target_uuid", clause);
    }

    [Fact]
    public void BuildEdgeMatchPattern_CommunityEdgesAllowCommunityTargets()
    {
        var pattern = Neo4jStatementBuilder.BuildEdgeMatchPattern<CommunityEdge>();

        Assert.Equal("MATCH (source:Community)-[e:HAS_MEMBER]->(target:Entity|Community)", pattern);
    }

    [Fact]
    public void LabelFor_ReturnsExpectedNeo4jLabels()
    {
        Assert.Equal("Entity", Neo4jStatementBuilder.LabelFor<EntityNode>());
        Assert.Equal("Episodic", Neo4jStatementBuilder.LabelFor<EpisodicNode>());
        Assert.Equal("Community", Neo4jStatementBuilder.LabelFor<CommunityNode>());
        Assert.Equal("Saga", Neo4jStatementBuilder.LabelFor<SagaNode>());
    }

    [Fact]
    public void BuildEdgeMatchPattern_ReturnsExpectedRelationshipShapes()
    {
        Assert.Equal(
            "MATCH (source:Entity)-[e:RELATES_TO]->(target:Entity)",
            Neo4jStatementBuilder.BuildEdgeMatchPattern<EntityEdge>());
        Assert.Equal(
            "MATCH (source:Episodic)-[e:MENTIONS]->(target:Entity)",
            Neo4jStatementBuilder.BuildEdgeMatchPattern<EpisodicEdge>());
        Assert.Equal(
            "MATCH (source:Saga)-[e:HAS_EPISODE]->(target:Episodic)",
            Neo4jStatementBuilder.BuildEdgeMatchPattern<HasEpisodeEdge>());
        Assert.Equal(
            "MATCH (source:Episodic)-[e:NEXT_EPISODE]->(target:Episodic)",
            Neo4jStatementBuilder.BuildEdgeMatchPattern<NextEpisodeEdge>());
    }

    [Fact]
    public void BuildRetrieveEpisodesStatement_ScopesSagaLookupToFirstGroupId()
    {
        var referenceTime = new DateTime(2026, 5, 27, 12, 30, 0, DateTimeKind.Utc);
        var statement = Neo4jStatementBuilder.BuildRetrieveEpisodesStatement(
            referenceTime,
            lastN: 4,
            groupIds: new[] { "tenant-a", "tenant-b" },
            source: EpisodeType.Message,
            saga: "onboarding");

        Assert.Contains(
            "MATCH (s:Saga {name: $saga, group_id: $saga_group_id})-[:HAS_EPISODE]->(e:Episodic)",
            statement.Query,
            StringComparison.Ordinal);
        Assert.Contains("AND e.group_id IN $group_ids", statement.Query, StringComparison.Ordinal);
        Assert.Contains("AND e.source = $source", statement.Query, StringComparison.Ordinal);
        Assert.Equal("tenant-a", statement.Parameters["saga_group_id"]);
        Assert.Equal(new[] { "tenant-a", "tenant-b" }, Assert.IsType<List<string>>(statement.Parameters["group_ids"]));
        Assert.Equal("message", statement.Parameters["source"]);
        Assert.Equal("onboarding", statement.Parameters["saga"]);
        Assert.Equal(4, statement.Parameters["limit"]);
        Assert.Equal(referenceTime, statement.Parameters["reference_time"]);
    }

    [Fact]
    public void BuildRetrieveEpisodesStatement_UsesNameOnlySagaLookupWithoutGroupIds()
    {
        var statement = Neo4jStatementBuilder.BuildRetrieveEpisodesStatement(
            DateTime.UnixEpoch,
            lastN: 3,
            saga: "onboarding");

        Assert.Contains(
            "MATCH (s:Saga {name: $saga})-[:HAS_EPISODE]->(e:Episodic)",
            statement.Query,
            StringComparison.Ordinal);
        Assert.DoesNotContain("group_id: $saga_group_id", statement.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("AND e.group_id IN $group_ids", statement.Query, StringComparison.Ordinal);
        Assert.Null(statement.Parameters["saga_group_id"]);
    }

    [Fact]
    public void MapNode_UsesDeterministicTimestampForMissingDates()
    {
        var node = Assert.IsType<EpisodicNode>(Neo4jRecordMapper.MapNode(
            new Dictionary<string, object>
            {
                ["uuid"] = "episode-1",
                ["name"] = "episode",
                ["group_id"] = "tenant",
                ["source"] = "message",
                ["source_description"] = "message",
                ["content"] = "Alice likes Bob"
            },
            new[] { "Episodic" }));

        Assert.Equal(GraphitiHelpers.DefaultTimestamp, node.CreatedAt);
        Assert.Equal(GraphitiHelpers.DefaultTimestamp, node.ValidAt);
    }

    [Fact]
    public void MapEdge_UsesDeterministicTimestampForMissingCreatedAt()
    {
        var edge = Assert.IsType<EntityEdge>(Neo4jRecordMapper.MapEdge(
            new Dictionary<string, object>
            {
                ["uuid"] = "edge-1",
                ["group_id"] = "tenant",
                ["name"] = "RELATES_TO",
                ["fact"] = "Alice likes Bob"
            },
            "source-1",
            "target-1",
            typeof(EntityEdge)));

        Assert.Equal(GraphitiHelpers.DefaultTimestamp, edge.CreatedAt);
    }

    [Fact]
    public void MapEntityNode_PreservesOnlyCustomAttributes()
    {
        var node = Assert.IsType<EntityNode>(Neo4jRecordMapper.MapNode(
            new Dictionary<string, object>
            {
                ["uuid"] = "node-1",
                ["name"] = "Alice",
                ["group_id"] = "tenant",
                ["created_at"] = DateTime.UnixEpoch,
                ["summary"] = "summary",
                ["labels"] = new[] { "Entity", "Person" },
                ["name_embedding"] = new[] { 0.1f, 0.2f },
                ["role"] = "engineer"
            },
            new[] { "Entity", "Person" }));

        Assert.DoesNotContain("summary", node.Attributes.Keys);
        Assert.DoesNotContain("name_embedding", node.Attributes.Keys);
        Assert.Equal("engineer", node.Attributes["role"]);
    }

    [Fact]
    public void MapEntityEdge_PreservesOnlyCustomAttributes()
    {
        var edge = Assert.IsType<EntityEdge>(Neo4jRecordMapper.MapEdge(
            new Dictionary<string, object>
            {
                ["uuid"] = "edge-1",
                ["group_id"] = "tenant",
                ["created_at"] = DateTime.UnixEpoch,
                ["name"] = "RELATES_TO",
                ["fact"] = "Alice likes Bob",
                ["fact_embedding"] = new[] { 0.3f, 0.4f },
                ["confidence"] = 0.9
            },
            "source-1",
            "target-1",
            typeof(EntityEdge)));

        Assert.DoesNotContain("fact", edge.Attributes.Keys);
        Assert.DoesNotContain("fact_embedding", edge.Attributes.Keys);
        Assert.Equal(0.9, edge.Attributes["confidence"]);
    }
}
