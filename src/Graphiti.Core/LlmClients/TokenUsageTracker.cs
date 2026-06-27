using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace Graphiti.Core.LlmClients;

/// <summary>
/// Thread-safe accumulator of per-prompt token usage across the lifetime of a client, used for
/// reporting and cost tracking.
/// </summary>
public sealed class TokenUsageTracker
{
    private readonly ConcurrentDictionary<string, PromptTokenUsage> _usage = new(StringComparer.Ordinal);

    /// <summary>Snapshot of accumulated usage keyed by prompt name.</summary>
    public IReadOnlyDictionary<string, PromptTokenUsage> Usage =>
        _usage.ToFrozenDictionary(
            pair => pair.Key,
            pair => CloneUsage(pair.Value),
            StringComparer.Ordinal);

    /// <summary>Adds token usage for a prompt, accumulating onto any existing total.</summary>
    public void AddUsage(string promptName, long inputTokens, long outputTokens)
    {
        promptName = string.IsNullOrWhiteSpace(promptName) ? "unknown" : promptName;
        _usage.AddOrUpdate(
            promptName,
            _ => new PromptTokenUsage
            {
                PromptName = promptName,
                CallCount = 1,
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            },
            (_, usage) => new PromptTokenUsage
            {
                PromptName = usage.PromptName,
                CallCount = checked(usage.CallCount + 1),
                InputTokens = checked(usage.InputTokens + inputTokens),
                OutputTokens = checked(usage.OutputTokens + outputTokens)
            });
    }

    /// <summary>Returns the combined usage across all tracked prompts.</summary>
    public TokenUsage GetTotalUsage()
    {
        long inputTokens = 0;
        long outputTokens = 0;
        foreach (var usage in _usage.Values)
        {
            inputTokens = checked(inputTokens + usage.InputTokens);
            outputTokens = checked(outputTokens + usage.OutputTokens);
        }

        return new TokenUsage
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens
        };
    }

    /// <summary>Clears all accumulated usage.</summary>
    public void Reset() => _usage.Clear();

    private static PromptTokenUsage CloneUsage(PromptTokenUsage usage) =>
        new()
        {
            PromptName = usage.PromptName,
            CallCount = usage.CallCount,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens
        };
}
