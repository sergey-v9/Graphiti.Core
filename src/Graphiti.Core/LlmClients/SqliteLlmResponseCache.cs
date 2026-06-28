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

    /// <inheritdoc />
    public async Task<JsonObject?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var payload = await GetPayloadAsync(key, cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            GraphitiTelemetry.RecordLlmCacheLookup(nameof(SqliteLlmResponseCache), hit: false);
            return null;
        }

        var parsed = LlmResponseCachePayload.Clone(payload);
        if (parsed is not null)
        {
            GraphitiTelemetry.RecordLlmCacheLookup(nameof(SqliteLlmResponseCache), hit: true);
            return parsed;
        }

        await RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        GraphitiTelemetry.RecordLlmCacheLookup(nameof(SqliteLlmResponseCache), hit: false);
        return null;
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, JsonObject value, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var payload = LlmResponseCachePayload.Serialize(value);
        await SetPayloadAsync(key, payload, cancellationToken).ConfigureAwait(false);
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
                var snapshot = LlmResponseCachePayload.CreateSnapshot(value, out var payload);
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

            var connection = await OpenConnectionCoreAsync(cancellationToken).ConfigureAwait(false);
            await using var connectionScope = connection.ConfigureAwait(false);
            var pragma = connection.CreateCommand();
            await using (pragma.ConfigureAwait(false))
            {
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
                await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var command = connection.CreateCommand();
            await using var commandScope = command.ConfigureAwait(false);
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
        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var connectionScope = connection.ConfigureAwait(false);
        return await GetPayloadAsync(key, connection, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> GetPayloadAsync(
        string key,
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using var commandScope = command.ConfigureAwait(false);
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
        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var connectionScope = connection.ConfigureAwait(false);
        var command = connection.CreateCommand();
        await using var commandScope = command.ConfigureAwait(false);
        command.CommandText = UpsertSql;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", payload);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var connectionScope = connection.ConfigureAwait(false);
        var command = connection.CreateCommand();
        await using var commandScope = command.ConfigureAwait(false);
        command.CommandText = "DELETE FROM cache WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
    /// <summary>Releases resources held by the cache.</summary>
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
