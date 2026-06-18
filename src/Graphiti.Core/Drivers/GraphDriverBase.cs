namespace Graphiti.Core.Drivers;

/// <summary>
/// Base implementation of <see cref="IGraphDriver"/> that provides shared bulk-save orchestration,
/// embedding backfill, and disposal, leaving backend-specific persistence and queries abstract.
/// </summary>
public abstract class GraphDriverBase : IGraphDriver
{
    private const int DefaultBulkSaveConcurrency = 8;

    /// <summary>Initializes base state and resolves the default group id for the provider.</summary>
    protected GraphDriverBase(GraphProvider provider, string database = "")
    {
        Provider = provider;
        Database = database;
        DefaultGroupId = GraphitiHelpers.GetDefaultGroupId(provider);
    }

    /// <inheritdoc />
    public GraphProvider Provider { get; }

    /// <inheritdoc />
    public string Database { get; protected set; }

    /// <inheritdoc />
    public string DefaultGroupId { get; protected set; }

    /// <summary>Maximum degree of parallelism used when saving items in bulk.</summary>
    protected virtual int BulkSaveConcurrency => DefaultBulkSaveConcurrency;

    /// <summary>Disposes the driver by closing its connection.</summary>
    public virtual async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Returns the group partitions that contain entity nodes. Defaults to the default group.</summary>
    public virtual Task<IReadOnlyList<string>> GetEntityGroupIdsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(new[] { DefaultGroupId });

    /// <summary>Returns the group partitions that contain communities. Defaults to the default group.</summary>
    public virtual Task<IReadOnlyList<string>> GetCommunityGroupIdsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(new[] { DefaultGroupId });

