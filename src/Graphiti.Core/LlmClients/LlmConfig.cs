using Microsoft.Extensions.Options;

namespace Graphiti.Core.LlmClients;

/// <summary>Configuration for an <see cref="LlmClient"/>: credentials, model selection, and sampling.</summary>
public sealed class LlmConfig
{
    /// <summary>API key for the LLM provider, if required.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Primary (medium-size) model identifier.</summary>
    public string Model { get; set; } = "gpt-4.1-mini";

    /// <summary>Optional base URL override for the provider endpoint.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Sampling temperature. Reasoning models that require 0 are handled by the client.</summary>
    public double Temperature { get; set; } = 1;

    /// <summary>Maximum number of tokens to generate per request.</summary>
    public int MaxTokens { get; set; } = 16_384;

    /// <summary>Optional smaller/faster model used for <see cref="ModelSize.Small"/> requests.</summary>
    public string? SmallModel { get; set; }
}

internal sealed class LlmConfigValidator : IValidateOptions<LlmConfig>
{
    public ValidateOptionsResult Validate(string? name, LlmConfig options)
    {
        var failures = LlmConfigValidation.GetFailures(options);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

internal static class LlmConfigValidation
{
    public static IReadOnlyList<string> GetFailures(LlmConfig options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Model))
        {
            failures.Add("LlmConfig.Model must not be empty.");
        }

        if (options.SmallModel is not null && string.IsNullOrWhiteSpace(options.SmallModel))
        {
            failures.Add("LlmConfig.SmallModel must not be empty when set.");
        }

        if (options.MaxTokens <= 0)
        {
            failures.Add("LlmConfig.MaxTokens must be positive.");
        }

        if (!double.IsFinite(options.Temperature) || options.Temperature < 0)
        {
            failures.Add("LlmConfig.Temperature must be a non-negative finite number.");
        }

        return failures;
    }

    public static void ThrowIfInvalid(LlmConfig options)
    {
        var failures = GetFailures(options);
        if (failures.Count > 0)
        {
            throw new ArgumentException(
                $"Invalid LLM configuration: {string.Join(" ", failures)}",
                nameof(options));
        }
    }

    public static int ResolveMaxTokens(int? maxTokens, int configuredMaxTokens)
    {
        var resolvedMaxTokens = maxTokens ?? configuredMaxTokens;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            resolvedMaxTokens,
            maxTokens is null ? nameof(configuredMaxTokens) : nameof(maxTokens));
        return resolvedMaxTokens;
    }
}
