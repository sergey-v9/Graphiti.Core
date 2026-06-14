namespace Graphiti.Core.Drivers.Ladybug;

/// <summary>
/// LadybugDB/Kuzu driver core over an abstract statement executor.
/// </summary>
internal sealed class LadybugGraphDriver : GraphDriverBase, ISearchGraphDriver, IEmbeddingLoadGraphDriver
{
    private readonly SharedState _shared;
    private readonly ILadybugQueryExecutor _executor;
    private readonly LadybugSearchExecutor _search;
    private readonly bool _canClone;
    private readonly bool _ownsExecutor;
    private bool _closed;

    internal LadybugGraphDriver(ILadybugQueryExecutor executor, string database = "")
        : this(new SharedState(executor), database, canClone: false, ownsExecutor: true)
    {
    }

    internal LadybugGraphDriver(Func<string, ILadybugQueryExecutor> executorFactory, string database = "")
        : this(new SharedState(executorFactory(database)), database, canClone: true, ownsExecutor: true)
    {
    }

    private LadybugGraphDriver(
        SharedState shared,
        string database,
        bool canClone,
        bool ownsExecutor)
        : base(GraphProvider.LadybugDb, database)
    {
        ArgumentNullException.ThrowIfNull(shared);
        _shared = shared;
        _executor = shared.Executor;
        _search = shared.Search;
        _canClone = canClone;
        _ownsExecutor = ownsExecutor;
    }

    /// <inheritdoc />
    public override async Task BuildIndicesAndConstraintsAsync(
        bool deleteExisting = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_shared.SchemaBuilt)
        {
            return;
        }

