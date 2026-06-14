namespace Graphiti.Core.Models.Nodes;

/// <summary>
/// An episode: a unit of raw input data ingested into the graph (a message, JSON document, or
/// plain text). Episodes are the provenance source from which entities and facts are extracted,
/// and they carry the bi-temporal <see cref="ValidAt"/> timestamp for event-time tracking.
/// </summary>
public sealed class EpisodicNode : Node
{
    /// <summary>The kind of source content this episode represents.</summary>
    public EpisodeType Source { get; set; } = EpisodeType.Message;

    /// <summary>Free-text description of where the episode came from.</summary>
    public string SourceDescription { get; set; } = string.Empty;

    /// <summary>Raw episode content as ingested.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Event time at which the episode's content became true.</summary>
    public DateTime ValidAt { get; set; } = GraphitiHelpers.DefaultTimestamp;

    /// <summary>UUIDs of entity edges extracted from this episode.</summary>
    public List<string> EntityEdges { get; set; } = new();

    /// <summary>Optional arbitrary metadata associated with the episode.</summary>
    public Dictionary<string, object?>? EpisodeMetadata { get; set; }

    /// <summary>Retrieves a single episodic node by UUID.</summary>
    public static Task<EpisodicNode> GetByUuidAsync(
        IGraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default) =>
        driver.GetNodeByUuidAsync<EpisodicNode>(uuid, cancellationToken);

    /// <summary>Retrieves the episodic nodes with the given UUIDs.</summary>
    public static Task<IReadOnlyList<EpisodicNode>> GetByUuidsAsync(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        driver.GetNodesByUuidsAsync<EpisodicNode>(uuids, cancellationToken: cancellationToken);

    /// <summary>Retrieves episodic nodes across the given group partitions, with optional UUID-cursor paging.</summary>
    public static Task<IReadOnlyList<EpisodicNode>> GetByGroupIdsAsync(
        IGraphDriver driver,
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        driver.GetNodesByGroupIdsAsync<EpisodicNode>(groupIds, limit, uuidCursor, false, cancellationToken);

    /// <summary>Retrieves the episodes that mention the given entity node.</summary>
    public static Task<IReadOnlyList<EpisodicNode>> GetByEntityNodeUuidAsync(
        IGraphDriver driver,
        string entityNodeUuid,
        CancellationToken cancellationToken = default) =>
        driver.GetEpisodesByEntityNodeUuidAsync(entityNodeUuid, cancellationToken);
}
