namespace Graphiti.Core.Text;

/// <summary>
/// Splits oversized episode content into token-bounded chunks. Provides format-aware strategies for
/// JSON, free text, and chat/message transcripts, plus a covering-chunk generator for batching items.
/// </summary>
public interface IContentChunker
{
    /// <summary>Estimates the number of tokens in <paramref name="text"/>.</summary>
    int EstimateTokens(string? text);

    /// <summary>Decides whether content should be chunked given its size and density.</summary>
    bool ShouldChunk(
        string? content,
        EpisodeType episodeType,
        int? minTokens = null,
        double? densityThreshold = null);

    /// <summary>Chunks JSON content, preserving valid JSON in each chunk where possible.</summary>
    IReadOnlyList<string> ChunkJsonContent(
        string content,
        int? chunkSizeTokens = null,
        int? overlapTokens = null);

    /// <summary>Chunks free-text content along paragraph and sentence boundaries.</summary>
    IReadOnlyList<string> ChunkTextContent(
        string content,
        int? chunkSizeTokens = null,
        int? overlapTokens = null);

    /// <summary>Chunks chat/message content, keeping speaker turns intact where possible.</summary>
    IReadOnlyList<string> ChunkMessageContent(
        string content,
        int? chunkSizeTokens = null,
        int? overlapTokens = null);

    /// <summary>Generates chunks of size <paramref name="k"/> that together cover all item pairs.</summary>
    IReadOnlyList<CoveringChunk<T>> GenerateCoveringChunks<T>(IReadOnlyList<T> items, int k);
}
