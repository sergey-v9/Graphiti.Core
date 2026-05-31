namespace Graphiti.Core.Drivers;

internal static partial class Neo4jStatementBuilder
{
    internal static Neo4jStatement BuildEntityNodeFulltextSearchStatement(
        string fulltextQuery,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        GraphProvider provider)
    {
        var (filterQueries, filterParams) = CompiledSearchFilter.Compile(searchFilter).BuildNodeQuery(provider);
        AddGroupFilter(filterQueries, filterParams, "n", groupIds);

        filterParams["query"] = fulltextQuery;
        filterParams["limit"] = limit;
        var filterQuery = WhereClause(filterQueries);

        return new Neo4jStatement(
            $$"""
            CALL db.index.fulltext.queryNodes("node_name_and_summary", $query, {limit: $limit})
            YIELD node AS n, score
            {{filterQuery}}
            RETURN n, score
            ORDER BY score DESC
            LIMIT $limit
            """,
            filterParams);
    }

    internal static Neo4jStatement BuildEntityNodeEmbeddingSearchStatement(
        IReadOnlyList<float> searchVector,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        GraphProvider provider)
    {
        var (filterQueries, filterParams) = CompiledSearchFilter.Compile(searchFilter).BuildNodeQuery(provider);
        filterQueries.Add("n.name_embedding IS NOT NULL");
        AddGroupFilter(filterQueries, filterParams, "n", groupIds);

        filterParams["search_vector"] = searchVector.ToList();
        filterParams["limit"] = limit;
        filterParams["min_score"] = minScore;
        var filterQuery = WhereClause(filterQueries);

        return new Neo4jStatement(
            $"""
            MATCH (n:Entity)
            {filterQuery}
            WITH n, vector.similarity.cosine(n.name_embedding, $search_vector) AS score
            WHERE score > $min_score
            RETURN n, score
            ORDER BY score DESC
            LIMIT $limit
            """,
            filterParams);
    }

    internal static Neo4jStatement BuildEntityEdgeFulltextSearchStatement(
        string fulltextQuery,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        GraphProvider provider)
    {
        var (filterQueries, filterParams) = CompiledSearchFilter.Compile(searchFilter).BuildEdgeQuery(provider);
        AddGroupFilter(filterQueries, filterParams, "e", groupIds);

        filterParams["query"] = fulltextQuery;
        filterParams["limit"] = limit;
        var filterQuery = WhereClause(filterQueries);

        return new Neo4jStatement(
            $$"""
            CALL db.index.fulltext.queryRelationships("edge_name_and_fact", $query, {limit: $limit})
            YIELD relationship AS rel, score
            MATCH (n:Entity)-[e:RELATES_TO {uuid: rel.uuid}]->(m:Entity)
            {{filterQuery}}
            RETURN e, n.uuid AS source_uuid, m.uuid AS target_uuid, score
            ORDER BY score DESC
            LIMIT $limit
            """,
            filterParams);
    }

    internal static Neo4jStatement BuildEntityEdgeEmbeddingSearchStatement(
        IReadOnlyList<float> searchVector,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        GraphProvider provider,
        string? sourceNodeUuid = null,
        string? targetNodeUuid = null)
    {
        var (filterQueries, filterParams) = CompiledSearchFilter.Compile(searchFilter).BuildEdgeQuery(provider);
        filterQueries.Add("e.fact_embedding IS NOT NULL");
        AddGroupFilter(filterQueries, filterParams, "e", groupIds);

        if (sourceNodeUuid is not null)
        {
            filterQueries.Add("n.uuid = $source_uuid");
            filterParams["source_uuid"] = sourceNodeUuid;
        }

        if (targetNodeUuid is not null)
        {
            filterQueries.Add("m.uuid = $target_uuid");
            filterParams["target_uuid"] = targetNodeUuid;
        }

        filterParams["search_vector"] = searchVector.ToList();
        filterParams["limit"] = limit;
        filterParams["min_score"] = minScore;
        var filterQuery = WhereClause(filterQueries);

        return new Neo4jStatement(
            $"""
            MATCH (n:Entity)-[e:RELATES_TO]->(m:Entity)
            {filterQuery}
            WITH DISTINCT e, n, m, vector.similarity.cosine(e.fact_embedding, $search_vector) AS score
            WHERE score > $min_score
            RETURN e, n.uuid AS source_uuid, m.uuid AS target_uuid, score
            ORDER BY score DESC
            LIMIT $limit
            """,
            filterParams);
    }

    internal static Neo4jStatement BuildEntityNodeBfsSearchStatement(
        IReadOnlyList<string> originNodeUuids,
        SearchFilters searchFilter,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        int limit,
        GraphProvider provider)
    {
        var (filterQueries, filterParams) = CompiledSearchFilter.Compile(searchFilter).BuildNodeQuery(provider);
        if (groupIds is not null)
        {
            filterQueries.Add("n.group_id IN $group_ids");
            filterQueries.Add("origin.group_id IN $group_ids");
            filterParams["group_ids"] = groupIds.ToList();
        }

        filterParams["bfs_origin_node_uuids"] = originNodeUuids.ToList();
        filterParams["limit"] = limit;
        var filterQuery = AndClause(filterQueries);

        return new Neo4jStatement(
            BuildNodeBfsSearchQuery(maxDepth, filterQuery),
            filterParams);
    }

