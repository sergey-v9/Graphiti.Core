namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="EntityNode"/>, handling name embeddings.</summary>
public sealed class EntityNodeNamespace
{
    private readonly IGraphDriver _driver;
    private readonly IEmbedderClient _embedder;

    public EntityNodeNamespace(IGraphDriver driver, IEmbedderClient embedder)
    {
        _driver = driver;
        _embedder = embedder;
    }

    public async Task<EntityNode> SaveAsync(EntityNode node, CancellationToken cancellationToken = default)
    {
        if (node.NameEmbedding is null)
        {
            await node.GenerateNameEmbeddingAsync(_embedder, cancellationToken).ConfigureAwait(false);
        }

        await node.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return node;
    }

    public async Task SaveBulkAsync(
        IEnumerable<EntityNode> nodes,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        NamespaceDriverHelpers.ValidateBatchSize(batchSize);
        ArgumentNullException.ThrowIfNull(nodes);

        await NamespaceDriverHelpers.SaveEntityNodesBulkAsync(
            _driver,
            nodes,
            batchSize,
            _embedder,
            cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(EntityNode node, CancellationToken cancellationToken = default) =>
        node.DeleteAsync(_driver, cancellationToken);

    public Task DeleteByGroupIdAsync(
        string groupId,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByGroupIdAsync<EntityNode>(
            _driver,
            groupId,
            batchSize,
            cancellationToken);

    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByUuidsAsync<EntityNode>(
            _driver,
            uuids,
            batchSize,
            cancellationToken);

    public Task<EntityNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) =>
        EntityNode.GetByUuidAsync(_driver, uuid, cancellationToken);

    public Task<IReadOnlyList<EntityNode>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        EntityNode.GetByUuidsAsync(_driver, uuids, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<EntityNode>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default) =>
        EntityNode.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, withEmbeddings, cancellationToken);

    public Task LoadEmbeddingsAsync(EntityNode node, CancellationToken cancellationToken = default) =>
        node.LoadNameEmbeddingAsync(_driver, cancellationToken);

    public Task LoadEmbeddingsBulkAsync(
        IEnumerable<EntityNode> nodes,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        _ = batchSize;
        return NamespaceDriverHelpers.LoadEntityNodeEmbeddingsAsync(_driver, nodes, cancellationToken);
    }
}