    /// <inheritdoc />
    public abstract Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public virtual Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public abstract Task CloseAsync(CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract IGraphDriver Clone(string database);

    /// <inheritdoc />
    public abstract Task SaveNodeAsync(Node node, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task SaveEdgeAsync(Edge edge, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public virtual async Task SaveBulkAsync(
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

        var episodicNodeList = MaterializeWithCancellation(episodicNodes, cancellationToken);
        await SaveNodesAsync(episodicNodeList, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var entityNodeList = MaterializeWithCancellation(entityNodes, cancellationToken);
        await EnsureEntityNodeEmbeddingsAsync(entityNodeList, embedder, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await SaveNodesAsync(entityNodeList, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var episodicEdgeList = MaterializeWithCancellation(episodicEdges, cancellationToken);
        await SaveEdgesAsync(episodicEdgeList, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var entityEdgeList = MaterializeWithCancellation(entityEdges, cancellationToken);
        await EnsureEntityEdgeEmbeddingsAsync(entityEdgeList, embedder, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await SaveEdgesAsync(entityEdgeList, cancellationToken).ConfigureAwait(false);
    }

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

    private Task SaveNodesAsync<TNode>(
        IReadOnlyList<TNode> nodes,
        CancellationToken cancellationToken)
        where TNode : Node =>
        SaveItemsAsync(nodes, SaveNodeAsync, cancellationToken);

    private Task SaveEdgesAsync<TEdge>(
        IReadOnlyList<TEdge> edges,
        CancellationToken cancellationToken)
        where TEdge : Edge =>
        SaveItemsAsync(edges, SaveEdgeAsync, cancellationToken);

    private async Task SaveItemsAsync<TItem>(
        IReadOnlyList<TItem> items,
        Func<TItem, CancellationToken, Task> saveAsync,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        if (items.Count == 1)
        {
            await saveAsync(items[0], cancellationToken).ConfigureAwait(false);
            return;
        }

        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Min(items.Count, Math.Max(1, BulkSaveConcurrency))
        };
        await Parallel.ForEachAsync(
            items,
            options,
            async (item, token) => await saveAsync(item, token).ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>Generates and assigns name embeddings for any entity nodes that lack one.</summary>
    protected static async Task EnsureEntityNodeEmbeddingsAsync(
        IReadOnlyList<EntityNode> nodes,
        IEmbedderClient embedder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(embedder);
        cancellationToken.ThrowIfCancellationRequested();
        var nodesMissingEmbeddings = new List<EntityNode>(nodes.Count);
        var inputs = new List<string>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = nodes[i];
            if (node.NameEmbedding is not null)
            {
                continue;
            }

            nodesMissingEmbeddings.Add(node);
            inputs.Add((node.Name ?? string.Empty).Replace('\n', ' '));
        }

        if (nodesMissingEmbeddings.Count == 0)
        {
            return;
        }

        var embeddings = EmbeddingVectorValidation.MaterializeBatch(
            await embedder.CreateBatchAsync(inputs, cancellationToken).ConfigureAwait(false),
            nodesMissingEmbeddings.Count,
            embedder.EmbeddingDimension,
            "entity node name embeddings",
            index => $"entity node '{nodesMissingEmbeddings[index].Name}' at index {index}");
        cancellationToken.ThrowIfCancellationRequested();
        for (var i = 0; i < nodesMissingEmbeddings.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            nodesMissingEmbeddings[i].NameEmbedding = embeddings[i];
        }
    }

    /// <summary>Generates and assigns fact embeddings for any entity edges that lack one.</summary>
    protected static async Task EnsureEntityEdgeEmbeddingsAsync(
        IReadOnlyList<EntityEdge> edges,
        IEmbedderClient embedder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(embedder);
        cancellationToken.ThrowIfCancellationRequested();
        var edgesMissingEmbeddings = new List<EntityEdge>(edges.Count);
        var inputs = new List<string>(edges.Count);
        for (var i = 0; i < edges.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var edge = edges[i];
            if (edge.FactEmbedding is not null)
            {
                continue;
            }

            edgesMissingEmbeddings.Add(edge);
            inputs.Add((edge.Fact ?? string.Empty).Replace('\n', ' '));
        }

        if (edgesMissingEmbeddings.Count == 0)
        {
            return;
        }

        var embeddings = EmbeddingVectorValidation.MaterializeBatch(
            await embedder.CreateBatchAsync(inputs, cancellationToken).ConfigureAwait(false),
            edgesMissingEmbeddings.Count,
            embedder.EmbeddingDimension,
            "entity edge fact embeddings",
            index => $"entity edge '{edgesMissingEmbeddings[index].Uuid}' at index {index}");
        cancellationToken.ThrowIfCancellationRequested();
        for (var i = 0; i < edgesMissingEmbeddings.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            edgesMissingEmbeddings[i].FactEmbedding = embeddings[i];
        }
    }

    /// <inheritdoc />
    public abstract Task DeleteNodeAsync(string uuid, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task DeleteNodesByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task DeleteNodesByUuidsAsync(IEnumerable<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task DeleteEdgeAsync(string uuid, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task DeleteEdgesByUuidsAsync(IEnumerable<string> uuids, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task ClearDataAsync(IReadOnlyList<string>? groupIds = null, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<TNode> GetNodeByUuidAsync<TNode>(string uuid, CancellationToken cancellationToken = default) where TNode : Node;

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<TNode>> GetNodesByUuidsAsync<TNode>(IEnumerable<string> uuids, string? groupId = null, CancellationToken cancellationToken = default) where TNode : Node;

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<TNode>> GetNodesByGroupIdsAsync<TNode>(IEnumerable<string> groupIds, int? limit = null, string? uuidCursor = null, bool withEmbeddings = false, CancellationToken cancellationToken = default) where TNode : Node;

    /// <inheritdoc />
    public abstract Task<T> GetEdgeByUuidAsync<T>(string uuid, CancellationToken cancellationToken = default) where T : Edge;

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<T>> GetEdgesByUuidsAsync<T>(IEnumerable<string> uuids, CancellationToken cancellationToken = default) where T : Edge;

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<T>> GetEdgesByGroupIdsAsync<T>(IEnumerable<string> groupIds, int? limit = null, string? uuidCursor = null, bool withEmbeddings = false, CancellationToken cancellationToken = default) where T : Edge;

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<EntityEdge>> GetEntityEdgesBetweenNodesAsync(string sourceNodeUuid, string targetNodeUuid, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<EntityEdge>> GetEntityEdgesByNodeUuidAsync(string nodeUuid, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<EpisodicNode>> GetEpisodesByEntityNodeUuidAsync(string entityNodeUuid, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(DateTime referenceTime, int lastN, IReadOnlyList<string>? groupIds = null, EpisodeType? source = null, string? saga = null, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<EntityNode>> GetMentionedNodesAsync(IReadOnlyList<EpisodicNode> episodes, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<CommunityNode>> GetCommunitiesByNodesAsync(IReadOnlyList<EntityNode> nodes, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<SagaNode?> FindSagaByNameAsync(string name, string groupId, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<string?> GetSagaPreviousEpisodeUuidAsync(string sagaUuid, string currentEpisodeUuid, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<SagaEpisodeContent>> GetSagaEpisodeContentsAsync(string sagaUuid, DateTime? since = null, int limit = 200, CancellationToken cancellationToken = default);
}
