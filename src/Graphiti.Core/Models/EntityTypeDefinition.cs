using System.Collections.Frozen;

namespace Graphiti.Core.Models;

/// <summary>
/// A developer-supplied ontology definition for a custom entity (or edge) type: its name,
/// description, and the typed attributes the LLM should populate during extraction.
/// </summary>
public sealed class EntityTypeDefinition
{
    private static readonly FrozenDictionary<string, EntityAttributeDefinition> EmptyAttributes =
        Array.Empty<KeyValuePair<string, EntityAttributeDefinition>>()
            .ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Creates a type definition with an optional description and attribute set.</summary>
    public EntityTypeDefinition(
        string name,
        string? description = null,
        IReadOnlyDictionary<string, EntityAttributeDefinition>? attributes = null)
    {
        Name = name;
        Description = description ?? string.Empty;
        Attributes = attributes is null || attributes.Count == 0
            ? EmptyAttributes
            : attributes.ToFrozenDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
    }

    /// <summary>Name of the entity type.</summary>
    public string Name { get; }

    /// <summary>Description guiding the LLM on when the type applies.</summary>
    public string Description { get; }

    /// <summary>Attributes the type declares, keyed by exact attribute name.</summary>
    public FrozenDictionary<string, EntityAttributeDefinition> Attributes { get; }
}
