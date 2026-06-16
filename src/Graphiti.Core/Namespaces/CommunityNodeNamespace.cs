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

    /// <summary>
    /// Persists the community node, regenerating its name embedding first, and returns the saved node.
    /// </summary>
    public async Task<CommunityNode> SaveAsync(CommunityNode node, CancellationToken cancellationToken = default)
    {
        await node.GenerateNameEmbeddingAsync(_embedder, cancellationToken).ConfigureAwait(false);
        await node.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return node;
    }

    /// <summary>Persists many community nodes in batches, preserving supplied name embeddings as-is.</summary>
    public async Task SaveBulkAsync(
        IEnumerable<CommunityNode> nodes,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        NamespaceDriverHelpers.ValidateBatchSize(batchSize);
        await NamespaceDriverHelpers.SaveNodesAsync(
            _driver,
            nodes,
            batchSize,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the community node.</summary>
    public Task DeleteAsync(CommunityNode node, CancellationToken cancellationToken = default) =>
        node.DeleteAsync(_driver, cancellationToken);

    /// <summary>Deletes every community node in the given group partition, in batches.</summary>
    public Task DeleteByGroupIdAsync(
        string groupId,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByGroupIdAsync<CommunityNode>(
            _driver,
            groupId,
            batchSize,
            cancellationToken);

    /// <summary>Deletes the community nodes with the given UUIDs.</summary>
    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByUuidsAsync<CommunityNode>(
            _driver,
            uuids,
            batchSize,
            cancellationToken);

    /// <summary>Loads a single community node by UUID.</summary>
    public Task<CommunityNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) =>
        CommunityNode.GetByUuidAsync(_driver, uuid, cancellationToken);

    /// <summary>Loads the community nodes with the given UUIDs.</summary>
    public Task<IReadOnlyList<CommunityNode>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        CommunityNode.GetByUuidsAsync(_driver, uuids, cancellationToken);

    /// <summary>Loads community nodes across the given group partitions, with optional UUID-cursor paging.</summary>
    public Task<IReadOnlyList<CommunityNode>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        CommunityNode.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);

    /// <summary>Populates the community node's name embedding from storage.</summary>
    public Task LoadNameEmbeddingAsync(CommunityNode node, CancellationToken cancellationToken = default) =>
        node.LoadNameEmbeddingAsync(_driver, cancellationToken);
}
