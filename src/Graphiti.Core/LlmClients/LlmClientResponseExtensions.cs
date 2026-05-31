using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Graphiti.Core.LlmClients;

internal static class LlmClientResponseExtensions
{
    internal static async Task<TResponse> GenerateTypedResponseAsync<TResponse>(
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

    internal static TResponse ToTypedResponse<TResponse>(JsonObject response)
        where TResponse : class, new()
    {
        try
        {
            return response.Deserialize<TResponse>(GraphitiJsonSerializer.Options) ?? new TResponse();
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

    private static TResponse MaterializeLeniently<TResponse>(JsonObject response)
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
