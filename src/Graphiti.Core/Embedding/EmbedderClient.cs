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

    /// <inheritdoc />
    public abstract Task<IReadOnlyList<float>> CreateAsync(
        string input,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>
    /// Stamps OpenTelemetry gen_ai/embedding tags onto the supplied <see cref="System.Diagnostics.Activity"/>.
    /// Sets <c>gen_ai.operation.name</c>, <c>gen_ai.request.model</c>, <c>graphiti.embedding.dimension</c>,
    /// and <c>graphiti.embedding.input_count</c>. No-op when <paramref name="activity"/> is null.
    /// </summary>
    /// <param name="activity">The activity to tag, or null when tracing is disabled.</param>
    /// <param name="embeddingDimension">The dimensionality of the produced embedding vectors.</param>
    /// <param name="inputCount">The number of inputs in the embedding request.</param>
    /// <param name="modelId">The embedding model identifier, or null when unknown.</param>
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
