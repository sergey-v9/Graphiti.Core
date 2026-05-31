using Neo4j.Driver;

namespace Graphiti.Core.Drivers;

/// <summary>
/// A graph driver backed by Neo4j (5.26+). Implements persistence (<see cref="GraphDriverBase"/>) and
/// search (<see cref="ISearchGraphDriver"/>) by issuing Cypher queries, including full-text and vector
/// retrieval, and provisions the required constraints and indexes on initialization.
/// </summary>
public sealed class Neo4jGraphDriver : GraphDriverBase, ISearchGraphDriver
{
    private static readonly IReadOnlyList<string> Neo4jSchemaStatements = Array.AsReadOnly(new[]
    {
        "CREATE CONSTRAINT entity_uuid IF NOT EXISTS FOR (n:Entity) REQUIRE n.uuid IS UNIQUE",
        "CREATE CONSTRAINT episodic_uuid IF NOT EXISTS FOR (n:Episodic) REQUIRE n.uuid IS UNIQUE",
        "CREATE CONSTRAINT community_uuid IF NOT EXISTS FOR (n:Community) REQUIRE n.uuid IS UNIQUE",
        "CREATE CONSTRAINT saga_uuid IF NOT EXISTS FOR (n:Saga) REQUIRE n.uuid IS UNIQUE",
        "CREATE INDEX relation_uuid IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.uuid)",
        "CREATE INDEX mention_uuid IF NOT EXISTS FOR ()-[e:MENTIONS]-() ON (e.uuid)",
        "CREATE INDEX has_member_uuid IF NOT EXISTS FOR ()-[e:HAS_MEMBER]-() ON (e.uuid)",
        "CREATE INDEX has_episode_uuid IF NOT EXISTS FOR ()-[e:HAS_EPISODE]-() ON (e.uuid)",
        "CREATE INDEX next_episode_uuid IF NOT EXISTS FOR ()-[e:NEXT_EPISODE]-() ON (e.uuid)",
        "CREATE INDEX entity_group_id IF NOT EXISTS FOR (n:Entity) ON (n.group_id)",
        "CREATE INDEX episode_group_id IF NOT EXISTS FOR (n:Episodic) ON (n.group_id)",
        "CREATE INDEX community_group_id IF NOT EXISTS FOR (n:Community) ON (n.group_id)",
        "CREATE INDEX saga_group_id IF NOT EXISTS FOR (n:Saga) ON (n.group_id)",
        "CREATE INDEX relation_group_id IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.group_id)",
        "CREATE INDEX mention_group_id IF NOT EXISTS FOR ()-[e:MENTIONS]-() ON (e.group_id)",
        "CREATE INDEX has_episode_group_id IF NOT EXISTS FOR ()-[e:HAS_EPISODE]-() ON (e.group_id)",
        "CREATE INDEX next_episode_group_id IF NOT EXISTS FOR ()-[e:NEXT_EPISODE]-() ON (e.group_id)",
        "CREATE INDEX name_entity_index IF NOT EXISTS FOR (n:Entity) ON (n.name)",
        "CREATE INDEX saga_name IF NOT EXISTS FOR (n:Saga) ON (n.name)",
        "CREATE INDEX created_at_entity_index IF NOT EXISTS FOR (n:Entity) ON (n.created_at)",
        "CREATE INDEX created_at_episodic_index IF NOT EXISTS FOR (n:Episodic) ON (n.created_at)",
        "CREATE INDEX valid_at_episodic_index IF NOT EXISTS FOR (n:Episodic) ON (n.valid_at)",
        "CREATE INDEX name_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.name)",
        "CREATE INDEX created_at_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.created_at)",
        "CREATE INDEX expired_at_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.expired_at)",
        "CREATE INDEX valid_at_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.valid_at)",
        "CREATE INDEX invalid_at_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.invalid_at)",
        "CREATE FULLTEXT INDEX episode_content IF NOT EXISTS FOR (e:Episodic) ON EACH [e.content, e.source, e.source_description, e.group_id]",
        "CREATE FULLTEXT INDEX node_name_and_summary IF NOT EXISTS FOR (n:Entity) ON EACH [n.name, n.summary, n.group_id]",
        "CREATE FULLTEXT INDEX community_name IF NOT EXISTS FOR (n:Community) ON EACH [n.name, n.group_id]",
        "CREATE FULLTEXT INDEX edge_name_and_fact IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON EACH [e.name, e.fact, e.group_id]"
    });

