using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Neo4j.Driver;

namespace Graphiti.Core.Tests.Drivers;

public sealed class Neo4jSessionExecutorTests
{
    [Fact]
    public async Task RunWriteAsync_UsesDefaultSessionForEmptyDatabase()
    {
        var state = new SessionOpeningState();
        var executor = new Neo4jSessionExecutor(state.CreateDriver(), database: "");

        await executor.RunWriteAsync("RETURN 1", new Dictionary<string, object?>(), CancellationToken.None);

        Assert.Equal(1, state.DefaultSessionCalls);
        Assert.Equal(0, state.ConfiguredSessionCalls);
    }

    [Fact]
    public async Task RunWriteAsync_UsesConfiguredSessionForNonEmptyDatabase()
    {
        var state = new SessionOpeningState();
        var executor = new Neo4jSessionExecutor(state.CreateDriver(), database: "tenant");

        await executor.RunWriteAsync("RETURN 1", new Dictionary<string, object?>(), CancellationToken.None);

        Assert.Equal(0, state.DefaultSessionCalls);
        Assert.Equal(1, state.ConfiguredSessionCalls);
        Assert.NotNull(state.SessionConfigAction);
    }

    [Fact]
    public async Task RunReadAsync_EmitsNeo4jSessionActivity()
    {
        var state = new SessionOpeningState();
        var executor = new Neo4jSessionExecutor(state.CreateDriver(), database: "tenant");
        var parameters = new Dictionary<string, object?> { ["value"] = 1 };

        var (activities, exception) = await CaptureActivitiesAsync(() =>
            executor.RunReadAsync("RETURN $value", parameters, CancellationToken.None));

        Assert.Null(exception);
        var activity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.GraphProvider.Neo4j.Read");
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal("neo4j", GetTag(activity, "db.system.name"));
        Assert.Equal("read", GetTag(activity, "db.operation.name"));
        Assert.Equal("Neo4j", GetTag(activity, "graphiti.graph.provider"));
        Assert.Equal("tenant", GetTag(activity, "graphiti.graph.database"));
        Assert.Equal(13, GetTag(activity, "graphiti.query.length"));
        Assert.Equal(1, GetTag(activity, "graphiti.query.parameters"));
        Assert.Equal(0, GetTag(activity, "graphiti.result.count"));
    }

    [Fact]
    public async Task RunReadAsync_PreCanceledDoesNotOpenSessionOrEmitActivity()
    {
        var state = new SessionOpeningState();
        var executor = new Neo4jSessionExecutor(state.CreateDriver(), database: "tenant");
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var (activities, exception) = await CaptureActivitiesAsync(() =>
            executor.RunReadAsync(
                "MATCH secret RETURN $token",
                new Dictionary<string, object?> { ["token"] = "super-secret-token" },
                source.Token));

        Assert.IsType<OperationCanceledException>(exception);
        Assert.Empty(activities);
        Assert.Equal(0, state.DefaultSessionCalls);
        Assert.Equal(0, state.ConfiguredSessionCalls);
    }

    [Fact]
    public async Task RunWritesAsync_EmitsNeo4jBatchActivity()
    {
        var state = new SessionOpeningState();
        var executor = new Neo4jSessionExecutor(state.CreateDriver(), database: "");
        var statements = new[]
        {
            new Neo4jStatement("RETURN $first", new Dictionary<string, object?> { ["first"] = 1 }),
            new Neo4jStatement("RETURN $second", new Dictionary<string, object?> { ["second"] = 2 })
        };

        var (activities, exception) = await CaptureActivitiesAsync(() =>
            executor.RunWritesAsync(statements, CancellationToken.None));

        Assert.Null(exception);
        var activity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.GraphProvider.Neo4j.WriteBatch");
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal("write_batch", GetTag(activity, "db.operation.name"));
        Assert.Equal(string.Empty, GetTag(activity, "graphiti.graph.database"));
        Assert.Equal(2, GetTag(activity, "graphiti.statement.count"));
        Assert.Equal(27, GetTag(activity, "graphiti.query.length"));
        Assert.Equal(2, GetTag(activity, "graphiti.query.parameters"));
    }

