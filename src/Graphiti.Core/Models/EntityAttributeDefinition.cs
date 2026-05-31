namespace Graphiti.Core.Models;

/// <summary>Describes a single custom attribute on an <see cref="EntityTypeDefinition"/>.</summary>
public sealed class EntityAttributeDefinition
{
    /// <summary>Creates an attribute definition, defaulting the type to <c>string</c>.</summary>
    public EntityAttributeDefinition(string? description = null, string? type = null)
    {
        Description = description ?? string.Empty;
        Type = string.IsNullOrWhiteSpace(type) ? "string" : type;
    }

    /// <summary>Description guiding extraction of the attribute value.</summary>
    public string Description { get; }

    /// <summary>Logical type name of the attribute (defaults to <c>string</c>).</summary>
    public string Type { get; }
}
