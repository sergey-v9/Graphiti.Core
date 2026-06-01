namespace Graphiti.Core.Internal;

internal static class ThrottledWork
{
    public static async Task ForEachAsync<T>(
        IEnumerable<T> items,
        Func<T, CancellationToken, Task> operation,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);

        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = maxDegreeOfParallelism
        };

        await Parallel.ForEachAsync(
            items,
            options,
            async (item, token) => await operation(item, token).ConfigureAwait(false)).ConfigureAwait(false);
    }

    public static async Task<TResult[]> SelectAsync<TSource, TResult>(
        IReadOnlyList<TSource> items,
        Func<TSource, CancellationToken, Task<TResult>> operation,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);
        if (items.Count == 0)
        {
            return Array.Empty<TResult>();
        }

        var results = new TResult[items.Count];
        var options = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Min(items.Count, maxDegreeOfParallelism)
        };
        await Parallel.ForAsync(
            0,
            items.Count,
            options,
            async (index, token) =>
            {
                results[index] = await operation(items[index], token).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return results;
    }
}
