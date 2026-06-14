namespace Graphiti.Core.Models.Edges;

/// <summary>
/// A fact relating two entities (the <c>RELATES_TO</c> relationship). Stores the natural-language
/// <see cref="Fact"/> and its embedding, the originating <see cref="Episodes"/>, and the
/// bi-temporal validity window (<see cref="ValidAt"/>/<see cref="InvalidAt"/>) plus the
/// transaction-time <see cref="ExpiredAt"/> used for temporal invalidation.
/// </summary>
public sealed class EntityEdge : Edge
{
    /// <summary>Relationship name/type of the fact (for example, <c>LOVES</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Natural-language statement of the fact.</summary>
    public string Fact { get; set; } = string.Empty;

    /// <summary>Embedding vector of <see cref="Fact"/>, or <c>null</c> if not yet generated.</summary>
    public List<float>? FactEmbedding { get; set; }

    /// <summary>UUIDs of the episodes that produced or reinforced this fact.</summary>
    public List<string> Episodes { get; set; } = new();

    /// <summary>Transaction time at which the fact was superseded/expired, if ever.</summary>
    public DateTime? ExpiredAt { get; set; }

    /// <summary>Event time from which the fact is considered true.</summary>
    public DateTime? ValidAt { get; set; }

    /// <summary>Event time at which the fact stopped being true, if known.</summary>
    public DateTime? InvalidAt { get; set; }

    /// <summary>Reference time used when resolving the fact's temporal window.</summary>
    public DateTime? ReferenceTime { get; set; }

    /// <summary>Custom, ontology-defined attributes for the fact.</summary>
    public Dictionary<string, object?> Attributes { get; set; } = new();

    /// <summary>
    /// Generates and stores the <see cref="FactEmbedding"/> from <see cref="Fact"/> using the
    /// supplied embedder, validating the returned vector dimension.
    /// </summary>
    public async Task<IReadOnlyList<float>> GenerateEmbeddingAsync(
        IEmbedderClient embedder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        var text = (Fact ?? string.Empty).Replace('\n', ' ');
        FactEmbedding = EmbeddingVectorValidation.MaterializeSingle(
            await embedder.CreateAsync(text, cancellationToken).ConfigureAwait(false),
            embedder.EmbeddingDimension,
            $"entity edge '{Uuid}' fact embedding");
        return FactEmbedding;
    }

    /// <summary>
    /// Loads <see cref="FactEmbedding"/> from storage, using the driver's bulk embedding-load path
    /// when available and otherwise re-reading the stored edge.
    /// </summary>
    public async Task LoadFactEmbeddingAsync(IGraphDriver driver, CancellationToken cancellationToken = default)
    {
        if (driver is IEmbeddingLoadGraphDriver embeddingDriver)
        {
            var embeddings = await embeddingDriver
                .LoadEntityEdgeEmbeddingsByUuidAsync(new[] { Uuid }, cancellationToken)
                .ConfigureAwait(false);
            if (embeddings.TryGetValue(Uuid, out var embedding))
            {
                FactEmbedding = EmbeddingVectorValidation.CopyNullableVector(embedding);
                return;
            }
        }

        var stored = await GetByUuidAsync(driver, Uuid, cancellationToken).ConfigureAwait(false);
        FactEmbedding = EmbeddingVectorValidation.CopyNullableVector(stored.FactEmbedding);
    }

    /// <summary>Loads a single entity edge (fact) by UUID via the driver.</summary>
    public static Task<EntityEdge> GetByUuidAsync(
        IGraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgeByUuidAsync<EntityEdge>(uuid, cancellationToken);

    /// <summary>Loads the entity edges (facts) with the given UUIDs via the driver.</summary>
    public static Task<IReadOnlyList<EntityEdge>> GetByUuidsAsync(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgesByUuidsAsync<EntityEdge>(uuids, cancellationToken);

    /// <summary>
    /// Loads entity edges (facts) across the given group partitions, with optional UUID-cursor paging
    /// and optional inclusion of fact embeddings.
    /// </summary>
    public static Task<IReadOnlyList<EntityEdge>> GetByGroupIdsAsync(
        IGraphDriver driver,
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default) =>
        driver.GetEdgesByGroupIdsAsync<EntityEdge>(groupIds, limit, uuidCursor, withEmbeddings, cancellationToken);

    /// <summary>Loads the entity edges (facts) that directly connect two nodes.</summary>
    public static Task<IReadOnlyList<EntityEdge>> GetBetweenNodesAsync(
        IGraphDriver driver,
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default) =>
        driver.GetEntityEdgesBetweenNodesAsync(sourceNodeUuid, targetNodeUuid, cancellationToken);

    /// <summary>Loads all entity edges (facts) incident to the given node.</summary>
    public static Task<IReadOnlyList<EntityEdge>> GetByNodeUuidAsync(
        IGraphDriver driver,
        string nodeUuid,
        CancellationToken cancellationToken = default) =>
        driver.GetEntityEdgesByNodeUuidAsync(nodeUuid, cancellationToken);
}
