using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Graphiti.Core.LlmClients;

/// <summary>
/// A persistent <see cref="ILlmResponseCache"/> backed by a SQLite database file in a given directory.
/// The schema is created lazily on first use and concurrent misses are coalesced via single-flight.
/// </summary>
public sealed class SqliteLlmResponseCache : ILlmResponseCache, IDisposable
{
    private const string UpsertSql = """
        INSERT INTO cache (key, value)
        VALUES ($key, $value)
        ON CONFLICT(key) DO UPDATE SET value = excluded.value
        """;

    private readonly string _connectionString;
    private readonly AsyncSingleFlight<string, LlmResponseCachePayloadSnapshot> _inflight = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private int _initialized;
    private bool _disposed;

    /// <summary>Creates the cache backed by <c>cache.db</c> inside <paramref name="directory"/>.</summary>
    public SqliteLlmResponseCache(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Cache directory must not be empty.", nameof(directory));
        }

        Directory.CreateDirectory(directory);
        var dbPath = Path.Combine(directory, "cache.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public async Task<JsonObject?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var payload = await GetPayloadAsync(key, cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            return null;
        }

        var parsed = LlmResponseCachePayload.Clone(payload);
        if (parsed is not null)
        {
            return parsed;
        }

        await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        return null;
    }

    public async Task SetAsync(string key, JsonObject value, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var payload = LlmResponseCachePayload.Serialize(value);
        await SetPayloadAsync(key, payload, cancellationToken).ConfigureAwait(false);
    }

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
                var cachedPayload = await GetPayloadAsync(key, CancellationToken.None).ConfigureAwait(false);
                if (cachedPayload is not null)
                {
                    if (LlmResponseCachePayload.TryCreateSnapshot(cachedPayload, out var cachedSnapshot))
                    {
                        return cachedSnapshot;
                    }

                    await RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
                }

                var value = await factory(CancellationToken.None).ConfigureAwait(false);
                var payload = LlmResponseCachePayload.Serialize(value);
                if (!LlmResponseCachePayload.TryCreateSnapshot(payload, out var snapshot))
                {
                    throw new InvalidOperationException("Regenerated LLM cache payload was not a JSON object.");
                }

                await SetPayloadAsync(key, payload, CancellationToken.None).ConfigureAwait(false);
                return snapshot;
            },
            cancellationToken).ConfigureAwait(false);
        return payload.CloneResponse();
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Volatile.Read(ref _initialized) != 0)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (Volatile.Read(ref _initialized) != 0)
            {
                return;
            }

            await using var connection = await OpenConnectionCoreAsync(cancellationToken).ConfigureAwait(false);
            await using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
                await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS cache (key TEXT PRIMARY KEY, value TEXT NOT NULL)";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _initialized, 1);
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await OpenConnectionCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> GetPayloadAsync(string key, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await GetPayloadAsync(key, connection, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> GetPayloadAsync(
        string key,
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM cache WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
    }

    private async Task<SqliteConnection> OpenConnectionCoreAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task SetPayloadAsync(string key, string payload, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = UpsertSql;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", payload);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM cache WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        using var connection = new SqliteConnection(_connectionString);
        SqliteConnection.ClearPool(connection);
        _initializeLock.Dispose();
    }
}
