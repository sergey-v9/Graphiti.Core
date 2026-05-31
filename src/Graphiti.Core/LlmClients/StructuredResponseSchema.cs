using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Graphiti.Core.LlmClients;

/// <summary>
/// A runtime-defined JSON schema used to constrain and validate an LLM response when no static .NET type
/// is available. Carries a name, description, and a content fingerprint for caching.
/// </summary>
public sealed class StructuredResponseSchema
{
    /// <summary>Creates a schema from a <see cref="JsonObject"/> schema document.</summary>
    public StructuredResponseSchema(string name, JsonObject schema, string? description = null)
        : this(name, ToSchemaElement(schema), description)
    {
    }

    /// <summary>Creates a schema from a <see cref="JsonElement"/> schema document.</summary>
    public StructuredResponseSchema(string name, JsonElement schemaElement, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Description = string.IsNullOrWhiteSpace(description)
            ? $"Graphiti {name} response"
            : description;
        SchemaElement = schemaElement.Clone();
        Fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(SchemaElement.GetRawText())));
        Schema = JsonSchema.Build(SchemaElement);
    }

    /// <summary>The schema name, surfaced to the model and used in cache keys.</summary>
    public string Name { get; }

    /// <summary>Human-readable description of the expected response.</summary>
    public string Description { get; }

    /// <summary>Stable content hash of the schema, used for cache invalidation.</summary>
    public string Fingerprint { get; }

    internal JsonSchema Schema { get; }

    /// <summary>The raw JSON schema element.</summary>
    public JsonElement SchemaElement { get; }

    private static JsonElement ToSchemaElement(JsonObject schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return JsonSerializer.SerializeToElement(schema, GraphitiJsonSerializer.Options);
    }
}
