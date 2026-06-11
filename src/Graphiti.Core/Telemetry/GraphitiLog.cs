using Microsoft.Extensions.Logging;

namespace Graphiti.Core.Telemetry;

/// <summary>
/// Source-generated, strongly-typed logging messages (via <c>LoggerMessage</c>) for the library's
/// episode-ingestion, search, and community-building flows. Event IDs are grouped by area.
/// </summary>
internal static partial class GraphitiLog
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Adding episode in group {GroupId} from source {Source} with body length {BodyLength}.")]
    public static partial void AddingEpisode(
        ILogger logger,
        string groupId,
        EpisodeType source,
        int bodyLength);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Added episode {EpisodeUuid} in group {GroupId}: {NodeCount} nodes, {EdgeCount} edges, {EpisodicEdgeCount} episodic edges.")]
    public static partial void EpisodeAdded(
        ILogger logger,
        string groupId,
        string episodeUuid,
        int nodeCount,
        int edgeCount,
        int episodicEdgeCount);

    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Information,
        Message = "Adding {EpisodeCount} episodes in group {GroupId}.")]
    public static partial void AddingEpisodeBulk(
        ILogger logger,
        string groupId,
        int episodeCount);

    [LoggerMessage(
        EventId = 1011,
        Level = LogLevel.Information,
        Message = "Added {EpisodeCount} episodes in group {GroupId}: {NodeCount} nodes, {EdgeCount} edges, {EpisodicEdgeCount} episodic edges.")]
    public static partial void EpisodeBulkAdded(
        ILogger logger,
        string groupId,
        int episodeCount,
        int nodeCount,
        int edgeCount,
        int episodicEdgeCount);

    [LoggerMessage(
        EventId = 1020,
        Level = LogLevel.Debug,
        Message = "Searching edges with query length {QueryLength} and limit {Limit}.")]
    public static partial void SearchingEdges(
        ILogger logger,
        int queryLength,
        int limit);

    [LoggerMessage(
        EventId = 1021,
        Level = LogLevel.Debug,
        Message = "Edge search completed with {EdgeCount} edges.")]
    public static partial void EdgeSearchCompleted(
        ILogger logger,
        int edgeCount);

    [LoggerMessage(
        EventId = 1030,
        Level = LogLevel.Debug,
        Message = "Searching graph with query length {QueryLength} and limit {Limit}.")]
    public static partial void Searching(
        ILogger logger,
        int queryLength,
        int limit);

    [LoggerMessage(
        EventId = 1031,
        Level = LogLevel.Debug,
        Message = "Graph search completed with {NodeCount} nodes, {EdgeCount} edges, {EpisodeCount} episodes, and {CommunityCount} communities.")]
    public static partial void SearchCompleted(
        ILogger logger,
        int nodeCount,
        int edgeCount,
        int episodeCount,
        int communityCount);

    [LoggerMessage(
        EventId = 1040,
        Level = LogLevel.Information,
        Message = "Building communities for {GroupCount} explicit groups.")]
    public static partial void BuildingCommunities(
        ILogger logger,
        int groupCount);

    [LoggerMessage(
        EventId = 1041,
        Level = LogLevel.Information,
        Message = "Built {CommunityCount} communities and {CommunityEdgeCount} community edges.")]
    public static partial void CommunitiesBuilt(
        ILogger logger,
        int communityCount,
        int communityEdgeCount);

    [LoggerMessage(
        EventId = 1050,
        Level = LogLevel.Warning,
        Message = "Failed to extract timestamps for edge {EdgeUuid}.")]
    public static partial void TimestampExtractionFailed(
        ILogger logger,
        Exception exception,
        string edgeUuid);

    [LoggerMessage(
        EventId = 1060,
        Level = LogLevel.Debug,
        Message = "Failed to search node deduplication candidates in group {GroupId}; falling back to lexical candidates.")]
    public static partial void NodeDedupCandidateSearchFailed(
        ILogger logger,
        Exception exception,
        string groupId);

    [LoggerMessage(
        EventId = 1070,
        Level = LogLevel.Warning,
        Message = "LLM returned summary for unknown entity {EntityName}.")]
    public static partial void UnknownEntitySummaryReturned(
        ILogger logger,
        string entityName);
}
