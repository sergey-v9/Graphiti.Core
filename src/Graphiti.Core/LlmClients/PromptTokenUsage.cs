namespace Graphiti.Core.LlmClients;

/// <summary>Token usage attributed to a specific named prompt.</summary>
public sealed class PromptTokenUsage : TokenUsage
{
    /// <summary>Name of the prompt the usage is attributed to.</summary>
    public string PromptName { get; init; } = string.Empty;

    /// <summary>Number of LLM calls recorded for this prompt.</summary>
    public long CallCount { get; init; }

    /// <summary>Average input tokens per recorded call.</summary>
    public double AvgInputTokens => CallCount > 0 ? InputTokens / (double)CallCount : 0;

    /// <summary>Average output tokens per recorded call.</summary>
    public double AvgOutputTokens => CallCount > 0 ? OutputTokens / (double)CallCount : 0;
}
