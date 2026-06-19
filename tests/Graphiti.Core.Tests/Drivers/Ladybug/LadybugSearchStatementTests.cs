using Graphiti.Core.Drivers.Ladybug;

namespace Graphiti.Core.Tests.Drivers.Ladybug;

public class LadybugSearchStatementTests
{
    [Fact]
    public void BuildFulltextIndexStatements_UseKuzuFtsWithoutChangingBaseSchema()
    {
        var statements = LadybugSearchStatementBuilder.BuildFulltextIndexStatements();

        Assert.Equal(4, statements.Count);
        Assert.Contains(
            "CALL CREATE_FTS_INDEX('Entity', 'node_name_and_summary', ['name', 'summary']);",
            statements.Select(statement => statement.Query));
        Assert.Contains(
            "CALL CREATE_FTS_INDEX('RelatesToNode_', 'edge_name_and_fact', ['name', 'fact']);",
            statements.Select(statement => statement.Query));
        Assert.All(statements, statement => Assert.Empty(statement.Parameters));
        Assert.DoesNotContain("CREATE_FTS_INDEX", LadybugSchema.SchemaQueries, StringComparison.OrdinalIgnoreCase);
        Assert.True(typeof(ISearchGraphDriver).IsAssignableFrom(typeof(LadybugGraphDriver)));
    }

    [Fact]
    public void BuildEntityNodeFulltextSearchStatement_IncludesKuzuFtsFiltersAndGroupScope()
    {
        var statement = LadybugSearchStatementBuilder.BuildEntityNodeFulltextSearchStatement(
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
            limit: 7);

        Assert.Contains(
            "CALL QUERY_FTS_INDEX('Entity', 'node_name_and_summary', $query, TOP := $limit)",
            statement.Query,
            StringComparison.Ordinal);
        Assert.Contains("WITH node AS n, score", statement.Query, StringComparison.Ordinal);
        Assert.Contains(
            "WHERE list_has_all(n.labels, $labels) AND n.group_id IN $group_ids",
            statement.Query,
            StringComparison.Ordinal);
        Assert.Contains("score AS score", statement.Query, StringComparison.Ordinal);
        Assert.Equal("alice", statement.Parameters["query"]);
        Assert.Equal(7, statement.Parameters["limit"]);
        Assert.Equal(new[] { "Person" }, Assert.IsType<List<string>>(statement.Parameters["labels"]));
        Assert.DoesNotContain("node_property", statement.Query, StringComparison.Ordinal);
        Assert.DoesNotContain(statement.Parameters, parameter => parameter.Key.StartsWith("node_property", StringComparison.Ordinal));
        Assert.Equal(
            new[] { "tenant-a", "tenant-b" },
            Assert.IsType<List<string>>(statement.Parameters["group_ids"]));
    }