    internal static Neo4jStatement BuildEntityEdgeBfsSearchStatement(
        IReadOnlyList<string> originNodeUuids,
        SearchFilters searchFilter,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        int limit,
        GraphProvider provider)
    {
        var (filterQueries, filterParams) = CompiledSearchFilter.Compile(searchFilter).BuildEdgeQuery(provider);
        var pathFilter = "";
        if (groupIds is not null)
        {
            pathFilter = "WHERE origin.group_id IN $group_ids";
            filterQueries.Add("e.group_id IN $group_ids");
            filterParams["group_ids"] = groupIds.ToList();
        }

        filterParams["bfs_origin_node_uuids"] = originNodeUuids.ToList();
        filterParams["limit"] = limit;
        var filterQuery = WhereClause(filterQueries);

        return new Neo4jStatement(
            BuildEdgeBfsSearchQuery(maxDepth, pathFilter, filterQuery),
            filterParams);
    }

    internal static Neo4jStatement BuildEpisodeFulltextSearchStatement(
        string fulltextQuery,
        IReadOnlyList<string>? groupIds,
        int limit)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["query"] = fulltextQuery,
            ["limit"] = limit
        };
        var groupFilter = "";
        if (groupIds is not null)
        {
            groupFilter = "AND e.group_id IN $group_ids";
            parameters["group_ids"] = groupIds.ToList();
        }

        return new Neo4jStatement(
            $$"""
            CALL db.index.fulltext.queryNodes("episode_content", $query, {limit: $limit})
            YIELD node AS episode, score
            MATCH (e:Episodic)
            WHERE e.uuid = episode.uuid
            {{groupFilter}}
            RETURN e AS n, score
            ORDER BY score DESC
            LIMIT $limit
            """,
            parameters);
    }

    internal static Neo4jStatement BuildCommunityFulltextSearchStatement(
        string fulltextQuery,
        IReadOnlyList<string>? groupIds,
        int limit)
    {
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["query"] = fulltextQuery,
            ["limit"] = limit
        };
        var groupFilter = "";
        if (groupIds is not null)
        {
            groupFilter = "WHERE c.group_id IN $group_ids";
            parameters["group_ids"] = groupIds.ToList();
        }

        return new Neo4jStatement(
            $$"""
            CALL db.index.fulltext.queryNodes("community_name", $query, {limit: $limit})
            YIELD node AS c, score
            WITH c, score
            {{groupFilter}}
            RETURN c AS n, score
            ORDER BY score DESC
            LIMIT $limit
            """,
            parameters);
    }

    internal static Neo4jStatement BuildCommunityEmbeddingSearchStatement(
        IReadOnlyList<float> searchVector,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore)
    {
        var filterQueries = new List<string> { "c.name_embedding IS NOT NULL" };
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["search_vector"] = searchVector.ToList(),
            ["limit"] = limit,
            ["min_score"] = minScore
        };
        AddGroupFilter(filterQueries, parameters, "c", groupIds);

        var filterQuery = WhereClause(filterQueries);
        return new Neo4jStatement(
            $"""
            MATCH (c:Community)
            {filterQuery}
            WITH c, vector.similarity.cosine(c.name_embedding, $search_vector) AS score
            WHERE score > $min_score
            RETURN c AS n, score
            ORDER BY score DESC
            LIMIT $limit
            """,
            parameters);
    }

    internal static Neo4jStatement BuildNodeDistanceRankStatement(
        IReadOnlyList<string> filteredNodeUuids,
        string centerNodeUuid) =>
        new(
            """
            UNWIND $node_uuids AS node_uuid
            MATCH (center:Entity {uuid: $center_uuid})-[:RELATES_TO]-(n:Entity {uuid: node_uuid})
            RETURN 1 AS score, node_uuid AS uuid
            """,
            new Dictionary<string, object?>
            {
                ["node_uuids"] = filteredNodeUuids.ToList(),
                ["center_uuid"] = centerNodeUuid
            });

    internal static Neo4jStatement BuildNodeEpisodeMentionsRankStatement(
        IReadOnlyList<string> nodeUuids) =>
        new(
            """
            UNWIND $node_uuids AS node_uuid
            MATCH (episode:Episodic)-[r:MENTIONS]->(n:Entity {uuid: node_uuid})
            RETURN count(*) AS score, n.uuid AS uuid
            """,
            new Dictionary<string, object?>
            {
                ["node_uuids"] = nodeUuids.ToList()
            });

    private static string WhereClause(List<string> filterQueries) =>
        filterQueries.Count == 0 ? "" : "WHERE " + string.Join(" AND ", filterQueries);

    private static string AndClause(List<string> filterQueries) =>
        filterQueries.Count == 0 ? "" : " AND " + string.Join(" AND ", filterQueries);

    private static void AddGroupFilter(
        List<string> filterQueries,
        Dictionary<string, object?> filterParams,
        string alias,
        IReadOnlyList<string>? groupIds)
    {
        if (groupIds is null)
        {
            return;
        }

        filterQueries.Add($"{alias}.group_id IN $group_ids");
        filterParams["group_ids"] = groupIds.ToList();
    }
}
