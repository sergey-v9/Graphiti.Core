using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graphiti.Core.LlmClients;

internal static class LlmResponseCachePayload
{
    public static string Serialize(JsonObject value) =>
        value.ToJsonString(GraphitiJsonSerializer.Options);

    public static bool TryCreateSnapshot(
        string payload,
        out LlmResponseCachePayloadSnapshot snapshot)
    {
        var response = Clone(payload);
        if (response is null)
        {
            snapshot = default;
            return false;
        }

        snapshot = new LlmResponseCachePayloadSnapshot(response);
        return true;
    }

    public static JsonObject Clone(JsonObject value) =>
        (JsonObject)value.DeepClone();

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

internal readonly struct LlmResponseCachePayloadSnapshot
{
    private readonly JsonObject? _response;

    public LlmResponseCachePayloadSnapshot(JsonObject response)
    {
        ArgumentNullException.ThrowIfNull(response);
        _response = response;
    }

    public JsonObject CloneResponse()
    {
        if (_response is null)
        {
            throw new InvalidOperationException("LLM cache payload snapshot is not initialized.");
        }

        return LlmResponseCachePayload.Clone(_response);
    }
}
