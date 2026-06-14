using System.Text.Json.Nodes;

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

    public async Task<(List<EntityNode> Nodes, List<Graphiti.ExtractedEdge> Edges, Dictionary<string, IReadOnlyList<int>> Attribution)> ExtractCombinedEpisodeGraphAsync(
        EpisodicNode episode,
        IReadOnlyList<EpisodicNode> previousEpisodes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes,
        IReadOnlyList<string>? excludedEntityTypes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap,
        string? customExtractionInstructions,
        CancellationToken cancellationToken)
    {
        using var activity = GraphitiTelemetry.StartActivity("Extraction.CombinedEpisodeGraph");
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
            var episodes = new[] { episode };
            var response = await llmClient.GenerateResponseAsync(
                ExtractNodesAndEdgesPrompts.BuildExtractMessage(
                    ExtractNodesAndEdgesPrompts.BuildContext(
                        episode,
                        previousEpisodes,
                        entityTypes,
                        edgeTypes,
                        edgeTypeMap,
                        customExtractionInstructions)),
                responseModel: typeof(Graphiti.CombinedExtractionResponse),
                groupId: episode.GroupId,
                promptName: "extract_nodes_and_edges.extract_message",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var nodes = BuildCombinedNodes(
                Graphiti.ExtractEntities(response, entityTypes),
                episode,
                excludedEntityTypes);
            nodes = CollapseExactDuplicateNodes(nodes);
            var edges = BuildCombinedEdges(response, episodes, nodes);
            await ExtractBatchTimestampsAsync(edges, cancellationToken).ConfigureAwait(false);
            nodes = DropOrphanNodes(nodes, edges);
            var attribution = BuildCombinedAttribution(nodes, edges, episodes.Length);

            activity?.SetTag("graphiti.result.nodes", nodes.Count);
            activity?.SetTag("graphiti.result.edges", edges.Count);
            activity?.SetTag("graphiti.result.attributions", attribution.Count);
            GraphitiTelemetry.SetOk(activity);
            return (nodes, edges, attribution);
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

    private List<EntityNode> BuildCombinedNodes(
        List<Graphiti.ExtractedEntity> extractedEntities,
        EpisodicNode episode,
        IReadOnlyList<string>? excludedEntityTypes)
    {
        var excluded = BuildExcludedEntityTypeSet(excludedEntityTypes);
        var nodes = new List<EntityNode>(extractedEntities.Count);
        for (var i = 0; i < extractedEntities.Count; i++)
        {
            var extracted = extractedEntities[i];
            if (excluded is not null && excluded.Contains(extracted.Type))
            {
                continue;
            }

            var labels = new List<string> { "Entity" };
            if (!string.IsNullOrWhiteSpace(extracted.Type) && !string.Equals(extracted.Type, "Entity", StringComparison.OrdinalIgnoreCase))
            {
                labels.Add(extracted.Type);
            }

            nodes.Add(new EntityNode
            {
                Name = extracted.Name,
                GroupId = episode.GroupId,
                Labels = labels,
                CreatedAt = utcNow()
            });
        }

        return nodes;
    }

    private static List<EntityNode> CollapseExactDuplicateNodes(List<EntityNode> nodes)
    {
        if (nodes.Count < 2)
        {
            return nodes;
        }

        var canonicalByName = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        var orderedNames = new List<string>();
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var normalized = GraphitiHelpers.NormalizeEntityKey(node.Name);
            if (!canonicalByName.TryGetValue(normalized, out var existing))
            {
                canonicalByName[normalized] = node;
                orderedNames.Add(normalized);
                continue;
            }

            if (IsMoreSpecific(node, existing))
            {
                canonicalByName[normalized] = node;
            }
        }

        var collapsed = new List<EntityNode>(orderedNames.Count);
        for (var i = 0; i < orderedNames.Count; i++)
        {
            collapsed.Add(canonicalByName[orderedNames[i]]);
        }

        return collapsed;
    }

    private static bool IsMoreSpecific(EntityNode candidate, EntityNode existing)
    {
        var candidateSpecific = SpecificLabelCount(candidate.Labels);
        var existingSpecific = SpecificLabelCount(existing.Labels);
        return candidateSpecific > existingSpecific
               || (candidateSpecific == existingSpecific
                   && candidate.Name.Trim().Length > existing.Name.Trim().Length);
    }

    private static int SpecificLabelCount(List<string> labels)
    {
        var count = 0;
        for (var i = 0; i < labels.Count; i++)
        {
            if (!string.Equals(labels[i], "Entity", StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static List<Graphiti.ExtractedEdge> BuildCombinedEdges(
        JsonObject response,
        EpisodicNode[] episodes,
        IReadOnlyList<EntityNode> nodes)
    {
        var nodesByNormalizedName = BuildNormalizedNodeMap(nodes);
        var rawEdges = Graphiti.ExtractEdges(response);
        var edges = new List<Graphiti.ExtractedEdge>(rawEdges.Count);
        for (var i = 0; i < rawEdges.Count; i++)
        {
            var raw = rawEdges[i];
            if (string.IsNullOrWhiteSpace(raw.Fact)
                || !nodesByNormalizedName.TryGetValue(GraphitiHelpers.NormalizeEntityKey(raw.SourceName), out var source)
                || !nodesByNormalizedName.TryGetValue(GraphitiHelpers.NormalizeEntityKey(raw.TargetName), out var target))
            {
                continue;
            }

            var episodeIndices = EpisodeAttribution.NormalizeIndices(raw.EpisodeIndices, episodes.Length);
            edges.Add(new Graphiti.ExtractedEdge(
                source.Name,
                target.Name,
                raw.RelationType,
                raw.Fact,
                validAt: null,
                invalidAt: null,
                episodeIndices,
                EpisodeAttribution.ReferenceTimeForFirstIndex(
                    raw.EpisodeIndices,
                    episodes,
                    episodes[0].ValidAt),
                allowSelfEdge: true));
        }

        return edges;
    }

    private async Task ExtractBatchTimestampsAsync(
        List<Graphiti.ExtractedEdge> edges,
        CancellationToken cancellationToken)
    {
        if (edges.Count == 0)
        {
            return;
        }

        try
        {
            var response = await llmClient.GenerateResponseAsync(
                ExtractEdgesPrompts.BuildExtractTimestampsBatch(edges),
                responseModel: typeof(Graphiti.BatchEdgeTimestampsResponse),
                modelSize: ModelSize.Small,
                promptName: "extract_edges.extract_timestamps_batch",
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var timestamps = ReadBatchTimestamps(response);
            var count = Math.Min(edges.Count, timestamps.Count);
            for (var i = 0; i < count; i++)
            {
                edges[i] = edges[i] with
                {
                    ValidAt = timestamps[i].ValidAt,
                    InvalidAt = timestamps[i].InvalidAt
                };
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
        }
    }

    private static List<(DateTime? ValidAt, DateTime? InvalidAt)> ReadBatchTimestamps(JsonObject response)
    {
        if (!response.TryGetPropertyValue("timestamps", out var node) || node is not JsonArray array)
        {
            return [];
        }

        var timestamps = new List<(DateTime? ValidAt, DateTime? InvalidAt)>(array.Count);
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject item)
            {
                continue;
            }

            timestamps.Add((
                ParseOptionalDate(ReadString(item, "valid_at")),
                ParseOptionalDate(ReadString(item, "invalid_at"))));
        }

        return timestamps;
    }

    private static List<EntityNode> DropOrphanNodes(
        List<EntityNode> nodes,
        List<Graphiti.ExtractedEdge> edges)
    {
        if (nodes.Count == 0 || edges.Count == 0)
        {
            return [];
        }

        var nodesByName = EntityTypeResolver.BuildNodeNameMap(nodes);
        var connectedNodeUuids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < edges.Count; i++)
        {
            if (nodesByName.TryGetValue(edges[i].SourceName, out var source))
            {
                connectedNodeUuids.Add(source.Uuid);
            }

            if (nodesByName.TryGetValue(edges[i].TargetName, out var target))
            {
                connectedNodeUuids.Add(target.Uuid);
            }
        }

        var connected = new List<EntityNode>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            if (connectedNodeUuids.Contains(nodes[i].Uuid))
            {
                connected.Add(nodes[i]);
            }
        }

        return connected;
    }

    private static Dictionary<string, IReadOnlyList<int>> BuildCombinedAttribution(
        IReadOnlyList<EntityNode> nodes,
        List<Graphiti.ExtractedEdge> edges,
        int episodeCount)
    {
        var nodesByName = EntityTypeResolver.BuildNodeNameMap(nodes);
        var merged = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            MergeAttribution(edge.SourceName, edge.EpisodeIndices);
            if (!string.Equals(edge.SourceName, edge.TargetName, StringComparison.OrdinalIgnoreCase))
            {
                MergeAttribution(edge.TargetName, edge.EpisodeIndices);
            }
        }

        var result = new Dictionary<string, IReadOnlyList<int>>(merged.Count, StringComparer.Ordinal);
        foreach (var pair in merged)
        {
            pair.Value.Sort();
            result[pair.Key] = DistinctSorted(pair.Value);
        }

        return result;

        void MergeAttribution(string nodeName, IReadOnlyList<int> episodeIndices)
        {
            if (!nodesByName.TryGetValue(nodeName, out var node))
            {
                return;
            }

            if (!merged.TryGetValue(node.Uuid, out var indices))
            {
                indices = new List<int>(episodeIndices.Count);
                merged[node.Uuid] = indices;
            }

            var normalized = EpisodeAttribution.NormalizeIndices(episodeIndices, episodeCount);
            for (var index = 0; index < normalized.Count; index++)
            {
                indices.Add(normalized[index]);
            }
        }
    }

    private static List<int> DistinctSorted(List<int> sorted)
    {
        if (sorted.Count < 2)
        {
            return sorted;
        }

        var write = 1;
        for (var read = 1; read < sorted.Count; read++)
        {
            if (sorted[read] == sorted[write - 1])
            {
                continue;
            }

            sorted[write++] = sorted[read];
        }

        if (write < sorted.Count)
        {
            sorted.RemoveRange(write, sorted.Count - write);
        }

        return sorted;
    }

    private static Dictionary<string, EntityNode> BuildNormalizedNodeMap(IReadOnlyList<EntityNode> nodes)
    {
        var nodesByName = new Dictionary<string, EntityNode>(nodes.Count, StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            nodesByName.TryAdd(GraphitiHelpers.NormalizeEntityKey(nodes[i].Name), nodes[i]);
        }

        return nodesByName;
    }

    private static string? ReadString(JsonObject item, string key)
    {
        if (!item.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        return node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static DateTime? ParseOptionalDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return GraphitiHelpers.TryParseDbDate(value, out var parsed) ? parsed : null;
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
