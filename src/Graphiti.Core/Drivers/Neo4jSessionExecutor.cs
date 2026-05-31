using System.Globalization;
using System.Diagnostics;
using Neo4j.Driver;

namespace Graphiti.Core.Drivers;

internal sealed class Neo4jSessionExecutor(IDriver driver, string database)
{
    public async Task<IReadOnlyList<IRecord>> RunReadAsync(
        string query,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = StartActivity("Read", "read", query, parameters);
        try
        {
            await using var session = OpenSession();
            var records = await session.ExecuteReadAsync(async tx =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cursor = await tx.RunAsync(query, parameters).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
            activity?.SetTag("graphiti.result.count", records.Count);
            GraphitiTelemetry.SetOk(activity);
            return records;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    public async Task RunWriteAsync(
        string query,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = StartActivity("Write", "write", query, parameters);
        try
        {
            await using var session = OpenSession();
            await session.ExecuteWriteAsync(async tx =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cursor = await tx.RunAsync(query, parameters).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await cursor.ForEachAsync(static _ => { }, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
            GraphitiTelemetry.SetOk(activity);
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    public async Task<long> RunWriteInt64Async(
        string query,
        Dictionary<string, object?> parameters,
        string column,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = StartActivity("WriteScalar", "write_scalar", query, parameters);
        activity?.SetTag("graphiti.result.column", column);
        try
        {
            await using var session = OpenSession();
            var records = await session.ExecuteWriteAsync(async tx =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cursor = await tx.RunAsync(query, parameters).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            activity?.SetTag("graphiti.result.count", records.Count);
            var value = records.Count == 0
                ? 0
                : Convert.ToInt64(records[0][column], CultureInfo.InvariantCulture);
            GraphitiTelemetry.SetOk(activity);
            return value;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    public async Task RunWritesAsync(
        IReadOnlyList<Neo4jStatement> statements,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = StartBatchActivity(statements);
        try
        {
            await using var session = OpenSession();
            await session.ExecuteWriteAsync(async tx =>
            {
                foreach (var statement in statements)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var cursor = await tx.RunAsync(statement.Query, statement.Parameters).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    await cursor.ForEachAsync(static _ => { }, cancellationToken).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
            GraphitiTelemetry.SetOk(activity);
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private Activity? StartActivity(
        string operation,
        string dbOperation,
        string query,
        Dictionary<string, object?> parameters)
    {
        var activity = StartBaseActivity(operation, dbOperation);
        activity?.SetTag("graphiti.query.length", query.Length);
        activity?.SetTag("graphiti.query.parameters", parameters.Count);
        return activity;
    }

    private Activity? StartBatchActivity(IReadOnlyList<Neo4jStatement> statements)
    {
        var activity = StartBaseActivity("WriteBatch", "write_batch");
        activity?.SetTag("graphiti.statement.count", statements.Count);
        activity?.SetTag("graphiti.query.length", statements.Sum(statement => statement.Query.Length));
        activity?.SetTag("graphiti.query.parameters", statements.Sum(statement => statement.Parameters.Count));
        return activity;
    }

    private Activity? StartBaseActivity(string operation, string dbOperation)
    {
        var activity = GraphitiTelemetry.StartActivity($"GraphProvider.Neo4j.{operation}");
        activity?.SetTag("db.system.name", "neo4j");
        activity?.SetTag("db.operation.name", dbOperation);
        activity?.SetTag("graphiti.graph.provider", GraphProvider.Neo4j.ToString());
        activity?.SetTag("graphiti.graph.database", database);
        return activity;
    }

    private IAsyncSession OpenSession() =>
        string.IsNullOrEmpty(database)
            ? driver.AsyncSession()
            : driver.AsyncSession(builder => builder.WithDatabase(database));
}
