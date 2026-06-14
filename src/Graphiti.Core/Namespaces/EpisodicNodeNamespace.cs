namespace Graphiti.Core.Namespaces;

/// <summary>Save/delete/query operations for <see cref="EpisodicNode"/>, including episode retrieval.</summary>
public sealed class EpisodicNodeNamespace
{
    private readonly IGraphDriver _driver;

    /// <summary>Creates the namespace bound to a driver.</summary>
    public EpisodicNodeNamespace(IGraphDriver driver) => _driver = driver;

    /// <summary>Persists the episodic node and returns it.</summary>
    public async Task<EpisodicNode> SaveAsync(EpisodicNode node, CancellationToken cancellationToken = default)
    {
        await node.SaveAsync(_driver, cancellationToken).ConfigureAwait(false);
        return node;
    }

    /// <summary>Persists many episodic nodes in batches.</summary>
    public async Task SaveBulkAsync(
        IEnumerable<EpisodicNode> nodes,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        await NamespaceDriverHelpers.SaveNodesAsync(
            _driver,
            nodes,
            batchSize,
            cancellationToken).ConfigureAwait(false);

    /// <summary>Deletes the episodic node.</summary>
    public Task DeleteAsync(EpisodicNode node, CancellationToken cancellationToken = default) =>
        node.DeleteAsync(_driver, cancellationToken);

    /// <summary>Deletes every episodic node in the given group partition, in batches.</summary>
    public Task DeleteByGroupIdAsync(
        string groupId,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByGroupIdAsync<EpisodicNode>(
            _driver,
            groupId,
            batchSize,
            cancellationToken);

    /// <summary>Deletes the episodic nodes with the given UUIDs.</summary>
    public Task DeleteByUuidsAsync(
        IEnumerable<string> uuids,
        int batchSize = 100,
        CancellationToken cancellationToken = default) =>
        NamespaceDriverHelpers.DeleteNodesByUuidsAsync<EpisodicNode>(
            _driver,
            uuids,
            batchSize,
            cancellationToken);

    /// <summary>Loads a single episodic node by UUID.</summary>
    public Task<EpisodicNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default) => EpisodicNode.GetByUuidAsync(_driver, uuid, cancellationToken);

    /// <summary>Loads the episodic nodes with the given UUIDs.</summary>
    public Task<IReadOnlyList<EpisodicNode>> GetByUuidsAsync(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        EpisodicNode.GetByUuidsAsync(_driver, uuids, cancellationToken);

    /// <summary>Loads episodic nodes across the given group partitions, with optional UUID-cursor paging.</summary>
    public Task<IReadOnlyList<EpisodicNode>> GetByGroupIdsAsync(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        EpisodicNode.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);

    /// <summary>Loads the episodes that mention the given entity node.</summary>
    public Task<IReadOnlyList<EpisodicNode>> GetByEntityNodeUuidAsync(
        string entityNodeUuid,
        CancellationToken cancellationToken = default) =>
        EpisodicNode.GetByEntityNodeUuidAsync(_driver, entityNodeUuid, cancellationToken);

    /// <summary>Retrieves the most recent episodes before a reference time, optionally filtered by group, source, or saga.</summary>
    public Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(
        DateTime referenceTime,
        int lastN = 3,
        IReadOnlyList<string>? groupIds = null,
        EpisodeType? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default) =>
        _driver.RetrieveEpisodesAsync(referenceTime, lastN, groupIds, source, saga, cancellationToken);
}
