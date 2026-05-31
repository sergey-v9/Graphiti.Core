namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="EpisodicNode"/>, including episode retrieval.</summary>
public sealed class EpisodicNodeNamespace
{
    private readonly IGraphDriver _driver;

    /// <summary>Creates the namespace bound to a driver.</summary>
    public EpisodicNodeNamespace(IGraphDriver driver) => _driver = driver;

    public async Task<EpisodicNode> SaveAsync(EpisodicNode node, CancellationToken cancellationToken = default)
    {
        await node.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return node;
    }

    public async Task SaveBulkAsync(
        IEnumerable<EpisodicNode> nodes,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        await NamespaceDriverHelpers.SaveNodesAsync(
            _driver,
            nodes,
            batchSize,
            cancellationToken).ConfigureAwait(false);

    public Task DeleteAsync(EpisodicNode node, CancellationToken cancellationToken = default) =>
        node.DeleteAsync(_driver, cancellationToken);

    public Task DeleteByGroupIdAsync(
        string groupId,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByGroupIdAsync<EpisodicNode>(
            _driver,
            groupId,
            batchSize,
            cancellationToken);

    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByUuidsAsync<EpisodicNode>(
            _driver,
            uuids,
            batchSize,
            cancellationToken);

    public Task<EpisodicNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) => EpisodicNode.GetByUuidAsync(_driver, uuid, cancellationToken);

    public Task<IReadOnlyList<EpisodicNode>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        EpisodicNode.GetByUuidsAsync(_driver, uuids, cancellationToken);

    public Task<IReadOnlyList<EpisodicNode>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        EpisodicNode.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);

    public Task<IReadOnlyList<EpisodicNode>> GetByEntityNodeUuidAsync(
        string entityNodeUuid,
        CancellationToken cancellationToken = default) =>
        EpisodicNode.GetByEntityNodeUuidAsync(_driver, entityNodeUuid, cancellationToken);

    public Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(
        DateTime referenceTime,
        int lastN = 3,
        IReadOnlyList<string>? groupIds = null,
        EpisodeType? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default) =>
        _driver.RetrieveEpisodesAsync(referenceTime, lastN, groupIds, source, saga, cancellationToken);
}
