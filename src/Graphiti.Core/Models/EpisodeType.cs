namespace Graphiti.Core.Models;

/// <summary>
/// The kind of source content an episode carries. Determines how the content is interpreted during
/// extraction.
/// </summary>
public enum EpisodeType
{
    /// <summary>A conversational message, formatted as <c>"actor: content"</c>.</summary>
    Message,

    /// <summary>A JSON string containing structured data.</summary>
    Json,

    /// <summary>Plain unstructured text.</summary>
    Text,

    /// <summary>A pre-formed entity-relationship-entity fact triple.</summary>
    FactTriple
}

/// <summary>
/// Conversions between <see cref="EpisodeType"/> and the lowercase string wire values
/// (for example <c>"fact_triple"</c>), preserving cross-language compatibility.
/// </summary>
public static class EpisodeTypeExtensions
{
    /// <summary>Returns the wire string for the episode type.</summary>
    public static string ToWireValue(this EpisodeType source) =>
        source switch
        {
            EpisodeType.Message => "message",
            EpisodeType.Json => "json",
            EpisodeType.Text => "text",
            EpisodeType.FactTriple => "fact_triple",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };

    /// <summary>Parses a wire string into an <see cref="EpisodeType"/>, throwing if unrecognized.</summary>
    public static EpisodeType FromWireValue(string source) =>
        source is null
            ? throw new ArgumentNullException(nameof(source))
            : TryFromWireValue(source, out var episodeType)
                ? episodeType
                : throw new ArgumentException(
                    $"Unsupported episode source wire value '{source}'.",
                    nameof(source));

    /// <summary>Attempts to parse a wire string into an <see cref="EpisodeType"/>.</summary>
    public static bool TryFromWireValue(string? source, out EpisodeType episodeType)
    {
        switch (source)
        {
            case "message":
                episodeType = EpisodeType.Message;
                return true;
            case "json":
                episodeType = EpisodeType.Json;
                return true;
            case "text":
                episodeType = EpisodeType.Text;
                return true;
            case "fact_triple":
                episodeType = EpisodeType.FactTriple;
                return true;
            default:
                episodeType = default;
                return false;
        }
    }
}
