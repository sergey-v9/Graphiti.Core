using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Graphiti.Core.Internal.Services;

internal sealed class NodeResolutionService(
    Func<IGraphDriver> driverAccessor,
    ILlmClient llmClient,
    IEmbedderClient embedder,
    ILogger logger)
{
    private const int NodeDedupCandidateLimit = 15;
    private const float NodeDedupCosineMinScore = 0.6f;

    private IGraphDriver Driver => driverAccessor();

    public async Task<EntityNodeResolution> ResolveExtractedNodesAsync(
        IReadOnlyList<EntityNode> extractedNodes,
        string groupId,
        EpisodicNode? episode,
        IReadOnlyList<EpisodicNode>? previousEpisodes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes,
        IReadOnlyList<EntityNode>? existingNodesOverride,
        CancellationToken cancellationToken)
    {
        using var activity = GraphitiTelemetry.StartActivity("Resolution.Nodes");
        activity?.SetTag("graphiti.group_id", groupId);
        activity?.SetTag("graphiti.input.nodes", extractedNodes.Count);
        activity?.SetTag("graphiti.previous_episodes.count", previousEpisodes?.Count ?? 0);
        activity?.SetTag("graphiti.extraction.entity_types.count", entityTypes?.Count ?? 0);
        activity?.SetTag("graphiti.existing_nodes.override", existingNodesOverride is not null);

        try
        {
            if (extractedNodes.Count == 0)
            {
                var emptyResolution = new EntityNodeResolution(
                    new List<EntityNode>(),
                    new Dictionary<string, EntityNode>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    Array.Empty<UuidMapPair>());
                activity?.SetTag("graphiti.existing.nodes", 0);
                activity?.SetTag("graphiti.result.nodes", 0);
                GraphitiTelemetry.SetOk(activity);
                return emptyResolution;
            }

            var candidateNodesByExtracted = await CollectCandidateNodesByExtractedAsync(
                extractedNodes,
                groupId,
                existingNodesOverride,
                cancellationToken).ConfigureAwait(false);
            activity?.SetTag("graphiti.existing.nodes", CountDistinctCandidates(candidateNodesByExtracted));

            var nodesByExtractedName = new Dictionary<string, EntityNode>(StringComparer.OrdinalIgnoreCase);
            var uuidMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var duplicatePairs = new List<UuidMapPair>();
            var unresolvedIndices = new List<int>();
            var deterministicNodes = 0;
            for (var i = 0; i < extractedNodes.Count; i++)
            {
                var extractedNode = extractedNodes[i];
                var candidates = candidateNodesByExtracted[i];
                if (candidates.Count == 0)
                {
                    continue;
                }

                if (TryResolveWithCandidates(
                    extractedNode,
                    candidates,
                    out var resolvedNode,
                    out var duplicatePair))
                {
                    deterministicNodes++;
                    nodesByExtractedName[extractedNode.Name] = resolvedNode;
                    uuidMap[extractedNode.Uuid] = resolvedNode.Uuid;
                    if (duplicatePair is { } pair)
                    {
                        duplicatePairs.Add(pair);
                    }

                    continue;
                }

                unresolvedIndices.Add(i);
            }

            activity?.SetTag("graphiti.resolution.deterministic_nodes", deterministicNodes);
            await ResolveUnresolvedNodesWithLlmAsync(
                episode,
                previousEpisodes,
                entityTypes,
                extractedNodes,
                candidateNodesByExtracted,
                unresolvedIndices,
                nodesByExtractedName,
                uuidMap,
                duplicatePairs,
                groupId,
                cancellationToken).ConfigureAwait(false);

            for (var i = 0; i < extractedNodes.Count; i++)
            {
                var extractedNode = extractedNodes[i];
                nodesByExtractedName.TryAdd(extractedNode.Name, extractedNode);
                uuidMap.TryAdd(extractedNode.Uuid, extractedNode.Uuid);
            }

            var resolution = new EntityNodeResolution(
                BuildResolvedNodeList(extractedNodes, nodesByExtractedName),
                nodesByExtractedName,
                uuidMap,
                duplicatePairs);
            activity?.SetTag("graphiti.result.nodes", resolution.Nodes.Count);
            GraphitiTelemetry.SetOk(activity);
            return resolution;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private async Task ResolveUnresolvedNodesWithLlmAsync(
        EpisodicNode? episode,
        IReadOnlyList<EpisodicNode>? previousEpisodes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes,
        IReadOnlyList<EntityNode> extractedNodes,
        List<List<EntityNode>> candidateNodesByExtracted,
        List<int> unresolvedIndices,
        Dictionary<string, EntityNode> nodesByExtractedName,
        Dictionary<string, string> uuidMap,
        List<UuidMapPair> duplicatePairs,
        string groupId,
        CancellationToken cancellationToken)
    {
        if (unresolvedIndices.Count == 0)
        {
            return;
        }

        var unresolvedNodes = new List<EntityNode>(unresolvedIndices.Count);
        var llmCandidateSeed = new List<EntityNode>();
        for (var i = 0; i < unresolvedIndices.Count; i++)
        {
            var index = unresolvedIndices[i];
            unresolvedNodes.Add(extractedNodes[index]);
            llmCandidateSeed.AddRange(candidateNodesByExtracted[index]);
        }

        var candidates = MergeCandidateNodes(llmCandidateSeed, existingNodesOverride: null);
        if (candidates.Count == 0)
        {
            return;
        }

        var response = await llmClient.GenerateResponseAsync(
            DedupeNodesPrompts.BuildNodes(DedupeNodesPrompts.BuildContext(
                unresolvedNodes,
                candidates,
                episode,
                previousEpisodes,
                entityTypes)),
            responseModel: typeof(Graphiti.NodeResolutionsResponse),
            modelSize: ModelSize.Small,
            groupId: groupId,
            promptName: "dedupe_nodes.nodes",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var processedIds = new HashSet<int>();
        foreach (var resolution in ReadNodeResolutions(response))
        {
            if (resolution.Id < 0
                || resolution.Id >= unresolvedNodes.Count
                || !processedIds.Add(resolution.Id))
            {
                continue;
            }

            var extractedNode = unresolvedNodes[resolution.Id];
            if (resolution.DuplicateCandidateId < 0
                || resolution.DuplicateCandidateId >= candidates.Count)
            {
                nodesByExtractedName[extractedNode.Name] = extractedNode;
                uuidMap[extractedNode.Uuid] = extractedNode.Uuid;
                continue;
            }

            var resolvedNode = MergeExtractedNode(candidates[resolution.DuplicateCandidateId], extractedNode);
            nodesByExtractedName[extractedNode.Name] = resolvedNode;
            uuidMap[extractedNode.Uuid] = resolvedNode.Uuid;
            if (!string.Equals(extractedNode.Uuid, resolvedNode.Uuid, StringComparison.Ordinal))
            {
                duplicatePairs.Add(new UuidMapPair(extractedNode.Uuid, resolvedNode.Uuid));
            }
        }

        foreach (var extractedNode in unresolvedNodes)
        {
            nodesByExtractedName.TryAdd(extractedNode.Name, extractedNode);
            uuidMap.TryAdd(extractedNode.Uuid, extractedNode.Uuid);
        }
    }

    private static bool TryResolveWithCandidates(
        EntityNode extractedNode,
        IReadOnlyList<EntityNode> candidates,
        out EntityNode resolvedNode,
        out UuidMapPair? duplicatePair)
    {
        var deterministic = EntityNodeDeduplicator.Resolve(
            new[] { extractedNode },
            candidates,
            MergeExtractedNode);
        if (!deterministic.NodesByExtractedName.TryGetValue(extractedNode.Name, out resolvedNode!)
            || ReferenceEquals(resolvedNode, extractedNode))
        {
            resolvedNode = extractedNode;
            duplicatePair = null;
            return false;
        }

        duplicatePair = string.Equals(extractedNode.Uuid, resolvedNode.Uuid, StringComparison.Ordinal)
            ? null
            : new UuidMapPair(extractedNode.Uuid, resolvedNode.Uuid);
        return true;
    }

    private async Task<List<List<EntityNode>>> CollectCandidateNodesByExtractedAsync(
        IReadOnlyList<EntityNode> extractedNodes,
        string groupId,
        IReadOnlyList<EntityNode>? existingNodesOverride,
        CancellationToken cancellationToken)
    {
        var semanticResults = new List<List<EntityNode>>(extractedNodes.Count);
        for (var i = 0; i < extractedNodes.Count; i++)
        {
            semanticResults.Add(new List<EntityNode>());
        }

        if (Driver is ISearchGraphDriver searchDriver)
        {
            try
            {
                var queryVectors = await embedder
                    .CreateBatchAsync(
                        BuildNodeDedupEmbeddingQueries(extractedNodes),
                        cancellationToken)
                    .ConfigureAwait(false);
                for (var i = 0; i < extractedNodes.Count; i++)
                {
                    var candidateGroupId = string.IsNullOrWhiteSpace(extractedNodes[i].GroupId)
                        ? groupId
                        : extractedNodes[i].GroupId;
                    var hits = await searchDriver.SearchEntityNodesByEmbeddingAsync(
                        queryVectors[i],
                        new SearchFilters(),
                        new[] { candidateGroupId },
                        NodeDedupCandidateLimit,
                        NodeDedupCosineMinScore,
                        cancellationToken).ConfigureAwait(false);

                    foreach (var hit in hits)
                    {
                        semanticResults[i].Add(hit.Item);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                GraphitiLog.NodeDedupCandidateSearchFailed(logger, exception, groupId);
                for (var i = 0; i < semanticResults.Count; i++)
                {
                    semanticResults[i].Clear();
                }
            }
        }

        var candidateNodesByExtracted = new List<List<EntityNode>>(extractedNodes.Count);
        for (var i = 0; i < semanticResults.Count; i++)
        {
            candidateNodesByExtracted.Add(MergeCandidateNodes(semanticResults[i], existingNodesOverride));
        }

        return candidateNodesByExtracted;
    }

    private static List<EntityNode> MergeCandidateNodes(
        IEnumerable<EntityNode> candidateNodes,
        IReadOnlyList<EntityNode>? existingNodesOverride)
    {
        var candidates = new List<EntityNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidateNodes)
        {
            if (seen.Add(candidate.Uuid))
            {
                candidates.Add(candidate);
            }
        }

        if (existingNodesOverride is not null)
        {
            for (var i = 0; i < existingNodesOverride.Count; i++)
            {
                var candidate = existingNodesOverride[i];
                if (seen.Add(candidate.Uuid))
                {
                    candidates.Add(candidate);
                }
            }
        }

        return candidates;
    }

    private static int CountDistinctCandidates(List<List<EntityNode>> candidateNodesByExtracted)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < candidateNodesByExtracted.Count; i++)
        {
            var candidates = candidateNodesByExtracted[i];
            for (var j = 0; j < candidates.Count; j++)
            {
                seen.Add(candidates[j].Uuid);
            }
        }

        return seen.Count;
    }

    private static IReadOnlyList<Graphiti.NodeDuplicateResponse> ReadNodeResolutions(JsonObject response)
    {
        if (!response.TryGetPropertyValue("entity_resolutions", out var node) || node is not JsonArray array)
        {
            return Array.Empty<Graphiti.NodeDuplicateResponse>();
        }

        var resolutions = new List<Graphiti.NodeDuplicateResponse>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonObject jsonObject)
            {
                continue;
            }

            resolutions.Add(new Graphiti.NodeDuplicateResponse(
                ReadInt(jsonObject, "id") ?? -1,
                ReadString(jsonObject, "name") ?? string.Empty,
                ReadInt(jsonObject, "duplicate_candidate_id") ?? -1));
        }

        return resolutions;
    }

    private static int? ReadInt(JsonObject item, string key)
    {
        if (!item.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<int>(out var id))
            {
                return id;
            }

            if (value.TryGetValue<string>(out var text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            {
                return id;
            }
        }

        return null;
    }

    private static string? ReadString(JsonObject item, string key)
    {
        if (!item.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return node.ToJsonString(GraphitiJsonSerializer.Options);
    }

    private static List<EntityNode> BuildResolvedNodeList(
        IReadOnlyList<EntityNode> extractedNodes,
        Dictionary<string, EntityNode> nodesByExtractedName)
    {
        var nodes = new List<EntityNode>(extractedNodes.Count);
        foreach (var extractedNode in extractedNodes)
        {
            if (!nodesByExtractedName.TryGetValue(extractedNode.Name, out var resolvedNode))
            {
                continue;
            }

            nodes.Add(resolvedNode);
        }

        return nodes;
    }

    internal static EntityNode MergeExtractedNode(EntityNode existing, EntityNode extracted)
    {
        PromoteResolvedNodeLabels(existing, extracted);

        if (!string.IsNullOrWhiteSpace(extracted.Summary))
        {
            existing.Summary = extracted.Summary;
        }

        foreach (var pair in extracted.Attributes)
        {
            existing.Attributes[pair.Key] = pair.Value;
        }

        return existing;
    }

    private static void PromoteResolvedNodeLabels(EntityNode existing, EntityNode extracted)
    {
        if (HasSpecificLabel(existing.Labels))
        {
            return;
        }

        if (!HasSpecificLabel(extracted.Labels))
        {
            return;
        }

        var labels = new List<string>(1 + existing.Labels.Count + extracted.Labels.Count);
        AddLabel("Entity", labels);
        AddLabels(existing.Labels, labels);
        for (var i = 0; i < extracted.Labels.Count; i++)
        {
            var label = extracted.Labels[i];
            if (!string.Equals(label, "Entity", StringComparison.Ordinal))
            {
                AddLabel(label, labels);
            }
        }

        existing.Labels = labels;
    }

    public async Task<EntityNode> ResolveTripletNodeAsync(EntityNode input, CancellationToken cancellationToken)
    {
        EntityNode resolved;
        try
        {
            resolved = await EntityNode.GetByUuidAsync(Driver, input.Uuid, cancellationToken).ConfigureAwait(false);
        }
        catch (NodeNotFoundException)
        {
            var groupId = string.IsNullOrWhiteSpace(input.GroupId)
                ? Driver.DefaultGroupId
                : input.GroupId;
            var resolution = await ResolveExtractedNodesAsync(
                new[] { input },
                groupId,
                episode: null,
                previousEpisodes: null,
                entityTypes: null,
                existingNodesOverride: null,
                cancellationToken).ConfigureAwait(false);
            resolved = resolution.Nodes.Count == 0 ? input : resolution.Nodes[0];
        }

        foreach (var pair in input.Attributes)
        {
            resolved.Attributes[pair.Key] = pair.Value;
        }

        if (!string.IsNullOrEmpty(input.Summary))
        {
            resolved.Summary = input.Summary;
        }

        resolved.Labels = MergeLabels(resolved.Labels, input.Labels);
        return resolved;
    }

    private static List<string> BuildNodeDedupEmbeddingQueries(IReadOnlyList<EntityNode> unresolvedNodes)
    {
        var queries = new List<string>(unresolvedNodes.Count);
        for (var i = 0; i < unresolvedNodes.Count; i++)
        {
            queries.Add(unresolvedNodes[i].Name.Replace('\n', ' '));
        }

        return queries;
    }

    private static List<string> MergeLabels(List<string> first, List<string> second)
    {
        var labels = new List<string>(first.Count + second.Count);
        AddLabels(first, labels);
        AddLabels(second, labels);
        return labels;
    }

    private static void AddLabels(
        List<string> source,
        List<string> target)
    {
        for (var i = 0; i < source.Count; i++)
        {
            AddLabel(source[i], target);
        }
    }

    private static void AddLabel(string label, List<string> labels)
    {
        if (!ContainsLabel(labels, label))
        {
            labels.Add(label);
        }
    }

    private static bool HasSpecificLabel(List<string> labels)
    {
        for (var i = 0; i < labels.Count; i++)
        {
            if (!string.Equals(labels[i], "Entity", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsLabel(List<string> labels, string label)
    {
        for (var i = 0; i < labels.Count; i++)
        {
            if (string.Equals(labels[i], label, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
