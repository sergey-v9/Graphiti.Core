using System.Threading.RateLimiting;

namespace Graphiti.Core.Internal;

internal static class AIProviderRateLimiter
{
    public static async ValueTask<RateLimitLease?> AcquireAsync(
        RateLimiter? rateLimiter,
        CancellationToken cancellationToken)
    {
        if (rateLimiter is null)
        {
            return null;
        }

        var lease = await rateLimiter.AcquireAsync(1, cancellationToken).ConfigureAwait(false);
        if (lease.IsAcquired)
        {
            return lease;
        }

        lease.Dispose();
        throw new InvalidOperationException("Could not acquire a Graphiti AI provider rate-limit permit.");
    }
}
