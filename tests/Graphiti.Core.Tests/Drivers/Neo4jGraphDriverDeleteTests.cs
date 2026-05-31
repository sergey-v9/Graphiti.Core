using System.Reflection;
using Graphiti.Core;
using Neo4j.Driver;

namespace Graphiti.Core.Tests.Drivers;

public class Neo4jGraphDriverDeleteTests
{
    [Fact]
    public void BuildDeleteNodesByGroupIdStatement_UsesBoundedDeleteAndReturnsDeletedCount()
    {
        var statement = Neo4jStatementBuilder.BuildDeleteNodesByGroupIdStatement("tenant", 42);

        Assert.Contains("WHERE n.group_id = $group_id", statement.Query, StringComparison.Ordinal);
        Assert.Contains("LIMIT $batch_size", statement.Query, StringComparison.Ordinal);
        Assert.Contains("DETACH DELETE n", statement.Query, StringComparison.Ordinal);
        Assert.Contains("RETURN count(*) AS deleted", statement.Query, StringComparison.Ordinal);
        Assert.Equal("tenant", statement.Parameters["group_id"]);
        Assert.Equal(42, statement.Parameters["batch_size"]);
    }

    [Fact]
    public void BuildDeleteNodesByUuidsStatements_ChunksUuidsAndUsesUnwind()
    {
        var statements = Neo4jStatementBuilder.BuildDeleteNodesByUuidsStatements(
            new[] { "a", "b", "c", "d", "e" },
            batchSize: 2);

        Assert.Collection(
            statements,
            statement => Assert.Equal(new List<string> { "a", "b" }, Uuids(statement)),
            statement => Assert.Equal(new List<string> { "c", "d" }, Uuids(statement)),
            statement => Assert.Equal(new List<string> { "e" }, Uuids(statement)));
        Assert.All(statements, statement =>
        {
            Assert.Contains("UNWIND $uuids AS uuid", statement.Query, StringComparison.Ordinal);
            Assert.Contains("MATCH (n {uuid: uuid})", statement.Query, StringComparison.Ordinal);
            Assert.Contains("DETACH DELETE n", statement.Query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void BuildDeleteNodesByUuidsStatements_ReturnsNoStatementsForEmptyInput()
    {
        var statements = Neo4jStatementBuilder.BuildDeleteNodesByUuidsStatements(
            Array.Empty<string>(),
            batchSize: 100);

        Assert.Empty(statements);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task DeleteNodesByGroupIdAsync_InvalidBatchSizeDoesNotOpenSession(int batchSize)
    {
        var proxy = DispatchProxy.Create<IDriver, SessionCountingNeo4jDriverProxy>();
        var tracker = (SessionCountingNeo4jDriverProxy)(object)proxy;
        var driver = CreateDriver(proxy);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            driver.DeleteNodesByGroupIdAsync("tenant", batchSize));

        Assert.Equal(0, tracker.AsyncSessionCalls);
        await driver.DisposeAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task DeleteNodesByUuidsAsync_InvalidBatchSizeDoesNotOpenSession(int batchSize)
    {
        var proxy = DispatchProxy.Create<IDriver, SessionCountingNeo4jDriverProxy>();
        var tracker = (SessionCountingNeo4jDriverProxy)(object)proxy;
        var driver = CreateDriver(proxy);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            driver.DeleteNodesByUuidsAsync(new[] { "node-1" }, batchSize));

        Assert.Equal(0, tracker.AsyncSessionCalls);
        await driver.DisposeAsync();
    }

    [Fact]
    public async Task DeleteNodesByUuidsAsync_EmptyInputDoesNotOpenSession()
    {
        var proxy = DispatchProxy.Create<IDriver, SessionCountingNeo4jDriverProxy>();
        var tracker = (SessionCountingNeo4jDriverProxy)(object)proxy;
        var driver = CreateDriver(proxy);

        await driver.DeleteNodesByUuidsAsync(Array.Empty<string>(), batchSize: 100);

        Assert.Equal(0, tracker.AsyncSessionCalls);
        await driver.DisposeAsync();
    }

    [Fact]
    public async Task DeleteEdgesByUuidsAsync_RejectsNullInputBeforeOpeningSession()
    {
        var proxy = DispatchProxy.Create<IDriver, SessionCountingNeo4jDriverProxy>();
        var tracker = (SessionCountingNeo4jDriverProxy)(object)proxy;
        var driver = CreateDriver(proxy);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            driver.DeleteEdgesByUuidsAsync(null!));

        Assert.Equal(0, tracker.AsyncSessionCalls);
        await driver.DisposeAsync();
    }

    [Fact]
    public async Task DeleteEdgesByUuidsAsync_EmptyInputDoesNotOpenSession()
    {
        var proxy = DispatchProxy.Create<IDriver, SessionCountingNeo4jDriverProxy>();
        var tracker = (SessionCountingNeo4jDriverProxy)(object)proxy;
        var driver = CreateDriver(proxy);

        await driver.DeleteEdgesByUuidsAsync(Array.Empty<string>());

        Assert.Equal(0, tracker.AsyncSessionCalls);
        await driver.DisposeAsync();
    }

    private static Neo4jGraphDriver CreateDriver(IDriver driver) =>
        new(
            driver,
            "bolt://test",
            user: null,
            password: null,
            database: "",
            ownsDriver: true);

    private static List<string> Uuids(Neo4jStatement statement) =>
        Assert.IsType<List<string>>(statement.Parameters["uuids"]);

    private class SessionCountingNeo4jDriverProxy : DispatchProxy
    {
        private int _asyncSessionCalls;

        public int AsyncSessionCalls => Volatile.Read(ref _asyncSessionCalls);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null)
            {
                throw new MissingMethodException();
            }

            if (targetMethod.Name == nameof(IAsyncDisposable.DisposeAsync))
            {
                return ValueTask.CompletedTask;
            }

            if (targetMethod.Name == nameof(IDisposable.Dispose))
            {
                return null;
            }

            if (targetMethod.Name == nameof(IDriver.AsyncSession))
            {
                Interlocked.Increment(ref _asyncSessionCalls);
                throw new InvalidOperationException("This test must not open a Neo4j session.");
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }
}
