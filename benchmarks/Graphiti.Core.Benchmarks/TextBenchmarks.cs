using BenchmarkDotNet.Attributes;
using Graphiti.Core.Text;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// Ingestion-side text paths: real tiktoken counting, density heuristics, sentence-aware truncation,
/// and free-text chunking over a multi-KB document.
/// </summary>
[MemoryDiagnoser]
public class TextBenchmarks
{
    private string _document = null!;
    private string _summarySource = null!;
    private ITokenCounter _tiktoken = null!;
    private HeuristicTokenCounter _heuristic = null!;
    private int _documentTokenEstimate;

    [Params(2000)]
    public int DocumentWords { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _document = BenchmarkData.CreateDocument(DocumentWords, seed: 61);
        _summarySource = BenchmarkData.CreateDocument(400, seed: 62);
        _tiktoken = TiktokenTokenCounter.CreateDefault("gpt-4o");
        _heuristic = new HeuristicTokenCounter();
        _documentTokenEstimate = _heuristic.CountTokens(_document);
    }

    [Benchmark]
    public int Tiktoken_CountTokens() => _tiktoken.CountTokens(_document);

    [Benchmark]
    public int Heuristic_CountTokens() => _heuristic.CountTokens(_document);

    [Benchmark]
    public bool TextLikelyDense() =>
        ContentChunking.TextLikelyDense(_document, _documentTokenEstimate);

    [Benchmark]
    public string? TruncateAtSentence() =>
        TextUtilities.TruncateAtSentence(_summarySource, TextUtilities.MaxSummaryChars);

    [Benchmark]
    public int ChunkTextContent_Heuristic()
    {
        var chunks = ContentChunking.ChunkTextContent(
            _document,
            chunkSizeTokens: 512,
            overlapTokens: 64,
            _heuristic);
        return chunks.Count;
    }
}
