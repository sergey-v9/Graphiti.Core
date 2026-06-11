namespace Graphiti.Core.Drivers;

internal interface IEmbeddingLoadGraphDriver
{
    Task<IReadOnlyDictionary<string, List<float>?>> LoadEntityNodeEmbeddingsByUuidAsync(
        IReadOnlyList<string> nodeUuids,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, List<float>?>> LoadEntityEdgeEmbeddingsByUuidAsync(
        IReadOnlyList<string> edgeUuids,
        CancellationToken cancellationToken = default);
}