    [Fact]
    public async Task RunWritesAsync_PreCanceledDoesNotOpenSessionOrEmitActivity()
    {
        var state = new SessionOpeningState();
        var executor = new Neo4jSessionExecutor(state.CreateDriver(), database: "tenant");
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var statements = new[]
        {
            new Neo4jStatement(
                "MATCH secret RETURN $token",
                new Dictionary<string, object?> { ["token"] = "super-secret-token" })
        };

        var (activities, exception) = await CaptureActivitiesAsync(() =>
            executor.RunWritesAsync(statements, source.Token));

        Assert.IsType<OperationCanceledException>(exception);
        Assert.Empty(activities);
        Assert.Equal(0, state.DefaultSessionCalls);
        Assert.Equal(0, state.ConfiguredSessionCalls);
    }

    [Fact]
    public async Task RunWriteInt64Async_EmitsNeo4jScalarActivity()
    {
        var state = new SessionOpeningState();
        var executor = new Neo4jSessionExecutor(state.CreateDriver(), database: "tenant");
        var parameters = new Dictionary<string, object?> { ["group_id"] = "group" };

        var (activities, exception) = await CaptureActivitiesAsync(async () =>
        {
            var value = await executor
                .RunWriteInt64Async("RETURN 0 AS deleted", parameters, "deleted", CancellationToken.None)
                .ConfigureAwait(false);
            Assert.Equal(0, value);
        });

        Assert.Null(exception);
        var activity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.GraphProvider.Neo4j.WriteScalar");
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal("write_scalar", GetTag(activity, "db.operation.name"));
        Assert.Equal("deleted", GetTag(activity, "graphiti.result.column"));
        Assert.Equal(19, GetTag(activity, "graphiti.query.length"));
        Assert.Equal(1, GetTag(activity, "graphiti.query.parameters"));
        Assert.Equal(0, GetTag(activity, "graphiti.result.count"));
    }

    [Fact]
    public async Task RunWriteInt64Async_PreCanceledDoesNotOpenSessionOrEmitActivity()
    {
        var state = new SessionOpeningState();
        var executor = new Neo4jSessionExecutor(state.CreateDriver(), database: "tenant");
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var (activities, exception) = await CaptureActivitiesAsync(() =>
            executor.RunWriteInt64Async(
                "MATCH secret RETURN $token AS deleted",
                new Dictionary<string, object?> { ["token"] = "super-secret-token" },
                "deleted",
                source.Token));

        Assert.IsType<OperationCanceledException>(exception);
        Assert.Empty(activities);
        Assert.Equal(0, state.DefaultSessionCalls);
        Assert.Equal(0, state.ConfiguredSessionCalls);
    }

    [Fact]
    public async Task RunWriteAsync_RecordsNeo4jSessionFailure()
    {
        var expected = new InvalidOperationException("neo4j write failed");
        var state = new SessionOpeningState { RunException = expected };
        var executor = new Neo4jSessionExecutor(state.CreateDriver(), database: "tenant");

        var (activities, exception) = await CaptureActivitiesAsync(() =>
            executor.RunWriteAsync("RETURN 1", new Dictionary<string, object?>(), CancellationToken.None));

        Assert.Same(expected, exception);
        var activity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.GraphProvider.Neo4j.Write");
        Assert.Equal("write", GetTag(activity, "db.operation.name"));
        AssertExceptionRecorded(activity, expected);
    }

    [Fact]
    public async Task RunWriteAsync_PreCanceledDoesNotOpenSessionOrEmitActivity()
    {
        var state = new SessionOpeningState();
        var executor = new Neo4jSessionExecutor(state.CreateDriver(), database: "tenant");
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        var (activities, exception) = await CaptureActivitiesAsync(() =>
            executor.RunWriteAsync(
                "MATCH secret RETURN $token",
                new Dictionary<string, object?> { ["token"] = "super-secret-token" },
                source.Token));

        Assert.IsType<OperationCanceledException>(exception);
        Assert.Empty(activities);
        Assert.Equal(0, state.DefaultSessionCalls);
        Assert.Equal(0, state.ConfiguredSessionCalls);
    }

