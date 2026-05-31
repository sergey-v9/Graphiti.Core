using System.Threading.RateLimiting;

namespace Graphiti.Core.Tests.Internal;

public class AIProviderRateLimiterTests
{
    [Fact]
    public async Task AcquireAsync_ReturnsNullWhenLimiterIsNull()
    {
        var lease = await AIProviderRateLimiter.AcquireAsync(null, CancellationToken.None);

        Assert.Null(lease);
    }

    [Fact]
    public async Task AcquireAsync_ReturnsAcquiredLeaseWithoutDisposingIt()
    {
        var acquiredLease = new TrackingRateLimitLease(isAcquired: true);
        using var limiter = new DelegatingRateLimiter(
            (_, _) => ValueTask.FromResult<RateLimitLease>(acquiredLease));

        var lease = await AIProviderRateLimiter.AcquireAsync(limiter, CancellationToken.None);

        Assert.Same(acquiredLease, lease);
        Assert.Equal(0, acquiredLease.DisposeCount);
        lease?.Dispose();
        Assert.Equal(1, acquiredLease.DisposeCount);
    }

    [Fact]
    public async Task AcquireAsync_PropagatesCancellationBeforeCreatingLease()
    {
        var leaseCreated = false;
        using var cancellation = new CancellationTokenSource();
        using var limiter = new DelegatingRateLimiter(
            (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                leaseCreated = true;
                return ValueTask.FromResult<RateLimitLease>(
                    new TrackingRateLimitLease(isAcquired: true));
            });
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AIProviderRateLimiter.AcquireAsync(limiter, cancellation.Token).AsTask());
        Assert.False(leaseCreated);
    }

    [Fact]
    public async Task AcquireAsync_DisposesNonAcquiredLeaseAndThrows()
    {
        var rejectedLease = new TrackingRateLimitLease(isAcquired: false);
        using var limiter = new DelegatingRateLimiter(
            (_, _) => ValueTask.FromResult<RateLimitLease>(rejectedLease));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AIProviderRateLimiter.AcquireAsync(limiter, CancellationToken.None).AsTask());

        Assert.Equal(
            "Could not acquire a Graphiti AI provider rate-limit permit.",
            exception.Message);
        Assert.Equal(1, rejectedLease.DisposeCount);
    }

    [Fact]
    public async Task AcquireAsync_PropagatesLimiterException()
    {
        var failure = new InvalidOperationException("limiter failed");
        using var limiter = new DelegatingRateLimiter((_, _) => throw failure);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AIProviderRateLimiter.AcquireAsync(limiter, CancellationToken.None).AsTask());

        Assert.Same(failure, exception);
    }

    private sealed class DelegatingRateLimiter : RateLimiter
    {
        private readonly Func<int, CancellationToken, ValueTask<RateLimitLease>> _acquireAsync;

        public DelegatingRateLimiter(
            Func<int, CancellationToken, ValueTask<RateLimitLease>> acquireAsync) =>
            _acquireAsync = acquireAsync;

        public override TimeSpan? IdleDuration => null;

        public override RateLimiterStatistics? GetStatistics() => null;

        protected override RateLimitLease AttemptAcquireCore(int permitCount) =>
            throw new NotSupportedException();

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(
            int permitCount,
            CancellationToken cancellationToken) =>
            _acquireAsync(permitCount, cancellationToken);
    }

    private sealed class TrackingRateLimitLease : RateLimitLease
    {
        private readonly bool _isAcquired;
        private int _disposeCount;

        public TrackingRateLimitLease(bool isAcquired) => _isAcquired = isAcquired;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public override bool IsAcquired => _isAcquired;

        public override IEnumerable<string> MetadataNames => Array.Empty<string>();

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref _disposeCount);
            }

            base.Dispose(disposing);
        }
    }
}
