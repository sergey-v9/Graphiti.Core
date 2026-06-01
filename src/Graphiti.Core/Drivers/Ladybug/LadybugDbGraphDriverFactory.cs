using Graphiti.Core.Drivers;

namespace Graphiti.Core.Drivers.Ladybug;

/// <summary>
/// Creates Graphiti graph drivers backed by the LadybugDB .NET package.
/// </summary>
public static class LadybugDbGraphDriverFactory
{
    /// <summary>
    /// Creates an in-memory LadybugDB-backed graph driver.
    /// </summary>
    public static IGraphDriver CreateInMemory() => Create(string.Empty);

    /// <summary>
    /// Creates a LadybugDB-backed graph driver for <paramref name="databasePath"/>.
    /// </summary>
    /// <param name="databasePath">
    /// The LadybugDB database path. Use an empty string for an in-memory database.
    /// </param>
    public static IGraphDriver Create(string databasePath)
    {
        ArgumentNullException.ThrowIfNull(databasePath);
        return new LadybugGraphDriver(
            path => new LadybugDbQueryExecutor(path),
            databasePath);
    }
}
