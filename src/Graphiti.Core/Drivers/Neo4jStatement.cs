namespace Graphiti.Core.Drivers;

/// <summary>A Cypher statement paired with its parameter map, queued for execution.</summary>
internal readonly record struct Neo4jStatement(string Query, Dictionary<string, object?> Parameters);
