namespace Graphiti.Core.Internal.Services;

internal sealed class EpisodeGraphExtractor(
    ILlmClient llmClient,
    Func<DateTime> utcNow)
{
    public async Task<(List<EntityNode> Nodes, List<Graphiti.ExtractedEdge> Edges, Dictionary<string, IReadOnlyList<int>> Attribution)> ExtractEpisodeGraphAsync(
        EpisodicNode episode,
        IReadOnlyList<EpisodicNode> previousEpisodes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes,
        IReadOnlyList<string>? excludedEntityTypes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap,
        string? customExtractionInstructions,
        CancellationToken cancellationToken)
    {
        using var activity = GraphitiTelemetry.StartActivity("Extraction.EpisodeGraph");
        activity?.SetTag("graphiti.group_id", episode.GroupId);
        activity?.SetTag("graphiti.episode.source", episode.Source.ToString());
        activity?.SetTag("graphiti.episode_content.length", episode.Content.Length);
        activity?.SetTag("graphiti.previous_episodes.count", previousEpisodes.Count);
        activity?.SetTag("graphiti.extraction.entity_types.count", entityTypes?.Count ?? 0);
        activity?.SetTag("graphiti.extraction.excluded_entity_types.count", excludedEntityTypes?.Count ?? 0);
        activity?.SetTag("graphiti.extraction.edge_types.count", edgeTypes?.Count ?? 0);
        activity?.SetTag("graphiti.extraction.edge_type_map.count", edgeTypeMap?.Count ?? 0);
        activity?.SetTag("graphiti.extraction.custom_instructions", !string.IsNullOrWhiteSpace(customExtractionInstructions));

        try
        {
            var nodeExtractionContext = ExtractNodesPrompts.BuildContext(
                episode,
                previousEpisodes,
                entityTypes,
                customExtractionInstructions);
            var (nodes, attribution) = await ExtractEpisodeNodesAsync(
                episode,
                nodeExtractionContext,
                entityTypes,
                excludedEntityTypes,
                cancellationToken).ConfigureAwait(false);
            var edgeExtractionContext = ExtractEdgesPrompts.BuildContext(
                episode,
                previousEpisodes,
                nodes,
                edgeTypes,
                edgeTypeMap,
                customExtractionInstructions);
            var extractedEdges = await ExtractEpisodeEdgesAsync(
                episode,
                edgeExtractionContext,
                nodes,
                cancellationToken).ConfigureAwait(false);

            activity?.SetTag("graphiti.result.nodes", nodes.Count);
            activity?.SetTag("graphiti.result.edges", extractedEdges.Count);
            activity?.SetTag("graphiti.result.attributions", attribution.Count);
            GraphitiTelemetry.SetOk(activity);
            return (nodes, extractedEdges, attribution);
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private async Task<(List<EntityNode> Nodes, Dictionary<string, IReadOnlyList<int>> Attribution)> ExtractEpisodeNodesAsync(
        EpisodicNode episode,
        ExtractNodesPrompts.NodeExtractionContext extractionContext,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes,
        IReadOnlyList<string>? excludedEntityTypes,
        CancellationToken cancellationToken)
    {
        using var activity = GraphitiTelemetry.StartActivity("Extraction.Nodes");
        activity?.SetTag("graphiti.group_id", episode.GroupId);
        activity?.SetTag("graphiti.episode.source", episode.Source.ToString());
        activity?.SetTag("graphiti.extraction.entity_types.count", entityTypes?.Count ?? 0);
        activity?.SetTag("graphiti.extraction.excluded_entity_types.count", excludedEntityTypes?.Count ?? 0);

        try
        {
            var llmResponse = await llmClient.GenerateResponseAsync(
                ExtractNodesPrompts.Build(episode.Source, extractionContext),
                responseModel: typeof(Graphiti.EpisodeNodeExtractionResponse),
                groupId: episode.GroupId,
                promptName: NodeExtractionPromptName(episode.Source),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var extractedEntities = Graphiti.ExtractEntities(llmResponse, entityTypes);

            var excluded = BuildExcludedEntityTypeSet(excludedEntityTypes);
            var skippedExcluded = 0;
            var nodes = new List<EntityNode>();
            var attribution = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
            foreach (var extracted in extractedEntities)
            {
                if (excluded is not null && excluded.Contains(extracted.Type))
                {
                    skippedExcluded++;
                    continue;
                }

                var labels = new List<string> { "Entity" };
                if (!string.IsNullOrWhiteSpace(extracted.Type) && !string.Equals(extracted.Type, "Entity", StringComparison.OrdinalIgnoreCase))
                {
                    labels.Add(extracted.Type);
                }

                var node = new EntityNode
                {
                    Name = extracted.Name,
                    GroupId = episode.GroupId,
                    Labels = labels,
                    CreatedAt = utcNow()
                };
                nodes.Add(node);
                attribution[node.Uuid] = EpisodeAttribution.NormalizeIndices(
                    extracted.EpisodeIndices,
                    episodeCount: 1);
            }

            activity?.SetTag("graphiti.extraction.candidates", extractedEntities.Count);
            activity?.SetTag("graphiti.extraction.fallback", false);
            activity?.SetTag("graphiti.extraction.excluded", skippedExcluded);
            activity?.SetTag("graphiti.result.nodes", nodes.Count);
            GraphitiTelemetry.SetOk(activity);
            return (nodes, attribution);
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private static HashSet<string>? BuildExcludedEntityTypeSet(IReadOnlyList<string>? excludedEntityTypes)
    {
        if (excludedEntityTypes is null || excludedEntityTypes.Count == 0)
        {
            return null;
        }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < excludedEntityTypes.Count; i++)
        {
            excluded.Add(excludedEntityTypes[i]);
        }

        return excluded;
    }

    private async Task<List<Graphiti.ExtractedEdge>> ExtractEpisodeEdgesAsync(
        EpisodicNode episode,
        ExtractEdgesPrompts.EdgeExtractionContext extractionContext,
        List<EntityNode> nodes,
        CancellationToken cancellationToken)
    {
        using var activity = GraphitiTelemetry.StartActivity("Extraction.Edges");
        activity?.SetTag("graphiti.group_id", episode.GroupId);
        activity?.SetTag("graphiti.input.nodes", nodes.Count);

        try
        {
            var llmResponse = await llmClient.GenerateResponseAsync(
                ExtractEdgesPrompts.BuildEdge(extractionContext),
                responseModel: typeof(Graphiti.EpisodeEdgeExtractionResponse),
                maxTokens: 16_384,
                groupId: episode.GroupId,
                promptName: "extract_edges.edge",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var extractedEdges = Graphiti.ExtractEdges(llmResponse);

            activity?.SetTag("graphiti.extraction.fallback", false);
            activity?.SetTag("graphiti.result.edges", extractedEdges.Count);
            GraphitiTelemetry.SetOk(activity);
            return extractedEdges;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private static string NodeExtractionPromptName(EpisodeType source) =>
        source switch
        {
            EpisodeType.Message => "extract_nodes.extract_message",
            EpisodeType.Json => "extract_nodes.extract_json",
            EpisodeType.Text or EpisodeType.FactTriple => "extract_nodes.extract_text",
            _ => "extract_nodes.extract_text"
        };
}
