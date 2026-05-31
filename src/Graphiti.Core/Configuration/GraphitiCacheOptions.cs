using Microsoft.Extensions.Options;

namespace Graphiti.Core.Configuration;

/// <summary>Options for Graphiti's built-in cache adapters.</summary>
public sealed class GraphitiCacheOptions
{
    /// <summary>Overall expiration for cached LLM responses. Null uses the HybridCache default.</summary>
    public TimeSpan? LlmResponseExpiration { get; set; }

    /// <summary>Local in-process expiration for cached LLM responses. Null uses the HybridCache default.</summary>
    public TimeSpan? LlmResponseLocalCacheExpiration { get; set; }

    /// <summary>Tags applied to LLM response cache entries for cache providers that support tagging.</summary>
    public string[] LlmResponseTags { get; set; } = [];
}

internal sealed class GraphitiCacheOptionsValidator : IValidateOptions<GraphitiCacheOptions>
{
    public ValidateOptionsResult Validate(string? name, GraphitiCacheOptions options)
    {
        var failures = new List<string>();

        if (options.LlmResponseExpiration < TimeSpan.Zero)
        {
            failures.Add("GraphitiCacheOptions.LlmResponseExpiration must be non-negative when set.");
        }

        if (options.LlmResponseLocalCacheExpiration < TimeSpan.Zero)
        {
            failures.Add("GraphitiCacheOptions.LlmResponseLocalCacheExpiration must be non-negative when set.");
        }

        if (options.LlmResponseTags.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add("GraphitiCacheOptions.LlmResponseTags must not contain blank values.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
