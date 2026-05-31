namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="CommunityNode"/>, handling name embeddings.</summary>
public sealed class CommunityNodeNamespace
{
    private readonly IGraphDriver _driver;
    private readonly IEmbedderClient _embedder;

    /// <summary>Creates the namespace bound to a driver and embedder.</summary>
    public CommunityNodeNamespace(IGraphDriver driver, IEmbedderClient embedder)
    {
        _driver = driver;
        _embedder = embedder;
    }

    public async Task<CommunityNode> SaveAsync(CommunityNode node, CancellationToken cancellationToken = default)
    {
        if (node.NameEmbedding is null)
        {
            await node.GenerateNameEmbeddingAsync(_embedder, cancellationToken).ConfigureAwait(false);
        }

        await node.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return node;
    }

    public async Task SaveBulkAsync(
        IEnumerable<CommunityNode> nodes,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        NamespaceDriverHelpers.ValidateBatchSize(batchSize);
        ArgumentNullException.ThrowIfNull(nodes);
        var nodeList = nodes.ToList();
        await NamespaceDriverHelpers.EnsureCommunityNodeEmbeddingsAsync(
            nodeList,
            _embedder,
            cancellationToken).ConfigureAwait(false);
        await NamespaceDriverHelpers.SaveNodesAsync(
            _driver,
            nodeList,
            batchSize,
            cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(CommunityNode node, CancellationToken cancellationToken = default) =>
        node.DeleteAsync(_driver, cancellationToken);

    public Task DeleteByGroupIdAsync(
        string groupId,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByGroupIdAsync<CommunityNode>(
            _driver,
            groupId,
            batchSize,
            cancellationToken);

    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByUuidsAsync<CommunityNode>(
            _driver,
            uuids,
            batchSize,
            cancellationToken);

    public Task<CommunityNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) =>
        CommunityNode.GetByUuidAsync(_driver, uuid, cancellationToken);

    public Task<IReadOnlyList<CommunityNode>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        CommunityNode.GetByUuidsAsync(_driver, uuids, cancellationToken);

    public Task<IReadOnlyList<CommunityNode>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        CommunityNode.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);

    public Task LoadNameEmbeddingAsync(CommunityNode node, CancellationToken cancellationToken = default) =>
        node.LoadNameEmbeddingAsync(_driver, cancellationToken);
}
