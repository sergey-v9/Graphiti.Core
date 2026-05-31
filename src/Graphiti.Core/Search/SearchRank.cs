namespace Graphiti.Core.Search;

/// <summary>A reranking result: an item UUID and its score under a reranker.</summary>
public readonly record struct SearchRank(string Uuid, float Score);
