using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graphiti.Core.Internal.Services;

internal sealed class AttributeExtractionService(
    ILlmClient llmClient,
    Func<int> getMaxDegreeOfParallelism)
{
    public async Task ExtractAttributesFromEdgesAsync(
        List<EntityEdge> edges,
        List<EntityNode> nodes,
        EpisodicNode episode,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap,
        CancellationToken cancellationToken)
    {
        using var activity = GraphitiTelemetry.StartActivity("Extraction.EdgeAttributes");
        activity?.SetTag("graphiti.group_id", episode.GroupId);
        activity?.SetTag("graphiti.input.edges", edges.Count);
        activity?.SetTag("graphiti.input.nodes", nodes.Count);
        activity?.SetTag("graphiti.extraction.edge_types.count", edgeTypes?.Count ?? 0);
        activity?.SetTag("graphiti.extraction.edge_type_map.count", edgeTypeMap?.Count ?? 0);

        try
        {
            if (edges.Count == 0 || edgeTypes is null || edgeTypes.Count == 0)
            {
                activity?.SetTag("graphiti.extraction.skipped", true);
                activity?.SetTag("graphiti.extraction.targets", 0);
                GraphitiTelemetry.SetOk(activity);
                return;
            }

            var nodesByUuid = nodes
                .GroupBy(node => node.Uuid, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var extractionTargets = edges
                .Select(edge => (Edge: edge, EdgeType: EntityTypeResolver.FindEdgeTypeDefinition(edge, nodesByUuid, edgeTypes, edgeTypeMap)))
                .Where(target => target.EdgeType is not null && target.EdgeType.Attributes.Count > 0)
                .Select(target => new EdgeAttributeExtractionTarget(
                    target.Edge,
                    target.EdgeType!,
                    AttributeSchema: null!))
                .ToList();
            activity?.SetTag("graphiti.extraction.targets", extractionTargets.Count);
            activity?.SetTag("graphiti.extraction.skipped", extractionTargets.Count == 0);
            var schemas = ExtractionContextBuilder.CreateAttributeResponseSchemas(
                extractionTargets.Select(target => target.EdgeType),
                "EdgeAttributeResponse");
            for (var i = 0; i < extractionTargets.Count; i++)
            {
                var target = extractionTargets[i];
                extractionTargets[i] = target with { AttributeSchema = schemas[target.EdgeType] };
            }

            await ThrottledWork.ForEachAsync(
                extractionTargets,
                async (target, token) =>
                {
                    var response = await llmClient.GenerateResponseAsync(
                        new[]
                        {
                            new Message("system", "Extract structured attributes for the fact."),
                            new Message("user", BuildEdgeAttributeExtractionContext(
                                target.Edge,
                                target.EdgeType,
                                episode,
                                nodesByUuid).ToJsonString(GraphitiJsonSerializer.Options))
                        },
                        responseSchema: target.AttributeSchema,
                        modelSize: ModelSize.Small,
                        groupId: target.Edge.GroupId,
                        promptName: "extract_edges.extract_attributes",
                        attributeExtraction: true,
                        cancellationToken: token).ConfigureAwait(false);

                    target.Edge.Attributes = AttributeMerger.ReplaceExtractedAttributes(
                        target.Edge.Attributes,
                        target.EdgeType,
                        response);
                },
                getMaxDegreeOfParallelism(),
                cancellationToken).ConfigureAwait(false);
            GraphitiTelemetry.SetOk(activity);
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    public async Task ExtractAttributesFromNodesAsync(
        List<EntityNode> nodes,
        EpisodicNode episode,
        IReadOnlyList<EpisodicNode> previousEpisodes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes,
        List<EntityEdge> edges,
        CancellationToken cancellationToken)
    {
        using var activity = GraphitiTelemetry.StartActivity("Extraction.NodeAttributes");
        activity?.SetTag("graphiti.group_id", episode.GroupId);
        activity?.SetTag("graphiti.input.nodes", nodes.Count);
        activity?.SetTag("graphiti.input.edges", edges.Count);
        activity?.SetTag("graphiti.previous_episodes.count", previousEpisodes.Count);
        activity?.SetTag("graphiti.extraction.entity_types.count", entityTypes?.Count ?? 0);

        try
        {
            if (nodes.Count == 0 || entityTypes is null || entityTypes.Count == 0)
            {
                activity?.SetTag("graphiti.extraction.skipped", true);
                activity?.SetTag("graphiti.extraction.targets", 0);
                GraphitiTelemetry.SetOk(activity);
                return;
            }

            var edgesByNode = EntityTypeResolver.BuildEdgesByNode(edges);
            var extractionTargets = nodes
                .Select(node => (Node: node, EntityType: EntityTypeResolver.FindEntityTypeDefinition(node, entityTypes)))
                .Where(target => target.EntityType is not null && target.EntityType.Attributes.Count > 0)
                .Select(target => new NodeAttributeExtractionTarget(
                    target.Node,
                    target.EntityType!,
                    AttributeSchema: null!))
                .ToList();
            activity?.SetTag("graphiti.extraction.targets", extractionTargets.Count);
            activity?.SetTag("graphiti.extraction.skipped", extractionTargets.Count == 0);
            var schemas = ExtractionContextBuilder.CreateAttributeResponseSchemas(
                extractionTargets.Select(target => target.EntityType),
                "NodeAttributeResponse");
            for (var i = 0; i < extractionTargets.Count; i++)
            {
                var target = extractionTargets[i];
                extractionTargets[i] = target with { AttributeSchema = schemas[target.EntityType] };
            }

            await ThrottledWork.ForEachAsync(
                extractionTargets,
                async (target, token) =>
                {
                    var connectedEdges = edgesByNode.TryGetValue(target.Node.Uuid, out var edgesForNode)
                        ? (IReadOnlyList<EntityEdge>)edgesForNode
                        : Array.Empty<EntityEdge>();
                    var context = BuildAttributeExtractionContext(
                        target.Node,
                        target.EntityType,
                        episode,
                        previousEpisodes,
                        connectedEdges);
                    var response = await llmClient.GenerateResponseAsync(
                        new[]
                        {
                            new Message("system", "Extract structured attributes for the entity."),
                            new Message("user", context.ToJsonString(GraphitiJsonSerializer.Options))
                        },
                        responseSchema: target.AttributeSchema,
                        modelSize: ModelSize.Small,
                        groupId: target.Node.GroupId,
                        promptName: "extract_nodes.extract_attributes",
                        attributeExtraction: true,
                        cancellationToken: token).ConfigureAwait(false);

                    AttributeMerger.OverlayExtractedAttributes(target.Node, target.EntityType, response);
                },
                getMaxDegreeOfParallelism(),
                cancellationToken).ConfigureAwait(false);
            GraphitiTelemetry.SetOk(activity);
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private static JsonObject BuildEdgeAttributeExtractionContext(
        EntityEdge edge,
        EntityTypeDefinition edgeType,
        EpisodicNode episode,
        Dictionary<string, EntityNode> nodesByUuid)
    {
        nodesByUuid.TryGetValue(edge.SourceNodeUuid, out var sourceNode);
        nodesByUuid.TryGetValue(edge.TargetNodeUuid, out var targetNode);
        return new JsonObject
        {
            ["fact"] = edge.Fact,
            ["relation_type"] = edge.Name,
            ["reference_time"] = GraphitiHelpers.EnsureUtc(episode.ValidAt).ToString("O"),
            ["existing_attributes"] = JsonSerializer.SerializeToNode(edge.Attributes, GraphitiJsonSerializer.Options),
            ["source"] = NodeReference(sourceNode, edge.SourceNodeUuid),
            ["target"] = NodeReference(targetNode, edge.TargetNodeUuid),
            ["edge_type"] = new JsonObject
            {
                ["name"] = edgeType.Name,
                ["description"] = edgeType.Description,
                ["attributes"] = ExtractionContextBuilder.BuildAttributeSchema(edgeType)
            }
        };
    }

    private static JsonObject NodeReference(EntityNode? node, string uuid) =>
        new()
        {
            ["uuid"] = uuid,
            ["name"] = node?.Name ?? string.Empty,
            ["entity_types"] = new JsonArray((node?.Labels ?? new List<string> { "Entity" })
                .Select(label => JsonValue.Create(label))
                .ToArray())
        };

    private static JsonObject BuildAttributeExtractionContext(
        EntityNode node,
        EntityTypeDefinition entityType,
        EpisodicNode episode,
        IReadOnlyList<EpisodicNode> previousEpisodes,
        IReadOnlyList<EntityEdge> connectedEdges)
    {
        var previous = new JsonArray();
        foreach (var previousEpisode in previousEpisodes)
        {
            previous.Add(new JsonObject
            {
                ["uuid"] = previousEpisode.Uuid,
                ["content"] = previousEpisode.Content,
                ["valid_at"] = GraphitiHelpers.EnsureUtc(previousEpisode.ValidAt).ToString("O")
            });
        }

        var facts = new JsonArray();
        foreach (var edge in connectedEdges)
        {
            facts.Add(edge.Fact);
        }

        return new JsonObject
        {
            ["entity"] = new JsonObject
            {
                ["name"] = node.Name,
                ["entity_types"] = new JsonArray(node.Labels.Select(label => JsonValue.Create(label)).ToArray()),
                ["attributes"] = JsonSerializer.SerializeToNode(node.Attributes, GraphitiJsonSerializer.Options)
            },
            ["entity_type"] = new JsonObject
            {
                ["name"] = entityType.Name,
                ["description"] = entityType.Description,
                ["attributes"] = ExtractionContextBuilder.BuildAttributeSchema(entityType)
            },
            ["episode"] = new JsonObject
            {
                ["uuid"] = episode.Uuid,
                ["content"] = episode.Content,
                ["valid_at"] = GraphitiHelpers.EnsureUtc(episode.ValidAt).ToString("O")
            },
            ["previous_episodes"] = previous,
            ["connected_facts"] = facts
        };
    }

    private sealed record EdgeAttributeExtractionTarget(
        EntityEdge Edge,
        EntityTypeDefinition EdgeType,
        StructuredResponseSchema AttributeSchema);

    private sealed record NodeAttributeExtractionTarget(
        EntityNode Node,
        EntityTypeDefinition EntityType,
        StructuredResponseSchema AttributeSchema);
}
