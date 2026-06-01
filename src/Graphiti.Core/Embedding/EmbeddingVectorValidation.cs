namespace Graphiti.Core.Embedding;

internal static class EmbeddingVectorValidation
{
    public static List<float>? CopyNullableVector(List<float>? embedding)
    {
        if (embedding is null)
        {
            return null;
        }

        var copy = new List<float>(embedding.Count);
        for (var i = 0; i < embedding.Count; i++)
        {
            copy.Add(embedding[i]);
        }

        return copy;
    }

    public static List<float> MaterializeSingle(
        IReadOnlyList<float>? embedding,
        int expectedDimension,
        string itemDescription)
    {
        ValidateExpectedDimension(expectedDimension);
        return MaterializeVector(embedding, expectedDimension, itemDescription);
    }

    public static List<float> MaterializeSingle(
        ReadOnlyMemory<float> embedding,
        int expectedDimension,
        string itemDescription)
    {
        ValidateExpectedDimension(expectedDimension);
        return MaterializeVector(embedding.Span, expectedDimension, itemDescription);
    }

    public static List<List<float>> MaterializeBatch(
        IReadOnlyList<IReadOnlyList<float>>? embeddings,
        int expectedCount,
        int expectedDimension,
        string batchDescription,
        Func<int, string> itemDescription)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedCount);
        ArgumentNullException.ThrowIfNull(itemDescription);
        ValidateExpectedDimension(expectedDimension);

        if (embeddings is null)
        {
            throw new InvalidOperationException(
                $"Embedding provider returned null embeddings for {batchDescription}; expected {expectedCount}.");
        }

        if (embeddings.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Embedding provider returned {embeddings.Count} embeddings for {batchDescription}; expected {expectedCount}.");
        }

        var materialized = new List<List<float>>(expectedCount);
        for (var i = 0; i < embeddings.Count; i++)
        {
            materialized.Add(MaterializeVector(embeddings[i], expectedDimension, itemDescription(i)));
        }

        return materialized;
    }

    private static void ValidateExpectedDimension(int expectedDimension)
    {
        if (expectedDimension <= 0)
        {
            throw new InvalidOperationException(
                $"Embedder reported invalid EmbeddingDimension {expectedDimension}; expected a positive value.");
        }
    }

    private static List<float> MaterializeVector(
        IReadOnlyList<float>? embedding,
        int expectedDimension,
        string itemDescription)
    {
        if (embedding is null)
        {
            throw new InvalidOperationException(
                $"Embedding provider returned null vector for {itemDescription}; expected dimension {expectedDimension}.");
        }

        if (embedding.Count != expectedDimension)
        {
            throw new InvalidOperationException(
                $"Embedding provider returned dimension {embedding.Count} for {itemDescription}; expected {expectedDimension}.");
        }

        var materialized = new List<float>(embedding.Count);
        for (var i = 0; i < embedding.Count; i++)
        {
            var value = embedding[i];
            if (!float.IsFinite(value))
            {
                throw new InvalidOperationException(
                    $"Embedding provider returned non-finite value at dimension {i} for {itemDescription}.");
            }

            materialized.Add(value);
        }

        return materialized;
    }

    private static List<float> MaterializeVector(
        ReadOnlySpan<float> embedding,
        int expectedDimension,
        string itemDescription)
    {
        if (embedding.Length != expectedDimension)
        {
            throw new InvalidOperationException(
                $"Embedding provider returned dimension {embedding.Length} for {itemDescription}; expected {expectedDimension}.");
        }

        var materialized = new List<float>(embedding.Length);
        for (var i = 0; i < embedding.Length; i++)
        {
            var value = embedding[i];
            if (!float.IsFinite(value))
            {
                throw new InvalidOperationException(
                    $"Embedding provider returned non-finite value at dimension {i} for {itemDescription}.");
            }

            materialized.Add(value);
        }

        return materialized;
    }
}
