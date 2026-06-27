using BenchmarkDotNet.Attributes;
using Graphiti.Core.Maintenance;
using Graphiti.Core.Models.Nodes;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// Entity-node fuzzy deduplication over high-entropy names. The fixture forces MinHash/LSH profile
/// construction for both existing and extracted nodes while keeping the merge delegate side-effect free.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Maintenance")]
public class EntityDeduplicationBenchmarks
{
    private EntityNode[] _existing = null!;
    private EntityNode[] _extracted = null!;

    [Params(64, 192)]
    public int NodeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _existing = new EntityNode[NodeCount];
        _extracted = new EntityNode[NodeCount];
        for (var i = 0; i < NodeCount; i++)
        {
            _existing[i] = new EntityNode
            {
                Uuid = $"existing-{i:D4}",
                Name = $"Northern Logistics Platform Alpha {i:D4}",
                GroupId = "bench",
                Labels = ["Entity", "Organization"]
            };
            _extracted[i] = new EntityNode
            {
                Uuid = $"extracted-{i:D4}",
                Name = $"Northern Logistics Platforms Alpha {i:D4}",
                GroupId = "bench",
                Labels = ["Entity", "Organization"]
            };
        }
    }

    [Benchmark]
    public int Resolve_FuzzyEntityNames()
    {
        var resolution = EntityNodeDeduplicator.Resolve(
            _extracted,
            _existing,
            static (existing, _) => existing);

        return resolution.Nodes.Count + resolution.NodesByExtractedName.Count + resolution.UuidMap.Count;
    }
}
