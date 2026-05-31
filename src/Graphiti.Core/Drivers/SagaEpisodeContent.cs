namespace Graphiti.Core.Drivers;

/// <summary>An episode's content paired with its event-time validity, as returned for saga queries.</summary>
public sealed record SagaEpisodeContent(string Content, DateTime? ValidAt);
