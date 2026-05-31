namespace Graphiti.Core.Drivers.Ladybug;

/// <summary>
/// Provider-ready LadybugDB/Kuzu driver core over an abstract statement executor. This intentionally
/// remains internal and unwired until the concrete LadybugDB package/native dependency decision is
/// made and search/runtime parity is proven.
/// </summary>
internal sealed class LadybugGraphDriver : GraphDriverBase
{
    private readonly ILadybugQueryExecutor _executor;
    private readonly Func<string, ILadybugQueryExecutor>? _executorFactory;
    private bool _closed;

    internal LadybugGraphDriver(ILadybugQueryExecutor executor, string database = "")
        : this(executor, executorFactory: null, database)
    {
    }

    internal LadybugGraphDriver(Func<string, ILadybugQueryExecutor> executorFactory, string database = "")
        : this(executorFactory(database), executorFactory, database)
    {
    }

    private LadybugGraphDriver(
        ILadybugQueryExecutor executor,
        Func<string, ILadybugQueryExecutor>? executorFactory,
        string database)
        : base(GraphProvider.Kuzu, database)
    {
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
        _executorFactory = executorFactory;
    }

    public override async Task BuildIndicesAndConstraintsAsync(
        bool deleteExisting = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _executor.ExecuteAsync(
            new LadybugStatement(
                LadybugSchema.SchemaQueries,
                new Dictionary<string, object?>(StringComparer.Ordinal)),
            cancellationToken).ConfigureAwait(false);
    }

