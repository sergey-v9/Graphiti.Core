namespace Graphiti.Core.Models;

/// <summary>Describes a single custom attribute on an <see cref="EntityTypeDefinition"/>.</summary>
public sealed class EntityAttributeDefinition
{
    /// <summary>Creates an attribute definition, defaulting the type to <c>string</c>.</summary>
    public EntityAttributeDefinition(
        string? description = null,
        string? type = null,
        int? maxLength = null,
        bool required = false)
    {
        if (maxLength is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxLength),
                maxLength,
                "Attribute maximum length must be positive.");
        }

        Description = description ?? string.Empty;
        Type = string.IsNullOrWhiteSpace(type) ? "string" : type;
        MaxLength = maxLength;
        Required = required;
    }

    /// <summary>Description guiding extraction of the attribute value.</summary>
    public string Description { get; }

    /// <summary>Logical type name of the attribute (defaults to <c>string</c>).</summary>
    public string Type { get; }

    /// <summary>Optional per-field cap for extracted string attribute values.</summary>
    public int? MaxLength { get; }

    /// <summary>Whether structured extraction must retain the field even when it exceeds the length cap.</summary>
    public bool Required { get; }
}
