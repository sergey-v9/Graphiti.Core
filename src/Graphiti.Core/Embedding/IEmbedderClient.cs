namespace Graphiti.Core.Embedding;

/// <summary>
/// Abstraction over an embedding model that turns text into fixed-length float vectors used for
/// semantic similarity search.
/// </summary>
public interface IEmbedderClient
{
    /// <summary>Dimensionality of the vectors produced by this embedder.</summary>
    int EmbeddingDimension { get; }

    /// <summary>Generates an embedding vector for a single text input.</summary>
    Task<IReadOnlyList<float>> CreateAsync(
        string input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a single embedding for a list of inputs. The default implementation embeds the
    /// first item; override for true multi-input semantics.
    /// </summary>
    async Task<IReadOnlyList<float>> CreateAsync(
        IReadOnlyList<string> input,
        CancellationToken cancellationToken = default)
    {
        if (input.Count == 0)
        {
            return Array.Empty<float>();
        }

        return await CreateAsync(input[0], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Generates one embedding per input. The default implementation embeds sequentially.</summary>
    async Task<IReadOnlyList<IReadOnlyList<float>>> CreateBatchAsync(
        IReadOnlyList<string> input,
        CancellationToken cancellationToken = default)
    {
        var results = new List<IReadOnlyList<float>>(input.Count);
        foreach (var item in input)
        {
            results.Add(await CreateAsync(item, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }
}
