using Microsoft.Extensions.Options;

namespace Graphiti.Core.Configuration;

/// <summary>Configuration for the embedder client when resolved through dependency injection.</summary>
public sealed class EmbeddingConfig
{
    /// <summary>Embedding dimension override; falls back to <see cref="GraphitiOptions.EmbeddingDimension"/>.</summary>
    public int? EmbeddingDimension { get; set; }

    /// <summary>Optional embedding model identifier.</summary>
    public string? ModelId { get; set; }

    /// <summary>Optional number of inputs per embedding batch.</summary>
    public int? BatchSize { get; set; }

    /// <summary>Optional maximum number of concurrent embedding batches.</summary>
    public int? BatchConcurrency { get; set; }
}

internal sealed class EmbeddingConfigValidator : IValidateOptions<EmbeddingConfig>
{
    public ValidateOptionsResult Validate(string? name, EmbeddingConfig options)
    {
        var failures = new List<string>();

        if (options.EmbeddingDimension is <= 0)
        {
            failures.Add("EmbeddingConfig.EmbeddingDimension must be positive when set.");
        }

        if (options.ModelId is not null && string.IsNullOrWhiteSpace(options.ModelId))
        {
            failures.Add("EmbeddingConfig.ModelId must not be empty when set.");
        }

        if (options.BatchSize is <= 0)
        {
            failures.Add("EmbeddingConfig.BatchSize must be positive when set.");
        }

        if (options.BatchConcurrency is <= 0)
        {
            failures.Add("EmbeddingConfig.BatchConcurrency must be positive when set.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
