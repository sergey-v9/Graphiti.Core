using System.Collections.Concurrent;
using Microsoft.ML.Tokenizers;

namespace Graphiti.Core.Text;

/// <summary>
/// A token counter backed by tiktoken (via <c>Microsoft.ML.Tokenizers</c>). Tokenizers are cached per
/// model name. Also exposes token-boundary lookups used to split text on exact token limits.
/// </summary>
public sealed class TiktokenTokenCounter : ITokenCounter, ITokenBoundaryProvider
{
    private static readonly ConcurrentDictionary<string, Lazy<Tokenizer>> Tokenizers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Tokenizer _tokenizer;

    /// <summary>Creates a counter for the named model, reusing a cached tokenizer when possible.</summary>
    public TiktokenTokenCounter(string model = "gpt-4o")
        : this(GetOrCreateTokenizer(model))
    {
    }

    /// <summary>Creates a counter wrapping an already-constructed tokenizer.</summary>
    public TiktokenTokenCounter(Tokenizer tokenizer)
    {
        _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
    }

    /// <inheritdoc />
    public int CountTokens(string? text) =>
        _tokenizer.CountTokens(text ?? string.Empty, considerPreTokenization: true, considerNormalization: true);

    /// <summary>
    /// Finds the character index up to which <paramref name="text"/> contains at most
    /// <paramref name="maxTokens"/> tokens, measuring from the start. Returns <c>false</c> if the
    /// tokenizer renormalized the text or produced an out-of-range index.
    /// </summary>
    public bool TryGetIndexByTokenCount(string text, int maxTokens, out int index)
    {
        if (maxTokens <= 0)
        {
            index = 0;
            return false;
        }

        index = _tokenizer.GetIndexByTokenCount(
            text,
            maxTokens,
            out var processedText,
            out _,
            considerPreTokenization: true,
            considerNormalization: true);
        return string.Equals(processedText, text, StringComparison.Ordinal)
            && index is >= 0
            && index <= text.Length;
    }

    /// <summary>
    /// Finds the character index from which the suffix of <paramref name="text"/> contains at most
    /// <paramref name="maxTokens"/> tokens, measuring from the end. Returns <c>false</c> on
    /// renormalization or out-of-range results.
    /// </summary>
    public bool TryGetIndexByTokenCountFromEnd(string text, int maxTokens, out int index)
    {
        if (maxTokens <= 0)
        {
            index = text.Length;
            return false;
        }

        index = _tokenizer.GetIndexByTokenCountFromEnd(
            text,
            maxTokens,
            out var processedText,
            out _,
            considerPreTokenization: true,
            considerNormalization: true);
        return string.Equals(processedText, text, StringComparison.Ordinal)
            && index is >= 0
            && index <= text.Length;
    }

    /// <summary>
    /// Creates a tiktoken counter for the model, falling back to a <see cref="HeuristicTokenCounter"/>
    /// if the tokenizer cannot be loaded on the current platform.
    /// </summary>
    public static ITokenCounter CreateDefault(string model = "gpt-4o")
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model name must not be empty.", nameof(model));
        }

        try
        {
            return new TiktokenTokenCounter(model);
        }
        catch (Exception) when (
            OperatingSystem.IsWindows() ||
            OperatingSystem.IsLinux() ||
            OperatingSystem.IsMacOS())
        {
            return new HeuristicTokenCounter();
        }
    }

    internal static int CachedTokenizerCount => Tokenizers.Count;

    internal static Tokenizer GetOrCreateTokenizer(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model name must not be empty.", nameof(model));
        }

        var lazy = Tokenizers.GetOrAdd(
            model,
            static key => new Lazy<Tokenizer>(
                () => TiktokenTokenizer.CreateForModel(key),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return lazy.Value;
        }
        catch
        {
            Tokenizers.TryRemove(new KeyValuePair<string, Lazy<Tokenizer>>(model, lazy));
            throw;
        }
    }
}