    private static readonly IReadOnlyList<string> Neo4jSchemaResetStatements =
        Array.AsReadOnly(Neo4jSchemaStatements.Select(CreateSchemaDropStatement).ToArray());

    private readonly IDriver _driver;
    private readonly Neo4jSessionExecutor _sessionExecutor;
    private readonly bool _ownsDriver;
    private int _closed;

    /// <summary>
    /// Creates a driver connecting to the Neo4j instance at <paramref name="uri"/>. When
    /// <paramref name="user"/> is empty, anonymous auth is used; otherwise basic auth is applied.
    /// </summary>
    public Neo4jGraphDriver(string uri, string? user = null, string? password = null, string database = "")
        : this(
            GraphDatabase.Driver(
                uri,
                string.IsNullOrEmpty(user)
                    ? AuthTokens.None
                    : AuthTokens.Basic(user, password ?? string.Empty)),
            uri,
            user,
            password,
            database,
            ownsDriver: true)
    {
    }

    internal Neo4jGraphDriver(
        IDriver driver,
        string uri,
        string? user,
        string? password,
        string database,
        bool ownsDriver)
        : base(GraphProvider.Neo4j, database)
    {
        _driver = driver;
        _sessionExecutor = new Neo4jSessionExecutor(driver, database);
        _ownsDriver = ownsDriver;
        Uri = uri;
        User = user;
        Password = password;
    }

    /// <summary>The Neo4j connection URI.</summary>
    public string Uri { get; }

    /// <summary>The authentication user, or <c>null</c> for anonymous connections.</summary>
    public string? User { get; }

    /// <summary>The authentication password, if any.</summary>
    public string? Password { get; }

