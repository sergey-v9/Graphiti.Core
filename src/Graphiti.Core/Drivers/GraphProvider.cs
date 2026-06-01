namespace Graphiti.Core.Drivers;

/// <summary>Identifies the graph storage backend a driver targets.</summary>
public enum GraphProvider
{
    /// <summary>Neo4j graph database.</summary>
    Neo4j,

    /// <summary>FalkorDB graph database.</summary>
    FalkorDb,

    // PORTING STATUS: LadybugDB is the primary provider target, using the alternative Kuzu fork's
    // LadybugDB package. GraphProvider.Kuzu remains the Python-parity compatibility value until the
    // driver-facing LadybugDB naming decision is explicit. Neptune is not implemented in the C# port
    // and remains present for enum/wire compatibility unless that decision changes.

    /// <summary>
    /// Kuzu compatibility provider backed by LadybugDB in the C# port.
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
