namespace Graphiti.Core.Drivers.Ladybug;

internal static class LadybugSearchStatementBuilder
{
    internal static IReadOnlyList<LadybugStatement> BuildFulltextIndexStatements() =>
        new[]
        {
            Statement("CALL CREATE_FTS_INDEX('Episodic', 'episode_content', ['content', 'source', 'source_description']);"),
            Statement("CALL CREATE_FTS_INDEX('Entity', 'node_name_and_summary', ['name', 'summary']);"),
            Statement("CALL CREATE_FTS_INDEX('Community', 'community_name', ['name']);"),
            Statement("CALL CREATE_FTS_INDEX('RelatesToNode_', 'edge_name_and_fact', ['name', 'fact']);")
        };

    internal static LadybugStatement BuildEntityNodeFulltextSearchStatement(
        string fulltextQuery,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit)
    {
        var (filterQueries, parameters) = CompiledSearchFilter
            .Compile(searchFilter)
            .BuildNodeQuery(GraphProvider.Kuzu);
        AddGroupFilter(filterQueries, parameters, "n", groupIds);
        parameters["query"] = fulltextQuery;
        parameters["limit"] = limit;

        return new LadybugStatement(
            $$"""
            CALL QUERY_FTS_INDEX('Entity', 'node_name_and_summary', $query, TOP := $limit)
            WITH node AS n, score
            {{WhereClause(filterQueries)}}
            WITH n, score
            ORDER BY score DESC
            LIMIT $limit
            RETURN
            {{EntityNodeReturnClause("n")}},
            score AS score
            """,
            parameters);
    }

    internal static LadybugStatement BuildEntityNodeEmbeddingSearchStatement(
        IReadOnlyList<float> searchVector,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore)
    {
        ArgumentNullException.ThrowIfNull(searchVector);
        var (filterQueries, parameters) = CompiledSearchFilter
            .Compile(searchFilter)
            .BuildNodeQuery(GraphProvider.Kuzu);
        AddGroupFilter(filterQueries, parameters, "n", groupIds);
        parameters["search_vector"] = SnapshotList(searchVector);
        parameters["limit"] = limit;
        parameters["min_score"] = minScore;

        return new LadybugStatement(
            $$"""
            MATCH (n:Entity)
            {{WhereClause(filterQueries)}}
            WITH n, array_cosine_similarity(n.name_embedding, CAST($search_vector AS FLOAT[{{searchVector.Count}}])) AS score
            WHERE score > $min_score
            RETURN
            {{EntityNodeReturnClause("n")}},
            score AS score
            ORDER BY score DESC
            LIMIT $limit
            """,
            parameters);
    }

    internal static LadybugStatement BuildEntityEdgeFulltextSearchStatement(
        string fulltextQuery,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit)
    {
        var (filterQueries, parameters) = CompiledSearchFilter
            .Compile(searchFilter)
            .BuildEdgeQuery(GraphProvider.Kuzu);
        AddGroupFilter(filterQueries, parameters, "e", groupIds);
        parameters["query"] = fulltextQuery;
        parameters["limit"] = limit;

        return new LadybugStatement(
            $$"""
            CALL QUERY_FTS_INDEX('RelatesToNode_', 'edge_name_and_fact', cast($query AS STRING), TOP := $limit)
            WITH node AS e, score
            MATCH (n:Entity)-[:RELATES_TO]->(e)-[:RELATES_TO]->(m:Entity)
            {{WhereClause(filterQueries)}}
            WITH e, score, n, m
            RETURN
            {{EntityEdgeReturnClause("e", "n", "m")}},
            score AS score
            ORDER BY score DESC
            LIMIT $limit
            """,
            parameters);
    }

