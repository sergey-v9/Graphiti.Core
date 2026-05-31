using Graphiti.Core;

namespace Graphiti.Core.Tests.Drivers;

public class Neo4jGraphDriverBfsTests
{
    [Fact]
    public void BuildNodeBfsSearchQuery_DeduplicatesByShortestDepthAndReturnsDepthScore()
    {
        var query = Neo4jStatementBuilder.BuildNodeBfsSearchQuery(3, " AND n.group_id IN $group_ids");

        Assert.Contains("[:RELATES_TO|MENTIONS*1..3]", query);
        Assert.Contains("WITH n, min(length(path)) AS depth", query);
        Assert.Contains("RETURN n, 1.0 / depth AS score", query);
        Assert.Contains("ORDER BY depth ASC, n.uuid ASC", query);
        Assert.DoesNotContain("RETURN n\r", query);
        Assert.DoesNotContain("RETURN n\n", query);
    }

    [Fact]
    public void BuildEdgeBfsSearchQuery_PreservesRelationshipDirectionAndReturnsShortestDepthScore()
    {
        var query = Neo4jStatementBuilder.BuildEdgeBfsSearchQuery(
            2,
            "WHERE origin.group_id IN $group_ids",
            "WHERE e.group_id IN $group_ids");

        Assert.Contains("[:RELATES_TO|MENTIONS*1..2]", query);
        Assert.Contains("WHERE origin.group_id IN $group_ids", query);
        Assert.Contains("WITH rel, length(path) AS depth", query);
        Assert.Contains("MATCH (n:Entity)-[e:RELATES_TO {uuid: rel.uuid}]->(m:Entity)", query);
        Assert.DoesNotContain("-[e:RELATES_TO {uuid: rel.uuid}]-(m:Entity)", query);
        Assert.Contains("WITH e, n, m, min(depth) AS depth", query);
        Assert.Contains(
            "RETURN e, n.uuid AS source_uuid, m.uuid AS target_uuid, 1.0 / depth AS score",
            query);
        Assert.Contains("ORDER BY depth ASC, e.uuid ASC", query);
    }
}
