using BenchmarkDotNet.Attributes;
using Graphiti.Core.Drivers.Ladybug;
using Graphiti.Core.Models.Nodes;
using Graphiti.Core.Search;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// Ladybug statement construction for common read/search paths. These builders run before every
/// driver execution and allocate parameter maps for the generated Cypher.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Ladybug")]
public class LadybugStatementBuilderBenchmarks
{
    private string _nodeUuid = null!;
    private string _sagaUuid = null!;
    private string _centerNodeUuid = null!;
    private string[] _nodeUuids = null!;
    private string[] _groupIds = null!;
    private float[] _searchVector = null!;
    private SearchFilters _filters = null!;
    private DateTime _since;

    [GlobalSetup]
    public void Setup()
    {
        _nodeUuid = "entity-0001";
        _sagaUuid = "saga-0001";
        _centerNodeUuid = "entity-center";
        _nodeUuids =
        [
            "entity-center",
            "entity-0001",
            "entity-0002",
            "entity-0003",
            "entity-0004",
            "entity-0005",
            "entity-0006",
            "entity-0007",
            "entity-0008",
            "entity-0009",
            "entity-0010",
            "entity-0011"
        ];
        _groupIds = ["tenant-a", "tenant-b"];
        _searchVector = BenchmarkData.CreateUnitVector(dimension: 64, seed: 9401);
        _filters = new SearchFilters
        {
            NodeLabels = new List<string> { "Entity", "Person" }
        };
        _since = new DateTime(2026, 2, 4, 10, 0, 0, DateTimeKind.Utc);
    }

    [Benchmark]
    public int BuildNodeGetByUuid_OneParameter() =>
        Measure(LadybugStatementBuilder.BuildNodeGetByUuid<EntityNode>(_nodeUuid));

    [Benchmark]
    public int BuildSagaEpisodeContentsGet_ThreeParameters() =>
        Measure(LadybugStatementBuilder.BuildSagaEpisodeContentsGet(_sagaUuid, _since, limit: 50));

    [Benchmark]
    public int BuildCommunityEmbeddingSearch_WithVectorAndGroups() =>
        Measure(LadybugSearchStatementBuilder.BuildCommunityEmbeddingSearchStatement(
            _searchVector,
            _groupIds,
            limit: 10,
            minScore: 0.25f));

    [Benchmark]
    public int BuildNodeDistanceRankStatements_ManyTwoParameterMaps() =>
        Measure(LadybugSearchStatementBuilder.BuildNodeDistanceRankStatements(_nodeUuids, _centerNodeUuid));

    [Benchmark]
    public int BuildEntityNodeBfsSearchStatements_Filtered() =>
        Measure(LadybugSearchStatementBuilder.BuildEntityNodeBfsSearchStatements(
            _nodeUuids,
            _filters,
            maxDepth: 2,
            _groupIds,
            limit: 10));

    private static int Measure(LadybugStatement statement) =>
        statement.Query.Length + statement.Parameters.Count;

    private static int Measure(IReadOnlyList<LadybugStatement> statements)
    {
        var checksum = statements.Count;
        for (var i = 0; i < statements.Count; i++)
        {
            checksum += statements[i].Query.Length + statements[i].Parameters.Count;
        }

        return checksum;
    }
}
