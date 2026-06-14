namespace Graphiti.Core.Models.Nodes;

/// <summary>
/// A saga: an ordered, named sequence of episodes that share a narrative thread. Tracks the
/// first/last episode in the chain and the watermark of the most recent summarization pass.
/// </summary>
public sealed class SagaNode : Node
{
    /// <summary>Rolling summary of the saga across its episodes.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>UUID of the first episode in the saga, if any.</summary>
    public string? FirstEpisodeUuid { get; set; }

    /// <summary>UUID of the most recent episode in the saga, if any.</summary>
    public string? LastEpisodeUuid { get; set; }

    /// <summary>Timestamp of the last time the saga summary was regenerated.</summary>
    public DateTime? LastSummarizedAt { get; set; }

    /// <summary>Event time of the last episode included in the current summary.</summary>
    public DateTime? LastSummarizedEpisodeValidAt { get; set; }

    /// <summary>Retrieves a single saga node by UUID.</summary>
    public static Task<SagaNode> GetByUuidAsync(
        IGraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default) =>
        driver.GetNodeByUuidAsync<SagaNode>(uuid, cancellationToken);

    /// <summary>Retrieves the saga nodes with the given UUIDs.</summary>
    public static Task<IReadOnlyList<SagaNode>> GetByUuidsAsync(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        driver.GetNodesByUuidsAsync<SagaNode>(uuids, cancellationToken: cancellationToken);

    /// <summary>Retrieves saga nodes across the given group partitions, with optional UUID-cursor paging.</summary>
    public static Task<IReadOnlyList<SagaNode>> GetByGroupIdsAsync(
        IGraphDriver driver,
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        driver.GetNodesByGroupIdsAsync<SagaNode>(groupIds, limit, uuidCursor, false, cancellationToken);
}
