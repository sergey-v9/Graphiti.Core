using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Microsoft.Extensions.AI;

namespace Graphiti.Core.LlmClients;

/// <summary>
/// Validates LLM JSON responses against either a static .NET type (whose JSON schema is derived and
/// cached) or a runtime <see cref="StructuredResponseSchema"/>, and builds the provider response-format
/// hints that ask the model to emit conforming JSON.
/// </summary>
internal static class StructuredResponseValidator
{
    private static readonly ConcurrentDictionary<Type, ResponseSchemaContract> Contracts = new();

    /// <summary>
    /// Validates <paramref name="response"/> against the JSON schema derived from <paramref name="responseModel"/>.
    /// </summary>
    /// <exception cref="JsonException">The response does not satisfy the schema.</exception>
    public static void Validate(JsonObject response, Type responseModel)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(responseModel);

        var schema = GetContract(responseModel).Schema;
        var responseElement = ToJsonElement(response);
        var results = schema.Evaluate(responseElement);
        if (results.IsValid)
        {
            return;
        }

        if (TryGetPythonCoercedResponseElement(response, responseModel, out var coercedElement)
            && schema.Evaluate(coercedElement).IsValid)
        {
            return;
        }

        throw new JsonException(
            $"LLM response did not satisfy the {responseModel.Name} JSON schema: {FormatErrors(results)}");
    }

    /// <summary>
    /// Validates <paramref name="response"/> against the supplied runtime <paramref name="responseSchema"/>.
    /// </summary>
    /// <exception cref="JsonException">The response does not satisfy the schema.</exception>
    public static void Validate(JsonObject response, StructuredResponseSchema responseSchema)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(responseSchema);

        var responseElement = ToJsonElement(response);
        var results = responseSchema.Schema.Evaluate(responseElement);
        if (results.IsValid)
        {
            return;
        }

        throw new JsonException(
            $"LLM response did not satisfy the {responseSchema.Name} JSON schema: {FormatErrors(results)}");
    }

    internal static string? GetSchemaFingerprint(Type? responseModel) =>
        responseModel is null ? null : GetContract(responseModel).Fingerprint;

    internal static string? GetSchemaFingerprint(
        Type? responseModel,
        StructuredResponseSchema? responseSchema) =>
        responseSchema?.Fingerprint ?? GetSchemaFingerprint(responseModel);

    internal static string? GetSchemaJson(
        Type? responseModel,
        StructuredResponseSchema? responseSchema) =>
        responseSchema?.SchemaElement.GetRawText()
            ?? (responseModel is null ? null : GetContract(responseModel).SchemaElement.GetRawText());

    internal static ChatResponseFormat CreateResponseFormat(Type responseModel)
    {
        ArgumentNullException.ThrowIfNull(responseModel);

        var contract = GetContract(responseModel);
        return ChatResponseFormat.ForJsonSchema(
            contract.SchemaElement,
            responseModel.Name,
            $"Graphiti {responseModel.Name} response");
    }

    internal static ChatResponseFormat CreateResponseFormat(StructuredResponseSchema responseSchema)
    {
        ArgumentNullException.ThrowIfNull(responseSchema);

        return ChatResponseFormat.ForJsonSchema(
            responseSchema.SchemaElement,
            responseSchema.Name,
            responseSchema.Description);
    }

    private static ResponseSchemaContract GetContract(Type responseModel) =>
        Contracts.GetOrAdd(responseModel, BuildContract);

    private static JsonElement ToJsonElement(JsonObject response) =>
        JsonSerializer.SerializeToElement(response, GraphitiJsonSerializer.Options);

    private static bool TryGetPythonCoercedResponseElement(
        JsonObject response,
        Type responseModel,
        out JsonElement responseElement)
    {
        responseElement = default;
        if (responseModel != typeof(global::Graphiti.Core.Graphiti.EpisodeNodeExtractionResponse)
            && responseModel != typeof(global::Graphiti.Core.Graphiti.CombinedExtractionResponse))
        {
            return false;
        }

        var clone = response.DeepClone().AsObject();
        if (clone["extracted_entities"] is not JsonArray entities)
        {
            return false;
        }

        var changed = false;
        foreach (var item in entities)
        {
            if (item is not JsonObject entity
                || entity["entity_type_id"] is not JsonValue value
                || !value.TryGetValue<string>(out var idText)
                || !int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            entity["entity_type_id"] = id;
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        responseElement = ToJsonElement(clone);
        return true;
    }

    private static ResponseSchemaContract BuildContract(Type responseModel)
    {
        var schemaElement = AIJsonUtilities.CreateJsonSchema(
            responseModel,
            description: null,
            hasDefaultValue: false,
            defaultValue: null,
            serializerOptions: GraphitiJsonSerializer.Options,
            inferenceOptions: null);
        var ownedSchemaElement = schemaElement.Clone();
        var fingerprint = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(ownedSchemaElement.GetRawText())));
        return new ResponseSchemaContract(
            JsonSchema.Build(ownedSchemaElement),
            fingerprint,
            ownedSchemaElement);
    }

    private static string FormatErrors(EvaluationResults results)
    {
        StringBuilder? builder = null;
        var count = 0;
        foreach (var error in EnumerateErrors(results))
        {
            if (count == 5)
            {
                break;
            }

            if (builder is null)
            {
                builder = new StringBuilder(error);
            }
            else
            {
                builder.Append("; ");
                builder.Append(error);
            }

            count++;
        }

        return builder is null ? "validation failed" : builder.ToString();
    }

    private static IEnumerable<string> EnumerateErrors(EvaluationResults results)
    {
        if (results.Errors is not null)
        {
            foreach (var error in results.Errors)
            {
                yield return $"{results.InstanceLocation}: {error.Value}";
            }
        }

        if (results.Details is null)
        {
            yield break;
        }

        foreach (var detail in results.Details)
        {
            foreach (var error in EnumerateErrors(detail))
            {
                yield return error;
            }
        }
    }

    private sealed record ResponseSchemaContract(
        JsonSchema Schema,
        string Fingerprint,
        JsonElement SchemaElement);
}