    [Fact]
    public async Task RunReadAsync_DoesNotTagQueryTextOrParameterValues()
    {
        var state = new SessionOpeningState();
        var executor = new Neo4jSessionExecutor(state.CreateDriver(), database: "tenant");

        var (activities, exception) = await CaptureActivitiesAsync(() =>
            executor.RunReadAsync(
                "MATCH secret RETURN $token",
                new Dictionary<string, object?> { ["token"] = "super-secret-token" },
                CancellationToken.None));

        Assert.Null(exception);
        var activity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.GraphProvider.Neo4j.Read");
        Assert.Equal(26, GetTag(activity, "graphiti.query.length"));
        Assert.Equal(1, GetTag(activity, "graphiti.query.parameters"));
        AssertActivityDoesNotContain(activity, "MATCH secret");
        AssertActivityDoesNotContain(activity, "super-secret-token");
    }

    [Fact]
    public async Task RunWritesAsync_DoesNotTagQueryTextOrParameterValues()
    {
        var state = new SessionOpeningState();
        var executor = new Neo4jSessionExecutor(state.CreateDriver(), database: "tenant");
        var statements = new[]
        {
            new Neo4jStatement(
                "MATCH secret RETURN $first",
                new Dictionary<string, object?> { ["first"] = "super-secret-token" }),
            new Neo4jStatement(
                "MATCH secret RETURN $second",
                new Dictionary<string, object?> { ["second"] = "another-secret-token" })
        };

        var (activities, exception) = await CaptureActivitiesAsync(() =>
            executor.RunWritesAsync(statements, CancellationToken.None));

        Assert.Null(exception);
        var activity = Assert.Single(
            activities,
            activity => activity.OperationName == "Graphiti.GraphProvider.Neo4j.WriteBatch");
        Assert.Equal(2, GetTag(activity, "graphiti.statement.count"));
        Assert.Equal(1, state.ConfiguredSessionCalls);
        AssertActivityDoesNotContain(activity, "MATCH secret");
        AssertActivityDoesNotContain(activity, "super-secret-token");
        AssertActivityDoesNotContain(activity, "another-secret-token");
    }

    private static async Task<(IReadOnlyList<Activity> Activities, Exception? Exception)> CaptureActivitiesAsync(
        Func<Task> action)
    {
        var activities = new List<Activity>();
        var traceId = default(ActivityTraceId);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == GraphitiTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == traceId)
                {
                    lock (activities)
                    {
                        activities.Add(activity);
                    }
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        Exception? exception = null;
        using (var rootActivity = new Activity("Graphiti.Neo4jSessionExecutorTest").Start())
        {
            traceId = rootActivity.TraceId;
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        }

        return (activities, exception);
    }

    private static void AssertExceptionRecorded(Activity activity, Exception exception)
    {
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal(exception.Message, activity.StatusDescription);
        var exceptionEvent = Assert.Single(activity.Events, activityEvent => activityEvent.Name == "exception");
        Assert.Equal(
            exception.GetType().FullName,
            exceptionEvent.Tags.FirstOrDefault(tag => tag.Key == "exception.type").Value);
        Assert.Equal(
            exception.Message,
            exceptionEvent.Tags.FirstOrDefault(tag => tag.Key == "exception.message").Value);
    }

    private static void AssertActivityDoesNotContain(Activity activity, string value)
    {
        Assert.DoesNotContain(value, activity.OperationName, StringComparison.Ordinal);
        foreach (var tag in activity.TagObjects)
        {
            Assert.DoesNotContain(value, tag.Key, StringComparison.Ordinal);
            Assert.DoesNotContain(
                value,
                Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparison.Ordinal);
        }

        foreach (var activityEvent in activity.Events)
        {
            Assert.DoesNotContain(value, activityEvent.Name, StringComparison.Ordinal);
            foreach (var tag in activityEvent.Tags)
            {
                Assert.DoesNotContain(value, tag.Key, StringComparison.Ordinal);
                Assert.DoesNotContain(
                    value,
                    Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                    StringComparison.Ordinal);
            }
        }
    }

    private static object? GetTag(Activity activity, string key) =>
        activity.TagObjects.FirstOrDefault(tag => tag.Key == key).Value;

    private sealed class SessionOpeningState
    {
        private int _defaultSessionCalls;
        private int _configuredSessionCalls;

