namespace Graphiti.Core.Internal.Services;

internal sealed class AttributeExtractionService(
    ILlmClient llmClient,
    Func<int> getMaxDegreeOfParallelism)
{
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

    private sealed record NodeAttributeExtractionTarget(
        EntityNode Node,
        EntityTypeDefinition EntityType,
        StructuredResponseSchema AttributeSchema);
}
