namespace Graphiti.Core.Drivers.Ladybug;

/// <summary>A LadybugDB/Kuzu Cypher statement paired with its parameter map.</summary>
internal readonly record struct LadybugStatement(
    string Query,
    Dictionary<string, object?> Parameters);
