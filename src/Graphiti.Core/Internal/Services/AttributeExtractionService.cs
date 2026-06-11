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

            var nodesByUuid = BuildNodesByUuid(nodes);
            var extractionTargets = BuildEdgeExtractionTargets(edges, nodesByUuid, edgeTypes, edgeTypeMap);
            activity?.SetTag("graphiti.extraction.targets", extractionTargets.Count);
            activity?.SetTag("graphiti.extraction.skipped", extractionTargets.Count == 0);
            ApplyEdgeAttributeSchemas(extractionTargets);

            await ThrottledWork.ForEachAsync(
                extractionTargets,
                async (target, token) =>
                {
                    var response = await llmClient.GenerateResponseAsync(
                        ExtractEdgesPrompts.BuildExtractAttributes(
                            target.Edge.Fact,
                            episode.ValidAt,
                            target.Edge.Attributes),
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

            var extractionTargets = BuildNodeExtractionTargets(nodes, entityTypes);
            activity?.SetTag("graphiti.extraction.targets", extractionTargets.Count);
            activity?.SetTag("graphiti.extraction.skipped", extractionTargets.Count == 0);
            ApplyNodeAttributeSchemas(extractionTargets);

            await ThrottledWork.ForEachAsync(
                extractionTargets,
                async (target, token) =>
                {
                    var context = ExtractNodesPrompts.BuildExtractAttributesContext(
                        target.Node,
                        episode,
                        previousEpisodes);
                    var response = await llmClient.GenerateResponseAsync(
                        ExtractNodesPrompts.BuildExtractAttributes(context),
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

    private static Dictionary<string, EntityNode> BuildNodesByUuid(List<EntityNode> nodes)
    {
        var nodesByUuid = new Dictionary<string, EntityNode>(nodes.Count, StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            nodesByUuid.TryAdd(nodes[i].Uuid, nodes[i]);
        }

        return nodesByUuid;
    }

    private static List<EdgeAttributeExtractionTarget> BuildEdgeExtractionTargets(
        List<EntityEdge> edges,
        Dictionary<string, EntityNode> nodesByUuid,
        IReadOnlyDictionary<string, EntityTypeDefinition> edgeTypes,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap)
    {
        var extractionTargets = new List<EdgeAttributeExtractionTarget>(edges.Count);
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            var edgeType = EntityTypeResolver.FindEdgeTypeDefinition(
                edge,
                nodesByUuid,
                edgeTypes,
                edgeTypeMap);
            if (edgeType is not null && edgeType.Attributes.Count > 0)
            {
                extractionTargets.Add(new EdgeAttributeExtractionTarget(
                    edge,
                    edgeType,
                    AttributeSchema: null!));
            }
        }

        return extractionTargets;
    }

    private static List<NodeAttributeExtractionTarget> BuildNodeExtractionTargets(
        List<EntityNode> nodes,
        IReadOnlyDictionary<string, EntityTypeDefinition> entityTypes)
    {
        var extractionTargets = new List<NodeAttributeExtractionTarget>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var entityType = EntityTypeResolver.FindEntityTypeDefinition(node, entityTypes);
            if (entityType is not null && entityType.Attributes.Count > 0)
            {
                extractionTargets.Add(new NodeAttributeExtractionTarget(
                    node,
                    entityType,
                    AttributeSchema: null!));
            }
        }

        return extractionTargets;
    }

    private static void ApplyEdgeAttributeSchemas(List<EdgeAttributeExtractionTarget> targets)
    {
        var schemas = new Dictionary<EntityTypeDefinition, StructuredResponseSchema>();
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (!schemas.TryGetValue(target.EdgeType, out var schema))
            {
                schema = ExtractionContextBuilder.BuildAttributeResponseSchema(
                    target.EdgeType,
                    "EdgeAttributeResponse");
                schemas[target.EdgeType] = schema;
            }

            targets[i] = target with { AttributeSchema = schema };
        }
    }

    private static void ApplyNodeAttributeSchemas(List<NodeAttributeExtractionTarget> targets)
    {
        var schemas = new Dictionary<EntityTypeDefinition, StructuredResponseSchema>();
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (!schemas.TryGetValue(target.EntityType, out var schema))
            {
                schema = ExtractionContextBuilder.BuildAttributeResponseSchema(
                    target.EntityType,
                    "NodeAttributeResponse");
                schemas[target.EntityType] = schema;
            }

            targets[i] = target with { AttributeSchema = schema };
        }
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
