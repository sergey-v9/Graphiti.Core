using Microsoft.Extensions.Options;

namespace Graphiti.Core.Text;

/// <summary>
/// Default <see cref="IContentChunker"/> implementation backed by an <see cref="ITokenCounter"/> and
/// configurable <see cref="ContentChunkingOptions"/>. Delegates to the static <see cref="ContentChunking"/>
/// algorithms while applying the instance's configured defaults.
/// </summary>
public sealed class DefaultContentChunker : IContentChunker
{
    private readonly ITokenCounter _tokenCounter;
    private readonly ContentChunkingOptions _options;

    /// <summary>Creates a chunker using the given token counter and optional options.</summary>
    public DefaultContentChunker(
        ITokenCounter tokenCounter,
        IOptions<ContentChunkingOptions>? options = null)
    {
        _tokenCounter = tokenCounter ?? throw new ArgumentNullException(nameof(tokenCounter));
        _options = options?.Value ?? new ContentChunkingOptions();
    }

    /// <inheritdoc />
    public int EstimateTokens(string? text)
    {
        return ContentChunking.EstimateTokens(text, _tokenCounter);
    }

    /// <inheritdoc />
    public bool ShouldChunk(
        string? content,
        EpisodeType episodeType,
        int? minTokens = null,
        double? densityThreshold = null)
    {
        return ContentChunking.ShouldChunk(
            content,
            episodeType,
            minTokens ?? _options.ChunkMinTokens,
            densityThreshold ?? _options.ChunkDensityThreshold,
            _tokenCounter);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ChunkJsonContent(
        string content,
        int? chunkSizeTokens = null,
        int? overlapTokens = null)
    {
        return ContentChunking.ChunkJsonContent(
            content,
            chunkSizeTokens ?? _options.ChunkTokenSize,
            overlapTokens ?? _options.ChunkOverlapTokens,
            _tokenCounter);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ChunkTextContent(
        string content,
        int? chunkSizeTokens = null,
        int? overlapTokens = null)
    {
        return ContentChunking.ChunkTextContent(
            content,
            chunkSizeTokens ?? _options.ChunkTokenSize,
            overlapTokens ?? _options.ChunkOverlapTokens,
            _tokenCounter);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ChunkMessageContent(
        string content,
        int? chunkSizeTokens = null,
        int? overlapTokens = null)
    {
        return ContentChunking.ChunkMessageContent(
            content,
            chunkSizeTokens ?? _options.ChunkTokenSize,
            overlapTokens ?? _options.ChunkOverlapTokens,
            _tokenCounter);
    }

    /// <inheritdoc />
    public IReadOnlyList<CoveringChunk<T>> GenerateCoveringChunks<T>(IReadOnlyList<T> items, int k) =>
        ContentChunking.GenerateCoveringChunks(items, k);
}