    public override async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_closed)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _executor.DisposeAsync().ConfigureAwait(false);
        _closed = true;
    }

    public override IGraphDriver Clone(string database)
    {
        if (_executorFactory is null)
        {
            throw new NotSupportedException("Cloning a Ladybug graph driver requires an executor factory.");
        }

        return new LadybugGraphDriver(_executorFactory, database);
    }

    public override Task SaveNodeAsync(Node node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return _executor.ExecuteAsync(LadybugStatementBuilder.BuildNodeSave(node), cancellationToken);
    }

    public override Task SaveEdgeAsync(Edge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return _executor.ExecuteAsync(LadybugStatementBuilder.BuildEdgeSave(edge), cancellationToken);
    }

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
        await ExecuteAllAsync(
            episodeList.Select(LadybugStatementBuilder.BuildEpisodicNodeSave),
            cancellationToken).ConfigureAwait(false);

        var entityList = MaterializeWithCancellation(entityNodes, cancellationToken);
        await EnsureEntityNodeEmbeddingsAsync(entityList, embedder, cancellationToken).ConfigureAwait(false);
        await ExecuteAllAsync(
            entityList.Select(LadybugStatementBuilder.BuildEntityNodeSave),
            cancellationToken).ConfigureAwait(false);

        var episodicEdgeList = MaterializeWithCancellation(episodicEdges, cancellationToken);
        await ExecuteAllAsync(
            episodicEdgeList.Select(LadybugStatementBuilder.BuildEpisodicEdgeSave),
            cancellationToken).ConfigureAwait(false);

        var entityEdgeList = MaterializeWithCancellation(entityEdges, cancellationToken);
        await EnsureEntityEdgeEmbeddingsAsync(entityEdgeList, embedder, cancellationToken).ConfigureAwait(false);
        await ExecuteAllAsync(
            entityEdgeList.Select(LadybugStatementBuilder.BuildEntityEdgeSave),
            cancellationToken).ConfigureAwait(false);
    }

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

    public override async Task DeleteNodesByUuidsAsync(
        IEnumerable<string> uuids,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        foreach (var batch in MaterializeWithCancellation(uuids, cancellationToken).Chunk(batchSize))
        {
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
        }
    }

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
        var nodes = records.Select(MapNode<TNode>);
        if (groupId is not null)
        {
            nodes = nodes.Where(node => string.Equals(node.GroupId, groupId, StringComparison.Ordinal));
        }

        return nodes.ToList();
    }

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
        return records.Select(MapNode<TNode>).ToList();
    }

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
        return records.Select(MapEdge<T>).ToList();
    }

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
        return records.Select(MapEdge<T>).ToList();
    }

    public override async Task<IReadOnlyList<EntityEdge>> GetEntityEdgesBetweenNodesAsync(
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildEntityEdgesBetweenNodesGet(sourceNodeUuid, targetNodeUuid),
            cancellationToken).ConfigureAwait(false);
        return records.Select(LadybugRecordMapper.MapEntityEdge).ToList();
    }

    public override async Task<IReadOnlyList<EntityEdge>> GetEntityEdgesByNodeUuidAsync(
        string nodeUuid,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildEntityEdgesByNodeUuidGet(nodeUuid),
            cancellationToken).ConfigureAwait(false);
        return records.Select(LadybugRecordMapper.MapEntityEdge).ToList();
    }

    public override async Task<IReadOnlyList<EpisodicNode>> GetEpisodesByEntityNodeUuidAsync(
        string entityNodeUuid,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildEpisodesByEntityNodeUuidGet(entityNodeUuid),
            cancellationToken).ConfigureAwait(false);
        return records.Select(LadybugRecordMapper.MapEpisodicNode).ToList();
    }

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
        return records.Select(LadybugRecordMapper.MapEpisodicNode).Reverse().ToList();
    }

    public override async Task<IReadOnlyList<EntityNode>> GetMentionedNodesAsync(
        IReadOnlyList<EpisodicNode> episodes,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildMentionedNodesGet(episodes.Select(episode => episode.Uuid)),
            cancellationToken).ConfigureAwait(false);
        return records.Select(LadybugRecordMapper.MapEntityNode).ToList();
    }

    public override async Task<IReadOnlyList<CommunityNode>> GetCommunitiesByNodesAsync(
        IReadOnlyList<EntityNode> nodes,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildCommunitiesByNodesGet(nodes.Select(node => node.Uuid)),
            cancellationToken).ConfigureAwait(false);
        return records.Select(LadybugRecordMapper.MapCommunityNode).ToList();
    }

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

    public override async Task<IReadOnlyList<SagaEpisodeContent>> GetSagaEpisodeContentsAsync(
        string sagaUuid,
        DateTime? since = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildSagaEpisodeContentsGet(sagaUuid, since, limit),
            cancellationToken).ConfigureAwait(false);
        var chronologicalRecords = since is null ? records.AsEnumerable().Reverse() : records;
        return chronologicalRecords
            .Select(record => new SagaEpisodeContent(
                GetString(record, "content") ?? string.Empty,
                record.TryGetValue("valid_at", out var validAt) ? GraphitiHelpers.ParseDbDate(validAt) : null))
            .Where(content => content.Content.Length > 0)
            .ToList();
    }

    public override async Task<IReadOnlyList<string>> GetEntityGroupIdsAsync(
        CancellationToken cancellationToken = default) =>
        await GetGroupIdsAsync<EntityNode>(cancellationToken).ConfigureAwait(false);

    public override async Task<IReadOnlyList<string>> GetCommunityGroupIdsAsync(
        CancellationToken cancellationToken = default) =>
        await GetGroupIdsAsync<CommunityNode>(cancellationToken).ConfigureAwait(false);

    private async Task<IReadOnlyList<string>> GetGroupIdsAsync<TNode>(CancellationToken cancellationToken)
        where TNode : Node
    {
        var records = await _executor.QueryAsync(
            LadybugStatementBuilder.BuildNodeGroupIdsGet<TNode>(),
            cancellationToken).ConfigureAwait(false);
        return records
            .Select(record => GetString(record, "group_id"))
            .Where(groupId => !string.IsNullOrEmpty(groupId))
            .Distinct(StringComparer.Ordinal)
            .ToList()!;
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

    private static string? GetString(IReadOnlyDictionary<string, object?> record, string key)
    {
        if (!record.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value is string text ? text : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
