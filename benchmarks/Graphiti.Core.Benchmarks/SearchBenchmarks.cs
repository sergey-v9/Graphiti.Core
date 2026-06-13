using BenchmarkDotNet.Attributes;
using Graphiti.Core.Models.Edges;
using Graphiti.Core.Models.Nodes;
using Graphiti.Core.Search;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// Hot search-composition paths: reciprocal-rank fusion, MMR diversity reranking, BM25 scoring,
/// and the lightweight term-overlap <see cref="TextScorer"/>. These run per search query over a few
/// hundred candidates, so allocation and time per call matter.
/// </summary>
[MemoryDiagnoser]
public class SearchBenchmarks
{
    private List<(EntityEdge Item, float Score)> _edgesA = null!;
    private List<(EntityEdge Item, float Score)> _edgesB = null!;
    private List<(EntityEdge Item, float Score)> _edgesC = null!;
    private List<List<(EntityEdge Item, float Score)>> _overlappingLists = null!;
    private List<(EntityNode Item, float Score)> _nodesWithEmbeddings = null!;
    private float[] _queryVector = null!;
    private List<EntityEdge> _bm25Candidates = null!;
    private string _query = null!;
    private TextScorer _textScorer = null!;
    private List<string> _scoreTexts = null!;

    [Params(200, 500)]
    public int CandidateCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _query = BenchmarkData.Query;
        _edgesA = BenchmarkData.CreateRankedEdges(CandidateCount, seed: 1);
        _edgesB = BenchmarkData.CreateRankedEdges(CandidateCount, seed: 2);
        _edgesC = BenchmarkData.CreateRankedEdges(CandidateCount, seed: 3);
        _overlappingLists = BenchmarkData.CreateOverlappingEdgeLists(listCount: 4, perList: CandidateCount, seed: 7);

        _nodesWithEmbeddings = BenchmarkData.CreateRankedNodesWithEmbeddings(CandidateCount, dimension: 256, seed: 11);
        _queryVector = BenchmarkData.CreateUnitVector(256, seed: 99);

        var rankedEdges = BenchmarkData.CreateRankedEdges(CandidateCount, seed: 5);
        _bm25Candidates = new List<EntityEdge>(rankedEdges.Count);
        foreach (var (item, _) in rankedEdges)
        {
            _bm25Candidates.Add(item);
        }

        _textScorer = new TextScorer(_query);
        _scoreTexts = new List<string>(CandidateCount);
        for (var i = 0; i < CandidateCount; i++)
        {
            _scoreTexts.Add(_bm25Candidates[i % _bm25Candidates.Count].Fact);
        }
    }

    [Benchmark]
    public int Rrf_TwoLists()
    {
        var fused = SearchResultComposer.FuseRanks(
            _edgesA,
            _edgesB,
            static edge => edge.Uuid,
            limit: CandidateCount);
        return fused.Count;
    }

    [Benchmark]
    public int Rrf_ThreeLists()
    {
        var fused = SearchResultComposer.FuseRanks(
            _edgesA,
            _edgesB,
            _edgesC,
            static edge => edge.Uuid,
            limit: CandidateCount);
        return fused.Count;
    }

    [Benchmark]
    public int Rrf_EnumerableLists()
    {
        var fused = SearchResultComposer.FuseRanks(
            _overlappingLists,
            static edge => edge.Uuid,
            limit: CandidateCount);
        return fused.Count;
    }

    [Benchmark]
    public int Mmr_Rerank()
    {
        var reranked = SearchResultComposer.ApplyMmrReranker(
            _nodesWithEmbeddings,
            _queryVector,
            static node => node.NameEmbedding,
            limit: 50,
            lambda: 0.5f,
            minScore: -2.0f);
        return reranked.Count;
    }

    [Benchmark]
    public int Bm25_Rank()
    {
        var ranked = Bm25TextScorer.Rank(
            _bm25Candidates,
            static edge => edge.Fact,
            _query,
            limit: 50);
        return ranked.Count;
    }

    [Benchmark]
    public float TextScorer_ScoreAll()
    {
        float total = 0;
        for (var i = 0; i < _scoreTexts.Count; i++)
        {
            total += _textScorer.Score(_scoreTexts[i]);
        }

        return total;
    }
}
