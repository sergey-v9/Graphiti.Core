namespace Graphiti.Core.Drivers;

/// <summary>Identifies the graph storage backend a driver targets.</summary>
public enum GraphProvider
{
    /// <summary>Neo4j graph database.</summary>
    Neo4j,

    /// <summary>FalkorDB graph database.</summary>
    FalkorDb,

    // PORTING STATUS: LadybugDB is the primary provider target, using the alternative Kuzu fork's
    // LadybugDB package. Existing GraphProvider.Kuzu branches are interim Python-parity
    // compatibility behavior and should be revisited when the LadybugDB provider lands. Neptune is
    // not implemented in the C# port and remains present for enum/wire compatibility unless that
    // decision changes.

    /// <summary>
    /// Kuzu compatibility provider. Planned to transition to a LadybugDB-backed provider.
    /// </summary>
    Kuzu,

    /// <summary>
    /// Amazon Neptune graph database. Not implemented in the C# port.
    /// Present for enum/wire compatibility only.
    /// </summary>
    Neptune,

    /// <summary>In-process deterministic driver used for tests and local runs.</summary>
    InMemory
}
