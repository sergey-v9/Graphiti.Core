using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Graphiti.Core.Configuration;

/// <summary>Options for the provider-call resilience pipelines used by AI adapters.</summary>
public sealed class GraphitiResilienceOptions
{
    /// <summary>Number of retries after the initial provider-call attempt. Set to 0 to disable retries.</summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>Initial retry delay. The default pipeline uses exponential backoff.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum retry delay for exponential backoff. Null leaves the Polly strategy uncapped.</summary>
    public TimeSpan? MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>Whether retries should add randomized jitter to avoid synchronized retry bursts.</summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>Per-provider-call attempt timeout. Set to <see cref="TimeSpan.Zero"/> to disable.</summary>
    public TimeSpan AttemptTimeout { get; set; }

    /// <summary>Optional maximum number of concurrent AI provider calls. Null means effectively unbounded.</summary>
    public int? ProviderConcurrencyLimit { get; set; }

    /// <summary>Number of provider calls allowed to wait when the concurrency limit is reached.</summary>
    public int ProviderQueueLimit { get; set; }

    /// <summary>Queue ordering policy used when provider calls wait for a concurrency permit.</summary>
    public QueueProcessingOrder ProviderQueueProcessingOrder { get; set; } = QueueProcessingOrder.OldestFirst;
}

internal sealed class GraphitiResilienceOptionsValidator : IValidateOptions<GraphitiResilienceOptions>
{
    public ValidateOptionsResult Validate(string? name, GraphitiResilienceOptions options)
    {
        var failures = new List<string>();

        if (options.MaxRetryAttempts < 0)
        {
            failures.Add("GraphitiResilienceOptions.MaxRetryAttempts must be non-negative.");
        }

        if (options.RetryDelay < TimeSpan.Zero)
        {
            failures.Add("GraphitiResilienceOptions.RetryDelay must be non-negative.");
        }

        if (options.MaxRetryDelay is { } maxRetryDelay)
        {
            if (maxRetryDelay < TimeSpan.Zero)
            {
                failures.Add("GraphitiResilienceOptions.MaxRetryDelay must be non-negative when set.");
            }
            else if (maxRetryDelay < options.RetryDelay)
            {
                failures.Add("GraphitiResilienceOptions.MaxRetryDelay must be greater than or equal to RetryDelay when set.");
            }
        }

        if (options.AttemptTimeout < TimeSpan.Zero)
        {
            failures.Add("GraphitiResilienceOptions.AttemptTimeout must be non-negative.");
        }

        if (options.ProviderConcurrencyLimit is <= 0)
        {
            failures.Add("GraphitiResilienceOptions.ProviderConcurrencyLimit must be positive when set.");
        }

        if (options.ProviderQueueLimit < 0)
        {
            failures.Add("GraphitiResilienceOptions.ProviderQueueLimit must be non-negative.");
        }

        if (!Enum.IsDefined(options.ProviderQueueProcessingOrder))
        {
            failures.Add("GraphitiResilienceOptions.ProviderQueueProcessingOrder must be a defined queue processing order.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
