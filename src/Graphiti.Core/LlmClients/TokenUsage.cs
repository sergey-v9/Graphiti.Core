namespace Graphiti.Core.LlmClients;

/// <summary>Counts the input/output tokens consumed by an LLM call.</summary>
public class TokenUsage
{
    /// <summary>Number of prompt (input) tokens.</summary>
    public long InputTokens { get; init; }

    /// <summary>Number of completion (output) tokens.</summary>
    public long OutputTokens { get; init; }

    /// <summary>Sum of input and output tokens.</summary>
    public long TotalTokens => checked(InputTokens + OutputTokens);
}
