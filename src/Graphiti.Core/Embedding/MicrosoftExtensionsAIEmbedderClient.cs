using System.Threading.RateLimiting;
using Microsoft.Extensions.AI;
using Polly;

namespace Graphiti.Core.Embedding;

/// <summary>
/// An <see cref="EmbedderClient"/> that delegates to a <c>Microsoft.Extensions.AI</c>
/// <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>, with optional Polly resilience, rate limiting,
/// and batching. Generated vectors are validated against the configured embedding dimension.
/// </summary>
public sealed class MicrosoftExtensionsAIEmbedderClient : EmbedderClient
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly ResiliencePipeline<GeneratedEmbeddings<Embedding<float>>>? _pipeline;
    private readonly string? _modelId;
    private readonly RateLimiter? _rateLimiter;

    /// <summary>Creates the embedder over the given generator and expected embedding dimension.</summary>
    public MicrosoftExtensionsAIEmbedderClient(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        int embeddingDimension,
        string? modelId = null,
        ResiliencePipeline<GeneratedEmbeddings<Embedding<float>>>? pipeline = null,
        RateLimiter? rateLimiter = null,
        int? batchSize = null,
        int? batchConcurrency = null)
        : base(new EmbedderConfig(embeddingDimension, batchConcurrency, batchSize))
    {
        _embeddingGenerator = embeddingGenerator;
        _pipeline = pipeline;
        _modelId = modelId;
        _rateLimiter = rateLimiter;
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<float>> CreateAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        using var activity = GraphitiTelemetry.StartActivity("Embedder.Create");
        SetEmbeddingActivityTags(activity, EmbeddingDimension, inputCount: 1, modelId: _modelId);

        try
        {
            var inputs = new[] { input };
            var options = CreateOptions();
            var embeddings = _pipeline is null
                ? await ExecuteProviderCallAsync(cancellationToken).ConfigureAwait(false)
                : await _pipeline.ExecuteAsync(
                    ExecuteProviderCallAsync,
                    cancellationToken).ConfigureAwait(false);

            activity?.SetTag("graphiti.embedding.output_count", embeddings.Count);
            ValidateEmbeddingCount(embeddings.Count, expectedCount: 1);
            var vector = MaterializeEmbeddingVector(embeddings[0], startIndex: 0);
            GraphitiTelemetry.SetOk(activity);
            return vector;

            async ValueTask<GeneratedEmbeddings<Embedding<float>>> ExecuteProviderCallAsync(CancellationToken token)
            {
                return await ExecuteProviderCallCoreAsync(
                    inputs,
                    options,
                    batchIndex: 0,
                    startIndex: 0,
                    token).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<IReadOnlyList<float>>> CreateBatchAsync(
        IReadOnlyList<string> input,
        CancellationToken cancellationToken = default)
    {
        using var activity = GraphitiTelemetry.StartActivity("Embedder.CreateBatch");
        SetEmbeddingActivityTags(activity, EmbeddingDimension, input.Count, _modelId);

        try
        {
            if (input.Count == 0)
            {
                GraphitiTelemetry.SetOk(activity);
                return Array.Empty<IReadOnlyList<float>>();
            }

            var chunks = CreateBatchChunks(input, Config.BatchSize);
            activity?.SetTag("graphiti.embedding.batch_size", Config.BatchSize);
            activity?.SetTag("graphiti.embedding.batch_count", chunks.Count);
            var options = CreateOptions();
            var output = new IReadOnlyList<float>[input.Count];
            await Parallel.ForEachAsync(
                chunks,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Math.Min(chunks.Count, Config.BatchConcurrency)
                },
                async (chunk, token) =>
                {
                    var embeddings = _pipeline is null
                        ? await ExecuteProviderCallAsync(chunk, token).ConfigureAwait(false)
                        : await _pipeline.ExecuteAsync(
                            pipelineToken => ExecuteProviderCallAsync(chunk, pipelineToken),
                            token).ConfigureAwait(false);

                    ValidateEmbeddingCount(embeddings.Count, chunk.Inputs.Length);
                    for (var i = 0; i < embeddings.Count; i++)
                    {
                        output[chunk.StartIndex + i] =
                            MaterializeEmbeddingVector(embeddings[i], chunk.StartIndex + i);
                    }
                }).ConfigureAwait(false);

            for (var i = 0; i < output.Length; i++)
            {
                if (output[i] is null)
                {
                    throw new InvalidOperationException(
                        $"Embedding provider did not return a vector at index {i}.");
                }
            }

            activity?.SetTag("graphiti.embedding.output_count", output.Length);
            GraphitiTelemetry.SetOk(activity);
            return output;

            async ValueTask<GeneratedEmbeddings<Embedding<float>>> ExecuteProviderCallAsync(
                EmbeddingBatchChunk chunk,
                CancellationToken token)
            {
                return await ExecuteProviderCallCoreAsync(
                    chunk.Inputs,
                    options,
                    chunk.BatchIndex,
                    chunk.StartIndex,
                    token).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private async ValueTask<GeneratedEmbeddings<Embedding<float>>> ExecuteProviderCallCoreAsync(
        string[] inputs,
        EmbeddingGenerationOptions options,
        int batchIndex,
        int startIndex,
        CancellationToken cancellationToken)
    {
        using var activity = GraphitiTelemetry.StartActivity("Embedder.ProviderCall");
        activity?.SetTag("graphiti.provider.abstraction", "microsoft_extensions_ai");
        SetEmbeddingActivityTags(activity, EmbeddingDimension, inputs.Length, _modelId);
        activity?.SetTag("graphiti.embedding.batch_index", batchIndex);
        activity?.SetTag("graphiti.embedding.batch_start_index", startIndex);
        activity?.SetTag("graphiti.embedding.batch_size", inputs.Length);
        activity?.SetTag("graphiti.provider.rate_limited", _rateLimiter is not null);

        try
        {
            using var lease = await AIProviderRateLimiter.AcquireAsync(
                _rateLimiter,
                cancellationToken).ConfigureAwait(false);
            var embeddings = await _embeddingGenerator.GenerateAsync(
                inputs,
                options,
                cancellationToken).ConfigureAwait(false);
            activity?.SetTag("graphiti.embedding.output_count", embeddings.Count);
            GraphitiTelemetry.SetOk(activity);
            return embeddings;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private static void ValidateEmbeddingCount(int actualCount, int expectedCount)
    {
        if (actualCount != expectedCount)
        {
            throw new InvalidOperationException(
                $"Embedding provider returned {actualCount} embedding(s) for {expectedCount} input(s).");
        }
    }

    private List<float> MaterializeEmbeddingVector(Embedding<float> embedding, int startIndex)
    {
        return EmbeddingVectorValidation.MaterializeSingle(
            embedding.Vector,
            EmbeddingDimension,
            $"provider embedding at index {startIndex}");
    }

    private static List<EmbeddingBatchChunk> CreateBatchChunks(
        IReadOnlyList<string> input,
        int batchSize)
    {
        var chunks = new List<EmbeddingBatchChunk>((input.Count + batchSize - 1) / batchSize);
        for (var startIndex = 0; startIndex < input.Count; startIndex += batchSize)
        {
            var count = Math.Min(batchSize, input.Count - startIndex);
            var values = new string[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = input[startIndex + i];
            }

            chunks.Add(new EmbeddingBatchChunk(chunks.Count, startIndex, values));
        }

        return chunks;
    }

    private EmbeddingGenerationOptions CreateOptions() =>
        new()
        {
            Dimensions = EmbeddingDimension,
            ModelId = _modelId
        };

    private readonly record struct EmbeddingBatchChunk(int BatchIndex, int StartIndex, string[] Inputs);
}
