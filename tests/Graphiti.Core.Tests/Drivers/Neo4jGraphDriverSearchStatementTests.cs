namespace Graphiti.Core.Tests.Drivers;

public class Neo4jGraphDriverSearchStatementTests
{
    [Fact]
    public void BuildEntityNodeFulltextSearchStatement_IncludesCompiledFiltersAndGroupScope()
    {
        var statement = Neo4jStatementBuilder.BuildEntityNodeFulltextSearchStatement(
            "alice",
            new SearchFilters
            {
                NodeLabels = new List<string> { "Person" },
                PropertyFilters = new List<PropertyFilter>
                {
                    new("status", ComparisonOperator.Equals, "active")
                }
            },
            new[] { "tenant-a", "tenant-b" },
            limit: 7,
            GraphProvider.Neo4j);

        Assert.Contains(
            "CALL db.index.fulltext.queryNodes(\"node_name_and_summary\", $query, {limit: $limit})",
            statement.Query);
        Assert.Contains(
            "WHERE n:Person AND (n[$node_property_name_0] = $node_property_value_0) AND n.group_id IN $group_ids",
            statement.Query);
        Assert.Equal("alice", statement.Parameters["query"]);
        Assert.Equal(7, statement.Parameters["limit"]);
        Assert.Equal("status", statement.Parameters["node_property_name_0"]);
        Assert.Equal("active", statement.Parameters["node_property_value_0"]);
        Assert.Equal(
            new[] { "tenant-a", "tenant-b" },
            Assert.IsType<List<string>>(statement.Parameters["group_ids"]));
    }

    [Fact]
    public void BuildEntityEdgeEmbeddingSearchStatement_IncludesEndpointAndFilterParameters()
    {
        var statement = Neo4jStatementBuilder.BuildEntityEdgeEmbeddingSearchStatement(
            new[] { 0.1f, 0.2f },
            new SearchFilters
            {
                EdgeTypes = new List<string> { "KNOWS" },
                NodeLabels = new List<string> { "Person" }
            },
            new[] { "tenant" },
            limit: 3,
            minScore: 0.42f,
            GraphProvider.Neo4j,
            sourceNodeUuid: "source-1",
            targetNodeUuid: "target-1");

        Assert.Contains("MATCH (n:Entity)-[e:RELATES_TO]->(m:Entity)", statement.Query);
        Assert.Contains("e.name in $edge_types", statement.Query);
        Assert.Contains("n:Person AND m:Person", statement.Query);
        Assert.Contains("e.fact_embedding IS NOT NULL", statement.Query);
        Assert.Contains("e.group_id IN $group_ids", statement.Query);
        Assert.Contains("n.uuid = $source_uuid", statement.Query);
        Assert.Contains("m.uuid = $target_uuid", statement.Query);
        Assert.Equal(new[] { 0.1f, 0.2f }, Assert.IsType<List<float>>(statement.Parameters["search_vector"]));
        Assert.Equal(new[] { "KNOWS" }, statement.Parameters["edge_types"]);
        Assert.Equal(new[] { "tenant" }, Assert.IsType<List<string>>(statement.Parameters["group_ids"]));
        Assert.Equal("source-1", statement.Parameters["source_uuid"]);
        Assert.Equal("target-1", statement.Parameters["target_uuid"]);
        Assert.Equal(3, statement.Parameters["limit"]);
        Assert.Equal(0.42f, statement.Parameters["min_score"]);
    }

