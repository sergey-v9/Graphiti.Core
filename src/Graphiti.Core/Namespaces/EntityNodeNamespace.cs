namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="EntityNode"/>, handling name embeddings.</summary>
public sealed class EntityNodeNamespace
{
    private readonly IGraphDriver _driver;
    private readonly IEmbedderClient _embedder;

    /// <summary>Creates the namespace bound to a graph driver and the embedder used for name vectors.</summary>
    public EntityNodeNamespace(IGraphDriver driver, IEmbedderClient embedder)
    {
        _driver = driver;
        _embedder = embedder;
    }

    /// <summary>
    /// Persists the node, regenerating its name embedding first, and returns the saved node.
    /// </summary>
    public async Task<EntityNode> SaveAsync(EntityNode node, CancellationToken cancellationToken = default)
    {
        await node.GenerateNameEmbeddingAsync(_embedder, cancellationToken).ConfigureAwait(false);
        await node.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return node;
    }

    /// <summary>Persists many nodes in batches, preserving supplied name embeddings as-is.</summary>
    public async Task SaveBulkAsync(
        IEnumerable<EntityNode> nodes,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await NamespaceDriverHelpers.SaveNodesAsync(
            _driver,
            nodes,
            batchSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the node and its attached edges.</summary>
    public Task DeleteAsync(EntityNode node, CancellationToken cancellationToken = default) =>
        node.DeleteAsync(_driver, cancellationToken);

    /// <summary>Deletes every entity node in the given group partition, in batches.</summary>
    public Task DeleteByGroupIdAsync(
        string groupId,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByGroupIdAsync<EntityNode>(
            _driver,
            groupId,
            batchSize,
            cancellationToken);

    /// <summary>Deletes the entity nodes with the given UUIDs, in batches.</summary>
    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByUuidsAsync<EntityNode>(
            _driver,
            uuids,
            batchSize,
            cancellationToken);

    /// <summary>Loads a single entity node by UUID.</summary>
    public Task<EntityNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) =>
        EntityNode.GetByUuidAsync(_driver, uuid, cancellationToken);

    /// <summary>Loads the entity nodes with the given UUIDs.</summary>
    public Task<IReadOnlyList<EntityNode>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        EntityNode.GetByUuidsAsync(_driver, uuids, cancellationToken: cancellationToken);

    /// <summary>
    /// Loads entity nodes across the given group partitions, with optional UUID-cursor paging and
    /// optional inclusion of name embeddings.
    /// </summary>
    public Task<IReadOnlyList<EntityNode>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default) =>
        EntityNode.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, withEmbeddings, cancellationToken);

    /// <summary>Populates the node's name embedding from storage.</summary>
    public Task LoadEmbeddingsAsync(EntityNode node, CancellationToken cancellationToken = default) =>
        node.LoadNameEmbeddingAsync(_driver, cancellationToken);

    /// <summary>Populates name embeddings for many nodes from storage.</summary>
    public Task LoadEmbeddingsBulkAsync(
        IEnumerable<EntityNode> nodes,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        _ = batchSize;
        return NamespaceDriverHelpers.LoadEntityNodeEmbeddingsAsync(_driver, nodes, cancellationToken);
    }
}
