namespace Graphiti.Core.Models.Results;

/// <summary>
/// Input descriptor for a single episode supplied to bulk ingestion, before it is persisted as an
/// <see cref="EpisodicNode"/>.
/// </summary>
public sealed class RawEpisode
{
    /// <summary>Display name for the episode.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional explicit UUID; a new one is generated when omitted.</summary>
    public string? Uuid { get; set; }

    /// <summary>Raw episode content.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Free-text description of where the episode came from.</summary>
    public string SourceDescription { get; set; } = string.Empty;

    /// <summary>The kind of source content this episode represents.</summary>
    public EpisodeType Source { get; set; } = EpisodeType.Message;

    /// <summary>Event time at which the episode's content became true.</summary>
    public DateTime ReferenceTime { get; set; } = GraphitiHelpers.DefaultTimestamp;
}
