namespace Graphiti.Core.Drivers;

/// <summary>Identifies the graph storage backend a driver targets.</summary>
/// <remarks>
/// Numeric values are assigned explicitly and are stable. Ordinal 0 is retired and unused (it once
/// belonged to a removed provider); the remaining members keep their original ordinals (FalkorDb=1,
/// Kuzu=2, Neptune=3, InMemory=4). <see cref="LadybugDb"/> is appended as a new value (5) rather than
/// reusing the <see cref="Kuzu"/> ordinal so that any caller persisting the numeric value sees a
/// distinct, stable identity for each name.
/// </remarks>
public enum GraphProvider
{
    /// <summary>FalkorDB graph database.</summary>
    FalkorDb = 1,

    // Provider status: LadybugDB is the primary provider target, backed by the LadybugDB package.
    // GraphProvider.LadybugDb is the driver-facing name. GraphProvider.Kuzu is retained as a still-
    // functional obsolete compatibility alias (it resolves to the same LadybugDB-backed driver) and
    // keeps its original ordinal (2) for value/wire compatibility. Neptune remains present for
    // enum/wire compatibility unless that decision changes.

    /// <summary>
    /// Kuzu compatibility provider backed by LadybugDB. Obsolete: use
    /// <see cref="LadybugDb"/>. Still functional — resolves to the same LadybugDB-backed driver.
    /// </summary>
    [Obsolete("Use GraphProvider.LadybugDb", DiagnosticId = "GRPH0001")]
    Kuzu = 2,

    /// <summary>
    /// Amazon Neptune graph database compatibility value. No built-in driver is registered for it.
    /// </summary>
    Neptune = 3,

    /// <summary>In-process deterministic driver used for tests and local runs.</summary>
    InMemory = 4,

    /// <summary>LadybugDB graph database. The driver-facing name for the LadybugDB-backed driver.</summary>
    LadybugDb = 5
}
