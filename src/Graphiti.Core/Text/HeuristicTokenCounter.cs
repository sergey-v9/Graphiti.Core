namespace Graphiti.Core.Text;

/// <summary>
/// A tokenizer-free token estimator that approximates token count as <c>floor(length / charsPerToken)</c>.
/// Used as the fallback chunking heuristic when a real tiktoken model cannot be loaded.
/// </summary>
public sealed class HeuristicTokenCounter : ITokenCounter, ITokenBoundaryProvider
{
    /// <summary>Creates a counter using the given average characters-per-token ratio.</summary>
    public HeuristicTokenCounter(int charsPerToken = ContentChunking.CharsPerToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(charsPerToken);

        CharsPerToken = charsPerToken;
    }

    /// <summary>Average number of characters assumed per token.</summary>
    public int CharsPerToken { get; }

    /// <inheritdoc />
    public int CountTokens(string? text)
    {
        var length = (text ?? string.Empty).Length;
        return length / CharsPerToken;
    }

    bool ITokenBoundaryProvider.TryGetIndexByTokenCount(ReadOnlySpan<char> text, int maxTokens, out int index)
    {
        if (maxTokens <= 0)
        {
            index = 0;
            return false;
        }

        index = Math.Min(text.Length, TokensToCharacters(maxTokens));
        return true;
    }

    bool ITokenBoundaryProvider.TryGetIndexByTokenCountFromEnd(ReadOnlySpan<char> text, int maxTokens, out int index)
    {
        if (maxTokens <= 0)
        {
            index = text.Length;
            return false;
        }

        index = Math.Max(0, text.Length - TokensToCharacters(maxTokens));
        return true;
    }

    private int TokensToCharacters(int tokens) =>
        tokens > int.MaxValue / CharsPerToken
            ? int.MaxValue
            : tokens * CharsPerToken;
}
