using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Graphiti.Core.Telemetry;

/// <summary>
/// Central OpenTelemetry instrumentation for the library. Exposes the <see cref="ActivitySource"/> that
/// callers subscribe to for distributed tracing, plus internal helpers used throughout the library to
/// start activities and record status and exceptions.
/// </summary>
public static class GraphitiTelemetry
{
    /// <summary>The name of the activity source; subscribe to this to collect Graphiti traces.</summary>
    public const string ActivitySourceName = "Graphiti.Core";

    /// <summary>The name of the meter; subscribe to this to collect Graphiti metrics.</summary>
    public const string MeterName = ActivitySourceName;

    private static readonly ActivitySource ActivitySourceInstance = new(
        ActivitySourceName,
        typeof(GraphitiTelemetry).Assembly.GetName().Version?.ToString());

    private static readonly Meter MeterInstance = new(
        MeterName,
        typeof(GraphitiTelemetry).Assembly.GetName().Version?.ToString());

    private static readonly Counter<long> EpisodesIngestedCounter = MeterInstance.CreateCounter<long>(
        "graphiti.episodes.ingested",
        unit: "{episode}",
        description: "Number of episodes successfully ingested.");

    private static readonly Histogram<double> EpisodeIngestionDurationHistogram = MeterInstance.CreateHistogram<double>(
        "graphiti.episode.ingestion.duration",
        unit: "s",
        description: "Duration of episode ingestion operations.");

    private static readonly Histogram<long> EpisodeIngestionResultsHistogram = MeterInstance.CreateHistogram<long>(
        "graphiti.episode.ingestion.results",
        unit: "{result}",
        description: "Number of graph elements produced by successful episode ingestion.");

    private static readonly Histogram<double> SearchDurationHistogram = MeterInstance.CreateHistogram<double>(
        "graphiti.search.duration",
        unit: "s",
        description: "Duration of Graphiti search operations.");

    private static readonly Histogram<long> SearchResultsHistogram = MeterInstance.CreateHistogram<long>(
        "graphiti.search.results",
        unit: "{result}",
        description: "Number of results returned by Graphiti searches.");

    private static readonly Counter<long> LlmTokensCounter = MeterInstance.CreateCounter<long>(
        "graphiti.llm.tokens",
        unit: "{token}",
        description: "LLM tokens reported by provider responses after validation.");

    private static readonly Counter<long> LlmCacheLookupCounter = MeterInstance.CreateCounter<long>(
        "graphiti.llm.cache.lookups",
        unit: "{lookup}",
        description: "LLM response cache lookups, tagged as hits or misses.");

    /// <summary>The shared <see cref="ActivitySource"/> emitting Graphiti spans.</summary>
    public static ActivitySource ActivitySource => ActivitySourceInstance;

    /// <summary>The shared <see cref="Meter"/> emitting Graphiti metrics.</summary>
    public static Meter Meter => MeterInstance;

    internal static Activity? StartActivity(string operation)
    {
        var activity = ActivitySource.StartActivity($"Graphiti.{operation}", ActivityKind.Internal);
        activity?.SetTag("graphiti.operation", operation);
        return activity;
    }

    internal static void SetOk(Activity? activity) => activity?.SetStatus(ActivityStatusCode.Ok);

    internal static long GetTimestamp() => Stopwatch.GetTimestamp();

    internal static TimeSpan GetElapsedTime(long startTimestamp) =>
        Stopwatch.GetElapsedTime(startTimestamp);

    internal static void RecordException(Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
    }

    internal static void SetGroupIds(Activity? activity, IReadOnlyList<string>? groupIds)
    {
        if (groupIds is null || groupIds.Count == 0)
        {
            return;
        }

        activity?.SetTag("graphiti.group_ids", string.Join(",", groupIds));
    }

