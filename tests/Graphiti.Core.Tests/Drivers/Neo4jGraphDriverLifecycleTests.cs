using System.Reflection;
using Graphiti.Core;
using Neo4j.Driver;

namespace Graphiti.Core.Tests.Drivers;

public class Neo4jGraphDriverLifecycleTests
{
    [Fact]
    public async Task CloneDispose_DoesNotDisposeSharedNeo4jDriver()
    {
        var proxy = DispatchProxy.Create<IDriver, TrackingNeo4jDriverProxy>();
        var tracker = (TrackingNeo4jDriverProxy)(object)proxy;
        var root = new Neo4jGraphDriver(
            proxy,
            "bolt://test",
            user: null,
            password: null,
            database: "",
            ownsDriver: true);
        var clone = root.Clone("tenant");

        await clone.CloseAsync();
        await clone.DisposeAsync();
        Assert.Equal(0, tracker.DisposeAsyncCalls);

        await root.CloseAsync();
        await root.DisposeAsync();
        Assert.Equal(1, tracker.DisposeAsyncCalls);
    }

    [Fact]
    public async Task SaveNodeAsync_PreCanceledTokenDoesNotOpenSession()
    {
        var proxy = DispatchProxy.Create<IDriver, TrackingNeo4jDriverProxy>();
        var tracker = (TrackingNeo4jDriverProxy)(object)proxy;
        var driver = new Neo4jGraphDriver(
            proxy,
            "bolt://test",
            user: null,
            password: null,
            database: "",
            ownsDriver: true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            driver.SaveNodeAsync(
                new EntityNode
                {
                    Uuid = "node-1",
                    Name = "Alice",
                    GroupId = "group",
                    Labels = new List<string> { "Entity" }
                },
                cancellation.Token));
        Assert.Equal(0, tracker.AsyncSessionCalls);

        await driver.DisposeAsync();
    }

