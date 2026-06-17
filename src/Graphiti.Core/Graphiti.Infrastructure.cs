namespace Graphiti.Core;

public sealed partial class Graphiti
{
    private string ResolveGroupId(string? groupId)
    {
        if (groupId is null)
        {
            return _rootDriver.DefaultGroupId;
        }

        GraphitiHelpers.ValidateGroupId(groupId);
        return groupId;
    }

    private DriverScope UseGroupDriver(string? groupId, out string resolvedGroupId)
    {
        resolvedGroupId = ResolveGroupId(groupId);
        if (groupId is null || string.Equals(resolvedGroupId, _rootDriver.Database, StringComparison.Ordinal))
        {
            return new DriverScope(this, null);
        }

        return new DriverScope(this, _rootDriver.Clone(resolvedGroupId));
    }

    private Task ForEachThrottledAsync<T>(
        IEnumerable<T> items,
        Func<T, CancellationToken, Task> operation,
        CancellationToken cancellationToken) =>
        ThrottledWork.ForEachAsync(items, operation, GetMaxDegreeOfParallelism(), cancellationToken);

    private Task<TResult[]> SelectThrottledAsync<TSource, TResult>(
        List<TSource> items,
        Func<TSource, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken) =>
        ThrottledWork.SelectAsync(items, operation, GetMaxDegreeOfParallelism(), cancellationToken);

    private int GetMaxDegreeOfParallelism() =>
        _maxCoroutines is null or 0
            ? GraphitiHelpers.DefaultSemaphoreLimit
            : _maxCoroutines.Value;

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private sealed class DriverScope : IAsyncDisposable
    {
        private readonly Graphiti _graphiti;
        private readonly IGraphDriver? _operationDriver;
        private readonly IGraphDriver? _previousDriver;

        public DriverScope(Graphiti graphiti, IGraphDriver? operationDriver)
        {
            _graphiti = graphiti;
            _operationDriver = operationDriver;
            _previousDriver = graphiti._operationDriver.Value;
            if (operationDriver is not null)
            {
                graphiti._operationDriver.Value = operationDriver;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _graphiti._operationDriver.Value = _previousDriver;
            if (_operationDriver is not null)
            {
                await _operationDriver.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
