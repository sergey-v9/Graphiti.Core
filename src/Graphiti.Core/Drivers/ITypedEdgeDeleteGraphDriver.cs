namespace Graphiti.Core.Drivers;

internal interface ITypedEdgeDeleteGraphDriver
{
    Task DeleteEdgeAsync<TEdge>(string uuid, CancellationToken cancellationToken = default)
        where TEdge : Edge;

    Task DeleteEdgesByUuidsAsync<TEdge>(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default)
        where TEdge : Edge;
}
