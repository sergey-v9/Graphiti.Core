namespace Graphiti.Core.Drivers.Ladybug;

/// <summary>
/// Internal execution seam for LadybugDB/Kuzu statements. The concrete LadybugDB package adapter can
/// implement this without forcing native dependencies into the core foundation tests.
/// </summary>
internal interface ILadybugQueryExecutor : IAsyncDisposable
{
    Task ExecuteAsync(LadybugStatement statement, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
        LadybugStatement statement,
        CancellationToken cancellationToken = default);
}
