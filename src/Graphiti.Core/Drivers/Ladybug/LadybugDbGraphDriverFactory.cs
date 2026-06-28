namespace Graphiti.Core.Drivers.Ladybug;

/// <summary>
/// Creates Graphiti graph drivers backed by the LadybugDB .NET package.
/// </summary>
public static class LadybugDbGraphDriverFactory
{
    private const string KuzuInMemoryDatabasePath = ":memory:";

    /// <summary>
    /// Creates an in-memory LadybugDB-backed graph driver.
    /// </summary>
    public static IGraphDriver CreateInMemory() => Create(string.Empty);

    /// <summary>
    /// Creates a LadybugDB-backed graph driver for <paramref name="databasePath"/>.
    /// </summary>
    /// <param name="databasePath">
    /// The LadybugDB database path. Use an empty string or the <c>:memory:</c>
    /// sentinel for an in-memory database.
    /// </param>
    public static IGraphDriver Create(string databasePath)
    {
        ArgumentNullException.ThrowIfNull(databasePath);
        var normalizedPath = string.Equals(databasePath, KuzuInMemoryDatabasePath, StringComparison.Ordinal)
            ? string.Empty
            : databasePath;
        return new LadybugGraphDriver(
            path => new LadybugDbQueryExecutor(path),
            normalizedPath);
    }
}
