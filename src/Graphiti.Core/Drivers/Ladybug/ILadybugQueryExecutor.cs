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

    /// <summary>
    /// Prepare-once / bind-many write path: <paramref name="cypher"/> is prepared a single time and
    /// re-executed against each parameter set in input order, with each result discarded. Behaves like a
    /// sequential per-set <see cref="ExecuteAsync"/> loop over statements that share this exact Cypher —
    /// same bound values, same order, same fail-fast (a mid-sequence throw leaves prior sets persisted).
    /// An empty sequence is a no-op.
    /// </summary>
    Task ExecuteManyAsync(
        string cypher,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> parameterSets,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Prepare-once / bind-many read path: <paramref name="cypher"/> is prepared a single time and
    /// re-executed against each parameter set in input order, materializing each result into the same
    /// record shape <see cref="QueryAsync"/> produces. The returned outer list holds one materialized
    /// record list per parameter set, in input order — equivalent to a sequential per-set
    /// <see cref="QueryAsync"/> loop over statements that share this exact Cypher. An empty sequence
    /// returns an empty list.
    /// </summary>
    Task<IReadOnlyList<IReadOnlyList<IReadOnlyDictionary<string, object?>>>> ExecuteManyQueryAsync(
        string cypher,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> parameterSets,
        CancellationToken cancellationToken = default);
}
