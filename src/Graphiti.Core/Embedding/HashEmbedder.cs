using System.Buffers;
using System.IO.Hashing;
using System.Text;

namespace Graphiti.Core.Embedding;

/// <summary>
/// A deterministic embedder that hashes tokens into a normalized bag-of-words vector. Requires no
/// external service, producing stable vectors for tests and offline/local use.
/// </summary>
public sealed class HashEmbedder : EmbedderClient
{
    /// <summary>Creates a hash embedder producing vectors of the given dimension.</summary>
    public HashEmbedder(int embeddingDimension = 1024)
        : base(new EmbedderConfig(embeddingDimension))
    {
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<float>> CreateAsync(string input, CancellationToken cancellationToken = default)
    {
        using var activity = GraphitiTelemetry.StartActivity("Embedder.Create");
        SetEmbeddingActivityTags(activity, EmbeddingDimension, inputCount: 1, modelId: "hash");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = new float[EmbeddingDimension];
            var source = (input ?? string.Empty).AsSpan();
            var position = 0;
            while (TryReadToken(source, ref position, out var token))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var index = (int)(HashToken(token) % (ulong)EmbeddingDimension);
                vector[index] += 1;
            }

            GraphitiHelpers.NormalizeL2InPlace(vector);
            activity?.SetTag("graphiti.embedding.output_count", 1);
            GraphitiTelemetry.SetOk(activity);
            return Task.FromResult<IReadOnlyList<float>>(vector);
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private static bool TryReadToken(
        ReadOnlySpan<char> source,
        ref int position,
        out ReadOnlySpan<char> token)
    {
        while (position < source.Length && IsTokenSeparator(source[position]))
        {
            position++;
        }

        var start = position;
        while (position < source.Length && !IsTokenSeparator(source[position]))
        {
            position++;
        }

        token = source[start..position];
        return token.Length > 0;
    }

    private static bool IsTokenSeparator(char character) =>
        char.IsWhiteSpace(character);

    private static ulong HashToken(ReadOnlySpan<char> token)
    {
        char[]? rentedChars = null;
        byte[]? rentedBytes = null;
        Span<char> normalizedChars = token.Length <= 256
            ? stackalloc char[token.Length]
            : rentedChars = ArrayPool<char>.Shared.Rent(token.Length);

        try
        {
            token.ToUpperInvariant(normalizedChars);
            normalizedChars = normalizedChars[..token.Length];

            var maxByteCount = Encoding.UTF8.GetMaxByteCount(normalizedChars.Length);
            Span<byte> bytes = maxByteCount <= 1024
                ? stackalloc byte[maxByteCount]
                : rentedBytes = ArrayPool<byte>.Shared.Rent(maxByteCount);
            var byteCount = Encoding.UTF8.GetBytes(normalizedChars, bytes);
            return XxHash64.HashToUInt64(bytes[..byteCount]);
        }
        finally
        {
            if (rentedChars is not null)
            {
                ArrayPool<char>.Shared.Return(rentedChars);
            }

            if (rentedBytes is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedBytes);
            }
        }
    }
}
