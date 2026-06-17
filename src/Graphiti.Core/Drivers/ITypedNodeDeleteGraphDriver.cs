namespace Graphiti.Core.Drivers;

internal interface ITypedNodeDeleteGraphDriver
{
    Task DeleteNodeAsync<TNode>(string uuid, CancellationToken cancellationToken = default)
        where TNode : Node;

    Task DeleteNodesByGroupIdAsync<TNode>(
        string groupId,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
        where TNode : Node;

    Task DeleteNodesByUuidsAsync<TNode>(
        IEnumerable<string> uuids,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
        where TNode : Node;
}
