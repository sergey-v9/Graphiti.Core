namespace Graphiti.Core.Text;

/// <summary>A subset of items selected to cover item pairs, with their original indices.</summary>
public sealed record CoveringChunk<T>(IReadOnlyList<T> Items, IReadOnlyList<int> Indices);