        await _shared.SchemaLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shared.SchemaBuilt)
            {
                return;
            }

            await _executor.ExecuteAsync(
                new LadybugStatement(
                    LadybugSchema.SchemaQueries,
                    new Dictionary<string, object?>(StringComparer.Ordinal)),
                cancellationToken).ConfigureAwait(false);
            await _executor.ExecuteAsync(
                new LadybugStatement(
                    "INSTALL FTS;",
                    new Dictionary<string, object?>(StringComparer.Ordinal)),
                cancellationToken).ConfigureAwait(false);
            await _executor.ExecuteAsync(
                new LadybugStatement(
                    "LOAD EXTENSION FTS;",
                    new Dictionary<string, object?>(StringComparer.Ordinal)),
                cancellationToken).ConfigureAwait(false);
            await ExecuteFulltextIndexStatementsAsync(cancellationToken).ConfigureAwait(false);
            _shared.SchemaBuilt = true;
        }
        finally
        {
            _shared.SchemaLock.Release();
        }
    }

    /// <inheritdoc />
    public override async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_closed)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _closed = true;
        if (!_ownsExecutor)
        {
            return;
        }

        await _executor.DisposeAsync().ConfigureAwait(false);
        _shared.SchemaLock.Dispose();
    }

    /// <inheritdoc />
    public override IGraphDriver Clone(string database)
    {
        if (!_canClone)
        {
            throw new NotSupportedException("Cloning a Ladybug graph driver requires an executor factory.");
        }

        return new LadybugGraphDriver(_shared, database, _canClone, ownsExecutor: false);
    }

    private sealed class SharedState
    {
        internal SharedState(ILadybugQueryExecutor executor)
        {
            ArgumentNullException.ThrowIfNull(executor);
            Executor = executor;
            Search = new LadybugSearchExecutor(executor);
        }

        internal ILadybugQueryExecutor Executor { get; }
        internal LadybugSearchExecutor Search { get; }
        internal SemaphoreSlim SchemaLock { get; } = new(1, 1);
        internal bool SchemaBuilt { get; set; }
    }

    /// <inheritdoc />
    public override Task SaveNodeAsync(Node node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return _executor.ExecuteAsync(LadybugStatementBuilder.BuildNodeSave(node), cancellationToken);
    }

    /// <inheritdoc />
    public override Task SaveEdgeAsync(Edge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return _executor.ExecuteAsync(LadybugStatementBuilder.BuildEdgeSave(edge), cancellationToken);
    }

    /// <inheritdoc />
    public override async Task SaveBulkAsync(
        IEnumerable<EpisodicNode> episodicNodes,
        IEnumerable<EpisodicEdge> episodicEdges,
        IEnumerable<EntityNode> entityNodes,
        IEnumerable<EntityEdge> entityEdges,
        IEmbedderClient embedder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episodicNodes);
        ArgumentNullException.ThrowIfNull(episodicEdges);
        ArgumentNullException.ThrowIfNull(entityNodes);
        ArgumentNullException.ThrowIfNull(entityEdges);
        ArgumentNullException.ThrowIfNull(embedder);

        var episodeList = MaterializeWithCancellation(episodicNodes, cancellationToken);
        for (var i = 0; i < episodeList.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _executor.ExecuteAsync(
                LadybugStatementBuilder.BuildEpisodicNodeSave(episodeList[i]),
                cancellationToken).ConfigureAwait(false);
        }

        var entityList = MaterializeWithCancellation(entityNodes, cancellationToken);
        await EnsureEntityNodeEmbeddingsAsync(entityList, embedder, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < entityList.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _executor.ExecuteAsync(
                LadybugStatementBuilder.BuildEntityNodeSave(entityList[i]),
                cancellationToken).ConfigureAwait(false);
        }

        var episodicEdgeList = MaterializeWithCancellation(episodicEdges, cancellationToken);
        for (var i = 0; i < episodicEdgeList.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _executor.ExecuteAsync(
                LadybugStatementBuilder.BuildEpisodicEdgeSave(episodicEdgeList[i]),
                cancellationToken).ConfigureAwait(false);
        }

        var entityEdgeList = MaterializeWithCancellation(entityEdges, cancellationToken);
        await EnsureEntityEdgeEmbeddingsAsync(entityEdgeList, embedder, cancellationToken).ConfigureAwait(false);
        for (var i = 0; i < entityEdgeList.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _executor.ExecuteAsync(
                LadybugStatementBuilder.BuildEntityEdgeSave(entityEdgeList[i]),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override async Task DeleteNodeAsync(string uuid, CancellationToken cancellationToken = default)
    {
        await ExecuteAllAsync(
            LadybugStatementBuilder.BuildNodeDeleteByUuidStatements<EntityNode>(uuid),
            cancellationToken).ConfigureAwait(false);
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildNodeDeleteByUuidStatements<EpisodicNode>(uuid)[0],
            cancellationToken).ConfigureAwait(false);
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildNodeDeleteByUuidStatements<CommunityNode>(uuid)[0],
            cancellationToken).ConfigureAwait(false);
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildNodeDeleteByUuidStatements<SagaNode>(uuid)[0],
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task DeleteNodesByGroupIdAsync(
        string groupId,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        await ExecuteAllAsync(
            LadybugStatementBuilder.BuildNodesDeleteByGroupIdStatements<EntityNode>(groupId),
            cancellationToken).ConfigureAwait(false);
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildNodesDeleteByGroupIdStatements<EpisodicNode>(groupId)[0],
            cancellationToken).ConfigureAwait(false);
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildNodesDeleteByGroupIdStatements<CommunityNode>(groupId)[0],
            cancellationToken).ConfigureAwait(false);
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildNodesDeleteByGroupIdStatements<SagaNode>(groupId)[0],
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task DeleteNodesByUuidsAsync(
        IEnumerable<string> uuids,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var uuidList = MaterializeWithCancellation(uuids, cancellationToken);
        for (var batchStart = 0; batchStart < uuidList.Count;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchCount = Math.Min(batchSize, uuidList.Count - batchStart);
            var batch = CopyRange(uuidList, batchStart, batchCount);
            await ExecuteAllAsync(
                LadybugStatementBuilder.BuildNodesDeleteByUuidsStatements<EntityNode>(batch),
                cancellationToken).ConfigureAwait(false);
            await _executor.ExecuteAsync(
                LadybugStatementBuilder.BuildNodesDeleteByUuidsStatements<EpisodicNode>(batch)[0],
                cancellationToken).ConfigureAwait(false);
            await _executor.ExecuteAsync(
                LadybugStatementBuilder.BuildNodesDeleteByUuidsStatements<CommunityNode>(batch)[0],
                cancellationToken).ConfigureAwait(false);
            await _executor.ExecuteAsync(
                LadybugStatementBuilder.BuildNodesDeleteByUuidsStatements<SagaNode>(batch)[0],
                cancellationToken).ConfigureAwait(false);
            batchStart += batchCount;
        }
    }

    /// <inheritdoc />
    public override async Task DeleteEdgeAsync(string uuid, CancellationToken cancellationToken = default)
    {
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildEdgeDeleteByUuid<EntityEdge>(uuid),
            cancellationToken).ConfigureAwait(false);
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildEdgeDeleteByUuid<EpisodicEdge>(uuid),
            cancellationToken).ConfigureAwait(false);
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildEdgeDeleteByUuid<CommunityEdge>(uuid),
            cancellationToken).ConfigureAwait(false);
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildEdgeDeleteByUuid<HasEpisodeEdge>(uuid),
            cancellationToken).ConfigureAwait(false);
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildEdgeDeleteByUuid<NextEpisodeEdge>(uuid),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task DeleteEdgesByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        var edgeUuids = MaterializeWithCancellation(uuids, cancellationToken);
        if (edgeUuids.Count == 0)
        {
            return;
        }

        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildEdgesDeleteByUuids<EntityEdge>(edgeUuids),
            cancellationToken).ConfigureAwait(false);
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildEdgesDeleteByUuids<EpisodicEdge>(edgeUuids),
            cancellationToken).ConfigureAwait(false);
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildEdgesDeleteByUuids<CommunityEdge>(edgeUuids),
            cancellationToken).ConfigureAwait(false);
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildEdgesDeleteByUuids<HasEpisodeEdge>(edgeUuids),
            cancellationToken).ConfigureAwait(false);
        await _executor.ExecuteAsync(
            LadybugStatementBuilder.BuildEdgesDeleteByUuids<NextEpisodeEdge>(edgeUuids),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task ClearDataAsync(
        IReadOnlyList<string>? groupIds = null,
        CancellationToken cancellationToken = default)
    {
        if (groupIds is { Count: > 0 })
        {
            foreach (var groupId in groupIds)
            {
                await DeleteNodesByGroupIdAsync(groupId, cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await ExecuteAllAsync(
            new[]
            {
                new LadybugStatement(
                    """
                    MATCH (n:Entity)-[:RELATES_TO]->(r:RelatesToNode_)
                    DETACH DELETE r
                    """,
                    new Dictionary<string, object?>(StringComparer.Ordinal)),
                new LadybugStatement(
                    """
                    MATCH (n:Entity)
                    DETACH DELETE n
                    """,
                    new Dictionary<string, object?>(StringComparer.Ordinal)),
                new LadybugStatement(
                    """
                    MATCH (n:Episodic)
                    DETACH DELETE n
                    """,
                    new Dictionary<string, object?>(StringComparer.Ordinal)),
                new LadybugStatement(
                    """
                    MATCH (n:Community)
                    DETACH DELETE n
                    """,
                    new Dictionary<string, object?>(StringComparer.Ordinal)),
                new LadybugStatement(
                    """
                    MATCH (n:Saga)
                    DETACH DELETE n
                    """,
                    new Dictionary<string, object?>(StringComparer.Ordinal))
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<TNode> GetNodeByUuidAsync<TNode>(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildNodeGetByUuid<TNode>(uuid),
            cancellationToken).ConfigureAwait(false);
        if (records.Count == 0)
        {
            throw new NodeNotFoundException(uuid);
        }

        return MapNode<TNode>(records[0]);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<TNode>> GetNodesByUuidsAsync<TNode>(
        IEnumerable<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        var uuidList = MaterializeWithCancellation(uuids, cancellationToken);
        if (uuidList.Count == 0)
        {
            return Array.Empty<TNode>();
        }

        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildNodesGetByUuids<TNode>(uuidList),
            cancellationToken).ConfigureAwait(false);
        var nodes = new List<TNode>(records.Count);
        for (var i = 0; i < records.Count; i++)
        {
            var node = MapNode<TNode>(records[i]);
            if (groupId is null || string.Equals(node.GroupId, groupId, StringComparison.Ordinal))
            {
                nodes.Add(node);
            }
        }

        return nodes;
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<TNode>> GetNodesByGroupIdsAsync<TNode>(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildNodesGetByGroupIds<TNode>(
                MaterializeWithCancellation(groupIds, cancellationToken),
                limit,
                uuidCursor,
                withEmbeddings),
            cancellationToken).ConfigureAwait(false);
        return MapNodeRecords<TNode>(records);
    }

    /// <inheritdoc />
    public override async Task<T> GetEdgeByUuidAsync<T>(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildEdgeGetByUuid<T>(uuid),
            cancellationToken).ConfigureAwait(false);
        if (records.Count == 0)
        {
            throw new EdgeNotFoundException(uuid);
        }

        return MapEdge<T>(records[0]);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<T>> GetEdgesByUuidsAsync<T>(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        var uuidList = MaterializeWithCancellation(uuids, cancellationToken);
        if (uuidList.Count == 0)
        {
            return Array.Empty<T>();
        }

        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildEdgesGetByUuids<T>(uuidList),
            cancellationToken).ConfigureAwait(false);
        return MapEdgeRecords<T>(records);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<T>> GetEdgesByGroupIdsAsync<T>(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildEdgesGetByGroupIds<T>(
                MaterializeWithCancellation(groupIds, cancellationToken),
                limit,
                uuidCursor,
                withEmbeddings),
            cancellationToken).ConfigureAwait(false);
        return MapEdgeRecords<T>(records);
    }

    async Task<IReadOnlyDictionary<string, List<float>?>> IEmbeddingLoadGraphDriver
        .LoadEntityNodeEmbeddingsByUuidAsync(
            IReadOnlyList<string> nodeUuids,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodeUuids);
        if (nodeUuids.Count == 0)
        {
            return new Dictionary<string, List<float>?>(0, StringComparer.Ordinal);
        }

        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildNodesLoadEmbeddings<EntityNode>(nodeUuids),
            cancellationToken).ConfigureAwait(false);
        return BuildEmbeddingLookup(
            records,
            static record => LadybugRecordMapper.MapEntityNode(record).NameEmbedding);
    }

    async Task<IReadOnlyDictionary<string, List<float>?>> IEmbeddingLoadGraphDriver
        .LoadEntityEdgeEmbeddingsByUuidAsync(
            IReadOnlyList<string> edgeUuids,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edgeUuids);
        if (edgeUuids.Count == 0)
        {
            return new Dictionary<string, List<float>?>(0, StringComparer.Ordinal);
        }

        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildEntityEdgesLoadEmbeddings(edgeUuids),
            cancellationToken).ConfigureAwait(false);
        return BuildEmbeddingLookup(
            records,
            static record => LadybugRecordMapper.MapEntityEdge(record).FactEmbedding);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<EntityEdge>> GetEntityEdgesBetweenNodesAsync(
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildEntityEdgesBetweenNodesGet(sourceNodeUuid, targetNodeUuid),
            cancellationToken).ConfigureAwait(false);
        return MapRecords(records, LadybugRecordMapper.MapEntityEdge);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<EntityEdge>> GetEntityEdgesByNodeUuidAsync(
        string nodeUuid,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildEntityEdgesByNodeUuidGet(nodeUuid),
            cancellationToken).ConfigureAwait(false);
        return MapRecords(records, LadybugRecordMapper.MapEntityEdge);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<EpisodicNode>> GetEpisodesByEntityNodeUuidAsync(
        string entityNodeUuid,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildEpisodesByEntityNodeUuidGet(entityNodeUuid),
            cancellationToken).ConfigureAwait(false);
        return MapRecords(records, LadybugRecordMapper.MapEpisodicNode);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(
        DateTime referenceTime,
        int lastN,
        IReadOnlyList<string>? groupIds = null,
        EpisodeType? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildRetrieveEpisodes(referenceTime, lastN, groupIds, source, saga),
            cancellationToken).ConfigureAwait(false);
        var episodes = new List<EpisodicNode>(records.Count);
        for (var i = records.Count - 1; i >= 0; i--)
        {
            episodes.Add(LadybugRecordMapper.MapEpisodicNode(records[i]));
        }

        return episodes;
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<EntityNode>> GetMentionedNodesAsync(
        IReadOnlyList<EpisodicNode> episodes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var episodeUuids = new List<string>(episodes.Count);
        for (var i = 0; i < episodes.Count; i++)
        {
            episodeUuids.Add(episodes[i].Uuid);
        }

        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildMentionedNodesGet(episodeUuids),
            cancellationToken).ConfigureAwait(false);
        return MapRecords(records, LadybugRecordMapper.MapEntityNode);
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<CommunityNode>> GetCommunitiesByNodesAsync(
        IReadOnlyList<EntityNode> nodes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nodeUuids = new List<string>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            nodeUuids.Add(nodes[i].Uuid);
        }

        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildCommunitiesByNodesGet(nodeUuids),
            cancellationToken).ConfigureAwait(false);
        return MapRecords(records, LadybugRecordMapper.MapCommunityNode);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default) =>
        _search.SearchEntityNodesFulltextAsync(query, searchFilter, groupIds, limit, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        CancellationToken cancellationToken = default) =>
        _search.SearchEntityNodesByEmbeddingAsync(
            searchVector,
            searchFilter,
            groupIds,
            limit,
            minScore,
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default) =>
        _search.SearchEntityEdgesFulltextAsync(query, searchFilter, groupIds, limit, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        string? sourceNodeUuid = null,
        string? targetNodeUuid = null,
        CancellationToken cancellationToken = default) =>
        _search.SearchEntityEdgesByEmbeddingAsync(
            searchVector,
            searchFilter,
            groupIds,
            limit,
            minScore,
            sourceNodeUuid,
            targetNodeUuid,
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesBfsAsync(
        IReadOnlyList<string>? originNodeUuids,
        SearchFilters searchFilter,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default) =>
        _search.SearchEntityNodesBfsAsync(
            originNodeUuids,
            searchFilter,
            maxDepth,
            groupIds,
            limit,
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesBfsAsync(
        IReadOnlyList<string>? originNodeUuids,
        SearchFilters searchFilter,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default) =>
        _search.SearchEntityEdgesBfsAsync(
            originNodeUuids,
            searchFilter,
            maxDepth,
            groupIds,
            limit,
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<EpisodicNode>>> SearchEpisodesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default) =>
        _search.SearchEpisodesFulltextAsync(query, searchFilter, groupIds, limit, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesFulltextAsync(
        string query,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default) =>
        _search.SearchCommunitiesFulltextAsync(query, groupIds, limit, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        CancellationToken cancellationToken = default) =>
        _search.SearchCommunitiesByEmbeddingAsync(searchVector, groupIds, limit, minScore, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchRank>> RankNodeDistanceAsync(
        IReadOnlyList<string> nodeUuids,
        string centerNodeUuid,
        float minScore = 0,
        CancellationToken cancellationToken = default) =>
        _search.RankNodeDistanceAsync(nodeUuids, centerNodeUuid, minScore, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchRank>> RankNodeEpisodeMentionsAsync(
        IReadOnlyList<string> nodeUuids,
        float minScore = 0,
        CancellationToken cancellationToken = default) =>
        _search.RankNodeEpisodeMentionsAsync(nodeUuids, minScore, cancellationToken);

    /// <inheritdoc />
    public override async Task<SagaNode?> FindSagaByNameAsync(
        string name,
        string groupId,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildSagaByNameGet(name, groupId),
            cancellationToken).ConfigureAwait(false);
        return records.Count == 0 ? null : LadybugRecordMapper.MapSagaNode(records[0]);
    }

    /// <inheritdoc />
    public override async Task<string?> GetSagaPreviousEpisodeUuidAsync(
        string sagaUuid,
        string currentEpisodeUuid,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildSagaPreviousEpisodeUuidGet(sagaUuid, currentEpisodeUuid),
            cancellationToken).ConfigureAwait(false);
        return records.Count == 0 ? null : GetString(records[0], "uuid");
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<SagaEpisodeContent>> GetSagaEpisodeContentsAsync(
        string sagaUuid,
        DateTime? since = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildSagaEpisodeContentsGet(sagaUuid, since, limit),
            cancellationToken).ConfigureAwait(false);
        var contents = new List<SagaEpisodeContent>(records.Count);
        if (since is null)
        {
            for (var i = records.Count - 1; i >= 0; i--)
            {
                AddSagaEpisodeContent(records[i], contents);
            }
        }
        else
        {
            for (var i = 0; i < records.Count; i++)
            {
                AddSagaEpisodeContent(records[i], contents);
            }
        }

        return contents;
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<string>> GetEntityGroupIdsAsync(
        CancellationToken cancellationToken = default) =>
        await GetGroupIdsAsync<EntityNode>(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public override async Task<IReadOnlyList<string>> GetCommunityGroupIdsAsync(
        CancellationToken cancellationToken = default) =>
        await GetGroupIdsAsync<CommunityNode>(cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<string>> GetGroupIdsAsync<TNode>(CancellationToken cancellationToken)
        where TNode : Node
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildNodeGroupIdsGet<TNode>(),
            cancellationToken).ConfigureAwait(false);
        var groupIds = new List<string>(records.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < records.Count; i++)
        {
            var groupId = GetString(records[i], "group_id");
            if (!string.IsNullOrEmpty(groupId) && seen.Add(groupId))
            {
                groupIds.Add(groupId);
            }
        }

        return groupIds;
    }

    private async Task ExecuteAllAsync(
        IEnumerable<LadybugStatement> statements,
        CancellationToken cancellationToken)
    {
        foreach (var statement in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _executor.ExecuteAsync(statement, cancellationToken).ConfigureAwait(false);
        }
    }

    private static List<TNode> MapNodeRecords<TNode>(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records)
        where TNode : Node
    {
        var nodes = new List<TNode>(records.Count);
        for (var i = 0; i < records.Count; i++)
        {
            nodes.Add(MapNode<TNode>(records[i]));
        }

        return nodes;
    }

    private static List<TEdge> MapEdgeRecords<TEdge>(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records)
        where TEdge : Edge
    {
        var edges = new List<TEdge>(records.Count);
        for (var i = 0; i < records.Count; i++)
        {
            edges.Add(MapEdge<TEdge>(records[i]));
        }

        return edges;
    }

    private static List<T> MapRecords<T>(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records,
        Func<IReadOnlyDictionary<string, object?>, T> mapper)
    {
        var results = new List<T>(records.Count);
        for (var i = 0; i < records.Count; i++)
        {
            results.Add(mapper(records[i]));
        }

        return results;
    }

    private static Dictionary<string, List<float>?> BuildEmbeddingLookup(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> records,
        Func<IReadOnlyDictionary<string, object?>, List<float>?> getEmbedding)
    {
        var embeddings = new Dictionary<string, List<float>?>(records.Count, StringComparer.Ordinal);
        for (var i = 0; i < records.Count; i++)
        {
            var uuid = GetString(records[i], "uuid");
            if (uuid is not null)
            {
                embeddings[uuid] = getEmbedding(records[i]);
            }
        }

        return embeddings;
    }

    private static void AddSagaEpisodeContent(
        IReadOnlyDictionary<string, object?> record,
        List<SagaEpisodeContent> contents)
    {
        var content = GetString(record, "content") ?? string.Empty;
        if (content.Length == 0)
        {
            return;
        }

        contents.Add(new SagaEpisodeContent(
            content,
            record.TryGetValue("valid_at", out var validAt) ? GraphitiHelpers.ParseDbDate(validAt) : null));
    }

    private static TNode MapNode<TNode>(IReadOnlyDictionary<string, object?> record)
        where TNode : Node =>
        typeof(TNode) == typeof(EntityNode) ? (TNode)(Node)LadybugRecordMapper.MapEntityNode(record) :
        typeof(TNode) == typeof(EpisodicNode) ? (TNode)(Node)LadybugRecordMapper.MapEpisodicNode(record) :
        typeof(TNode) == typeof(CommunityNode) ? (TNode)(Node)LadybugRecordMapper.MapCommunityNode(record) :
        typeof(TNode) == typeof(SagaNode) ? (TNode)(Node)LadybugRecordMapper.MapSagaNode(record) :
        throw new ArgumentOutOfRangeException(typeof(TNode).Name);

    private static TEdge MapEdge<TEdge>(IReadOnlyDictionary<string, object?> record)
        where TEdge : Edge =>
        typeof(TEdge) == typeof(EntityEdge) ? (TEdge)(Edge)LadybugRecordMapper.MapEntityEdge(record) :
        typeof(TEdge) == typeof(EpisodicEdge) ? (TEdge)(Edge)LadybugRecordMapper.MapEpisodicEdge(record) :
        typeof(TEdge) == typeof(CommunityEdge) ? (TEdge)(Edge)LadybugRecordMapper.MapCommunityEdge(record) :
        typeof(TEdge) == typeof(HasEpisodeEdge) ? (TEdge)(Edge)LadybugRecordMapper.MapHasEpisodeEdge(record) :
        typeof(TEdge) == typeof(NextEpisodeEdge) ? (TEdge)(Edge)LadybugRecordMapper.MapNextEpisodeEdge(record) :
        throw new ArgumentOutOfRangeException(typeof(TEdge).Name);

    private async Task ExecuteFulltextIndexStatementsAsync(CancellationToken cancellationToken)
    {
        var statements = LadybugSearchStatementBuilder.BuildFulltextIndexStatements();
        for (var i = 0; i < statements.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _executor.ExecuteAsync(statements[i], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsDuplicateFulltextIndexError(statements[i], exception))
            {
                // CREATE_FTS_INDEX has no IF NOT EXISTS/skip flag; public setup remains idempotent.
            }
        }
    }

    private static bool IsDuplicateFulltextIndexError(
        LadybugStatement statement,
        Exception exception)
    {
        var message = exception.Message;
        return IsDuplicateFulltextIndexError(statement, message, "Episodic", "episode_content") ||
            IsDuplicateFulltextIndexError(statement, message, "Entity", "node_name_and_summary") ||
            IsDuplicateFulltextIndexError(statement, message, "Community", "community_name") ||
            IsDuplicateFulltextIndexError(statement, message, "RelatesToNode_", "edge_name_and_fact");
    }

    private static bool IsDuplicateFulltextIndexError(
        LadybugStatement statement,
        string message,
        string tableName,
        string indexName) =>
        statement.Query.Contains(
            $"CREATE_FTS_INDEX('{tableName}', '{indexName}'",
            StringComparison.Ordinal) &&
        message.Contains(
            $"Index {indexName} already exists in table {tableName}.",
            StringComparison.Ordinal);

    private static List<T> MaterializeWithCancellation<T>(
        IEnumerable<T> values,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = values.TryGetNonEnumeratedCount(out var count)
            ? new List<T>(count)
            : new List<T>();
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            list.Add(value);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return list;
    }

    private static List<string> CopyRange(List<string> values, int start, int count)
    {
        var copy = new List<string>(count);
        var end = start + count;
        for (var i = start; i < end; i++)
        {
            copy.Add(values[i]);
        }

        return copy;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> record, string key)
    {
        if (!record.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value is string text ? text : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
