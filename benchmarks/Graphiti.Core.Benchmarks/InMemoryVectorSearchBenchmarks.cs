using BenchmarkDotNet.Attributes;
using Graphiti.Core.Drivers;
using Graphiti.Core.Models.Nodes;
using Graphiti.Core.Search;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// In-memory vector search over deterministic stored nodes. This covers the reference driver's
/// full-scan cosine path and final-hit cloning rather than the lower-level scorer alone.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Search")]
public class InMemoryVectorSearchBenchmarks : IAsyncDisposable
{
    private const string GroupId = "bench-vector";
    private const int VectorDimension = 256;
    private InMemoryGraphDriver? _driver;
    private SearchFilters _searchFilters = null!;
    private string[] _groupIds = null!;
    private float[] _queryVector = null!;

    [Params(500, 2000)]
    public int CandidateCount { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _driver = new InMemoryGraphDriver("bench-vector-search");
        _searchFilters = new SearchFilters { NodeLabels = new List<string> { "Entity" } };
        _groupIds = [GroupId];
        _queryVector = BenchmarkData.CreateUnitVector(VectorDimension, seed: 7001);

        for (var i = 0; i < CandidateCount; i++)
        {
            var node = new EntityNode
            {
                Uuid = $"vector-node-{i:D6}",
                Name = $"Vector candidate {i:D6}",
                Summary = BenchmarkData.CreateDocument(approximateWords: 20, seed: 7100 + i),
                GroupId = GroupId,
                Labels = { "Entity" },
                NameEmbedding = [.. BenchmarkData.CreateUnitVector(VectorDimension, seed: 7200 + i)]
            };
            await node.SaveAsync(_driver).ConfigureAwait(false);
        }
    }

    [GlobalCleanup]
    public ValueTask CleanupAsync() => DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        if (_driver is not null)
        {
            await _driver.DisposeAsync().ConfigureAwait(false);
            _driver = null;
        }

        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public async Task<int> SearchEntityNodesByEmbedding_TopK()
    {
        var hits = await _driver!
            .SearchEntityNodesByEmbeddingAsync(
                _queryVector,
                _searchFilters,
                _groupIds,
                limit: 25,
                minScore: -2.0f)
            .ConfigureAwait(false);
        return hits.Count;
    }
}
