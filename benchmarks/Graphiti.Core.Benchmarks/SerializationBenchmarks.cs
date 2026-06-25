using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using BenchmarkDotNet.Attributes;
using Graphiti.Core.LlmClients;
using Graphiti.Core.Serialization;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// LLM-cache-key serialization (run once per LLM call: serialize the deterministic payload, SHA-256,
/// hex-encode) and general Graphiti JSON round-trips. The cache-key path is byte-sensitive — output
/// must stay identical — so this measures the existing implementation as a baseline only.
/// </summary>
[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private static readonly JsonTypeInfo<LlmCacheKeyPayload> CacheKeyPayloadJsonTypeInfo =
        (JsonTypeInfo<LlmCacheKeyPayload>)GraphitiJsonSerializer.Options.GetTypeInfo(typeof(LlmCacheKeyPayload));

    private LlmCacheKeyPayload _cacheKeyPayload = null!;
    private JsonObject _responsePayload = null!;
    private string _responsePayloadText = null!;

    [GlobalSetup]
    public void Setup()
    {
        var messages = new List<Message>
        {
            new("system", "You are a knowledge-graph extraction assistant. Extract entities and facts."),
            new("user", BenchmarkData.CreateDocument(approximateWords: 250, seed: 71)),
            new("assistant", BenchmarkData.CreateDocument(approximateWords: 80, seed: 72)),
            new("user", "Now resolve duplicate entities and emit the final structured output."),
        };

        _cacheKeyPayload = new LlmCacheKeyPayload(
            Model: "gpt-4.1-mini",
            SmallModel: "gpt-4.1-nano",
            ResolvedModel: "gpt-4.1-mini",
            ModelSize: "Medium",
            Temperature: 0.0,
            MaxTokens: 8192,
            ResponseModel: "Graphiti.Core.Prompts.ExtractedEntitiesResponse, Graphiti.Core",
            ResponseSchemaFingerprint: "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6",
            Messages: messages);

        _responsePayload = new JsonObject
        {
            ["entities"] = new JsonArray(
                BuildEntity("Alice", "Person", 0),
                BuildEntity("Acme Corp", "Company", 1),
                BuildEntity("Knowledge Graph", "Concept", 2),
                BuildEntity("Bob", "Person", 3)),
            ["duplicates"] = new JsonArray(),
            ["summary"] = BenchmarkData.CreateDocument(approximateWords: 60, seed: 73),
        };
        _responsePayloadText = _responsePayload.ToJsonString(GraphitiJsonSerializer.Options);
    }

    private static JsonObject BuildEntity(string name, string label, int id) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["label"] = label,
        ["summary"] = $"{name} is a {label.ToLowerInvariant()} referenced in the source episode.",
    };

    [Benchmark]
    public string CacheKey_SerializeHashHex()
    {
        var keyBytes = JsonSerializer.SerializeToUtf8Bytes(_cacheKeyPayload, CacheKeyPayloadJsonTypeInfo);
        var hash = SHA256.HashData(keyBytes);
        return Convert.ToHexStringLower(hash);
    }

    [Benchmark]
    public string ResponsePayload_Serialize() =>
        _responsePayload.ToJsonString(GraphitiJsonSerializer.Options);

    [Benchmark]
    public JsonObject? ResponsePayload_Parse() =>
        JsonNode.Parse(_responsePayloadText) as JsonObject;

    [Benchmark]
    public JsonObject ResponsePayload_DeepClone() =>
        (JsonObject)_responsePayload.DeepClone();
}
