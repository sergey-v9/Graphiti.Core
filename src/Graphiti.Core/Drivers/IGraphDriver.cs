namespace Graphiti.Core.Drivers;

/// <summary>
/// The graph storage contract Graphiti depends on. A driver persists and retrieves nodes and edges,
/// manages indices/constraints, and exposes the higher-level traversal and saga queries the
/// ingestion and search pipelines need. Implementations exist for Neo4j and an in-memory backend.
/// </summary>
public interface IGraphDriver : IAsyncDisposable
{
    /// <summary>The storage backend this driver targets.</summary>
    GraphProvider Provider { get; }

    /// <summary>Name of the database the driver operates against.</summary>
    string Database { get; }

    /// <summary>Default graph partition id used when callers omit one.</summary>
    string DefaultGroupId { get; }

    /// <summary>Creates required indices and constraints, optionally dropping existing ones first.</summary>
    Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default);

    /// <summary>Closes the underlying connection/session.</summary>
    Task CloseAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a new driver instance bound to a different database.</summary>
    IGraphDriver Clone(string database);

    /// <summary>Persists a single node.</summary>
    Task SaveNodeAsync(Node node, CancellationToken cancellationToken = default);

    /// <summary>Persists a single edge.</summary>
    Task SaveEdgeAsync(Edge edge, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists episodes, entities, and their edges together, generating any missing embeddings
    /// via <paramref name="embedder"/>.
    /// </summary>
    Task SaveBulkAsync(
        IEnumerable<EpisodicNode> episodicNodes,
        IEnumerable<EpisodicEdge> episodicEdges,
        IEnumerable<EntityNode> entityNodes,
        IEnumerable<EntityEdge> entityEdges,
        IEmbedderClient embedder,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a node and its attached edges by UUID.</summary>
    Task DeleteNodeAsync(string uuid, CancellationToken cancellationToken = default);

    /// <summary>Deletes all nodes in a group partition, in batches.</summary>
    Task DeleteNodesByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Deletes nodes by UUID, in batches.</summary>
    Task DeleteNodesByUuidsAsync(IEnumerable<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Deletes a single edge by UUID.</summary>
    Task DeleteEdgeAsync(string uuid, CancellationToken cancellationToken = default);

    /// <summary>Deletes edges by UUID.</summary>
    Task DeleteEdgesByUuidsAsync(IEnumerable<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Removes all data, optionally limited to the given group partitions.</summary>
    Task ClearDataAsync(IReadOnlyList<string>? groupIds = null, CancellationToken cancellationToken = default);

    /// <summary>Retrieves a node of type <typeparamref name="TNode"/> by UUID.</summary>
    Task<TNode> GetNodeByUuidAsync<TNode>(string uuid, CancellationToken cancellationToken = default)
        where TNode : Node;

    /// <summary>Retrieves nodes of type <typeparamref name="TNode"/> by UUID, optionally scoped to a group.</summary>
    Task<IReadOnlyList<TNode>> GetNodesByUuidsAsync<TNode>(
        IEnumerable<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
        where TNode : Node;

    /// <summary>
    /// Retrieves nodes of type <typeparamref name="TNode"/> across the given groups, with optional
    /// paging via <paramref name="limit"/>/<paramref name="uuidCursor"/> and optional embeddings.
    /// </summary>
    Task<IReadOnlyList<TNode>> GetNodesByGroupIdsAsync<TNode>(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
        where TNode : Node;

    /// <summary>Retrieves an edge of type <typeparamref name="T"/> by UUID.</summary>
    Task<T> GetEdgeByUuidAsync<T>(string uuid, CancellationToken cancellationToken = default)
        where T : Edge;

    /// <summary>Retrieves edges of type <typeparamref name="T"/> by UUID.</summary>
    Task<IReadOnlyList<T>> GetEdgesByUuidsAsync<T>(IEnumerable<string> uuids, CancellationToken cancellationToken = default)
        where T : Edge;

    /// <summary>
    /// Retrieves edges of type <typeparamref name="T"/> across the given groups, with optional paging
    /// and optional embeddings.
    /// </summary>
    Task<IReadOnlyList<T>> GetEdgesByGroupIdsAsync<T>(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
        where T : Edge;

    /// <summary>Returns the entity edges (facts) directly connecting two nodes.</summary>
    Task<IReadOnlyList<EntityEdge>> GetEntityEdgesBetweenNodesAsync(
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all entity edges (facts) incident to a node.</summary>
    Task<IReadOnlyList<EntityEdge>> GetEntityEdgesByNodeUuidAsync(
        string nodeUuid,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the episodes that mention a given entity node.</summary>
    Task<IReadOnlyList<EpisodicNode>> GetEpisodesByEntityNodeUuidAsync(
        string entityNodeUuid,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the <paramref name="lastN"/> most recent episodes before <paramref name="referenceTime"/>,
    /// optionally filtered by group, source type, or saga.
    /// </summary>
    Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(
        DateTime referenceTime,
        int lastN,
        IReadOnlyList<string>? groupIds = null,
        EpisodeType? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the entity nodes mentioned by the given episodes.</summary>
    Task<IReadOnlyList<EntityNode>> GetMentionedNodesAsync(
        IReadOnlyList<EpisodicNode> episodes,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the communities the given entity nodes belong to.</summary>
    Task<IReadOnlyList<CommunityNode>> GetCommunitiesByNodesAsync(
        IReadOnlyList<EntityNode> nodes,
        CancellationToken cancellationToken = default);

    /// <summary>Finds a saga by name within a group, or <c>null</c> if none exists.</summary>
    Task<SagaNode?> FindSagaByNameAsync(string name, string groupId, CancellationToken cancellationToken = default);

    /// <summary>Returns the UUID of the episode preceding the given one within a saga, if any.</summary>
    Task<string?> GetSagaPreviousEpisodeUuidAsync(
        string sagaUuid,
        string currentEpisodeUuid,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the contents of a saga's episodes, optionally only those after <paramref name="since"/>.</summary>
    Task<IReadOnlyList<SagaEpisodeContent>> GetSagaEpisodeContentsAsync(
        string sagaUuid,
        DateTime? since = null,
        int limit = 200,
        CancellationToken cancellationToken = default);
}