    internal static void RecordEpisodesIngested(
        string operation,
        string? groupId,
        string? source,
        long episodeCount,
        int nodeCount,
        int edgeCount,
        int episodicEdgeCount,
        TimeSpan elapsed)
    {
        var tags = CreateOperationTags(operation, success: true, groupId);
        AddOptionalTag(ref tags, "graphiti.episode.source", source);
        EpisodesIngestedCounter.Add(episodeCount, tags);
        EpisodeIngestionDurationHistogram.Record(elapsed.TotalSeconds, tags);
        RecordEpisodeResultCount("node", nodeCount, tags);
        RecordEpisodeResultCount("edge", edgeCount, tags);
        RecordEpisodeResultCount("episodic_edge", episodicEdgeCount, tags);
    }

    internal static void RecordEpisodeIngestionDuration(
        string operation,
        string? groupId,
        string? source,
        TimeSpan elapsed,
        bool success)
    {
        var tags = CreateOperationTags(operation, success, groupId);
        AddOptionalTag(ref tags, "graphiti.episode.source", source);
        EpisodeIngestionDurationHistogram.Record(elapsed.TotalSeconds, tags);
    }

    internal static void RecordSearch(
        string operation,
        IReadOnlyList<string>? groupIds,
        int limit,
        int edgeCount,
        int nodeCount,
        int episodeCount,
        int communityCount,
        TimeSpan elapsed)
    {
        var tags = CreateSearchTags(operation, groupIds, limit, success: true);
        SearchDurationHistogram.Record(elapsed.TotalSeconds, tags);
        RecordSearchResultCount("edge", edgeCount, tags);
        RecordSearchResultCount("node", nodeCount, tags);
        RecordSearchResultCount("episode", episodeCount, tags);
        RecordSearchResultCount("community", communityCount, tags);
    }

    internal static void RecordSearchDuration(
        string operation,
        IReadOnlyList<string>? groupIds,
        int limit,
        TimeSpan elapsed,
        bool success)
    {
        var tags = CreateSearchTags(operation, groupIds, limit, success);
        SearchDurationHistogram.Record(elapsed.TotalSeconds, tags);
    }

    internal static void RecordLlmTokens(string promptName, long inputTokens, long outputTokens)
    {
        if (inputTokens > 0)
        {
            var inputTags = CreatePromptTags(promptName);
            inputTags.Add("graphiti.token.type", "input");
            LlmTokensCounter.Add(inputTokens, inputTags);
        }

        if (outputTokens > 0)
        {
            var outputTags = CreatePromptTags(promptName);
            outputTags.Add("graphiti.token.type", "output");
            LlmTokensCounter.Add(outputTokens, outputTags);
        }
    }

    internal static void RecordLlmCacheLookup(string cacheKind, bool hit)
    {
        var tags = new TagList
        {
            { "graphiti.cache.kind", cacheKind },
            { "graphiti.cache.outcome", hit ? "hit" : "miss" }
        };
        LlmCacheLookupCounter.Add(1, tags);
    }

    private static void RecordSearchResultCount(string resultKind, int count, TagList tags)
    {
        tags.Add("graphiti.result.kind", resultKind);
        SearchResultsHistogram.Record(count, tags);
    }

    private static void RecordEpisodeResultCount(string resultKind, int count, TagList tags)
    {
        tags.Add("graphiti.result.kind", resultKind);
        EpisodeIngestionResultsHistogram.Record(count, tags);
    }

    private static TagList CreateOperationTags(string operation, bool success, string? groupId)
    {
        var tags = new TagList
        {
            { "graphiti.operation", operation },
            { "graphiti.status", success ? "success" : "error" }
        };
        AddOptionalTag(ref tags, "graphiti.group_id", groupId);
        return tags;
    }

    private static TagList CreateSearchTags(
        string operation,
        IReadOnlyList<string>? groupIds,
        int limit,
        bool success)
    {
        var tags = new TagList
        {
            { "graphiti.operation", operation },
            { "graphiti.status", success ? "success" : "error" },
            { "graphiti.limit", limit },
            { "graphiti.group_count", groupIds?.Count ?? 0 }
        };
        return tags;
    }

    private static TagList CreatePromptTags(string promptName)
    {
        var tags = new TagList();
        AddOptionalTag(ref tags, "graphiti.prompt_name", promptName);
        return tags;
    }

    private static void AddOptionalTag(ref TagList tags, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            tags.Add(name, value);
        }
    }
}
