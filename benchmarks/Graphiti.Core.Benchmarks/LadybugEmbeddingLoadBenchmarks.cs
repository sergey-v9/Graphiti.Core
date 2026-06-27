using BenchmarkDotNet.Attributes;
using Graphiti.Core.Drivers.Ladybug;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// Ladybug embedding-load record mapping. Namespace embedding loads query only uuid + embedding columns,
/// so the benchmark isolates the cost of reading those vectors from a representative record.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Ladybug")]
public class LadybugEmbeddingLoadBenchmarks
{
    private IReadOnlyDictionary<string, object?> _entityRecord = null!;
    private IReadOnlyDictionary<string, object?> _edgeRecord = null!;

    [GlobalSetup]
    public void Setup()
    {
        var createdAt = new DateTime(2026, 2, 4, 10, 0, 0, DateTimeKind.Utc);
        _entityRecord = new Dictionary<string, object?>
        {
            ["uuid"] = "entity-embedding-load",
            ["name"] = "Embedding Load Entity",
            ["group_id"] = "tenant",
            ["labels"] = new[] { "Entity", "Person" },
            ["created_at"] = createdAt,
            ["summary"] = BenchmarkData.CreateDocument(approximateWords: 60, seed: 91),
            ["name_embedding"] = BenchmarkData.CreateUnitVector(dimension: 64, seed: 92),
            ["attributes"] = """{"role":"operator","score":0.75,"active":true}"""
        };
        _edgeRecord = new Dictionary<string, object?>
        {
            ["uuid"] = "edge-embedding-load",
            ["source_node_uuid"] = "entity-embedding-source",
            ["target_node_uuid"] = "entity-embedding-target",
            ["group_id"] = "tenant",
            ["created_at"] = createdAt,
            ["name"] = "RELATES_TO",
            ["fact"] = BenchmarkData.CreateDocument(approximateWords: 40, seed: 93),
            ["fact_embedding"] = BenchmarkData.CreateUnitVector(dimension: 64, seed: 94),
            ["episodes"] = new[] { "episode-1", "episode-2", "episode-3" },
            ["expired_at"] = null,
            ["valid_at"] = createdAt,
            ["invalid_at"] = null,
            ["reference_time"] = createdAt,
            ["attributes"] = """{"confidence":0.91,"source":"fixture"}"""
        };
    }

    [Benchmark]
    public List<float>? LoadEntityNodeEmbeddingFromRecord() =>
        LadybugRecordMapper.GetFloatList(_entityRecord, "name_embedding");

    [Benchmark]
    public List<float>? LoadEntityEdgeEmbeddingFromRecord() =>
        LadybugRecordMapper.GetFloatList(_edgeRecord, "fact_embedding");
}