        public int DefaultSessionCalls => Volatile.Read(ref _defaultSessionCalls);
        public int ConfiguredSessionCalls => Volatile.Read(ref _configuredSessionCalls);
        public Delegate? SessionConfigAction { get; private set; }
        public Exception? RunException { get; init; }

        public IDriver CreateDriver()
        {
            var proxy = DispatchProxy.Create<IDriver, DriverProxy>();
            ((DriverProxy)(object)proxy).State = this;
            return proxy;
        }

        public IAsyncSession CreateSession()
        {
            var proxy = DispatchProxy.Create<IAsyncSession, SessionProxy>();
            ((SessionProxy)(object)proxy).State = this;
            return proxy;
        }

        public IAsyncQueryRunner CreateQueryRunner()
        {
            var proxy = DispatchProxy.Create<IAsyncQueryRunner, QueryRunnerProxy>();
            ((QueryRunnerProxy)(object)proxy).State = this;
            return proxy;
        }

        public IResultCursor CreateCursor()
        {
            var proxy = DispatchProxy.Create<IResultCursor, EmptyCursorProxy>();
            ((EmptyCursorProxy)(object)proxy).State = this;
            return proxy;
        }

        public void RecordDefaultSession() => Interlocked.Increment(ref _defaultSessionCalls);

        public void RecordConfiguredSession(Delegate action)
        {
            SessionConfigAction = action;
            Interlocked.Increment(ref _configuredSessionCalls);
        }
    }

    private class DriverProxy : DispatchProxy
    {
        public SessionOpeningState State { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new MissingMethodException();
            }

            if (targetMethod.Name == nameof(IDriver.AsyncSession))
            {
                if (args is { Length: > 0 } && args[0] is Delegate action)
                {
                    State.RecordConfiguredSession(action);
                }
                else
                {
                    State.RecordDefaultSession();
                }

                return State.CreateSession();
            }

            if (targetMethod.Name == nameof(IAsyncDisposable.DisposeAsync))
            {
                return ValueTask.CompletedTask;
            }

            if (targetMethod.Name == nameof(IDisposable.Dispose))
            {
                return null;
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }

    private class SessionProxy : DispatchProxy
    {
        public SessionOpeningState State { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new MissingMethodException();
            }

            if (targetMethod.Name is nameof(IAsyncSession.ExecuteReadAsync) or nameof(IAsyncSession.ExecuteWriteAsync))
            {
                var work = Assert.IsAssignableFrom<Delegate>(args?[0]);
                return work.DynamicInvoke(State.CreateQueryRunner());
            }

            if (targetMethod.Name == nameof(IAsyncDisposable.DisposeAsync))
            {
                return ValueTask.CompletedTask;
            }

            if (targetMethod.Name == nameof(IAsyncSession.CloseAsync))
            {
                return Task.CompletedTask;
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }

    private class QueryRunnerProxy : DispatchProxy
    {
        public SessionOpeningState State { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new MissingMethodException();
            }

            if (targetMethod.Name == nameof(IAsyncQueryRunner.RunAsync))
            {
                if (State.RunException is not null)
                {
                    throw State.RunException;
                }

                return Task.FromResult(State.CreateCursor());
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }

    private class EmptyCursorProxy : DispatchProxy
    {
        public SessionOpeningState State { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new MissingMethodException();
            }

            return targetMethod.Name switch
            {
                nameof(IAsyncEnumerable<IRecord>.GetAsyncEnumerator) => new EmptyRecordEnumerator(),
                nameof(IResultCursor.FetchAsync) => Task.FromResult(false),
                nameof(IResultCursor.ConsumeAsync) => Task.FromResult<IResultSummary>(null!),
                nameof(IResultCursor.KeysAsync) => Task.FromResult(Array.Empty<string>()),
                "get_IsOpen" => false,
                "get_Current" => throw new InvalidOperationException("No record has been fetched."),
                _ => throw new NotSupportedException(targetMethod.Name)
            };
        }
    }

    private sealed class EmptyRecordEnumerator : IAsyncEnumerator<IRecord>
    {
        public IRecord Current => throw new InvalidOperationException("No record has been fetched.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(false);
    }
}
