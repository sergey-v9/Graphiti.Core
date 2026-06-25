using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;

namespace Graphiti.Core.LlmClients;

/// <summary>
/// An in-memory <see cref="ILlmResponseCache"/> backed by <see cref="IMemoryCache"/> with sliding
/// expiration. Concurrent misses for the same key are coalesced via single-flight.
/// </summary>
public sealed class MemoryLlmResponseCache : ILlmResponseCache, IDisposable
{
    private readonly IMemoryCache _cache;
    private readonly AsyncSingleFlight<string, LlmResponseCachePayloadSnapshot> _inflight = new(StringComparer.Ordinal);
    private readonly bool _disposeCache;
    private readonly MemoryCacheEntryOptions _entryOptions;
    private bool _disposed;

    /// <summary>Creates the cache, optionally over an existing memory cache and sliding expiration.</summary>
    public MemoryLlmResponseCache(IMemoryCache? cache = null, TimeSpan? expiration = null)
    {
        _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
        _disposeCache = cache is null;
        _entryOptions = new MemoryCacheEntryOptions
        {
            SlidingExpiration = expiration ?? TimeSpan.FromHours(12)
        };
    }

    /// <inheritdoc />
    public Task<JsonObject?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        JsonObject? response = null;
        if (TryGetPayload(key, out var payload))
        {
            response = LlmResponseCachePayload.Clone(payload);
        }

        GraphitiTelemetry.RecordLlmCacheLookup(nameof(MemoryLlmResponseCache), response is not null);
        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public Task SetAsync(string key, JsonObject value, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        _cache.Set(key, LlmResponseCachePayload.Serialize(value), _entryOptions);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<JsonObject> GetOrCreateAsync(
        string key,
        Func<CancellationToken, Task<JsonObject>> factory,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var cached = await GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var payload = await _inflight.RunAsync(
            key,
            async () =>
            {
                if (TryGetPayload(key, out var cachedPayload))
                {
                    if (LlmResponseCachePayload.TryCreateSnapshot(cachedPayload, out var cachedSnapshot))
                    {
                        return cachedSnapshot;
                    }

                    _cache.Remove(key);
                }

                var value = await factory(CancellationToken.None).ConfigureAwait(false);
                var snapshot = LlmResponseCachePayload.CreateSnapshot(value, out var payload);
                _cache.Set(key, payload, _entryOptions);
                return snapshot;
            },
            cancellationToken).ConfigureAwait(false);
        return payload.CloneResponse();
    }

    private bool TryGetPayload(string key, out string payload)
    {
        if (_cache.TryGetValue(key, out var value) && value is string cachedPayload)
        {
            payload = cachedPayload;
            return true;
        }

        payload = string.Empty;
        return false;
    }

    /// <summary>Releases resources held by the cache.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_disposeCache)
        {
            _cache.Dispose();
        }

        _disposed = true;
    }
}
