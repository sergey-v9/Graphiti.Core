using BenchmarkDotNet.Attributes;
using Graphiti.Core.Internal.Services;
using Graphiti.Core.Models.Nodes;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// Microbenchmarks for edge candidate preparation before driver/LLM resolution. These isolate the
/// exact endpoint-name validation that runs once per extracted edge in bulk ingestion.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Ingestion")]
public class EdgeResolutionBenchmarks
{
    private const string GroupId = "bench-edge-resolution";
    private static readonly int[] FirstEpisodeIndex = [0];
    private readonly DateTime _now = new(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
    private Dictionary<string, EntityNode> _nodesByExtractedName = null!;
    private global::Graphiti.Core.Graphiti.ExtractedEdge[] _extractedEdges = null!;
    private EpisodicNode[] _episodes = null!;

    [Params(64, 256)]
    public int NodeCount { get; set; }

    [Params(512)]
    public int EdgeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _episodes =
        [
            new EpisodicNode
            {
                Uuid = "episode-0",
                Name = "episode",
                GroupId = GroupId,
                ValidAt = _now
            }
        ];

        _nodesByExtractedName = new Dictionary<string, EntityNode>(NodeCount, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < NodeCount; i++)
        {
            var name = NodeName(i);
            _nodesByExtractedName.Add(name, new EntityNode
            {
                Uuid = $"node-{i:D4}",
                Name = name,
                GroupId = GroupId
            });
        }

        _extractedEdges = new global::Graphiti.Core.Graphiti.ExtractedEdge[EdgeCount];
        for (var i = 0; i < _extractedEdges.Length; i++)
        {
            var sourceIndex = i % NodeCount;
            var targetIndex = (i * 17 + 1) % NodeCount;
            if (sourceIndex == targetIndex)
            {
                targetIndex = (targetIndex + 1) % NodeCount;
            }

            _extractedEdges[i] = new global::Graphiti.Core.Graphiti.ExtractedEdge(
                NodeName(sourceIndex),
                NodeName(targetIndex),
                "COORDINATES",
                $"{NodeName(sourceIndex)} coordinates milestone {i:D4} with {NodeName(targetIndex)}.",
                validAt: _now,
                invalidAt: null,
                episodeIndices: FirstEpisodeIndex);
        }
    }

    [Benchmark]
    public int BuildExtractedEdgeCandidates_ManyExactEndpoints()
    {
        var candidates = EdgeResolutionService.BuildExtractedEdgeCandidates(
            _extractedEdges,
            _nodesByExtractedName,
            _episodes,
            GroupId,
            _now,
            out var skippedEdges);

        return candidates.Count + skippedEdges;
    }

    private static string NodeName(int index) => $"Node {index:D4}";
}
