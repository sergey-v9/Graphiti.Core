using Graphiti.Core;

namespace Graphiti.Core.Tests.Internal;

public class ConcurrencyHelperTests
{
    [Fact]
    public async Task SemaphoreGatherAsync_UsesBoundedConcurrencyAndPreservesOrder()
    {
        var activeCount = 0;
        var maxObservedConcurrency = 0;
        var operations = Enumerable.Range(0, 12)
            .Select<int, Func<CancellationToken, Task<int>>>(index => async cancellationToken =>
            {
                var active = Interlocked.Increment(ref activeCount);
                UpdateMax(ref maxObservedConcurrency, active);
                try
                {
                    await Task.Delay(20, cancellationToken);
                    return index;
                }
                finally
                {
                    Interlocked.Decrement(ref activeCount);
                }
            })
            .ToList();

        var results = await GraphitiHelpers.SemaphoreGatherAsync(
            operations,
            maxConcurrency: 3);

        Assert.Equal(Enumerable.Range(0, 12), results);
        Assert.InRange(maxObservedConcurrency, 1, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task SemaphoreGatherAsync_DefaultModeUsesDefaultSemaphoreLimit(int? maxConcurrency)
    {
        const int DefaultLimit = 20;
        var started = 0;
        var reachedLimit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var extraStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = Enumerable.Range(0, 25)
            .Select<int, Func<CancellationToken, Task<int>>>(index => async cancellationToken =>
            {
                var current = Interlocked.Increment(ref started);
                if (current == DefaultLimit)
                {
                    reachedLimit.TrySetResult();
                }

                if (current > DefaultLimit)
                {
                    extraStarted.TrySetResult();
                }

                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return index;
            })
            .ToList();

        var gatherTask = maxConcurrency is null
            ? GraphitiHelpers.SemaphoreGatherAsync(operations)
            : GraphitiHelpers.SemaphoreGatherAsync(operations, maxConcurrency);

        await reachedLimit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var firstCompleted = await Task.WhenAny(
            extraStarted.Task,
            Task.Delay(TimeSpan.FromMilliseconds(100)));

        Assert.NotSame(extraStarted.Task, firstCompleted);
        Assert.Equal(DefaultLimit, Volatile.Read(ref started));

        release.SetResult();
        var results = await gatherTask;

        Assert.Equal(Enumerable.Range(0, operations.Count), results);
    }

    [Fact]
    public async Task SemaphoreGatherAsync_RejectsNegativeMaxConcurrency()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            GraphitiHelpers.SemaphoreGatherAsync(
                new[] { new Func<CancellationToken, Task<int>>(_ => Task.FromResult(1)) },
                maxConcurrency: -1));
    }

    [Fact]
    public async Task SemaphoreGatherAsync_SnapshotsOperationsBeforeStarting()
    {
        var operations = new List<Func<CancellationToken, Task<int>>>();
        operations.Add(_ =>
        {
            operations.Add(_ => Task.FromResult(99));
            return Task.FromResult(1);
        });
        operations.Add(_ => Task.FromResult(2));

        var results = await GraphitiHelpers.SemaphoreGatherAsync(operations);

        Assert.Equal(new[] { 1, 2 }, results);
        Assert.Equal(3, operations.Count);
    }

    [Fact]
    public async Task SemaphoreGatherAsync_MaterializesDeferredOperationsOnce()
    {
        var enumerations = 0;
        var operationStarted = false;

        IEnumerable<Func<CancellationToken, Task<int>>> Operations()
        {
            enumerations++;
            yield return _ =>
            {
                operationStarted = true;
                return Task.FromResult(enumerations);
            };
            yield return _ => Task.FromResult(operationStarted ? 2 : 0);
        }

        var results = await GraphitiHelpers.SemaphoreGatherAsync(Operations(), maxConcurrency: 1);

        Assert.Equal(1, enumerations);
        Assert.Equal(new[] { 1, 2 }, results);
    }

