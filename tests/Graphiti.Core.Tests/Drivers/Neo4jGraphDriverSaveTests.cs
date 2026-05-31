using Graphiti.Core;

namespace Graphiti.Core.Tests.Drivers;

public class Neo4jGraphDriverSaveTests
{
    [Fact]
    public void BuildEntityNodeSave_ReplacesPropertiesAndPreservesLabels()
    {
        var node = new EntityNode
        {
            Uuid = "node-1",
            Name = "Alice",
            GroupId = "group",
            Labels = new List<string> { "Person" },
            Summary = "summary",
            NameEmbedding = new List<float> { 0.1f, 0.2f },
            Attributes = new Dictionary<string, object?>
            {
                ["role"] = "engineer",
                ["summary"] = "stale override"
            }
        };

        var statement = Neo4jStatementBuilder.BuildEntityNodeSave(node);
        var props = Assert.IsType<Dictionary<string, object?>>(statement.Parameters["props"]);

        Assert.Contains("MERGE (n:Entity {uuid: $props.uuid})", statement.Query, StringComparison.Ordinal);
        Assert.Contains("SET n:Person:Entity", statement.Query, StringComparison.Ordinal);
        Assert.Contains("SET n = $props", statement.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("SET n += $props", statement.Query, StringComparison.Ordinal);
        Assert.Equal("engineer", props["role"]);
        Assert.Equal("summary", props["summary"]);
        Assert.Equal(new List<float> { 0.1f, 0.2f }, Assert.IsType<List<float>>(props["name_embedding"]));
    }

    [Fact]
    public void BuildEntityEdgeSave_ReplacesPropertiesAndPreservesEndpoints()
    {
        var edge = new EntityEdge
        {
            Uuid = "edge-1",
            SourceNodeUuid = "source-1",
            TargetNodeUuid = "target-1",
            GroupId = "group",
            Name = "KNOWS",
            Fact = "Alice knows Bob.",
            FactEmbedding = new List<float> { 0.3f, 0.4f },
            Episodes = new List<string> { "episode-1" },
            ReferenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Attributes = new Dictionary<string, object?>
            {
                ["confidence"] = 0.9,
                ["fact"] = "stale override"
            }
        };

        var statement = Neo4jStatementBuilder.BuildEntityEdgeSave(edge);
        var props = Assert.IsType<Dictionary<string, object?>>(statement.Parameters["props"]);

        Assert.Contains("MATCH (source:Entity {uuid: $source_uuid})", statement.Query, StringComparison.Ordinal);
        Assert.Contains("MATCH (target:Entity {uuid: $target_uuid})", statement.Query, StringComparison.Ordinal);
        Assert.Contains("SET e = $props", statement.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("SET e += $props", statement.Query, StringComparison.Ordinal);
        Assert.Equal(edge.SourceNodeUuid, statement.Parameters["source_uuid"]);
        Assert.Equal(edge.TargetNodeUuid, statement.Parameters["target_uuid"]);
        Assert.Equal("Alice knows Bob.", props["fact"]);
        Assert.Equal(0.9, props["confidence"]);
        Assert.Equal(new List<float> { 0.3f, 0.4f }, Assert.IsType<List<float>>(props["fact_embedding"]));
    }

    [Fact]
    public void BuildCommunityEdgeSave_AllowsEntityOrCommunityTargets()
    {
        var edge = new CommunityEdge
        {
            Uuid = "community-edge-1",
            SourceNodeUuid = "community-1",
            TargetNodeUuid = "community-2",
            GroupId = "group",
            CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        var statement = Neo4jStatementBuilder.BuildCommunityEdgeSave(edge);
        var props = Assert.IsType<Dictionary<string, object?>>(statement.Parameters["props"]);

        Assert.Contains("MATCH (source:Community {uuid: $source_uuid})", statement.Query, StringComparison.Ordinal);
        Assert.Contains("MATCH (target:Entity|Community {uuid: $target_uuid})", statement.Query, StringComparison.Ordinal);
        Assert.Contains("MERGE (source)-[e:HAS_MEMBER", statement.Query, StringComparison.Ordinal);
        Assert.Contains("SET e = $props", statement.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("target:Entity {uuid: $target_uuid}", statement.Query, StringComparison.Ordinal);
        Assert.Equal(edge.SourceNodeUuid, statement.Parameters["source_uuid"]);
        Assert.Equal(edge.TargetNodeUuid, statement.Parameters["target_uuid"]);
        Assert.Equal(edge.Uuid, props["uuid"]);
        Assert.Equal(edge.GroupId, props["group_id"]);
    }
}
