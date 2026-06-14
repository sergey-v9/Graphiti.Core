namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="SagaNode"/>.</summary>
public sealed class SagaNodeNamespace
{
    private readonly IGraphDriver _driver;

    /// <summary>Creates the namespace bound to a driver.</summary>
    public SagaNodeNamespace(IGraphDriver driver) => _driver = driver;

    /// <summary>Persists the saga node and returns it.</summary>
    public async Task<SagaNode> SaveAsync(SagaNode node, CancellationToken cancellationToken = default)
    {
        await node.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return node;
    }

    /// <summary>Persists many saga nodes in batches.</summary>
    public async Task SaveBulkAsync(
        IEnumerable<SagaNode> nodes,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        await NamespaceDriverHelpers.SaveNodesAsync(
            _driver,
            nodes,
            batchSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Deletes the saga node.</summary>
    public Task DeleteAsync(SagaNode node, CancellationToken cancellationToken = default) =>
        node.DeleteAsync(_driver, cancellationToken);

    /// <summary>Deletes every saga node in the given group partition, in batches.</summary>
    public Task DeleteByGroupIdAsync(
        string groupId,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByGroupIdAsync<SagaNode>(
            _driver,
            groupId,
            batchSize,
            cancellationToken);

    /// <summary>Deletes the saga nodes with the given UUIDs.</summary>
    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByUuidsAsync<SagaNode>(
            _driver,
            uuids,
            batchSize,
            cancellationToken);

    /// <summary>Loads a single saga node by UUID.</summary>
    public Task<SagaNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) => SagaNode.GetByUuidAsync(_driver, uuid, cancellationToken);

    /// <summary>Loads the saga nodes with the given UUIDs.</summary>
    public Task<IReadOnlyList<SagaNode>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        SagaNode.GetByUuidsAsync(_driver, uuids, cancellationToken);

    /// <summary>Loads saga nodes across the given group partitions, with optional UUID-cursor paging.</summary>
    public Task<IReadOnlyList<SagaNode>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        SagaNode.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
}
