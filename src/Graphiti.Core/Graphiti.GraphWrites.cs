namespace Graphiti.Core;

public sealed partial class Graphiti
{
    private async Task SaveBulkWithTelemetryAsync(
        string phase,
        string groupId,
        IEnumerable<EpisodicNode> episodicNodes,
        IEnumerable<EpisodicEdge> episodicEdges,
        IEnumerable<EntityNode> entityNodes,
        IEnumerable<EntityEdge> entityEdges,
        CancellationToken cancellationToken)
    {
        var episodicNodeList = episodicNodes.ToList();
        var episodicEdgeList = episodicEdges.ToList();
        var entityNodeList = entityNodes.ToList();
        var entityEdgeList = entityEdges.ToList();
        var driver = Driver;

        using var activity = GraphitiTelemetry.StartActivity("GraphWrite.SaveBulk");
        activity?.SetTag("graphiti.group_id", groupId);
        activity?.SetTag("graphiti.graph.provider", driver.Provider.ToString());
        activity?.SetTag("graphiti.graph.database", driver.Database);
        activity?.SetTag("graphiti.write.phase", phase);
        activity?.SetTag("graphiti.write.episodic_nodes", episodicNodeList.Count);
        activity?.SetTag("graphiti.write.entity_nodes", entityNodeList.Count);
        activity?.SetTag("graphiti.write.total_nodes", episodicNodeList.Count + entityNodeList.Count);
        activity?.SetTag("graphiti.write.episodic_edges", episodicEdgeList.Count);
        activity?.SetTag("graphiti.write.entity_edges", entityEdgeList.Count);
        activity?.SetTag("graphiti.write.total_edges", episodicEdgeList.Count + entityEdgeList.Count);

        try
        {
            await driver.SaveBulkAsync(
                episodicNodeList,
                episodicEdgeList,
                entityNodeList,
                entityEdgeList,
                Embedder,
                cancellationToken).ConfigureAwait(false);
            GraphitiTelemetry.SetOk(activity);
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }
}
