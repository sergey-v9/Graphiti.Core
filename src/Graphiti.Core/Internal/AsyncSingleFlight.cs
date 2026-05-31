using System.Collections.Concurrent;

namespace Graphiti.Core.Internal;

/// <summary>
/// Coalesces concurrent asynchronous work by key: callers sharing a key await the same in-flight task,
/// so the <c>factory</c> runs at most once per key at a time. The entry is removed once the task
/// completes. Individual callers can cancel their own wait without affecting others.
/// </summary>
internal sealed class AsyncSingleFlight<TKey, TValue>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Lazy<Task<TValue>>> _inflight;

    /// <summary>Creates the single-flight coordinator with an optional key comparer.</summary>
    public AsyncSingleFlight(IEqualityComparer<TKey>? comparer = null)
    {
        _inflight = new ConcurrentDictionary<TKey, Lazy<Task<TValue>>>(
            comparer ?? EqualityComparer<TKey>.Default);
    }

    /// <summary>
    /// Runs (or joins the in-flight run of) <paramref name="factory"/> for <paramref name="key"/> and
    /// returns its result. The supplied <paramref name="cancellationToken"/> cancels only this caller's wait.
    /// </summary>
    public async Task<TValue> RunAsync(
        TKey key,
        Func<Task<TValue>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var lazy = _inflight.GetOrAdd(
            key,
            static (currentKey, state) => state.Self.CreateLazy(currentKey, state.Factory),
            (Self: this, Factory: factory));

        return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Lazy<Task<TValue>> CreateLazy(TKey key, Func<Task<TValue>> factory)
    {
        Lazy<Task<TValue>>? lazy = null;
        lazy = new Lazy<Task<TValue>>(
            async () =>
            {
                try
                {
                    return await factory().ConfigureAwait(false);
                }
                finally
                {
                    if (lazy is not null)
                    {
                        _inflight.TryRemove(new KeyValuePair<TKey, Lazy<Task<TValue>>>(key, lazy));
                    }
                }
            },
            LazyThreadSafetyMode.ExecutionAndPublication);

        return lazy;
    }
}
