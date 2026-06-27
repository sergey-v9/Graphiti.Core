using BenchmarkDotNet.Attributes;
using Graphiti.Core.Embedding;
using Microsoft.Extensions.AI;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// Embedding vector materialization and adapter output-shaping hot paths. Provider vectors must be
/// copied before returning so downstream graph state is isolated from provider-owned buffers.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Embedding")]
public class EmbeddingBenchmarks
{
    private float[] _vector = null!;
    private List<float> _vectorList = null!;
    private string[] _batchInput = null!;
    private MicrosoftExtensionsAIEmbedderClient _embedder = null!;

    [Params(256, 1024)]
    public int Dimension { get; set; }

    [Params(32)]
    public int BatchCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _vector = CreateVector(Dimension, seed: 1);
        _vectorList = [.. _vector];
        _batchInput = new string[BatchCount];
        for (var i = 0; i < _batchInput.Length; i++)
        {
            _batchInput[i] = $"embedding input {i:D2}";
        }

        _embedder = new MicrosoftExtensionsAIEmbedderClient(
            new StaticEmbeddingGenerator(Dimension, BatchCount),
            Dimension,
            batchSize: BatchCount,
            batchConcurrency: 1);
    }

    [Benchmark]
    public List<float> MaterializeSingle_ReadOnlyMemory() =>
        EmbeddingVectorValidation.MaterializeSingle(
            _vector.AsMemory(),
            Dimension,
            "benchmark embedding");

    [Benchmark]
    public List<float> MaterializeSingle_IReadOnlyList() =>
        EmbeddingVectorValidation.MaterializeSingle(
            _vectorList,
            Dimension,
            "benchmark embedding");

    [Benchmark]
    public Task<IReadOnlyList<IReadOnlyList<float>>> MicrosoftExtensionsAI_CreateBatch() =>
        _embedder.CreateBatchAsync(_batchInput);

    private static float[] CreateVector(int dimension, int seed)
    {
        var vector = new float[dimension];
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (seed + i + 1) / (float)(dimension + seed + 1);
        }

        return vector;
    }

    private sealed class StaticEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        private readonly Embedding<float>[] _embeddings;

        public StaticEmbeddingGenerator(int dimension, int count)
        {
            _embeddings = new Embedding<float>[count];
            for (var i = 0; i < _embeddings.Length; i++)
            {
                _embeddings[i] = new Embedding<float>(CreateVector(dimension, i + 10));
            }
        }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(_embeddings));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