    [Fact]
    public async Task GetEntityGroupIdsAsync_CancelsDuringResultStreaming()
    {
        using var cancellation = new CancellationTokenSource();
        var state = new StreamingCancellationState(cancellation);
        var proxy = CreateStreamingDriver(state);
        var driver = new Neo4jGraphDriver(
            proxy,
            "bolt://test",
            user: null,
            password: null,
            database: "",
            ownsDriver: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            driver.GetEntityGroupIdsAsync(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(1, state.MoveNextAsyncCalls);
        Assert.Equal(0, state.ConsumeAsyncCalls);
        await driver.DisposeAsync();
    }

    [Fact]
    public async Task SaveNodeAsync_CancelsDuringResultStreaming()
    {
        using var cancellation = new CancellationTokenSource();
        var state = new StreamingCancellationState(cancellation);
        var proxy = CreateStreamingDriver(state);
        var driver = new Neo4jGraphDriver(
            proxy,
            "bolt://test",
            user: null,
            password: null,
            database: "",
            ownsDriver: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            driver.SaveNodeAsync(
                new EntityNode
                {
                    Uuid = "node-1",
                    Name = "Alice",
                    GroupId = "group",
                    Labels = new List<string> { "Entity" }
                },
                cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(1, state.MoveNextAsyncCalls);
        Assert.Equal(0, state.ConsumeAsyncCalls);
        await driver.DisposeAsync();
    }

    private static IDriver CreateStreamingDriver(StreamingCancellationState state)
    {
        var proxy = DispatchProxy.Create<IDriver, StreamingNeo4jDriverProxy>();
        ((StreamingNeo4jDriverProxy)(object)proxy).State = state;
        return proxy;
    }

    private class TrackingNeo4jDriverProxy : DispatchProxy
    {
        private int _disposeAsyncCalls;
        private int _asyncSessionCalls;

        public int DisposeAsyncCalls => Volatile.Read(ref _disposeAsyncCalls);
        public int AsyncSessionCalls => Volatile.Read(ref _asyncSessionCalls);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new MissingMethodException();
            }

            if (targetMethod.Name == nameof(IAsyncDisposable.DisposeAsync))
            {
                Interlocked.Increment(ref _disposeAsyncCalls);
                return ValueTask.CompletedTask;
            }

            if (targetMethod.Name == nameof(IDisposable.Dispose))
            {
                return null;
            }

            if (targetMethod.Name == nameof(IDriver.AsyncSession))
            {
                Interlocked.Increment(ref _asyncSessionCalls);
                throw new InvalidOperationException("A pre-canceled operation must not open a Neo4j session.");
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }

    private class StreamingCancellationState
    {
        private readonly CancellationTokenSource? _cancellation;
        private int _moveNextAsyncCalls;
        private int _consumeAsyncCalls;

        public StreamingCancellationState(CancellationTokenSource? cancellation = null)
        {
            _cancellation = cancellation;
        }

        public int MoveNextAsyncCalls => Volatile.Read(ref _moveNextAsyncCalls);
        public int ConsumeAsyncCalls => Volatile.Read(ref _consumeAsyncCalls);

        public IAsyncSession CreateSession()
        {
            var proxy = DispatchProxy.Create<IAsyncSession, StreamingSessionProxy>();
            ((StreamingSessionProxy)(object)proxy).State = this;
            return proxy;
        }

        public IAsyncQueryRunner CreateQueryRunner()
        {
            var proxy = DispatchProxy.Create<IAsyncQueryRunner, StreamingQueryRunnerProxy>();
            ((StreamingQueryRunnerProxy)(object)proxy).State = this;
            return proxy;
        }

        public IResultCursor CreateCursor()
        {
            var proxy = DispatchProxy.Create<IResultCursor, NeverCompletingCursorProxy>();
            ((NeverCompletingCursorProxy)(object)proxy).State = this;
            return proxy;
        }

        public void RecordMoveNextAsync()
        {
            Interlocked.Increment(ref _moveNextAsyncCalls);
            _cancellation?.Cancel();
        }

        public void RecordConsumeAsync() => Interlocked.Increment(ref _consumeAsyncCalls);
    }

    private class StreamingNeo4jDriverProxy : DispatchProxy
    {
        public StreamingCancellationState State { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new MissingMethodException();
            }

            if (targetMethod.Name == nameof(IDriver.AsyncSession))
            {
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

    private class StreamingSessionProxy : DispatchProxy
    {
        public StreamingCancellationState State { get; set; } = null!;

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

    private class StreamingQueryRunnerProxy : DispatchProxy
    {
        public StreamingCancellationState State { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new MissingMethodException();
            }

            if (targetMethod.Name == nameof(IAsyncQueryRunner.RunAsync))
            {
                return Task.FromResult(State.CreateCursor());
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }

    private class NeverCompletingCursorProxy : DispatchProxy
    {
        private readonly TaskCompletionSource<bool> _fetch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<IResultSummary> _summary = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StreamingCancellationState State { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new MissingMethodException();
            }

            return targetMethod.Name switch
            {
                nameof(IAsyncEnumerable<IRecord>.GetAsyncEnumerator) => new NeverCompletingRecordEnumerator(
                    State,
                    args?[0] is CancellationToken token ? token : default),
                nameof(IResultCursor.FetchAsync) => FetchAsync(),
                nameof(IResultCursor.ConsumeAsync) => ConsumeAsync(),
                nameof(IResultCursor.KeysAsync) => Task.FromResult(Array.Empty<string>()),
                "get_IsOpen" => true,
                "get_Current" => throw new InvalidOperationException("No record has been fetched."),
                _ => throw new NotSupportedException(targetMethod.Name)
            };
        }

        private Task<bool> FetchAsync()
        {
            State.RecordMoveNextAsync();
            return _fetch.Task;
        }

        private Task<IResultSummary> ConsumeAsync()
        {
            State.RecordConsumeAsync();
            return _summary.Task;
        }
    }

    private sealed class NeverCompletingRecordEnumerator : IAsyncEnumerator<IRecord>
    {
        private readonly StreamingCancellationState _state;
        private readonly CancellationToken _cancellationToken;

        public NeverCompletingRecordEnumerator(
            StreamingCancellationState state,
            CancellationToken cancellationToken)
        {
            _state = state;
            _cancellationToken = cancellationToken;
        }

        public IRecord Current => throw new InvalidOperationException("No record has been fetched.");

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<bool> MoveNextAsync()
        {
            _state.RecordMoveNextAsync();
            return new ValueTask<bool>(WaitForCancellationAsync());
        }

        private async Task<bool> WaitForCancellationAsync()
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, _cancellationToken).ConfigureAwait(false);
            return false;
        }
    }
}