    [Fact]
    public async Task SemaphoreGatherAsync_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var operations = Enumerable.Range(0, 4)
            .Select<int, Func<CancellationToken, Task<int>>>(_ => async cancellationToken =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                return 0;
            })
            .ToList();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GraphitiHelpers.SemaphoreGatherAsync(
                operations,
                maxConcurrency: 1,
                cancellation.Token));
    }

    [Fact]
    public async Task SemaphoreGatherAsync_DoesNotStartOperationsWhenAlreadyCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var started = 0;
        var operations = Enumerable.Range(0, 4)
            .Select<int, Func<CancellationToken, Task<int>>>(index => _ =>
            {
                Interlocked.Increment(ref started);
                return Task.FromResult(index);
            })
            .ToList();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GraphitiHelpers.SemaphoreGatherAsync(operations, cancellationToken: cancellation.Token));

        Assert.Equal(0, Volatile.Read(ref started));
    }

    [Fact]
    public async Task SemaphoreGatherAsync_PropagatesOperationExceptions()
    {
        var operations = new Func<CancellationToken, Task<int>>[]
        {
            _ => Task.FromResult(1),
            _ => throw new InvalidOperationException("boom"),
            _ => Task.FromResult(3)
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GraphitiHelpers.SemaphoreGatherAsync(operations, maxConcurrency: 2));

        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public async Task ThrottledWork_ForEachAsync_UsesBoundedConcurrency()
    {
        var activeCount = 0;
        var maxObservedConcurrency = 0;
        var items = Enumerable.Range(0, 10).ToList();

        await ThrottledWork.ForEachAsync(
            items,
            async (_, cancellationToken) =>
            {
                var active = Interlocked.Increment(ref activeCount);
                UpdateMax(ref maxObservedConcurrency, active);
                try
                {
                    await Task.Delay(20, cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref activeCount);
                }
            },
            maxDegreeOfParallelism: 2,
            CancellationToken.None);

        Assert.InRange(maxObservedConcurrency, 1, 2);
    }

    [Fact]
    public async Task ThrottledWork_SelectAsync_PreservesInputOrderAndHandlesEmptyInput()
    {
        var items = Enumerable.Range(0, 8).ToList();

        var selected = await ThrottledWork.SelectAsync(
            items,
            async (item, cancellationToken) =>
            {
                await Task.Delay((items.Count - item) * 2, cancellationToken);
                return item * item;
            },
            maxDegreeOfParallelism: 3,
            CancellationToken.None);
        var empty = await ThrottledWork.SelectAsync(
            new List<int>(),
            (item, _) => Task.FromResult(item),
            maxDegreeOfParallelism: 3,
            CancellationToken.None);

        Assert.Equal(items.Select(item => item * item), selected);
        Assert.Empty(empty);
    }

    [Fact]
    public async Task ThrottledWork_SelectAsync_AcceptsReadOnlyListInputs()
    {
        var items = Array.AsReadOnly(Enumerable.Range(0, 8).ToArray());

        var selected = await ThrottledWork.SelectAsync(
            items,
            async (item, cancellationToken) =>
            {
                await Task.Delay((items.Count - item) * 2, cancellationToken);
                return item * item;
            },
            maxDegreeOfParallelism: 3,
            CancellationToken.None);

        Assert.Equal(items.Select(item => item * item), selected);
    }

    [Fact]
    public async Task ThrottledWork_SelectAsync_DoesNotStartOperationsWhenAlreadyCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var started = 0;
        var items = Array.AsReadOnly(Enumerable.Range(0, 4).ToArray());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ThrottledWork.SelectAsync(
                items,
                (item, _) =>
                {
                    Interlocked.Increment(ref started);
                    return Task.FromResult(item);
                },
                maxDegreeOfParallelism: 2,
                cancellation.Token));

        Assert.Equal(0, Volatile.Read(ref started));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ThrottledWork_RejectsInvalidMaxDegreeOfParallelism(int maxDegreeOfParallelism)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ThrottledWork.ForEachAsync(
                new[] { 1 },
                (_, _) => Task.CompletedTask,
                maxDegreeOfParallelism,
                CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ThrottledWork.SelectAsync(
                new List<int>(),
                (item, _) => Task.FromResult(item),
                maxDegreeOfParallelism,
                CancellationToken.None));
    }

    [Fact]
    public async Task AsyncSingleFlight_CoalescesConcurrentCallsAndCancelsOnlyWaiter()
    {
        var singleFlight = new AsyncSingleFlight<string, int>(StringComparer.Ordinal);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<int> Factory()
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return 42;
        }

        using var cancellation = new CancellationTokenSource();
        var cancelledWait = singleFlight.RunAsync("key", Factory, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWait);

        var coalescedWait = singleFlight.RunAsync("key", Factory);
        release.SetResult();
        var result = await coalescedWait;
        var next = await singleFlight.RunAsync("key", () =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(7);
        });

        Assert.Equal(42, result);
        Assert.Equal(7, next);
        Assert.Equal(2, calls);
    }

    private static void UpdateMax(ref int target, int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (value <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }
}