    public override async Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default)
    {
        if (deleteExisting)
        {
            foreach (var statement in BuildSchemaResetStatements())
            {
                await RunWriteAsync(statement, new Dictionary<string, object?>(), cancellationToken).ConfigureAwait(false);
            }
        }

        foreach (var statement in BuildSchemaStatements())
        {
            await RunWriteAsync(statement, new Dictionary<string, object?>(), cancellationToken).ConfigureAwait(false);
        }
    }

    internal static IReadOnlyList<string> BuildSchemaStatements() =>
        Neo4jSchemaStatements;

    internal static IReadOnlyList<string> BuildSchemaResetStatements() =>
        Neo4jSchemaResetStatements;

    public override async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (!_ownsDriver)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            return;
        }

        await _driver.DisposeAsync().ConfigureAwait(false);
    }

    public override IGraphDriver Clone(string database) =>
        new Neo4jGraphDriver(_driver, Uri, User, Password, database, ownsDriver: false);

    public override async Task<IReadOnlyList<string>> GetEntityGroupIdsAsync(CancellationToken cancellationToken = default)
    {
        var records = await RunReadAsync(
            """
            MATCH (n:Entity)
            WHERE n.group_id IS NOT NULL
            RETURN DISTINCT n.group_id AS group_id
            ORDER BY group_id
            """,
            new Dictionary<string, object?>(),
            cancellationToken).ConfigureAwait(false);
        return records.Select(record => record["group_id"].As<string>()).ToList();
    }

    public override async Task<IReadOnlyList<string>> GetCommunityGroupIdsAsync(CancellationToken cancellationToken = default)
    {
        var records = await RunReadAsync(
            """
            MATCH (n:Community)
            WHERE n.group_id IS NOT NULL
            RETURN DISTINCT n.group_id AS group_id
            ORDER BY group_id
            """,
            new Dictionary<string, object?>(),
            cancellationToken).ConfigureAwait(false);
        return records.Select(record => record["group_id"].As<string>()).ToList();
    }

    public override Task SaveNodeAsync(Node node, CancellationToken cancellationToken = default)
    {
        var (query, parameters) = Neo4jStatementBuilder.BuildNodeSave(node);

        return RunWriteAsync(query, parameters, cancellationToken);
    }

    public override Task SaveEdgeAsync(Edge edge, CancellationToken cancellationToken = default)
    {
        var (query, parameters) = Neo4jStatementBuilder.BuildEdgeSave(edge);

        return RunWriteAsync(query, parameters, cancellationToken);
    }

    public override async Task SaveBulkAsync(
        IEnumerable<EpisodicNode> episodicNodes,
        IEnumerable<EpisodicEdge> episodicEdges,
        IEnumerable<EntityNode> entityNodes,
        IEnumerable<EntityEdge> entityEdges,
        IEmbedderClient embedder,
        CancellationToken cancellationToken = default)
    {
        var episodeList = episodicNodes.ToList();
        var episodicEdgeList = episodicEdges.ToList();
        var entityNodeList = entityNodes.ToList();
        var entityEdgeList = entityEdges.ToList();

        await EnsureEntityNodeEmbeddingsAsync(entityNodeList, embedder, cancellationToken).ConfigureAwait(false);
        await EnsureEntityEdgeEmbeddingsAsync(entityEdgeList, embedder, cancellationToken).ConfigureAwait(false);

        var statements = Neo4jStatementBuilder.BuildBulkSaveStatements(
            episodeList,
            episodicEdgeList,
            entityNodeList,
            entityEdgeList);
        if (statements.Count == 0)
        {
            return;
        }

        await RunWritesAsync(statements, cancellationToken).ConfigureAwait(false);
    }

    public override Task DeleteNodeAsync(string uuid, CancellationToken cancellationToken = default) =>
        RunWriteAsync("MATCH (n {uuid: $uuid}) DETACH DELETE n", new Dictionary<string, object?> { ["uuid"] = uuid }, cancellationToken);

    public override async Task DeleteNodesByGroupIdAsync(
        string groupId,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var statement = Neo4jStatementBuilder.BuildDeleteNodesByGroupIdStatement(groupId, batchSize);
        while (true)
        {
            var deleted = await RunWriteInt64Async(
                statement.Query,
                statement.Parameters,
                "deleted",
                cancellationToken).ConfigureAwait(false);
            if (deleted < batchSize)
            {
                return;
            }
        }
    }

    public override async Task DeleteNodesByUuidsAsync(
        IEnumerable<string> uuids,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        foreach (var statement in Neo4jStatementBuilder.BuildDeleteNodesByUuidsStatements(uuids, batchSize))
        {
            await RunWriteAsync(statement.Query, statement.Parameters, cancellationToken).ConfigureAwait(false);
        }
    }

    public override Task DeleteEdgeAsync(string uuid, CancellationToken cancellationToken = default) =>
        RunWriteAsync(
            "MATCH ()-[e {uuid: $uuid}]-() DELETE e",
            new Dictionary<string, object?> { ["uuid"] = uuid },
            cancellationToken);

    public override Task DeleteEdgesByUuidsAsync(IEnumerable<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        cancellationToken.ThrowIfCancellationRequested();
        var uuidList = uuids.ToList();
        return uuidList.Count == 0
            ? Task.CompletedTask
            : RunWriteAsync(
            "MATCH ()-[e]-() WHERE e.uuid IN $uuids DELETE e",
            new Dictionary<string, object?> { ["uuids"] = uuidList },
            cancellationToken);
    }

    public override Task ClearDataAsync(IReadOnlyList<string>? groupIds = null, CancellationToken cancellationToken = default) =>
        groupIds is null
            ? RunWriteAsync("MATCH (n) DETACH DELETE n", new Dictionary<string, object?>(), cancellationToken)
            : RunWriteAsync(
                "MATCH (n) WHERE n.group_id IN $group_ids DETACH DELETE n",
                new Dictionary<string, object?> { ["group_ids"] = groupIds.ToList() },
                cancellationToken);

    public override async Task<TNode> GetNodeByUuidAsync<TNode>(string uuid, CancellationToken cancellationToken = default)
    {
        var label = Neo4jStatementBuilder.LabelFor<TNode>();
        var records = await RunReadAsync(
            $"MATCH (n:{label} {{uuid: $uuid}}) RETURN n LIMIT 1",
            new Dictionary<string, object?> { ["uuid"] = uuid },
            cancellationToken).ConfigureAwait(false);

        if (records.Count == 0)
        {
            throw new NodeNotFoundException(uuid);
        }

        return (TNode)Neo4jRecordMapper.MapNode(records[0]["n"].As<INode>());
    }

    public override async Task<IReadOnlyList<TNode>> GetNodesByUuidsAsync<TNode>(
        IEnumerable<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        var label = Neo4jStatementBuilder.LabelFor<TNode>();
        var query = groupId is null
            ? $"MATCH (n:{label}) WHERE n.uuid IN $uuids RETURN n"
            : $"MATCH (n:{label}) WHERE n.uuid IN $uuids AND n.group_id = $group_id RETURN n";
        var records = await RunReadAsync(
            query,
            new Dictionary<string, object?> { ["uuids"] = uuids.ToList(), ["group_id"] = groupId },
            cancellationToken).ConfigureAwait(false);

        return records.Select(record => (TNode)Neo4jRecordMapper.MapNode(record["n"].As<INode>())).ToList();
    }

    public override async Task<IReadOnlyList<TNode>> GetNodesByGroupIdsAsync<TNode>(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        var label = Neo4jStatementBuilder.LabelFor<TNode>();
        var cursorClause = uuidCursor is null ? "" : "AND n.uuid < $uuid_cursor";
        var limitClause = limit is null ? "" : "LIMIT $limit";
        var returnClause = Neo4jStatementBuilder.BuildNodeGroupReturnClause<TNode>(withEmbeddings);
        var records = await RunReadAsync(
            $"""
            MATCH (n:{label})
            WHERE n.group_id IN $group_ids {cursorClause}
            {returnClause}
            ORDER BY n.uuid DESC
            {limitClause}
            """,
            new Dictionary<string, object?> { ["group_ids"] = groupIds.ToList(), ["uuid_cursor"] = uuidCursor, ["limit"] = limit },
            cancellationToken).ConfigureAwait(false);

        return records
            .Select(record => Neo4jRecordMapper.ProjectNodeEmbedding((TNode)Neo4jRecordMapper.MapNode(record, "n"), withEmbeddings))
            .ToList();
    }

    public override async Task<T> GetEdgeByUuidAsync<T>(string uuid, CancellationToken cancellationToken = default)
    {
        var records = await QueryEdgesAsync<T>("e.uuid = $uuid", new Dictionary<string, object?> { ["uuid"] = uuid }, null, cancellationToken).ConfigureAwait(false);
        if (records.Count == 0)
        {
            throw new EdgeNotFoundException(uuid);
        }

        return records[0];
    }

    public override Task<IReadOnlyList<T>> GetEdgesByUuidsAsync<T>(IEnumerable<string> uuids, CancellationToken cancellationToken = default) =>
        QueryEdgesAsync<T>("e.uuid IN $uuids", new Dictionary<string, object?> { ["uuids"] = uuids.ToList() }, null, cancellationToken);

    public override Task<IReadOnlyList<T>> GetEdgesByGroupIdsAsync<T>(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        var where = uuidCursor is null
            ? "e.group_id IN $group_ids"
            : "e.group_id IN $group_ids AND e.uuid < $uuid_cursor";
        return QueryEdgesAsync<T>(
            where,
            new Dictionary<string, object?> { ["group_ids"] = groupIds.ToList(), ["uuid_cursor"] = uuidCursor },
            limit,
            withEmbeddings,
            cancellationToken);
    }

    public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesBetweenNodesAsync(
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default) =>
        QueryEdgesAsync<EntityEdge>(
            "source.uuid = $source_uuid AND target.uuid = $target_uuid",
            new Dictionary<string, object?> { ["source_uuid"] = sourceNodeUuid, ["target_uuid"] = targetNodeUuid },
            null,
            cancellationToken);

    public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesByNodeUuidAsync(string nodeUuid, CancellationToken cancellationToken = default) =>
        QueryEdgesAsync<EntityEdge>(
            "source.uuid = $node_uuid OR target.uuid = $node_uuid",
            new Dictionary<string, object?> { ["node_uuid"] = nodeUuid },
            null,
            cancellationToken);

    public override async Task<IReadOnlyList<EpisodicNode>> GetEpisodesByEntityNodeUuidAsync(string entityNodeUuid, CancellationToken cancellationToken = default)
    {
        var records = await RunReadAsync(
            """
            MATCH (episode:Episodic)-[:MENTIONS]->(:Entity {uuid: $uuid})
            RETURN DISTINCT episode AS n
            """,
            new Dictionary<string, object?> { ["uuid"] = entityNodeUuid },
            cancellationToken).ConfigureAwait(false);
        return records.Select(record => (EpisodicNode)Neo4jRecordMapper.MapNode(record["n"].As<INode>())).ToList();
    }

    public override async Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(
        DateTime referenceTime,
        int lastN,
        IReadOnlyList<string>? groupIds = null,
        EpisodeType? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default)
    {
        var statement = Neo4jStatementBuilder.BuildRetrieveEpisodesStatement(referenceTime, lastN, groupIds, source, saga);
        var records = await RunReadAsync(statement.Query, statement.Parameters, cancellationToken).ConfigureAwait(false);

        return records.Select(record => (EpisodicNode)Neo4jRecordMapper.MapNode(record["n"].As<INode>())).Reverse().ToList();
    }

    public override async Task<IReadOnlyList<EntityNode>> GetMentionedNodesAsync(IReadOnlyList<EpisodicNode> episodes, CancellationToken cancellationToken = default)
    {
        var records = await RunReadAsync(
            """
            MATCH (episode:Episodic)-[:MENTIONS]->(n:Entity)
            WHERE episode.uuid IN $uuids
            RETURN DISTINCT n
            """,
            new Dictionary<string, object?> { ["uuids"] = episodes.Select(episode => episode.Uuid).ToList() },
            cancellationToken).ConfigureAwait(false);
        return records.Select(record => (EntityNode)Neo4jRecordMapper.MapNode(record["n"].As<INode>())).ToList();
    }

    public override async Task<IReadOnlyList<CommunityNode>> GetCommunitiesByNodesAsync(IReadOnlyList<EntityNode> nodes, CancellationToken cancellationToken = default)
    {
        var records = await RunReadAsync(
            """
            MATCH (c:Community)-[:HAS_MEMBER]->(n:Entity)
            WHERE n.uuid IN $uuids
            RETURN DISTINCT c AS n
            """,
            new Dictionary<string, object?> { ["uuids"] = nodes.Select(node => node.Uuid).ToList() },
            cancellationToken).ConfigureAwait(false);
        return records.Select(record => (CommunityNode)Neo4jRecordMapper.MapNode(record["n"].As<INode>())).ToList();
    }

    public override async Task<SagaNode?> FindSagaByNameAsync(string name, string groupId, CancellationToken cancellationToken = default)
    {
        var records = await RunReadAsync(
            "MATCH (n:Saga {name: $name, group_id: $group_id}) RETURN n LIMIT 1",
            new Dictionary<string, object?> { ["name"] = name, ["group_id"] = groupId },
            cancellationToken).ConfigureAwait(false);
        return records.Count == 0 ? null : (SagaNode)Neo4jRecordMapper.MapNode(records[0]["n"].As<INode>());
    }

    public override async Task<string?> GetSagaPreviousEpisodeUuidAsync(string sagaUuid, string currentEpisodeUuid, CancellationToken cancellationToken = default)
    {
        var records = await RunReadAsync(
            """
            MATCH (:Saga {uuid: $saga_uuid})-[:HAS_EPISODE]->(e:Episodic)
            WHERE e.uuid <> $current_episode_uuid
            RETURN e.uuid AS uuid
            ORDER BY e.valid_at DESC, e.created_at DESC
            LIMIT 1
            """,
            new Dictionary<string, object?> { ["saga_uuid"] = sagaUuid, ["current_episode_uuid"] = currentEpisodeUuid },
            cancellationToken).ConfigureAwait(false);
        return records.Count == 0 ? null : records[0]["uuid"].As<string>();
    }

    public override async Task<IReadOnlyList<SagaEpisodeContent>> GetSagaEpisodeContentsAsync(
        string sagaUuid,
        DateTime? since = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var sinceFilter = since is null ? "" : "AND e.created_at > $since";
        var orderBy = since is null
            ? "ORDER BY e.valid_at DESC, e.created_at DESC"
            : "ORDER BY e.valid_at ASC, e.created_at ASC";
        var records = await RunReadAsync(
            "MATCH (:Saga {uuid: $saga_uuid})-[:HAS_EPISODE]->(e:Episodic)\n" +
            $"WHERE true {sinceFilter}\n" +
            "RETURN e.content AS content, e.valid_at AS valid_at\n" +
            $"{orderBy}\n" +
            "LIMIT $limit",
            new Dictionary<string, object?> { ["saga_uuid"] = sagaUuid, ["since"] = since, ["limit"] = limit },
            cancellationToken).ConfigureAwait(false);
        var chronologicalRecords = since is null ? records.AsEnumerable().Reverse() : records;
        return chronologicalRecords
            .Where(record => !string.IsNullOrEmpty(record["content"].As<string?>()))
            .Select(record => new SagaEpisodeContent(record["content"].As<string>(), GraphitiHelpers.ParseDbDate(record["valid_at"])))
            .ToList();
    }

    public async Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var fulltextQuery = SearchUtilities.FulltextQuery(query, groupIds, this);
        if (fulltextQuery.Length == 0)
        {
            return Array.Empty<SearchHit<EntityNode>>();
        }

        var statement = Neo4jStatementBuilder.BuildEntityNodeFulltextSearchStatement(
            fulltextQuery,
            searchFilter,
            groupIds,
            limit,
            Provider);
        var records = await RunReadAsync(
            statement.Query,
            statement.Parameters,
            cancellationToken).ConfigureAwait(false);

        return records
            .Select(record => new SearchHit<EntityNode>((EntityNode)Neo4jRecordMapper.MapNode(record["n"].As<INode>()), GetScore(record)))
            .ToList();
    }

    public async Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        CancellationToken cancellationToken = default)
    {
        var statement = Neo4jStatementBuilder.BuildEntityNodeEmbeddingSearchStatement(
            searchVector,
            searchFilter,
            groupIds,
            limit,
            minScore,
            Provider);
        var records = await RunReadAsync(
            statement.Query,
            statement.Parameters,
            cancellationToken).ConfigureAwait(false);

        return records
            .Select(record => new SearchHit<EntityNode>((EntityNode)Neo4jRecordMapper.MapNode(record["n"].As<INode>()), GetScore(record)))
            .ToList();
    }

    public async Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var fulltextQuery = SearchUtilities.FulltextQuery(query, groupIds, this);
        if (fulltextQuery.Length == 0)
        {
            return Array.Empty<SearchHit<EntityEdge>>();
        }

        var statement = Neo4jStatementBuilder.BuildEntityEdgeFulltextSearchStatement(
            fulltextQuery,
            searchFilter,
            groupIds,
            limit,
            Provider);
        var records = await RunReadAsync(
            statement.Query,
            statement.Parameters,
            cancellationToken).ConfigureAwait(false);

        return records
            .Select(record => new SearchHit<EntityEdge>(
                (EntityEdge)Neo4jRecordMapper.MapEdge(
                    record["e"].As<IRelationship>(),
                    record["source_uuid"].As<string>(),
                    record["target_uuid"].As<string>(),
                    typeof(EntityEdge)),
                GetScore(record)))
            .ToList();
    }

    public async Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        string? sourceNodeUuid = null,
        string? targetNodeUuid = null,
        CancellationToken cancellationToken = default)
    {
        var statement = Neo4jStatementBuilder.BuildEntityEdgeEmbeddingSearchStatement(
            searchVector,
            searchFilter,
            groupIds,
            limit,
            minScore,
            Provider,
            sourceNodeUuid,
            targetNodeUuid);
        var records = await RunReadAsync(
            statement.Query,
            statement.Parameters,
            cancellationToken).ConfigureAwait(false);

        return records
            .Select(record => new SearchHit<EntityEdge>(
                (EntityEdge)Neo4jRecordMapper.MapEdge(
                    record["e"].As<IRelationship>(),
                    record["source_uuid"].As<string>(),
                    record["target_uuid"].As<string>(),
                    typeof(EntityEdge)),
                GetScore(record)))
            .ToList();
    }

    public async Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesBfsAsync(
        IReadOnlyList<string>? originNodeUuids,
        SearchFilters searchFilter,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (originNodeUuids is null || originNodeUuids.Count == 0 || maxDepth < 1)
        {
            return Array.Empty<SearchHit<EntityNode>>();
        }

        var statement = Neo4jStatementBuilder.BuildEntityNodeBfsSearchStatement(
            originNodeUuids,
            searchFilter,
            maxDepth,
            groupIds,
            limit,
            Provider);
        var records = await RunReadAsync(
            statement.Query,
            statement.Parameters,
            cancellationToken).ConfigureAwait(false);

        return records
            .Select(record => new SearchHit<EntityNode>(
                (EntityNode)Neo4jRecordMapper.MapNode(record["n"].As<INode>()),
                GetScore(record)))
            .ToList();
    }

    public async Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesBfsAsync(
        IReadOnlyList<string>? originNodeUuids,
        SearchFilters searchFilter,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (originNodeUuids is null || originNodeUuids.Count == 0 || maxDepth < 1)
        {
            return Array.Empty<SearchHit<EntityEdge>>();
        }

        var statement = Neo4jStatementBuilder.BuildEntityEdgeBfsSearchStatement(
            originNodeUuids,
            searchFilter,
            maxDepth,
            groupIds,
            limit,
            Provider);
        var records = await RunReadAsync(
            statement.Query,
            statement.Parameters,
            cancellationToken).ConfigureAwait(false);

        return records
            .Select(record => new SearchHit<EntityEdge>(
                (EntityEdge)Neo4jRecordMapper.MapEdge(
                    record["e"].As<IRelationship>(),
                    record["source_uuid"].As<string>(),
                    record["target_uuid"].As<string>(),
                    typeof(EntityEdge)),
                GetScore(record)))
            .ToList();
    }

    public async Task<IReadOnlyList<SearchHit<EpisodicNode>>> SearchEpisodesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        _ = searchFilter;
        var fulltextQuery = SearchUtilities.FulltextQuery(query, groupIds, this);
        if (fulltextQuery.Length == 0)
        {
            return Array.Empty<SearchHit<EpisodicNode>>();
        }

        var statement = Neo4jStatementBuilder.BuildEpisodeFulltextSearchStatement(
            fulltextQuery,
            groupIds,
            limit);
        var records = await RunReadAsync(
            statement.Query,
            statement.Parameters,
            cancellationToken).ConfigureAwait(false);

        return records
            .Select(record => new SearchHit<EpisodicNode>((EpisodicNode)Neo4jRecordMapper.MapNode(record["n"].As<INode>()), GetScore(record)))
            .ToList();
    }

    public async Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesFulltextAsync(
        string query,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var fulltextQuery = SearchUtilities.FulltextQuery(query, groupIds, this);
        if (fulltextQuery.Length == 0)
        {
            return Array.Empty<SearchHit<CommunityNode>>();
        }

        var statement = Neo4jStatementBuilder.BuildCommunityFulltextSearchStatement(
            fulltextQuery,
            groupIds,
            limit);
        var records = await RunReadAsync(
            statement.Query,
            statement.Parameters,
            cancellationToken).ConfigureAwait(false);

        return records
            .Select(record => new SearchHit<CommunityNode>((CommunityNode)Neo4jRecordMapper.MapNode(record["n"].As<INode>()), GetScore(record)))
            .ToList();
    }

    public async Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        CancellationToken cancellationToken = default)
    {
        var statement = Neo4jStatementBuilder.BuildCommunityEmbeddingSearchStatement(
            searchVector,
            groupIds,
            limit,
            minScore);
        var records = await RunReadAsync(
            statement.Query,
            statement.Parameters,
            cancellationToken).ConfigureAwait(false);

        return records
            .Select(record => new SearchHit<CommunityNode>((CommunityNode)Neo4jRecordMapper.MapNode(record["n"].As<INode>()), GetScore(record)))
            .ToList();
    }

    public async Task<IReadOnlyList<SearchRank>> RankNodeDistanceAsync(
        IReadOnlyList<string> nodeUuids,
        string centerNodeUuid,
        float minScore = 0,
        CancellationToken cancellationToken = default)
    {
        if (nodeUuids.Count == 0)
        {
            return Array.Empty<SearchRank>();
        }

        var filteredUuids = nodeUuids.Where(uuid => uuid != centerNodeUuid).Distinct(StringComparer.Ordinal).ToList();
        var statement = Neo4jStatementBuilder.BuildNodeDistanceRankStatement(filteredUuids, centerNodeUuid);
        var records = await RunReadAsync(
            statement.Query,
            statement.Parameters,
            cancellationToken).ConfigureAwait(false);

        var scoreByUuid = filteredUuids.ToDictionary(uuid => uuid, _ => 0f, StringComparer.Ordinal);
        foreach (var record in records)
        {
            scoreByUuid[record["uuid"].As<string>()] = GetScore(record);
        }

        if (nodeUuids.Contains(centerNodeUuid, StringComparer.Ordinal))
        {
            scoreByUuid[centerNodeUuid] = 10f;
        }

        var indexByUuid = nodeUuids
            .Select((uuid, index) => (uuid, index))
            .GroupBy(item => item.uuid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);

        return scoreByUuid
            .Where(pair => pair.Value >= minScore)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => indexByUuid.GetValueOrDefault(pair.Key, int.MaxValue))
            .Select(pair => new SearchRank(pair.Key, pair.Value))
            .ToList();
    }

    public async Task<IReadOnlyList<SearchRank>> RankNodeEpisodeMentionsAsync(
        IReadOnlyList<string> nodeUuids,
        float minScore = 0,
        CancellationToken cancellationToken = default)
    {
        if (nodeUuids.Count == 0)
        {
            return Array.Empty<SearchRank>();
        }

        var distinctUuids = nodeUuids.Distinct(StringComparer.Ordinal).ToList();
        var statement = Neo4jStatementBuilder.BuildNodeEpisodeMentionsRankStatement(distinctUuids);
        var records = await RunReadAsync(
            statement.Query,
            statement.Parameters,
            cancellationToken).ConfigureAwait(false);

        var scoreByUuid = distinctUuids.ToDictionary(uuid => uuid, _ => float.PositiveInfinity, StringComparer.Ordinal);
        foreach (var record in records)
        {
            scoreByUuid[record["uuid"].As<string>()] = GetScore(record);
        }

        var indexByUuid = nodeUuids
            .Select((uuid, index) => (uuid, index))
            .GroupBy(item => item.uuid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);

        return scoreByUuid
            .Where(pair => pair.Value >= minScore)
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => indexByUuid.GetValueOrDefault(pair.Key, int.MaxValue))
            .Select(pair => new SearchRank(pair.Key, pair.Value))
            .ToList();
    }

    private async Task<IReadOnlyList<T>> QueryEdgesAsync<T>(
        string whereClause,
        Dictionary<string, object?> parameters,
        int? limit,
        CancellationToken cancellationToken)
        where T : Edge =>
        await QueryEdgesAsync<T>(whereClause, parameters, limit, withEmbeddings: true, cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<T>> QueryEdgesAsync<T>(
        string whereClause,
        Dictionary<string, object?> parameters,
        int? limit,
        bool withEmbeddings,
        CancellationToken cancellationToken)
        where T : Edge
    {
        var (relationshipType, sourceLabel, targetLabel) = Neo4jStatementBuilder.EdgeShapeFor<T>();
        parameters["limit"] = limit;
        var limitClause = limit is null ? "" : "LIMIT $limit";
        var returnClause = Neo4jStatementBuilder.BuildEdgeReturnClause<T>(withEmbeddings);
        var records = await RunReadAsync(
            $"""
            {Neo4jStatementBuilder.BuildEdgeMatchPattern(relationshipType, sourceLabel, targetLabel)}
            WHERE {whereClause}
            {returnClause}
            ORDER BY e.uuid DESC
            {limitClause}
            """,
            parameters,
            cancellationToken).ConfigureAwait(false);

        return records
            .Select(record => Neo4jRecordMapper.ProjectEdgeEmbedding((T)Neo4jRecordMapper.MapEdge(
                record,
                "e",
                record["source_uuid"].As<string>(),
                record["target_uuid"].As<string>(),
                typeof(T)), withEmbeddings))
            .ToList();
    }

    private static float GetScore(IRecord record)
    {
        var value = record["score"];
        return value switch
        {
            float score => score,
            double score => (float)score,
            int score => score,
            long score => score,
            _ => Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private Task<IReadOnlyList<IRecord>> RunReadAsync(
        string query,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        _sessionExecutor.RunReadAsync(query, parameters, cancellationToken);

    private Task RunWriteAsync(
        string query,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken) =>
        _sessionExecutor.RunWriteAsync(query, parameters, cancellationToken);

    private Task<long> RunWriteInt64Async(
        string query,
        Dictionary<string, object?> parameters,
        string column,
        CancellationToken cancellationToken) =>
        _sessionExecutor.RunWriteInt64Async(query, parameters, column, cancellationToken);

    private Task RunWritesAsync(
        IReadOnlyList<Neo4jStatement> statements,
        CancellationToken cancellationToken) =>
        _sessionExecutor.RunWritesAsync(statements, cancellationToken);

    private static string CreateSchemaDropStatement(string createStatement)
    {
        const string constraintPrefix = "CREATE CONSTRAINT ";
        const string fullTextIndexPrefix = "CREATE FULLTEXT INDEX ";
        const string indexPrefix = "CREATE INDEX ";

        if (createStatement.StartsWith(constraintPrefix, StringComparison.Ordinal))
        {
            return $"DROP CONSTRAINT {ExtractSchemaName(createStatement, constraintPrefix)} IF EXISTS";
        }

        if (createStatement.StartsWith(fullTextIndexPrefix, StringComparison.Ordinal))
        {
            return $"DROP INDEX {ExtractSchemaName(createStatement, fullTextIndexPrefix)} IF EXISTS";
        }

        if (createStatement.StartsWith(indexPrefix, StringComparison.Ordinal))
        {
            return $"DROP INDEX {ExtractSchemaName(createStatement, indexPrefix)} IF EXISTS";
        }

        throw new InvalidOperationException($"Unsupported Neo4j schema statement: {createStatement}");
    }

    private static string ExtractSchemaName(string createStatement, string prefix)
    {
        var start = prefix.Length;
        var end = createStatement.IndexOf(" IF NOT EXISTS", start, StringComparison.Ordinal);
        if (end <= start)
        {
            throw new InvalidOperationException($"Could not parse Neo4j schema statement: {createStatement}");
        }

        return createStatement[start..end];
    }

}
