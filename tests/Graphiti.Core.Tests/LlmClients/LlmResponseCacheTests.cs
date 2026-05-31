using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Graphiti.Core.Tests.LlmClients;

public class LlmResponseCacheTests
{
    [Fact]
    public async Task MemoryLlmResponseCache_RechecksCacheBeforeRunningDelayedMissFactory()
    {
        using var backingCache = new CoordinatedFirstMissMemoryCache();
        using var cache = new MemoryLlmResponseCache(backingCache);
        var delayedFactoryCalls = 0;

        var delayedMiss = Task.Run(() => cache.GetOrCreateAsync(
            "shared-key",
            _ =>
            {
                Interlocked.Increment(ref delayedFactoryCalls);
                return Task.FromResult(new JsonObject { ["value"] = 99 });
            }));

        Assert.True(backingCache.FirstMissReached.Wait(TimeSpan.FromSeconds(5)));
        var winner = await cache.GetOrCreateAsync(
            "shared-key",
            _ => Task.FromResult(new JsonObject { ["value"] = 1 }));

        backingCache.ReleaseFirstMiss();
        var delayed = await delayedMiss;

        Assert.Equal(1, winner["value"]?.GetValue<int>());
        Assert.Equal(1, delayed["value"]?.GetValue<int>());
        Assert.Equal(0, Volatile.Read(ref delayedFactoryCalls));
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("[1,2,3]")]
    public async Task MemoryLlmResponseCache_CorruptPayloadRegeneratesOnceUnderSingleFlight(
        string corruptPayload)
    {
        using var backingCache = new MemoryCache(new MemoryCacheOptions());
        using var cache = new MemoryLlmResponseCache(backingCache);
        backingCache.Set("shared-key", corruptPayload);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<JsonObject> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            Assert.False(cancellationToken.CanBeCanceled);
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return new JsonObject { ["value"] = 1 };
        }

        var waits = Enumerable.Range(0, 16)
            .Select(_ => cache.GetOrCreateAsync("shared-key", Factory))
            .ToArray();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        var responses = await Task.WhenAll(waits);
        var cached = await cache.GetOrCreateAsync(
            "shared-key",
            _ => Task.FromResult(new JsonObject { ["value"] = 2 }));

        Assert.Equal(1, calls);
        Assert.All(responses, response => Assert.Equal(1, response["value"]?.GetValue<int>()));
        Assert.Equal(1, cached["value"]?.GetValue<int>());
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("[1,2,3]")]
    public async Task HybridCacheLlmResponseCache_CorruptPayloadRegeneratesOnceUnderSingleFlight(
        string corruptPayload)
    {
        using var provider = BuildHybridCacheProvider();
        var hybridCache = provider.GetRequiredService<HybridCache>();
        var cache = new HybridCacheLlmResponseCache(hybridCache);
        await hybridCache.SetAsync("shared-key", corruptPayload);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<JsonObject> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            Assert.False(cancellationToken.CanBeCanceled);
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return new JsonObject { ["value"] = 1 };
        }

        var waits = Enumerable.Range(0, 16)
            .Select(_ => cache.GetOrCreateAsync("shared-key", Factory))
            .ToArray();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        var responses = await Task.WhenAll(waits);
        var cached = await cache.GetOrCreateAsync(
            "shared-key",
            _ => Task.FromResult(new JsonObject { ["value"] = 2 }));

        Assert.Equal(1, calls);
        Assert.All(responses, response => Assert.Equal(1, response["value"]?.GetValue<int>()));
        Assert.Equal(1, cached["value"]?.GetValue<int>());
    }

    [Fact]
    public async Task HybridCacheLlmResponseCache_CancelledCorruptRepairWaitDoesNotCancelSharedFill()
    {
        using var provider = BuildHybridCacheProvider();
        var hybridCache = provider.GetRequiredService<HybridCache>();
        var cache = new HybridCacheLlmResponseCache(hybridCache);
        await hybridCache.SetAsync("shared-key", "{not-json");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<JsonObject> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            Assert.False(cancellationToken.CanBeCanceled);
            started.TrySetResult();
            await release.Task.ConfigureAwait(false);
            return new JsonObject { ["value"] = 1 };
        }

        using var cancellation = new CancellationTokenSource();
        var firstWait = cache.GetOrCreateAsync("shared-key", Factory, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstWait);

        var secondWait = cache.GetOrCreateAsync("shared-key", Factory);
        release.SetResult();
        var second = await secondWait;
        var cached = await cache.GetOrCreateAsync(
            "shared-key",
            _ => Task.FromResult(new JsonObject { ["value"] = 2 }));

        Assert.Equal(1, calls);
        Assert.Equal(1, second["value"]?.GetValue<int>());
        Assert.Equal(1, cached["value"]?.GetValue<int>());
    }

    private static ServiceProvider BuildHybridCacheProvider()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return services.BuildServiceProvider();
    }

    private sealed class CoordinatedFirstMissMemoryCache : IMemoryCache
    {
        private readonly MemoryCache _inner = new(new MemoryCacheOptions());
        private readonly ManualResetEventSlim _releaseFirstMiss = new(false);
        private int _delayFirstMiss = 1;

        public ManualResetEventSlim FirstMissReached { get; } = new(false);

        public ICacheEntry CreateEntry(object key) => _inner.CreateEntry(key);

        public void Dispose()
        {
            _releaseFirstMiss.Dispose();
            FirstMissReached.Dispose();
            _inner.Dispose();
        }

        public void ReleaseFirstMiss() => _releaseFirstMiss.Set();

        public void Remove(object key) => _inner.Remove(key);

        public bool TryGetValue(object key, out object? value)
        {
            var found = _inner.TryGetValue(key, out value);
            if (!found && Interlocked.Exchange(ref _delayFirstMiss, 0) == 1)
            {
                FirstMissReached.Set();
                if (!_releaseFirstMiss.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Timed out waiting to release the coordinated cache miss.");
                }
            }

            return found;
        }
    }
}