    internal static LadybugStatement BuildEntityEdgeEmbeddingSearchStatement(
        IReadOnlyList<float> searchVector,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        string? sourceNodeUuid = null,
        string? targetNodeUuid = null)
    {
        ArgumentNullException.ThrowIfNull(searchVector);
        var (filterQueries, parameters) = CompiledSearchFilter
            .Compile(searchFilter)
            .BuildEdgeQuery(GraphProvider.Kuzu);
        AddGroupFilter(filterQueries, parameters, "e", groupIds);
        if (sourceNodeUuid is not null)
        {
            filterQueries.Add("n.uuid = $source_uuid");
            parameters["source_uuid"] = sourceNodeUuid;
        }

        if (targetNodeUuid is not null)
        {
            filterQueries.Add("m.uuid = $target_uuid");
            parameters["target_uuid"] = targetNodeUuid;
        }

        parameters["search_vector"] = SnapshotList(searchVector);
        parameters["limit"] = limit;
        parameters["min_score"] = minScore;

        return new LadybugStatement(
            $$"""
            MATCH (n:Entity)-[:RELATES_TO]->(e:RelatesToNode_)-[:RELATES_TO]->(m:Entity)
            {{WhereClause(filterQueries)}}
            WITH DISTINCT e, n, m, array_cosine_similarity(e.fact_embedding, CAST($search_vector AS FLOAT[{{searchVector.Count}}])) AS score
            WHERE score > $min_score
            RETURN
            {{EntityEdgeReturnClause("e", "n", "m")}},
            score AS score
            ORDER BY score DESC
            LIMIT $limit
            """,
            parameters);
    }

    internal static IReadOnlyList<LadybugStatement> BuildEntityNodeBfsSearchStatements(
        IReadOnlyList<string>? originNodeUuids,
        SearchFilters searchFilter,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        int limit)
    {
        if (originNodeUuids is null || originNodeUuids.Count == 0 || maxDepth < 1)
        {
            return Array.Empty<LadybugStatement>();
        }

        var (filterQueries, filterParams) = CompiledSearchFilter
            .Compile(searchFilter)
            .BuildNodeQuery(GraphProvider.Kuzu);
        AddGroupFilter(filterQueries, filterParams, "n", groupIds);
        var filterQuery = AndClause(filterQueries);
        var doubledDepth = maxDepth * 2;
        var statements = new List<LadybugStatement>(
            originNodeUuids.Count * (maxDepth > 1 ? 3 : 2));

        foreach (var originUuid in originNodeUuids)
        {
            statements.Add(new LadybugStatement(
                $$"""
                MATCH (origin:Episodic {uuid: $origin_uuid})-[:MENTIONS]->(n:Entity)
                WHERE n.group_id = origin.group_id
                {{filterQuery}}
                RETURN
                {{EntityNodeReturnClause("n")}},
                1.0 AS score
                LIMIT $limit
                """,
                SearchParameters(filterParams, originUuid, limit)));

            statements.Add(new LadybugStatement(
                $$"""
                MATCH (origin:Entity {uuid: $origin_uuid})-[:RELATES_TO*2..{{doubledDepth}}]->(n:Entity)
                WHERE n.group_id = origin.group_id
                {{filterQuery}}
                RETURN
                {{EntityNodeReturnClause("n")}},
                1.0 AS score
                LIMIT $limit
                """,
                SearchParameters(filterParams, originUuid, limit)));

            if (maxDepth <= 1)
            {
                continue;
            }

            var combinedDepth = (maxDepth - 1) * 2;
            statements.Add(new LadybugStatement(
                $$"""
                MATCH (origin:Episodic {uuid: $origin_uuid})-[:MENTIONS]->(:Entity)-[:RELATES_TO*2..{{combinedDepth}}]->(n:Entity)
                WHERE n.group_id = origin.group_id
                {{filterQuery}}
                RETURN
                {{EntityNodeReturnClause("n")}},
                1.0 AS score
                LIMIT $limit
                """,
                SearchParameters(filterParams, originUuid, limit)));
        }

        return statements;
    }

    internal static IReadOnlyList<LadybugStatement> BuildEntityEdgeBfsSearchStatements(
        IReadOnlyList<string>? originNodeUuids,
        SearchFilters searchFilter,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        int limit)
    {
        if (originNodeUuids is null || originNodeUuids.Count == 0 || maxDepth < 1)
        {
            return Array.Empty<LadybugStatement>();
        }

        var (filterQueries, filterParams) = CompiledSearchFilter
            .Compile(searchFilter)
            .BuildEdgeQuery(GraphProvider.Kuzu);
        AddGroupFilter(filterQueries, filterParams, "e", groupIds);
        var filterQuery = WhereClause(filterQueries);
        var doubledDepth = maxDepth * 2;
        var statements = new List<LadybugStatement>(originNodeUuids.Count * 2);

        foreach (var originUuid in originNodeUuids)
        {
            statements.Add(new LadybugStatement(
                $$"""
                MATCH (origin:Entity {uuid: $origin_uuid})-[:RELATES_TO*2..{{doubledDepth}}]->(e:RelatesToNode_)
                MATCH (n:Entity)-[:RELATES_TO]->(e)-[:RELATES_TO]->(m:Entity)
                {{filterQuery}}
                RETURN DISTINCT
                {{EntityEdgeReturnClause("e", "n", "m")}},
                1.0 AS score
                LIMIT $limit
                """,
                SearchParameters(filterParams, originUuid, limit)));

            statements.Add(new LadybugStatement(
                $$"""
                MATCH (origin:Episodic {uuid: $origin_uuid})-[:MENTIONS]->(start:Entity)-[:RELATES_TO]->(e:RelatesToNode_)-[:RELATES_TO]->(m:Entity)
                MATCH (n:Entity)-[:RELATES_TO]->(e)
                {{filterQuery}}
                RETURN DISTINCT
                {{EntityEdgeReturnClause("e", "n", "m")}},
                1.0 AS score
                LIMIT $limit
                """,
                SearchParameters(filterParams, originUuid, limit)));
        }

        return statements;
    }

