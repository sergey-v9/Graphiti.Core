using System.Text.Json.Nodes;

namespace Graphiti.Core.LlmClients;

/// <summary>
/// A cache for LLM JSON responses keyed by a deterministic request hash, used to avoid repeated model
/// calls for identical requests.
/// </summary>
public interface ILlmResponseCache
{
    /// <summary>Returns the cached response for <paramref name="key"/>, or <c>null</c> if absent.</summary>
    Task<JsonObject?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/>.</summary>
    Task SetAsync(string key, JsonObject value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the cached response, or invokes <paramref name="factory"/> to produce and cache one.
    /// Implementations may override this to deduplicate concurrent misses for the same key.
    /// </summary>
    async Task<JsonObject> GetOrCreateAsync(
        string key,
        Func<CancellationToken, Task<JsonObject>> factory,
        CancellationToken cancellationToken = default)
    {
        var cached = await GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var value = await factory(cancellationToken).ConfigureAwait(false);
        await SetAsync(key, value, cancellationToken).ConfigureAwait(false);
        return value;
    }
}
