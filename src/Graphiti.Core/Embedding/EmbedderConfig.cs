namespace Graphiti.Core.Embedding;

/// <summary>Configuration for an <see cref="EmbedderClient"/>: vector size and batching behavior.</summary>
public sealed class EmbedderConfig
{
    /// <summary>Default number of inputs sent per embedding batch.</summary>
    public const int DefaultBatchSize = 128;

    /// <summary>Default maximum number of concurrent batch operations.</summary>
    public const int DefaultBatchConcurrency = 8;

    /// <summary>Creates a config, applying defaults and validating positive values.</summary>
    public EmbedderConfig(
        int? embeddingDimension = null,
        int? batchConcurrency = null,
        int? batchSize = null)
    {
        EmbeddingDimension = embeddingDimension ?? 1024;
        BatchConcurrency = batchConcurrency ?? DefaultBatchConcurrency;
        BatchSize = batchSize ?? DefaultBatchSize;
        if (EmbeddingDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(embeddingDimension));
        }

        if (BatchConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchConcurrency));
        }

        if (BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }
    }

    /// <summary>Dimensionality of produced vectors.</summary>
    public int EmbeddingDimension { get; }

    /// <summary>Maximum number of concurrent batch operations.</summary>
    public int BatchConcurrency { get; }

    /// <summary>Number of inputs per embedding batch.</summary>
    public int BatchSize { get; }
}
