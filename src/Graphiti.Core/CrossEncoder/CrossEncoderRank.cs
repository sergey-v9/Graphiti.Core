namespace Graphiti.Core.CrossEncoder;

/// <summary>A reranked passage carrying its original list <paramref name="Index"/> and relevance <paramref name="Score"/>.</summary>
public readonly record struct CrossEncoderRank(int Index, string Passage, float Score);