    [Fact]
    public void BuildEntityNodeEmbeddingSearchStatement_UsesKuzuVectorFunctionAndDimension()
    {
        var statement = LadybugSearchStatementBuilder.BuildEntityNodeEmbeddingSearchStatement(
            new[] { 0.1f, 0.2f, 0.3f },
            new SearchFilters { NodeLabels = new List<string> { "Person" } },
            new[] { "tenant" },
            limit: 5,
            minScore: 0.42f);

        Assert.Contains("MATCH (n:Entity)", statement.Query, StringComparison.Ordinal);
        Assert.Contains(
            "array_cosine_similarity(n.name_embedding, CAST($search_vector AS FLOAT[3])) AS score",
            statement.Query,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE list_has_all(n.labels, $labels) AND n.group_id IN $group_ids",
            statement.Query,
            StringComparison.Ordinal);
        Assert.Contains("WHERE score > $min_score", statement.Query, StringComparison.Ordinal);
        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, Assert.IsType<List<float>>(statement.Parameters["search_vector"]));
        Assert.Equal(0.42f, statement.Parameters["min_score"]);
    }

    [Fact]
    public void BuildEntityEdgeSearchStatements_UseRelatesToNodeAndKuzuFilters()
    {
        var validAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var filters = new SearchFilters
        {
            EdgeTypes = new List<string> { "KNOWS" },
            EdgeUuids = new List<string> { "edge-1" },
            NodeLabels = new List<string> { "Person" },
            ValidAt = new List<List<DateFilter>>
            {
                new() { new DateFilter(ComparisonOperator.GreaterThanEqual, validAt) }
            }
        };

        var fulltext = LadybugSearchStatementBuilder.BuildEntityEdgeFulltextSearchStatement(
            "alice knows",
            filters,
            new[] { "tenant" },
            limit: 4);
        var embedding = LadybugSearchStatementBuilder.BuildEntityEdgeEmbeddingSearchStatement(
            new[] { 0.1f, 0.2f },
            filters,
            new[] { "tenant" },
            limit: 3,
            minScore: 0.6f,
            sourceNodeUuid: "source-1",
            targetNodeUuid: "target-1");

        Assert.Contains(
            "CALL QUERY_FTS_INDEX('RelatesToNode_', 'edge_name_and_fact', cast($query AS STRING), TOP := $limit)",
            fulltext.Query,
            StringComparison.Ordinal);
        Assert.Contains(
            "MATCH (n:Entity)-[:RELATES_TO]->(e)-[:RELATES_TO]->(m:Entity)",
            fulltext.Query,
            StringComparison.Ordinal);
        Assert.Contains("e.name in $edge_types", fulltext.Query, StringComparison.Ordinal);
        Assert.Contains("e.uuid in $edge_uuids", fulltext.Query, StringComparison.Ordinal);
        Assert.Contains(
            "list_has_all(n.labels, $labels) AND list_has_all(m.labels, $labels)",
            fulltext.Query,
            StringComparison.Ordinal);
        Assert.Contains("(e.valid_at >= $valid_at_0)", fulltext.Query, StringComparison.Ordinal);
        Assert.Contains("e.group_id IN $group_ids", fulltext.Query, StringComparison.Ordinal);
        Assert.Contains("e.reference_time AS reference_time", fulltext.Query, StringComparison.Ordinal);
        Assert.Equal(validAt, fulltext.Parameters["valid_at_0"]);

        Assert.Contains(
            "MATCH (n:Entity)-[:RELATES_TO]->(e:RelatesToNode_)-[:RELATES_TO]->(m:Entity)",
            embedding.Query,
            StringComparison.Ordinal);
        Assert.Contains(
            "array_cosine_similarity(e.fact_embedding, CAST($search_vector AS FLOAT[2])) AS score",
            embedding.Query,
            StringComparison.Ordinal);
        Assert.Contains("n.uuid = $source_uuid", embedding.Query, StringComparison.Ordinal);
        Assert.Contains("m.uuid = $target_uuid", embedding.Query, StringComparison.Ordinal);
        Assert.Equal(new[] { "KNOWS" }, embedding.Parameters["edge_types"]);
        Assert.Equal(new[] { "edge-1" }, embedding.Parameters["edge_uuids"]);
        Assert.Equal(new[] { "tenant" }, Assert.IsType<List<string>>(embedding.Parameters["group_ids"]));
        Assert.Equal("source-1", embedding.Parameters["source_uuid"]);
        Assert.Equal("target-1", embedding.Parameters["target_uuid"]);

        var unscopedEmbedding = LadybugSearchStatementBuilder.BuildEntityEdgeEmbeddingSearchStatement(
            new[] { 0.1f, 0.2f },
            new SearchFilters(),
            groupIds: null,
            limit: 3,
            minScore: 0.6f,
            sourceNodeUuid: "source-1",
            targetNodeUuid: "target-1");

        Assert.DoesNotContain("n.uuid = $source_uuid", unscopedEmbedding.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("m.uuid = $target_uuid", unscopedEmbedding.Query, StringComparison.Ordinal);
        Assert.False(unscopedEmbedding.Parameters.ContainsKey("source_uuid"));
        Assert.False(unscopedEmbedding.Parameters.ContainsKey("target_uuid"));
    }

    [Fact]
    public void BuildEdgeQuery_PreservesAllFiltersThroughLadybugAdapter()
    {
        var validAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var invalidAt = validAt.AddDays(1);
        var createdAt = validAt.AddDays(-1);
        var expiredAt = validAt.AddDays(2);
        var filters = new SearchFilters
        {
            EdgeTypes = new List<string> { "KNOWS" },
            EdgeUuids = new List<string> { "edge-1" },
            NodeLabels = new List<string> { "Person" },
            ValidAt = new List<List<DateFilter>>
            {
                new() { new DateFilter(ComparisonOperator.GreaterThanEqual, validAt) }
            },
            InvalidAt = new List<List<DateFilter>>
            {
                new() { new DateFilter(ComparisonOperator.LessThan, invalidAt) }
            },
            CreatedAt = new List<List<DateFilter>>
            {
                new() { new DateFilter(ComparisonOperator.Equals, createdAt) }
            },
            ExpiredAt = new List<List<DateFilter>>
            {
                new() { new DateFilter(ComparisonOperator.Equals, expiredAt) }
            },
            PropertyFilters = new List<PropertyFilter>
            {
                new("confidence", ComparisonOperator.GreaterThan, 0.5)
            }
        };

        var (queries, parameters) = LadybugSearchFilter.BuildEdgeQuery(filters);

        Assert.Equal(
            new[]
            {
                "e.name in $edge_types",
                "e.uuid in $edge_uuids",
                "list_has_all(n.labels, $labels) AND list_has_all(m.labels, $labels)",
                "((e.valid_at >= $valid_at_0))",
                "((e.invalid_at < $invalid_at_0))",
                "((e.created_at = $created_at_0))",
                "((e.expired_at = $expired_at_0))"
            },
            queries);
        Assert.Equal(new[] { "KNOWS" }, parameters["edge_types"]);
        Assert.Equal(new[] { "edge-1" }, parameters["edge_uuids"]);
        Assert.Equal(new[] { "Person" }, Assert.IsType<List<string>>(parameters["labels"]));
        Assert.Equal(validAt, parameters["valid_at_0"]);
        Assert.Equal(invalidAt, parameters["invalid_at_0"]);
        Assert.Equal(createdAt, parameters["created_at_0"]);
        Assert.Equal(expiredAt, parameters["expired_at_0"]);
        Assert.DoesNotContain(parameters, parameter => parameter.Key.StartsWith("edge_property", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildEdgeQuery_PreservesEmptyEdgeListsBeforeLadybugLabelFilter()
    {
        var filters = new SearchFilters
        {
            EdgeTypes = new List<string>(),
            EdgeUuids = new List<string>(),
            NodeLabels = new List<string> { "Person" }
        };

        var (queries, parameters) = LadybugSearchFilter.BuildEdgeQuery(filters);

        Assert.Equal(
            new[]
            {
                "e.name in $edge_types",
                "e.uuid in $edge_uuids",
                "list_has_all(n.labels, $labels) AND list_has_all(m.labels, $labels)"
            },
            queries);
        Assert.Same(filters.EdgeTypes, parameters["edge_types"]);
        Assert.Same(filters.EdgeUuids, parameters["edge_uuids"]);
        Assert.Equal(new[] { "Person" }, Assert.IsType<List<string>>(parameters["labels"]));
    }

    [Fact]
    public void BuildEpisodeAndCommunitySearchStatements_PreserveKuzuIndexesAndVectors()
    {
        var episode = LadybugSearchStatementBuilder.BuildEpisodeFulltextSearchStatement(
            "episode",
            new SearchFilters(),
            new[] { "tenant" },
            limit: 6);
        var communityFulltext = LadybugSearchStatementBuilder.BuildCommunityFulltextSearchStatement(
            "community",
            new[] { "tenant" },
            limit: 8);
        var communityEmbedding = LadybugSearchStatementBuilder.BuildCommunityEmbeddingSearchStatement(
            new[] { 0.3f, 0.4f },
            new[] { "tenant" },
            limit: 9,
            minScore: 0.5f);

        Assert.Contains(
            "CALL QUERY_FTS_INDEX('Episodic', 'episode_content', $query, TOP := $limit)",
            episode.Query,
            StringComparison.Ordinal);
        Assert.Contains("AND e.group_id IN $group_ids", episode.Query, StringComparison.Ordinal);
        Assert.Contains("score AS score", episode.Query, StringComparison.Ordinal);
        Assert.Equal("episode", episode.Parameters["query"]);

        Assert.Contains(
            "CALL QUERY_FTS_INDEX('Community', 'community_name', $query, TOP := $limit)",
            communityFulltext.Query,
            StringComparison.Ordinal);
        Assert.Contains("WHERE c.group_id IN $group_ids", communityFulltext.Query, StringComparison.Ordinal);
        Assert.Equal(8, communityFulltext.Parameters["limit"]);

        Assert.Contains("MATCH (c:Community)", communityEmbedding.Query, StringComparison.Ordinal);
        Assert.Contains(
            "array_cosine_similarity(c.name_embedding, CAST($search_vector AS FLOAT[2])) AS score",
            communityEmbedding.Query,
            StringComparison.Ordinal);
        Assert.Equal(new[] { 0.3f, 0.4f }, Assert.IsType<List<float>>(communityEmbedding.Parameters["search_vector"]));
        Assert.Equal(0.5f, communityEmbedding.Parameters["min_score"]);
    }

    [Fact]
    public void SearchStatements_SnapshotReadOnlyListParametersWithIndexAccess()
    {
        var groupIds = new IndexOnlyReadOnlyList<string>("tenant", "archive");
        var vector = new IndexOnlyReadOnlyList<float>(0.1f, 0.2f, 0.3f);
        var nodeUuids = new IndexOnlyReadOnlyList<string>("node-1", "node-2");

        var nodeEmbedding = LadybugSearchStatementBuilder.BuildEntityNodeEmbeddingSearchStatement(
            vector,
            new SearchFilters(),
            groupIds,
            limit: 5,
            minScore: 0.4f);
        var edgeEmbedding = LadybugSearchStatementBuilder.BuildEntityEdgeEmbeddingSearchStatement(
            vector,
            new SearchFilters(),
            groupIds,
            limit: 6,
            minScore: 0.5f);
        var episode = LadybugSearchStatementBuilder.BuildEpisodeFulltextSearchStatement(
            "episode",
            new SearchFilters(),
            groupIds,
            limit: 7);
        var community = LadybugSearchStatementBuilder.BuildCommunityEmbeddingSearchStatement(
            vector,
            groupIds,
            limit: 8,
            minScore: 0.6f);
        var fetch = LadybugSearchStatementBuilder.BuildEntityNodesByUuidsForRankStatement(nodeUuids);

        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, Assert.IsType<List<float>>(nodeEmbedding.Parameters["search_vector"]));
        Assert.Equal(new[] { "tenant", "archive" }, Assert.IsType<List<string>>(nodeEmbedding.Parameters["group_ids"]));
        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, Assert.IsType<List<float>>(edgeEmbedding.Parameters["search_vector"]));
        Assert.Equal(new[] { "tenant", "archive" }, Assert.IsType<List<string>>(edgeEmbedding.Parameters["group_ids"]));
        Assert.Equal(new[] { "tenant", "archive" }, Assert.IsType<List<string>>(episode.Parameters["group_ids"]));
        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, Assert.IsType<List<float>>(community.Parameters["search_vector"]));
        Assert.Equal(new[] { "tenant", "archive" }, Assert.IsType<List<string>>(community.Parameters["group_ids"]));
        Assert.Equal(new[] { "node-1", "node-2" }, Assert.IsType<List<string>>(fetch.Parameters["uuids"]));
    }

    [Fact]
    public void BuildBfsSearchStatements_ReturnSeparateKuzuStatementsWithDoubledDepth()
    {
        var nodeStatements = LadybugSearchStatementBuilder.BuildEntityNodeBfsSearchStatements(
            new[] { "origin-1", "origin-2" },
            new SearchFilters { NodeLabels = new List<string> { "Person" } },
            maxDepth: 2,
            new[] { "tenant" },
            limit: 5);
        var edgeStatements = LadybugSearchStatementBuilder.BuildEntityEdgeBfsSearchStatements(
            new[] { "origin-1" },
            new SearchFilters { EdgeTypes = new List<string> { "KNOWS" } },
            maxDepth: 3,
            new[] { "tenant" },
            limit: 6);

        Assert.Equal(6, nodeStatements.Count);
        Assert.All(nodeStatements, statement => Assert.DoesNotContain("UNWIND", statement.Query, StringComparison.Ordinal));
        Assert.Contains(
            "MATCH (origin:Episodic {uuid: $origin_uuid})-[:MENTIONS]->(n:Entity)",
            nodeStatements[0].Query,
            StringComparison.Ordinal);
        Assert.Contains("[:RELATES_TO*2..4]->(n:Entity)", nodeStatements[1].Query, StringComparison.Ordinal);
        Assert.Contains("[:RELATES_TO*2..2]->(n:Entity)", nodeStatements[2].Query, StringComparison.Ordinal);
        Assert.Contains(
            "AND list_has_all(n.labels, $labels) AND n.group_id IN $group_ids",
            nodeStatements[0].Query,
            StringComparison.Ordinal);
        Assert.Equal("origin-1", nodeStatements[0].Parameters["origin_uuid"]);
        Assert.Equal("origin-2", nodeStatements[3].Parameters["origin_uuid"]);

        Assert.Equal(2, edgeStatements.Count);
        Assert.All(edgeStatements, statement => Assert.DoesNotContain("UNWIND", statement.Query, StringComparison.Ordinal));
        Assert.Contains(
            "[:RELATES_TO*2..6]->(e:RelatesToNode_)",
            edgeStatements[0].Query,
            StringComparison.Ordinal);
        Assert.Contains(
            "MATCH (n:Entity)-[:RELATES_TO]->(e)-[:RELATES_TO]->(m:Entity)",
            edgeStatements[0].Query,
            StringComparison.Ordinal);
        Assert.Contains(
            "MATCH (origin:Episodic {uuid: $origin_uuid})-[:MENTIONS]->(start:Entity)-[:RELATES_TO]->(e:RelatesToNode_)",
            edgeStatements[1].Query,
            StringComparison.Ordinal);
        Assert.Contains("RETURN DISTINCT", edgeStatements[0].Query, StringComparison.Ordinal);
        Assert.Contains("WHERE e.name in $edge_types AND e.group_id IN $group_ids", edgeStatements[0].Query, StringComparison.Ordinal);
        Assert.Equal(new[] { "KNOWS" }, edgeStatements[0].Parameters["edge_types"]);
    }

    [Fact]
    public void BuildRankStatements_UsePerUuidQueriesWithoutUnwind()
    {
        var distance = LadybugSearchStatementBuilder.BuildNodeDistanceRankStatements(
            new[] { "center", "node-b", "node-c", "node-b" },
            "center");
        var mentions = LadybugSearchStatementBuilder.BuildNodeEpisodeMentionsRankStatements(
            new[] { "node-a", "node-b", "node-a" });
        var fetch = LadybugSearchStatementBuilder.BuildEntityNodesByUuidsForRankStatement(
            new[] { "node-b", "node-c" });

        Assert.Equal(2, distance.Count);
        Assert.All(distance, statement => Assert.DoesNotContain("UNWIND", statement.Query, StringComparison.Ordinal));
        Assert.Contains(
            "MATCH (center:Entity {uuid: $center_uuid})-[:RELATES_TO]->(:RelatesToNode_)-[:RELATES_TO]-(n:Entity {uuid: $node_uuid})",
            distance[0].Query,
            StringComparison.Ordinal);
        Assert.Equal("node-b", distance[0].Parameters["node_uuid"]);
        Assert.Equal("node-c", distance[1].Parameters["node_uuid"]);
        Assert.Equal("center", distance[0].Parameters["center_uuid"]);

        Assert.Equal(2, mentions.Count);
        Assert.All(mentions, statement => Assert.DoesNotContain("UNWIND", statement.Query, StringComparison.Ordinal));
        Assert.Contains(
            "MATCH (episode:Episodic)-[r:MENTIONS]->(n:Entity {uuid: $node_uuid})",
            mentions[0].Query,
            StringComparison.Ordinal);
        Assert.Equal("node-a", mentions[0].Parameters["node_uuid"]);
        Assert.Equal("node-b", mentions[1].Parameters["node_uuid"]);

        Assert.Contains("WHERE n.uuid IN $uuids", fetch.Query, StringComparison.Ordinal);
        Assert.Equal(new[] { "node-b", "node-c" }, Assert.IsType<List<string>>(fetch.Parameters["uuids"]));
    }

    private sealed class IndexOnlyReadOnlyList<T> : IReadOnlyList<T>
    {
        private readonly T[] _values;

        internal IndexOnlyReadOnlyList(params T[] values) => _values = values;

        public int Count => _values.Length;

        public T this[int index] => _values[index];

        public IEnumerator<T> GetEnumerator() =>
            throw new InvalidOperationException("Search statement builders should copy IReadOnlyList values by index.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
