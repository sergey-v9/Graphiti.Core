using Microsoft.Extensions.Options;

namespace Graphiti.Core.Text;

/// <summary>Tunable parameters controlling how episode content is chunked before processing.</summary>
public sealed class ContentChunkingOptions
{
    /// <summary>Target chunk size in tokens.</summary>
    public int ChunkTokenSize { get; set; } = ContentChunking.DefaultChunkTokenSize;

    /// <summary>Number of overlapping tokens carried between consecutive chunks.</summary>
    public int ChunkOverlapTokens { get; set; } = ContentChunking.DefaultChunkOverlapTokens;

    /// <summary>Minimum token count below which content is never chunked.</summary>
    public int ChunkMinTokens { get; set; } = ContentChunking.DefaultChunkMinTokens;

    /// <summary>Density threshold used to decide whether dense content warrants chunking.</summary>
    public double ChunkDensityThreshold { get; set; } = ContentChunking.DefaultChunkDensityThreshold;
}

internal sealed class ContentChunkingOptionsValidator : IValidateOptions<ContentChunkingOptions>
{
    public ValidateOptionsResult Validate(string? name, ContentChunkingOptions options)
    {
        var failures = new List<string>();
        if (options.ChunkTokenSize <= 0)
        {
            failures.Add("ContentChunkingOptions.ChunkTokenSize must be positive.");
        }

        if (options.ChunkOverlapTokens < 0)
        {
            failures.Add("ContentChunkingOptions.ChunkOverlapTokens must be non-negative.");
        }

        if (options.ChunkMinTokens <= 0)
        {
            failures.Add("ContentChunkingOptions.ChunkMinTokens must be positive.");
        }

        if (double.IsNaN(options.ChunkDensityThreshold)
            || double.IsInfinity(options.ChunkDensityThreshold)
            || options.ChunkDensityThreshold <= 0)
        {
            failures.Add("ContentChunkingOptions.ChunkDensityThreshold must be a positive finite number.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
