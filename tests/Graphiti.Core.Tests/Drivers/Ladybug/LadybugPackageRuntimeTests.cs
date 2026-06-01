using Graphiti.Core.Drivers.Ladybug;
using LadybugDB;
using GraphitiSagaNode = Graphiti.Core.Models.Nodes.SagaNode;

namespace Graphiti.Core.Tests.Drivers.Ladybug;

public class LadybugPackageRuntimeTests
{
    [Fact]
    public async Task PackageRuntime_BuildsSchemaAndRoundTripsScalarSagaThroughInternalDriver()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var createdAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var summarizedAt = createdAt.AddHours(2);
        var summarizedValidAt = createdAt.AddHours(1);
        var saga = new GraphitiSagaNode
        {
            Uuid = "saga-1",
            Name = "checkout",
            GroupId = "tenant",
            CreatedAt = createdAt,
            Summary = "summary",
            FirstEpisodeUuid = "episode-1",
            LastEpisodeUuid = "episode-2",
            LastSummarizedAt = summarizedAt,
            LastSummarizedEpisodeValidAt = summarizedValidAt
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(saga);
        var fetched = await driver.GetNodeByUuidAsync<GraphitiSagaNode>("saga-1");
        Assert.Contains("uuid", executor.LastColumnNames);

        var vectorRows = await executor.QueryAsync(new LadybugStatement(
            "RETURN array_cosine_similarity([1.0, 0.0], [1.0, 0.0]) AS score",
            new Dictionary<string, object?>(StringComparer.Ordinal)));

        Assert.Equal(GraphProvider.Kuzu, driver.Provider);
        Assert.Equal("checkout", fetched.Name);
        Assert.Equal("tenant", fetched.GroupId);
        Assert.Equal("summary", fetched.Summary);
        Assert.Equal("episode-1", fetched.FirstEpisodeUuid);
        Assert.Equal("episode-2", fetched.LastEpisodeUuid);
        Assert.Equal(summarizedAt, fetched.LastSummarizedAt);
        Assert.Equal(summarizedValidAt, fetched.LastSummarizedEpisodeValidAt);
        Assert.Equal(1.0, Assert.IsType<double>(Assert.Single(vectorRows)["score"]), precision: 6);
        Assert.Equal(new[] { "score" }, executor.LastColumnNames);
    }

    [Fact]
    public void PackageRuntime_DoesNotBindGraphitiListArrayOrNullParametersDirectlyYet()
    {
        using var database = new Database("");
        using var connection = new Connection(database);

        Assert.Throws<NotSupportedException>(() => ExecuteParameterQuery(
            connection,
            new List<string> { "tenant" }));
        Assert.Throws<NotSupportedException>(() => ExecuteParameterQuery(
            connection,
            new[] { "tenant" }));
        Assert.Throws<NotSupportedException>(() => ExecuteParameterQuery(
            connection,
            new[] { 0.1f, 0.2f }));
        Assert.Throws<NotSupportedException>(() => ExecuteParameterQuery(
            connection,
            new object[] { "tenant" }));
        Assert.Throws<ArgumentNullException>(() => ExecuteParameterQuery(
            connection,
            null!));
    }

    [Fact]
    public void PackageRuntime_FtsCallsRequireExtensionLoadingBeforeSearchProof()
    {
        using var database = new Database("");
        using var connection = new Connection(database);
        connection.Query("CREATE NODE TABLE IF NOT EXISTS FtNode(uuid STRING PRIMARY KEY, name STRING);").Dispose();

        Assert.ThrowsAny<LadybugException>(() => connection
            .Query("CALL CREATE_FTS_INDEX('FtNode', 'ft_node_name', ['name']);")
            .Dispose());
    }

    [Fact]
    public void CoreProject_DoesNotReferenceLadybugPackageBeforeProviderWiring()
    {
        var project = File.ReadAllText(Path.Combine(
            FindCSharpRoot(),
            "src",
            "Graphiti.Core",
            "Graphiti.Core.csproj"));

        Assert.DoesNotContain("LadybugDB", project, StringComparison.Ordinal);
    }

    private static void ExecuteParameterQuery(Connection connection, object value)
    {
        using var result = connection.Execute(
            "RETURN $value AS value",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["value"] = value
            });
    }

    private static string FindCSharpRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Graphiti.Core.CSharp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the csharp solution root.");
    }

    private sealed class PackageLadybugExecutor : ILadybugQueryExecutor
    {
        private readonly Database _database = new("");
        private readonly Connection _connection;
        private bool _disposed;

        internal PackageLadybugExecutor()
        {
            _connection = new Connection(_database);
        }

        internal IReadOnlyList<string> LastColumnNames { get; private set; } = Array.Empty<string>();

        public Task ExecuteAsync(LadybugStatement statement, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var result = statement.Parameters.Count == 0
                ? _connection.Query(statement.Query)
                : _connection.Execute(statement.Query, SnapshotParameters(statement.Parameters));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
            LadybugStatement statement,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var result = statement.Parameters.Count == 0
                ? _connection.Query(statement.Query)
                : _connection.Execute(statement.Query, SnapshotParameters(statement.Parameters));
            var columns = result.ColumnNames;
            LastColumnNames = columns;
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

        private static Dictionary<string, object?> SnapshotParameters(
            Dictionary<string, object?> parameters)
        {
            var snapshot = new Dictionary<string, object?>(parameters.Count, StringComparer.Ordinal);
            foreach (var (key, value) in parameters)
            {
                snapshot[key] = value ?? throw new NotSupportedException(
                    "The current LadybugDB package cannot bind null parameters directly.");
            }

            return snapshot;
        }
    }
}