    internal static LadybugStatement BuildEpisodeFulltextSearchStatement(
        string fulltextQuery,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(searchFilter);
        var parameters = Parameters(("query", fulltextQuery), ("limit", limit));
        var groupFilter = string.Empty;
        if (groupIds is not null)
        {
            groupFilter = "AND e.group_id IN $group_ids";
            parameters["group_ids"] = SnapshotList(groupIds);
        }

        return new LadybugStatement(
            $$"""
            CALL QUERY_FTS_INDEX('Episodic', 'episode_content', $query, TOP := $limit)
            WITH node AS episode, score
            MATCH (e:Episodic)
            WHERE e.uuid = episode.uuid
            {{groupFilter}}
            RETURN
            {{EpisodicNodeReturnClause("e")}},
            score AS score
            ORDER BY score DESC
            LIMIT $limit
            """,
            parameters);
    }

    internal static LadybugStatement BuildCommunityFulltextSearchStatement(
        string fulltextQuery,
        IReadOnlyList<string>? groupIds,
        int limit)
    {
        var parameters = Parameters(("query", fulltextQuery), ("limit", limit));
        var groupFilter = string.Empty;
        if (groupIds is not null)
        {
            groupFilter = "WHERE c.group_id IN $group_ids";
            parameters["group_ids"] = SnapshotList(groupIds);
        }

        return new LadybugStatement(
            $$"""
            CALL QUERY_FTS_INDEX('Community', 'community_name', $query, TOP := $limit)
            WITH node AS c, score
            WITH c, score
            {{groupFilter}}
            RETURN
            {{CommunityNodeReturnClause("c")}},
            score AS score
            ORDER BY score DESC
            LIMIT $limit
            """,
            parameters);
    }

    internal static LadybugStatement BuildCommunityEmbeddingSearchStatement(
        IReadOnlyList<float> searchVector,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore)
    {
        ArgumentNullException.ThrowIfNull(searchVector);
        var parameters = Parameters(
            ("search_vector", SnapshotList(searchVector)),
            ("limit", limit),
            ("min_score", minScore));
        var groupFilter = string.Empty;
        if (groupIds is not null)
        {
            groupFilter = "WHERE c.group_id IN $group_ids";
            parameters["group_ids"] = SnapshotList(groupIds);
        }

        return new LadybugStatement(
            $$"""
            MATCH (c:Community)
            {{groupFilter}}
            WITH c, array_cosine_similarity(c.name_embedding, CAST($search_vector AS FLOAT[{{searchVector.Count}}])) AS score
            WHERE score > $min_score
            RETURN
            {{CommunityNodeReturnClause("c")}},
            score AS score
            ORDER BY score DESC
            LIMIT $limit
            """,
            parameters);
    }

    internal static IReadOnlyList<LadybugStatement> BuildNodeDistanceRankStatements(
        IReadOnlyList<string> nodeUuids,
        string centerNodeUuid)
    {
        ArgumentNullException.ThrowIfNull(nodeUuids);
        var statements = new List<LadybugStatement>(nodeUuids.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < nodeUuids.Count; i++)
        {
            var uuid = nodeUuids[i];
            if (string.Equals(uuid, centerNodeUuid, StringComparison.Ordinal)
                || !seen.Add(uuid))
            {
                continue;
            }

            statements.Add(new LadybugStatement(
                """
                MATCH (center:Entity {uuid: $center_uuid})-[:RELATES_TO]->(:RelatesToNode_)-[:RELATES_TO]-(n:Entity {uuid: $node_uuid})
                RETURN 1 AS score, n.uuid AS uuid
                """,
                Parameters(("node_uuid", uuid), ("center_uuid", centerNodeUuid))));
        }

        return statements;
    }

