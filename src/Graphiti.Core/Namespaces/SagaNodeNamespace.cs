namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="SagaNode"/>.</summary>
public sealed class SagaNodeNamespace
{
    private readonly IGraphDriver _driver;

    /// <summary>Creates the namespace bound to a driver.</summary>
    public SagaNodeNamespace(IGraphDriver driver) => _driver = driver;

    public async Task<SagaNode> SaveAsync(SagaNode node, CancellationToken cancellationToken = default)
    {
        await node.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return node;
    }

    public async Task SaveBulkAsync(
        IEnumerable<SagaNode> nodes,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        await NamespaceDriverHelpers.SaveNodesAsync(
            _driver,
            nodes,
            batchSize,
            cancellationToken).ConfigureAwait(false);

    public Task DeleteAsync(SagaNode node, CancellationToken cancellationToken = default) =>
        node.DeleteAsync(_driver, cancellationToken);

    public Task DeleteByGroupIdAsync(
        string groupId,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByGroupIdAsync<SagaNode>(
            _driver,
            groupId,
            batchSize,
            cancellationToken);

    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByUuidsAsync<SagaNode>(
            _driver,
            uuids,
            batchSize,
            cancellationToken);

    public Task<SagaNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) => SagaNode.GetByUuidAsync(_driver, uuid, cancellationToken);

    public Task<IReadOnlyList<SagaNode>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        SagaNode.GetByUuidsAsync(_driver, uuids, cancellationToken);

    public Task<IReadOnlyList<SagaNode>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        SagaNode.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
}
