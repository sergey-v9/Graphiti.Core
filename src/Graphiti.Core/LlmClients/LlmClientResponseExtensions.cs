using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Graphiti.Core.LlmClients;

internal static class LlmClientResponseExtensions
{
    internal static async Task<TResponse> GenerateTypedResponseAsync<
        [DynamicallyAccessedMembers(ResponseMembers)] TResponse>(
        this ILlmClient client,
        IReadOnlyList<Message> messages,
        int? maxTokens = null,
        ModelSize modelSize = ModelSize.Medium,
        string? groupId = null,
        string? promptName = null,
        bool attributeExtraction = false,
        CancellationToken cancellationToken = default)
        where TResponse : class, new()
    {
        var response = await client.GenerateResponseAsync(
            messages,
            responseModel: typeof(TResponse),
            maxTokens: maxTokens,
            modelSize: modelSize,
            groupId: groupId,
            promptName: promptName,
            attributeExtraction: attributeExtraction,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return ToTypedResponse<TResponse>(response);
    }

    // The structured-output response DTOs (PublicProperties + a parameterless constructor) are preserved
    // by this annotation, so the reflection-based lenient materializer below stays trim-safe.
    private const DynamicallyAccessedMemberTypes ResponseMembers =
        DynamicallyAccessedMemberTypes.PublicProperties
        | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor;

    internal static TResponse ToTypedResponse<
        [DynamicallyAccessedMembers(ResponseMembers)] TResponse>(JsonObject response)
        where TResponse : class, new()
    {
        try
        {
            return GraphitiJsonSerializer.Deserialize<TResponse>(response) ?? new TResponse();
        }
        catch (JsonException)
        {
            return MaterializeLeniently<TResponse>(response);
        }
        catch (NotSupportedException)
        {
            return MaterializeLeniently<TResponse>(response);
        }
    }

    // Best-effort fallback that copies whatever fields parse from a partially-malformed structured
    // response onto a fresh DTO. TResponse is a known structured-output DTO whose PublicProperties are
    // preserved by ResponseMembers, so reflecting over them is trim-safe. The residual IL2026 on the
    // per-property Type-based Deserialize is suppressed below: the property types belong to the
    // DAM-preserved TResponse and serialization for them is exercised by the structured-output tests.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification =
            "Per-property deserialize targets properties of the DAM-preserved TResponse DTO; this is a "
            + "best-effort fallback over an already-validated structured-output type.")]
    private static TResponse MaterializeLeniently<
        [DynamicallyAccessedMembers(ResponseMembers)] TResponse>(JsonObject response)
        where TResponse : class, new()
    {
        var materialized = new TResponse();
        foreach (var property in typeof(TResponse).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.SetMethod is null || !property.SetMethod.IsPublic)
            {
                continue;
            }

            var jsonName = GetJsonPropertyName(property);
            if (!response.TryGetPropertyValue(jsonName, out var node) || node is null)
            {
                continue;
            }

            if (property.PropertyType == typeof(string))
            {
                property.SetValue(materialized, ReadString(node));
                continue;
            }

            try
            {
                property.SetValue(materialized, node.Deserialize(property.PropertyType, GraphitiJsonSerializer.Options));
            }
            catch (JsonException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        return materialized;
    }

    private static string GetJsonPropertyName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? GraphitiJsonSerializer.Options.PropertyNamingPolicy?.ConvertName(property.Name)
        ?? property.Name;

    private static string ReadString(JsonNode node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return node.ToJsonString(GraphitiJsonSerializer.Options);
    }
}
