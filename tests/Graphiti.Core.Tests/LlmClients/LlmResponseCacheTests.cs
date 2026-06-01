using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Graphiti.Core.Tests.LlmClients;

public class LlmResponseCacheTests
{
    [Fact]
    public async Task MemoryLlmResponseCache_SetAndGetUseRawPayloadAndClonedResponses()
    {
        using var backingCache = new MemoryCache(new MemoryCacheOptions());
        using var cache = new MemoryLlmResponseCache(backingCache);
        var original = new JsonObject { ["value"] = 1 };

        await cache.SetAsync("key", original);
        original["value"] = 99;

        Assert.True(backingCache.TryGetValue("key", out var stored));
        Assert.Equal("{\"value\":1}", Assert.IsType<string>(stored));

        var first = await cache.GetAsync("key");
        Assert.NotNull(first);
        first["value"] = 2;

        var second = await cache.GetAsync("key");
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(1, second["value"]?.GetValue<int>());
    }

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

    [Fact]
    public async Task MemoryLlmResponseCache_ConcurrentMissWaitersReceiveDistinctResponses()
    {
        using var cache = new MemoryLlmResponseCache();
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
        responses[0]["value"] = 99;
        var cached = await cache.GetOrCreateAsync(
            "shared-key",
            _ => Task.FromResult(new JsonObject { ["value"] = 2 }));

        Assert.Equal(1, calls);
        Assert.NotSame(responses[0], responses[1]);
        Assert.Equal(1, responses[1]["value"]?.GetValue<int>());
        Assert.Equal(1, cached["value"]?.GetValue<int>());
    }

