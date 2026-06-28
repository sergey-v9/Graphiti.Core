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
        var episodicNodeList = MaterializeList(episodicNodes);
        var episodicEdgeList = MaterializeList(episodicEdges);
        var entityNodeList = MaterializeList(entityNodes);
        var entityEdgeList = MaterializeList(entityEdges);
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
            await EnsureEntityEmbeddingsBeforeWriteAsync(
                entityNodeList,
                entityEdgeList,
                cancellationToken).ConfigureAwait(false);
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

    private async Task EnsureEntityEmbeddingsBeforeWriteAsync(
        IReadOnlyList<EntityNode> entityNodes,
        IReadOnlyList<EntityEdge> entityEdges,
        CancellationToken cancellationToken)
    {
        await EnsureEntityNodeEmbeddingsBeforeWriteAsync(entityNodes, cancellationToken).ConfigureAwait(false);
        await EnsureEntityEdgeEmbeddingsBeforeWriteAsync(entityEdges, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureEntityNodeEmbeddingsBeforeWriteAsync(
        IReadOnlyList<EntityNode> entityNodes,
        CancellationToken cancellationToken)
    {
        var nodesMissingEmbeddings = new List<EntityNode>(entityNodes.Count);
        var inputs = new List<string>(entityNodes.Count);
        for (var i = 0; i < entityNodes.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = entityNodes[i];
            if (node.NameEmbedding is not null)
            {
                continue;
            }

            nodesMissingEmbeddings.Add(node);
            inputs.Add((node.Name ?? string.Empty).Replace('\n', ' '));
        }

        if (nodesMissingEmbeddings.Count == 0)
        {
            return;
        }

        var embeddings = EmbeddingVectorValidation.MaterializeBatch(
            await Embedder.CreateBatchAsync(inputs, cancellationToken).ConfigureAwait(false),
            nodesMissingEmbeddings.Count,
            Embedder.EmbeddingDimension,
            "entity node name embeddings",
            index => $"entity node '{nodesMissingEmbeddings[index].Name}' at index {index}");
        for (var i = 0; i < nodesMissingEmbeddings.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            nodesMissingEmbeddings[i].NameEmbedding = embeddings[i];
        }
    }

    private async Task EnsureEntityEdgeEmbeddingsBeforeWriteAsync(
        IReadOnlyList<EntityEdge> entityEdges,
        CancellationToken cancellationToken)
    {
        var edgesMissingEmbeddings = new List<EntityEdge>(entityEdges.Count);
        var inputs = new List<string>(entityEdges.Count);
        for (var i = 0; i < entityEdges.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var edge = entityEdges[i];
            if (edge.FactEmbedding is not null)
            {
                continue;
            }

            edgesMissingEmbeddings.Add(edge);
            inputs.Add((edge.Fact ?? string.Empty).Replace('\n', ' '));
        }

        if (edgesMissingEmbeddings.Count == 0)
        {
            return;
        }

        var embeddings = EmbeddingVectorValidation.MaterializeBatch(
            await Embedder.CreateBatchAsync(inputs, cancellationToken).ConfigureAwait(false),
            edgesMissingEmbeddings.Count,
            Embedder.EmbeddingDimension,
            "entity edge fact embeddings",
            index => $"entity edge '{edgesMissingEmbeddings[index].Uuid}' at index {index}");
        for (var i = 0; i < edgesMissingEmbeddings.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            edgesMissingEmbeddings[i].FactEmbedding = embeddings[i];
        }
    }

    private static List<T> MaterializeList<T>(IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source is IReadOnlyList<T> list)
        {
            return CopyList(list);
        }

        var capacity = source.TryGetNonEnumeratedCount(out var count) ? count : 0;
        var results = capacity == 0 ? new List<T>() : new List<T>(capacity);
        foreach (var item in source)
        {
            results.Add(item);
        }

        return results;
    }
}