    [Fact]
    public void BuildBfsSearchStatements_MoveFilterAndParameterAssemblyIntoBuilder()
    {
        var nodeStatement = Neo4jStatementBuilder.BuildEntityNodeBfsSearchStatement(
            new[] { "origin-1" },
            new SearchFilters { NodeLabels = new List<string> { "Person" } },
            maxDepth: 2,
            new[] { "tenant" },
            limit: 5,
            GraphProvider.Neo4j);

        Assert.Contains("[:RELATES_TO|MENTIONS*1..2]", nodeStatement.Query);
        Assert.Contains(" AND n:Person AND n.group_id IN $group_ids AND origin.group_id IN $group_ids", nodeStatement.Query);
        Assert.Equal(new[] { "origin-1" }, Assert.IsType<List<string>>(nodeStatement.Parameters["bfs_origin_node_uuids"]));
        Assert.Equal(new[] { "tenant" }, Assert.IsType<List<string>>(nodeStatement.Parameters["group_ids"]));
        Assert.Equal(5, nodeStatement.Parameters["limit"]);

        var edgeStatement = Neo4jStatementBuilder.BuildEntityEdgeBfsSearchStatement(
            new[] { "origin-1" },
            new SearchFilters { EdgeUuids = new List<string> { "edge-1" } },
            maxDepth: 3,
            new[] { "tenant" },
            limit: 6,
            GraphProvider.Neo4j);

        Assert.Contains("[:RELATES_TO|MENTIONS*1..3]", edgeStatement.Query);
        Assert.Contains("WHERE origin.group_id IN $group_ids", edgeStatement.Query);
        Assert.Contains("WHERE e.uuid in $edge_uuids AND e.group_id IN $group_ids", edgeStatement.Query);
        Assert.Equal(new[] { "edge-1" }, edgeStatement.Parameters["edge_uuids"]);
        Assert.Equal(new[] { "origin-1" }, Assert.IsType<List<string>>(edgeStatement.Parameters["bfs_origin_node_uuids"]));
        Assert.Equal(new[] { "tenant" }, Assert.IsType<List<string>>(edgeStatement.Parameters["group_ids"]));
        Assert.Equal(6, edgeStatement.Parameters["limit"]);
    }

    [Fact]
    public void BuildEpisodeAndCommunitySearchStatements_PreserveIndexesAndGroupFilters()
    {
        var episode = Neo4jStatementBuilder.BuildEpisodeFulltextSearchStatement(
            "episode",
            new[] { "tenant" },
            limit: 4);

        Assert.Contains("queryNodes(\"episode_content\"", episode.Query);
        Assert.Contains("AND e.group_id IN $group_ids", episode.Query);
        Assert.Equal("episode", episode.Parameters["query"]);
        Assert.Equal(new[] { "tenant" }, Assert.IsType<List<string>>(episode.Parameters["group_ids"]));

        var community = Neo4jStatementBuilder.BuildCommunityFulltextSearchStatement(
            "community",
            new[] { "tenant" },
            limit: 8);

        Assert.Contains("queryNodes(\"community_name\"", community.Query);
        Assert.Contains("WHERE c.group_id IN $group_ids", community.Query);
        Assert.Equal("community", community.Parameters["query"]);
        Assert.Equal(8, community.Parameters["limit"]);

        var vector = Neo4jStatementBuilder.BuildCommunityEmbeddingSearchStatement(
            new[] { 0.3f, 0.4f },
            new[] { "tenant" },
            limit: 9,
            minScore: 0.5f);

        Assert.Contains("MATCH (c:Community)", vector.Query);
        Assert.Contains("WHERE c.name_embedding IS NOT NULL AND c.group_id IN $group_ids", vector.Query);
        Assert.Equal(new[] { 0.3f, 0.4f }, Assert.IsType<List<float>>(vector.Parameters["search_vector"]));
        Assert.Equal(0.5f, vector.Parameters["min_score"]);
    }

    [Fact]
    public void BuildRankStatements_PreserveRankerQueriesAndParameterNames()
    {
        var distance = Neo4jStatementBuilder.BuildNodeDistanceRankStatement(
            new[] { "node-b", "node-c" },
            "node-a");

        Assert.Contains("MATCH (center:Entity {uuid: $center_uuid})-[:RELATES_TO]-(n:Entity {uuid: node_uuid})", distance.Query);
        Assert.Equal(new[] { "node-b", "node-c" }, Assert.IsType<List<string>>(distance.Parameters["node_uuids"]));
        Assert.Equal("node-a", distance.Parameters["center_uuid"]);

        var mentions = Neo4jStatementBuilder.BuildNodeEpisodeMentionsRankStatement(
            new[] { "node-a", "node-b" });

        Assert.Contains("MATCH (episode:Episodic)-[r:MENTIONS]->(n:Entity {uuid: node_uuid})", mentions.Query);
        Assert.Equal(new[] { "node-a", "node-b" }, Assert.IsType<List<string>>(mentions.Parameters["node_uuids"]));
    }
}
