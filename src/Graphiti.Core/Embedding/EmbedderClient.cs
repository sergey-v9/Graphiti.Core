using Graphiti.Core.Internal;

namespace Graphiti.Core.Embedding;

/// <summary>
/// Base class for embedder implementations. Provides config-driven batching and telemetry while
/// requiring subclasses to implement single-input embedding.
/// </summary>
public abstract class EmbedderClient : IEmbedderClient
{
    /// <summary>Initializes the embedder with the given config (or defaults).</summary>
    protected EmbedderClient(EmbedderConfig? config = null)
    {
        Config = config ?? new EmbedderConfig();
    }

    /// <summary>Configuration governing dimension and batching.</summary>
    public EmbedderConfig Config { get; }

    /// <inheritdoc />
    public int EmbeddingDimension => Config.EmbeddingDimension;

    public abstract Task<IReadOnlyList<float>> CreateAsync(
        string input,
        CancellationToken cancellationToken = default);

    public virtual async Task<IReadOnlyList<float>> CreateAsync(
        IReadOnlyList<string> input,
        CancellationToken cancellationToken = default)
    {
        if (input.Count == 0)
        {
            return Array.Empty<float>();
        }

        return await CreateAsync(input[0], cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<IReadOnlyList<IReadOnlyList<float>>> CreateBatchAsync(
        IReadOnlyList<string> input,
        CancellationToken cancellationToken = default)
    {
        using var activity = GraphitiTelemetry.StartActivity("Embedder.CreateBatch");
        SetEmbeddingActivityTags(activity, EmbeddingDimension, input.Count);

        try
        {
            if (input.Count == 0)
            {
                GraphitiTelemetry.SetOk(activity);
                return Array.Empty<IReadOnlyList<float>>();
            }

            var inputs = new string[input.Count];
            for (var i = 0; i < input.Count; i++)
            {
                inputs[i] = input[i];
            }

            var embeddings = await ThrottledWork.SelectAsync(
                inputs,
                CreateAsync,
                Config.BatchConcurrency,
                cancellationToken).ConfigureAwait(false);
            activity?.SetTag("graphiti.embedding.output_count", embeddings.Length);
            GraphitiTelemetry.SetOk(activity);
            return embeddings;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    protected static void SetEmbeddingActivityTags(
        System.Diagnostics.Activity? activity,
        int embeddingDimension,
        int inputCount,
        string? modelId = null)
    {
        activity?.SetTag("gen_ai.operation.name", "embeddings");
        activity?.SetTag("gen_ai.request.model", modelId);
        activity?.SetTag("graphiti.embedding.dimension", embeddingDimension);
        activity?.SetTag("graphiti.embedding.input_count", inputCount);
    }
}
