using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graphiti.Core.LlmClients;

internal static class LlmResponseCachePayload
{
    public static string Serialize(JsonObject value) =>
        value.ToJsonString(GraphitiJsonSerializer.Options);

    public static JsonObject? Clone(string payload)
    {
        try
        {
            return JsonNode.Parse(payload) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
