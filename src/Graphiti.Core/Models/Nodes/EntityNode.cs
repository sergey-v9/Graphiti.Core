namespace Graphiti.Core.Models.Nodes;

/// <summary>
/// An entity extracted from one or more episodes (a person, product, concept, and so on).
/// Carries an evolving <see cref="Summary"/>, optional custom <see cref="Attributes"/>, and a
/// <see cref="NameEmbedding"/> used for semantic search.
/// </summary>
public sealed class EntityNode : Node
{
    /// <summary>Embedding vector of the entity name, or <c>null</c> if not yet generated.</summary>
    public List<float>? NameEmbedding { get; set; }

    /// <summary>Running natural-language summary of what is known about the entity.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Custom, ontology-defined attributes for the entity.</summary>
    public Dictionary<string, object?> Attributes { get; set; } = new();

    /// <summary>
    /// Generates and stores the <see cref="NameEmbedding"/> from the entity name using the
    /// supplied embedder, validating the returned vector dimension.
    /// </summary>
    public async Task<IReadOnlyList<float>> GenerateNameEmbeddingAsync(
        IEmbedderClient embedder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        var text = (Name ?? string.Empty).Replace('\n', ' ');
        NameEmbedding = EmbeddingVectorValidation.MaterializeSingle(
            await embedder.CreateAsync(text, cancellationToken).ConfigureAwait(false),
            embedder.EmbeddingDimension,
            $"entity node '{Name}' name embedding");
        return NameEmbedding;
    }

    /// <summary>Loads the persisted <see cref="NameEmbedding"/> for this entity from the graph.</summary>
    public async Task LoadNameEmbeddingAsync(IGraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver is IEmbeddingLoadGraphDriver embeddingDriver)
        {
            var embeddings = await embeddingDriver
                .LoadEntityNodeEmbeddingsByUuidAsync([Uuid], cancellationToken)
                .ConfigureAwait(false);
            if (embeddings.TryGetValue(Uuid, out var embedding))
            {
                NameEmbedding = EmbeddingVectorValidation.CopyNullableVector(embedding);
                return;
            }
        }

        var stored = await GetByUuidAsync(driver, Uuid, cancellationToken).ConfigureAwait(false);
        NameEmbedding = EmbeddingVectorValidation.CopyNullableVector(stored.NameEmbedding);
    }

    /// <summary>Retrieves a single entity node by UUID.</summary>
    public static Task<EntityNode> GetByUuidAsync(
        IGraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default) =>
        driver.GetNodeByUuidAsync<EntityNode>(uuid, cancellationToken);

    /// <summary>
    /// Retrieves the entity nodes with the given UUIDs. The optional <paramref name="groupId"/>
    /// parameter is accepted for signature compatibility but is not applied by the fallback query.
    /// </summary>
    public static Task<IReadOnlyList<EntityNode>> GetByUuidsAsync(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default) =>
        driver.GetNodesByUuidsAsync<EntityNode>(uuids, cancellationToken: cancellationToken);

    /// <summary>
    /// Retrieves entity nodes across the given group partitions, with optional UUID-cursor paging and
    /// optional inclusion of name embeddings.
    /// </summary>
    public static Task<IReadOnlyList<EntityNode>> GetByGroupIdsAsync(
        IGraphDriver driver,
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default) =>
        driver.GetNodesByGroupIdsAsync<EntityNode>(groupIds, limit, uuidCursor, withEmbeddings, cancellationToken);
}
