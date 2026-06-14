using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Hybrid;

namespace Graphiti.Core.LlmClients;

/// <summary>
/// An <see cref="ILlmResponseCache"/> backed by <c>Microsoft.Extensions.Caching.Hybrid</c>'s
/// <see cref="HybridCache"/>, combining in-process and (optionally) distributed layers. Entries are
/// tagged for group invalidation.
/// </summary>
public sealed class HybridCacheLlmResponseCache : ILlmResponseCache
{
    private const string CacheMissSentinel = "\uE000_graphiti_cache_miss";

    private readonly HybridCache _cache;
    private readonly AsyncSingleFlight<string, LlmResponseCachePayloadSnapshot> _inflight = new(StringComparer.Ordinal);
    private readonly HybridCacheEntryOptions? _entryOptions;
    private readonly IReadOnlyList<string> _tags;

    /// <summary>Creates the cache over a <see cref="HybridCache"/> with optional entry options and tags.</summary>
    public HybridCacheLlmResponseCache(
        HybridCache cache,
        HybridCacheEntryOptions? entryOptions = null,
        IReadOnlyList<string>? tags = null)
    {
        _cache = cache;
        _entryOptions = entryOptions;
        _tags = tags ?? new[] { "graphiti", "llm" };
    }

    /// <inheritdoc />
    public async Task<JsonObject?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var payload = await _cache.GetOrCreateAsync(
            key,
            static _ => ValueTask.FromResult(CacheMissSentinel),
            _entryOptions,
            _tags,
            cancellationToken).ConfigureAwait(false);

        if (payload == CacheMissSentinel)
        {
            await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var parsed = LlmResponseCachePayload.Clone(payload);
        if (parsed is not null)
        {
            return parsed;
        }

        await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        return null;
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, JsonObject value, CancellationToken cancellationToken = default)
    {
        var payload = LlmResponseCachePayload.Serialize(value);
        await _cache.SetAsync(
            key,
            payload,
            _entryOptions,
            _tags,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<JsonObject> GetOrCreateAsync(
        string key,
        Func<CancellationToken, Task<JsonObject>> factory,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _inflight.RunAsync(
            key,
            () => GetOrCreateSnapshotAsync(key, factory),
            cancellationToken).ConfigureAwait(false);
        return snapshot.CloneResponse();
    }

    private async Task<LlmResponseCachePayloadSnapshot> GetOrCreateSnapshotAsync(
        string key,
        Func<CancellationToken, Task<JsonObject>> factory)
    {
        LlmResponseCachePayloadSnapshot? generatedSnapshot = null;
        var payload = await _cache.GetOrCreateAsync(
            key,
            async _ =>
            {
                var value = await factory(CancellationToken.None).ConfigureAwait(false);
                generatedSnapshot = LlmResponseCachePayload.CreateSnapshot(value, out var generatedPayload);
                return generatedPayload;
            },
            _entryOptions,
            _tags,
            CancellationToken.None).ConfigureAwait(false);

        if (generatedSnapshot is { } generated)
        {
            return generated;
        }

        return payload != CacheMissSentinel
            && LlmResponseCachePayload.TryCreateSnapshot(payload, out var snapshot)
            ? snapshot
            : await RegenerateSnapshotAsync(key, factory).ConfigureAwait(false);
    }

    private async Task<LlmResponseCachePayloadSnapshot> RegenerateSnapshotAsync(
        string key,
        Func<CancellationToken, Task<JsonObject>> factory)
    {
        var currentPayload = await ReadPayloadOrSentinelAsync(key, CancellationToken.None)
            .ConfigureAwait(false);
        if (currentPayload != CacheMissSentinel
            && LlmResponseCachePayload.TryCreateSnapshot(currentPayload, out var currentSnapshot))
        {
            return currentSnapshot;
        }

        await _cache.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
        var value = await factory(CancellationToken.None).ConfigureAwait(false);
        var regeneratedSnapshot = LlmResponseCachePayload.CreateSnapshot(value, out var regeneratedPayload);
        await _cache.SetAsync(
            key,
            regeneratedPayload,
            _entryOptions,
            _tags,
            CancellationToken.None).ConfigureAwait(false);
        return regeneratedSnapshot;
    }

    private ValueTask<string> ReadPayloadOrSentinelAsync(
        string key,
        CancellationToken cancellationToken) =>
        _cache.GetOrCreateAsync(
            key,
            static _ => ValueTask.FromResult(CacheMissSentinel),
            _entryOptions,
            _tags,
            cancellationToken);
}
