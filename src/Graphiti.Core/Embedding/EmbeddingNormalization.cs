using System.Numerics.Tensors;

namespace Graphiti.Core.Embedding;

/// <summary>
/// L2 (Euclidean) normalization helpers for embedding vectors. A zero-norm vector is left unchanged.
/// </summary>
internal static class EmbeddingNormalization
{
    /// <summary>Returns an L2-normalized copy of the vector; a zero norm is left unchanged.</summary>
    public static float[] NormalizeL2(IEnumerable<float> embedding)
    {
        var vector = SnapshotEmbedding(embedding);
        NormalizeL2InPlace(vector);
        return vector;
    }

    public static void NormalizeL2InPlace(Span<float> vector)
    {
        var norm = TensorPrimitives.Norm(vector);
        if (norm == 0)
        {
            return;
        }

        TensorPrimitives.Divide(vector, norm, vector);
    }

    private static float[] SnapshotEmbedding(IEnumerable<float> embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);

        if (embedding is ICollection<float> collection)
        {
            if (collection.Count == 0)
            {
                return Array.Empty<float>();
            }

            var snapshot = new float[collection.Count];
            collection.CopyTo(snapshot, 0);
            return snapshot;
        }

        if (embedding is IReadOnlyList<float> list)
        {
            return [.. list];
        }

        return [.. embedding];
    }
}
