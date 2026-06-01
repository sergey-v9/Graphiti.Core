namespace Graphiti.Core.Models.Nodes;

/// <summary>
/// A community: a cluster of related entities produced by community detection, with an
/// aggregated <see cref="Summary"/> and a <see cref="NameEmbedding"/> for semantic search.
/// </summary>
public sealed class CommunityNode : Node
{
    /// <summary>Embedding vector of the community name, or <c>null</c> if not yet generated.</summary>
    public List<float>? NameEmbedding { get; set; }

    /// <summary>Aggregated summary describing the community's members.</summary>
    public string Summary { get; set; } = string.Empty;

    public async Task<IReadOnlyList<float>> GenerateNameEmbeddingAsync(
        IEmbedderClient embedder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(embedder);
        var text = (Name ?? string.Empty).Replace('\n', ' ');
        NameEmbedding = EmbeddingVectorValidation.MaterializeSingle(
            await embedder.CreateAsync(text, cancellationToken).ConfigureAwait(false),
            embedder.EmbeddingDimension,
            $"community node '{Name}' name embedding");
        return NameEmbedding;
    }

    public async Task LoadNameEmbeddingAsync(IGraphDriver driver, CancellationToken cancellationToken = default)
    {
        var stored = await GetByUuidAsync(driver, Uuid, cancellationToken).ConfigureAwait(false);
        NameEmbedding = EmbeddingVectorValidation.CopyNullableVector(stored.NameEmbedding);
    }

    public static Task<CommunityNode> GetByUuidAsync(
        IGraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default) =>
        driver.GetNodeByUuidAsync<CommunityNode>(uuid, cancellationToken);

    public static Task<IReadOnlyList<CommunityNode>> GetByUuidsAsync(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default) =>
        driver.GetNodesByUuidsAsync<CommunityNode>(uuids, cancellationToken: cancellationToken);

    public static Task<IReadOnlyList<CommunityNode>> GetByGroupIdsAsync(
        IGraphDriver driver,
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default) =>
        driver.GetNodesByGroupIdsAsync<CommunityNode>(groupIds, limit, uuidCursor, false, cancellationToken);
}