    [Fact]
    public async Task MemoryLlmResponseCache_CancelledCorruptRepairWaitDoesNotCancelSharedFill()
    {
        using var backingCache = new MemoryCache(new MemoryCacheOptions());
        using var cache = new MemoryLlmResponseCache(backingCache);
        backingCache.Set("shared-key", "{not-json");
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

    [Fact]
    public async Task SqliteLlmResponseCache_SetAndGetUseRawPayloadAndClonedResponses()
    {
        var tempDir = CreateTempCacheDirectory();
        var cache = new SqliteLlmResponseCache(tempDir);
        try
        {
            var original = new JsonObject { ["value"] = 1 };

            await cache.SetAsync("key", original);
            original["value"] = 99;

            Assert.Equal("{\"value\":1}", await ReadSqlitePayloadAsync(tempDir, "key"));

            var first = await cache.GetAsync("key");
            Assert.NotNull(first);
            first["value"] = 2;

            var second = await cache.GetAsync("key");
            Assert.NotNull(second);
            Assert.NotSame(first, second);
            Assert.Equal(1, second["value"]?.GetValue<int>());
        }
        finally
        {
            cache.Dispose();
            DeleteCacheDirectory(tempDir);
        }
    }

    [Fact]
    public async Task SqliteLlmResponseCache_ConcurrentMissWaitersReceiveDistinctResponses()
    {
        var tempDir = CreateTempCacheDirectory();
        var cache = new SqliteLlmResponseCache(tempDir);
        try
        {
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
            responses[0]["value"] = 99;
            var cached = await cache.GetOrCreateAsync(
                "shared-key",
                _ => Task.FromResult(new JsonObject { ["value"] = 2 }));

            Assert.Equal(1, calls);
            Assert.NotSame(responses[0], responses[1]);
            Assert.Equal(1, responses[1]["value"]?.GetValue<int>());
            Assert.Equal(1, cached["value"]?.GetValue<int>());
        }
        finally
        {
            cache.Dispose();
            DeleteCacheDirectory(tempDir);
        }
    }

    [Theory]
    [InlineData("{not-json")]
    [InlineData("[1,2,3]")]
    public async Task SqliteLlmResponseCache_CorruptPayloadRegeneratesOnceUnderSingleFlight(
        string corruptPayload)
    {
        var tempDir = CreateTempCacheDirectory();
        var cache = new SqliteLlmResponseCache(tempDir);
        try
        {
            await cache.SetAsync("shared-key", new JsonObject { ["value"] = 0 });
            await WriteSqlitePayloadAsync(tempDir, "shared-key", corruptPayload);
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
        finally
        {
            cache.Dispose();
            DeleteCacheDirectory(tempDir);
        }
    }

    [Fact]
    public async Task SqliteLlmResponseCache_CancelledCorruptRepairWaitDoesNotCancelSharedFill()
    {
        var tempDir = CreateTempCacheDirectory();
        var cache = new SqliteLlmResponseCache(tempDir);
        try
        {
            await cache.SetAsync("shared-key", new JsonObject { ["value"] = 0 });
            await WriteSqlitePayloadAsync(tempDir, "shared-key", "{not-json");
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
        finally
        {
            cache.Dispose();
            DeleteCacheDirectory(tempDir);
        }
    }

    [Fact]
    public async Task HybridCacheLlmResponseCache_SetAndGetUseRawPayloadAndClonedResponses()
    {
        using var provider = BuildHybridCacheProvider();
        var hybridCache = provider.GetRequiredService<HybridCache>();
        var cache = new HybridCacheLlmResponseCache(hybridCache);
        var original = new JsonObject { ["value"] = 1 };

        await cache.SetAsync("key", original);
        original["value"] = 99;

        var stored = await hybridCache.GetOrCreateAsync(
            "key",
            static _ => ValueTask.FromResult("missing"));
        Assert.Equal("{\"value\":1}", stored);

        var first = await cache.GetAsync("key");
        Assert.NotNull(first);
        first["value"] = 2;

        var second = await cache.GetAsync("key");
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(1, second["value"]?.GetValue<int>());
    }

    [Fact]
    public async Task HybridCacheLlmResponseCache_GetAsyncMissRemovesSentinelEntry()
    {
        using var provider = BuildHybridCacheProvider();
        var hybridCache = provider.GetRequiredService<HybridCache>();
        var cache = new HybridCacheLlmResponseCache(hybridCache);

        var missing = await cache.GetAsync("missing-key");
        var direct = await hybridCache.GetOrCreateAsync(
            "missing-key",
            static _ => ValueTask.FromResult("fallback"));

        Assert.Null(missing);
        Assert.Equal("fallback", direct);
    }

    [Fact]
    public async Task HybridCacheLlmResponseCache_ConcurrentMissWaitersReceiveDistinctResponses()
    {
        using var provider = BuildHybridCacheProvider();
        var cache = new HybridCacheLlmResponseCache(provider.GetRequiredService<HybridCache>());
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
        responses[0]["value"] = 99;
        var cached = await cache.GetOrCreateAsync(
            "shared-key",
            _ => Task.FromResult(new JsonObject { ["value"] = 2 }));

        Assert.Equal(1, calls);
        Assert.NotSame(responses[0], responses[1]);
        Assert.Equal(1, responses[1]["value"]?.GetValue<int>());
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

    private static string CreateTempCacheDirectory() =>
        Path.Combine(Path.GetTempPath(), "graphiti-llm-cache-" + Guid.NewGuid());

    private static async Task<string?> ReadSqlitePayloadAsync(string directory, string key)
    {
        await using var connection = await OpenSqliteCacheConnectionAsync(directory).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM cache WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync().ConfigureAwait(false) as string;
    }

    private static async Task WriteSqlitePayloadAsync(string directory, string key, string payload)
    {
        await using var connection = await OpenSqliteCacheConnectionAsync(directory).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE cache SET value = $value WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", payload);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<SqliteConnection> OpenSqliteCacheConnectionAsync(string directory)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "cache.db"),
            Mode = SqliteOpenMode.ReadWrite,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync().ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static void DeleteCacheDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
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