    internal static IReadOnlyList<LadybugStatement> BuildNodeEpisodeMentionsRankStatements(
        IReadOnlyList<string> nodeUuids)
    {
        ArgumentNullException.ThrowIfNull(nodeUuids);
        var statements = new List<LadybugStatement>(nodeUuids.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < nodeUuids.Count; i++)
        {
            var uuid = nodeUuids[i];
            if (!seen.Add(uuid))
            {
                continue;
            }

            statements.Add(new LadybugStatement(
                """
                MATCH (episode:Episodic)-[r:MENTIONS]->(n:Entity {uuid: $node_uuid})
                RETURN count(*) AS score, n.uuid AS uuid
                """,
                Parameters(("node_uuid", uuid))));
        }

        return statements;
    }

    internal static LadybugStatement BuildEntityNodesByUuidsForRankStatement(
        IReadOnlyList<string> nodeUuids)
    {
        ArgumentNullException.ThrowIfNull(nodeUuids);
        return new LadybugStatement(
            $$"""
            MATCH (n:Entity)
            WHERE n.uuid IN $uuids
            RETURN
            {{EntityNodeReturnClause("n")}}
            """,
            Parameters(("uuids", SnapshotList(nodeUuids))));
    }

    private static LadybugStatement Statement(string query) =>
        new(query, new Dictionary<string, object?>(StringComparer.Ordinal));

    private static Dictionary<string, object?> SearchParameters(
        Dictionary<string, object?> filterParams,
        string originUuid,
        int limit)
    {
        var parameters = new Dictionary<string, object?>(filterParams, StringComparer.Ordinal)
        {
            ["origin_uuid"] = originUuid,
            ["limit"] = limit
        };
        return parameters;
    }

    private static string WhereClause(List<string> filterQueries) =>
        filterQueries.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", filterQueries);

    private static string AndClause(List<string> filterQueries) =>
        filterQueries.Count == 0 ? string.Empty : "AND " + string.Join(" AND ", filterQueries);

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
        filterParams["group_ids"] = SnapshotList(groupIds);
    }

    private static string EntityNodeReturnClause(string variable) =>
        $$"""
            {{variable}}.uuid AS uuid,
            {{variable}}.name AS name,
            {{variable}}.group_id AS group_id,
            {{variable}}.labels AS labels,
            {{variable}}.created_at AS created_at,
            {{variable}}.summary AS summary,
            {{variable}}.attributes AS attributes
        """;

    private static string EpisodicNodeReturnClause(string variable) =>
        $$"""
            {{variable}}.uuid AS uuid,
            {{variable}}.name AS name,
            {{variable}}.group_id AS group_id,
            {{variable}}.created_at AS created_at,
            {{variable}}.source AS source,
            {{variable}}.source_description AS source_description,
            {{variable}}.content AS content,
            {{variable}}.valid_at AS valid_at,
            {{variable}}.entity_edges AS entity_edges
        """;

    private static string CommunityNodeReturnClause(string variable) =>
        $$"""
            {{variable}}.uuid AS uuid,
            {{variable}}.name AS name,
            {{variable}}.group_id AS group_id,
            {{variable}}.created_at AS created_at,
            {{variable}}.name_embedding AS name_embedding,
            {{variable}}.summary AS summary
        """;

    private static string EntityEdgeReturnClause(string edgeVariable, string sourceVariable, string targetVariable) =>
        $$"""
            {{edgeVariable}}.uuid AS uuid,
            {{sourceVariable}}.uuid AS source_node_uuid,
            {{targetVariable}}.uuid AS target_node_uuid,
            {{edgeVariable}}.group_id AS group_id,
            {{edgeVariable}}.created_at AS created_at,
            {{edgeVariable}}.name AS name,
            {{edgeVariable}}.fact AS fact,
            {{edgeVariable}}.episodes AS episodes,
            {{edgeVariable}}.expired_at AS expired_at,
            {{edgeVariable}}.valid_at AS valid_at,
            {{edgeVariable}}.invalid_at AS invalid_at,
            {{edgeVariable}}.reference_time AS reference_time,
            {{edgeVariable}}.attributes AS attributes
        """;

    private static Dictionary<string, object?> Parameters(params (string Name, object? Value)[] parameters)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in parameters)
        {
            dictionary[name] = value;
        }

        return dictionary;
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
}
