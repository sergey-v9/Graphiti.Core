namespace Graphiti.Core.Search;

/// <summary>A single retrieval result: the matched item and its relevance score.</summary>
public readonly record struct SearchHit<T>(T Item, float Score);
