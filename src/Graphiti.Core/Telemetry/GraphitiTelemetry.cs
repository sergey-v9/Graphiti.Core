using System.Diagnostics;

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

    private static readonly ActivitySource ActivitySourceInstance = new(
        ActivitySourceName,
        typeof(GraphitiTelemetry).Assembly.GetName().Version?.ToString());

    /// <summary>The shared <see cref="ActivitySource"/> emitting Graphiti spans.</summary>
    public static ActivitySource ActivitySource => ActivitySourceInstance;

    internal static Activity? StartActivity(string operation)
    {
        var activity = ActivitySource.StartActivity($"Graphiti.{operation}", ActivityKind.Internal);
        activity?.SetTag("graphiti.operation", operation);
        return activity;
    }

    internal static void SetOk(Activity? activity) => activity?.SetStatus(ActivityStatusCode.Ok);

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
}
