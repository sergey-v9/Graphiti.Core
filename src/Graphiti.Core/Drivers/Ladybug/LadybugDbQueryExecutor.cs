using LadybugDB;

namespace Graphiti.Core.Drivers.Ladybug;

internal sealed class LadybugDbQueryExecutor : ILadybugQueryExecutor
{
    private readonly Database _database;
    private readonly Connection _connection;
    private bool _disposed;

    public LadybugDbQueryExecutor(string databasePath)
    {
        ArgumentNullException.ThrowIfNull(databasePath);
        _database = new Database(databasePath);
        _connection = new Connection(_database);
    }

    public Task ExecuteAsync(LadybugStatement statement, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var result = ExecuteStatement(statement);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        LadybugStatement statement,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var result = ExecuteStatement(statement);
        var columns = result.ColumnNames;
        var records = new List<IReadOnlyDictionary<string, object?>>((int)result.RowCount);
        foreach (var row in result.Rows())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = new Dictionary<string, object?>(columns.Count, StringComparer.Ordinal);
            for (var i = 0; i < columns.Count; i++)
            {
                record[columns[i]] = row[i];
            }

            records.Add(record);
        }

        return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(records);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _connection.Dispose();
        _database.Dispose();
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private QueryResult ExecuteStatement(LadybugStatement statement)
    {
        return statement.Parameters.Count == 0
            ? _connection.Query(statement.Query)
            : _connection.Execute(statement.Query, statement.Parameters);
    }
}
