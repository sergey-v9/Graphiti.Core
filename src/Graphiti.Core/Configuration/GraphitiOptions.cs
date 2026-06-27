using Microsoft.Extensions.Options;

namespace Graphiti.Core.Configuration;

/// <summary>
/// Options that control how Graphiti Core is constructed when registered through dependency
/// injection: which graph backend to use, its connection details, and instance-level behavior.
/// Validated by <see cref="IValidateOptions{TOptions}"/>.
/// </summary>
public sealed class GraphitiOptions
{
    /// <summary>Graph backend to use. Defaults to the in-memory driver.</summary>
    public GraphProvider Provider { get; set; } = GraphProvider.InMemory;

    /// <summary>Database name; empty uses the driver default.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>Whether to persist raw episode content.</summary>
    public bool StoreRawEpisodeContent { get; set; } = true;

    /// <summary>Optional cap on concurrent operations; null or zero uses the default.</summary>
    public int? MaxCoroutines { get; set; }

    /// <summary>Embedding vector dimension used when no embedder-specific value is set.</summary>
    public int EmbeddingDimension { get; set; } = 1024;

    /// <summary>Time source for timestamps; defaults to the system clock when unset.</summary>
    public TimeProvider? TimeProvider { get; set; }

    /// <summary>Optional factory that builds a custom <see cref="IGraphDriver"/>, bypassing <see cref="Provider"/>.</summary>
    public Func<IServiceProvider, IGraphDriver>? GraphDriverFactory { get; set; }
}

internal sealed class GraphitiOptionsValidator : IValidateOptions<GraphitiOptions>
{
    public ValidateOptionsResult Validate(string? name, GraphitiOptions options)
    {
        var failures = new List<string>();

        if (options.EmbeddingDimension <= 0)
        {
            failures.Add("GraphitiOptions.EmbeddingDimension must be positive.");
        }

        if (options.MaxCoroutines is < 0)
        {
            failures.Add("GraphitiOptions.MaxCoroutines must be non-negative when set.");
        }

        if (!string.IsNullOrEmpty(options.Database) && string.IsNullOrWhiteSpace(options.Database))
        {
            failures.Add("GraphitiOptions.Database must not be blank when set.");
        }

        if (options.GraphDriverFactory is not null)
        {
            return failures.Count == 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(failures);
        }

        switch (options.Provider)
        {
            case GraphProvider.InMemory:
            case GraphProvider.LadybugDb:
#pragma warning disable GRPH0001
            // LadybugDb is the driver-facing value; Kuzu is the obsolete compatibility alias.
            case GraphProvider.Kuzu:
#pragma warning restore GRPH0001
                break;
            default:
                failures.Add($"{options.Provider} is not supported by Graphiti.Core's built-in driver factory.");
                break;
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
