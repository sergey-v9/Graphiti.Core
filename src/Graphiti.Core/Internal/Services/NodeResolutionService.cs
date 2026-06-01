using System.Globalization;
using System.Text.Json;
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
                    new Dictionary<string, EntityNode>(StringComparer.OrdinalIgnoreCase));
                activity?.SetTag("graphiti.existing.nodes", 0);
                activity?.SetTag("graphiti.result.nodes", 0);
                GraphitiTelemetry.SetOk(activity);
                return emptyResolution;
            }

            var existingNodes = existingNodesOverride ?? await Driver.GetNodesByGroupIdsAsync<EntityNode>(
                new[] { groupId },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            activity?.SetTag("graphiti.existing.nodes", existingNodes.Count);
            var deterministicResolution = EntityNodeDeduplicator.Resolve(extractedNodes, existingNodes, MergeExtractedNode);
            activity?.SetTag("graphiti.resolution.deterministic_nodes", deterministicResolution.Nodes.Count);
            var resolution = await ResolveUnresolvedNodesWithLlmAsync(
                deterministicResolution,
                extractedNodes,
                existingNodes,
                groupId,
                episode,
                previousEpisodes,
                entityTypes,
                cancellationToken).ConfigureAwait(false);
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

    private async Task<EntityNodeResolution> ResolveUnresolvedNodesWithLlmAsync(
        EntityNodeResolution deterministicResolution,
        IReadOnlyList<EntityNode> extractedNodes,
        IReadOnlyList<EntityNode> existingNodes,
        string groupId,
        EpisodicNode? episode,
        IReadOnlyList<EpisodicNode>? previousEpisodes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes,
        CancellationToken cancellationToken)
    {
        var unresolvedNodes = FindLlmNodeDedupTargets(deterministicResolution, extractedNodes);
        if (unresolvedNodes.Count == 0 || existingNodes.Count == 0)
        {
            return deterministicResolution;
        }

        var candidateContext = await CollectNodeDedupCandidatesAsync(
            unresolvedNodes,
            existingNodes,
            groupId,
            cancellationToken).ConfigureAwait(false);
        unresolvedNodes = candidateContext.UnresolvedNodes;
        var candidates = candidateContext.Candidates;
        if (candidates.Count == 0)
        {
            return deterministicResolution;
        }

        var response = await llmClient.GenerateResponseAsync(
            BuildNodeDeduplicationMessages(unresolvedNodes, candidates, episode, previousEpisodes, entityTypes),
            responseModel: typeof(Graphiti.NodeResolutionsResponse),
            modelSize: ModelSize.Small,
            groupId: groupId,
            promptName: "dedupe_nodes.nodes",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var nodesByExtractedName = new Dictionary<string, EntityNode>(
            deterministicResolution.NodesByExtractedName,
            StringComparer.OrdinalIgnoreCase);

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
                continue;
            }

            var resolvedNode = MergeExtractedNode(candidates[resolution.DuplicateCandidateId], extractedNode);
            nodesByExtractedName[extractedNode.Name] = resolvedNode;
        }

        foreach (var extractedNode in unresolvedNodes)
        {
            nodesByExtractedName.TryAdd(extractedNode.Name, extractedNode);
        }

        return new EntityNodeResolution(
            BuildResolvedNodeList(extractedNodes, nodesByExtractedName),
            nodesByExtractedName);
    }

    private static List<EntityNode> FindLlmNodeDedupTargets(
        EntityNodeResolution resolution,
        IReadOnlyList<EntityNode> extractedNodes)
    {
        var unresolved = new List<EntityNode>();
        var seenUuids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in extractedNodes)
        {
            if (!resolution.NodesByExtractedName.TryGetValue(node.Name, out var resolved)
                || !string.Equals(resolved.Uuid, node.Uuid, StringComparison.Ordinal)
                || !seenUuids.Add(node.Uuid))
            {
                continue;
            }

            unresolved.Add(node);
        }

        return unresolved;
    }

    private sealed record NodeDedupCandidateContext(
        List<EntityNode> UnresolvedNodes,
        List<EntityNode> Candidates);

    private async Task<NodeDedupCandidateContext> CollectNodeDedupCandidatesAsync(
        List<EntityNode> unresolvedNodes,
        IReadOnlyList<EntityNode> existingNodes,
        string groupId,
        CancellationToken cancellationToken)
    {
        var candidates = new List<EntityNode>();
        var eligibleNodes = new List<EntityNode>();
        var eligibleUuids = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void AddCandidate(EntityNode candidate)
        {
            if (seen.Add(candidate.Uuid))
            {
                candidates.Add(candidate);
            }
        }

        void MarkEligible(EntityNode node)
        {
            if (eligibleUuids.Add(node.Uuid))
            {
                eligibleNodes.Add(node);
            }
        }

        if (Driver is ISearchGraphDriver searchDriver)
        {
            try
            {
                var queryVectors = await embedder
                    .CreateBatchAsync(
                        BuildNodeDedupEmbeddingQueries(unresolvedNodes),
                        cancellationToken)
                    .ConfigureAwait(false);
                for (var i = 0; i < unresolvedNodes.Count; i++)
                {
                    var hits = await searchDriver.SearchEntityNodesByEmbeddingAsync(
                        queryVectors[i],
                        new SearchFilters(),
                        new[] { groupId },
                        NodeDedupCandidateLimit,
                        NodeDedupCosineMinScore,
                        cancellationToken).ConfigureAwait(false);
                    if (hits.Count > 0)
                    {
                        MarkEligible(unresolvedNodes[i]);
                    }

                    foreach (var hit in hits)
                    {
                        AddCandidate(hit.Item);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                GraphitiLog.NodeDedupCandidateSearchFailed(logger, exception, groupId);
            }
        }

        var maxCandidates = Math.Max(NodeDedupCandidateLimit, NodeDedupCandidateLimit * unresolvedNodes.Count);
        foreach (var unresolvedNode in unresolvedNodes)
        {
            var fallbackCandidates = RankFallbackNodeDedupCandidates(
                unresolvedNode,
                existingNodes,
                NodeDedupCandidateLimit);
            if (fallbackCandidates.Count > 0)
            {
                MarkEligible(unresolvedNode);
            }

            foreach (var candidate in fallbackCandidates)
            {
                AddCandidate(candidate);
            }
        }

        return new NodeDedupCandidateContext(
            eligibleNodes,
            candidates.Count <= maxCandidates ? candidates : candidates.GetRange(0, maxCandidates));
    }

    private static List<EntityNode> RankFallbackNodeDedupCandidates(
        EntityNode unresolvedNode,
        IReadOnlyList<EntityNode> existingNodes,
        int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        var scoredCandidates = new List<(EntityNode Node, int Index, float Score)>(existingNodes.Count);
        for (var i = 0; i < existingNodes.Count; i++)
        {
            var node = existingNodes[i];
            var score = SearchUtilities.TextScore(unresolvedNode.Name, $"{node.Name} {node.Summary}");
            if (score > 0)
            {
                scoredCandidates.Add((node, i, score));
            }
        }

        if (scoredCandidates.Count == 0)
        {
            return [];
        }

        scoredCandidates.Sort(static (left, right) =>
        {
            var scoreOrder = right.Score.CompareTo(left.Score);
            return scoreOrder != 0
                ? scoreOrder
                : left.Index.CompareTo(right.Index);
        });

        var resultCount = Math.Min(limit, scoredCandidates.Count);
        var result = new List<EntityNode>(resultCount);
        for (var i = 0; i < resultCount; i++)
        {
            result.Add(scoredCandidates[i].Node);
        }

        return result;
    }

    private static Message[] BuildNodeDeduplicationMessages(
        IReadOnlyList<EntityNode> unresolvedNodes,
        IReadOnlyList<EntityNode> candidates,
        EpisodicNode? episode,
        IReadOnlyList<EpisodicNode>? previousEpisodes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        return
        [
            new Message(
                "system",
                "You are an entity deduplication assistant. NEVER fabricate entity names or mark distinct entities as duplicates."),
            new Message(
                "user",
                BuildNodeDeduplicationContext(
                    unresolvedNodes,
                    candidates,
                    episode,
                    previousEpisodes,
                    entityTypes))
        ];
    }

    private static string BuildNodeDeduplicationContext(
        IReadOnlyList<EntityNode> unresolvedNodes,
        IReadOnlyList<EntityNode> candidates,
        EpisodicNode? episode,
        IReadOnlyList<EpisodicNode>? previousEpisodes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        var previousMessages = new JsonArray();
        foreach (var previousEpisode in previousEpisodes ?? Array.Empty<EpisodicNode>())
        {
            previousMessages.Add(new JsonObject
            {
                ["content"] = previousEpisode.Content,
                ["timestamp"] = GraphitiHelpers.EnsureUtc(previousEpisode.ValidAt).ToString("O")
            });
        }

        var extractedNodes = new JsonArray();
        for (var i = 0; i < unresolvedNodes.Count; i++)
        {
            var node = unresolvedNodes[i];
            extractedNodes.Add(new JsonObject
            {
                ["id"] = i,
                ["name"] = node.Name,
                ["entity_type"] = ExtractionContextBuilder.BuildStringArray(node.Labels),
                ["entity_type_description"] = EntityTypeDescription(node, entityTypes)
            });
        }

        var existingNodes = new JsonArray();
        for (var i = 0; i < candidates.Count; i++)
        {
            existingNodes.Add(BuildNodeDedupCandidateContext(candidates[i], i));
        }

        return $"""
<PREVIOUS MESSAGES>
{previousMessages.ToJsonString(GraphitiJsonSerializer.Options)}
</PREVIOUS MESSAGES>

<CURRENT MESSAGE>
{episode?.Content ?? string.Empty}
</CURRENT MESSAGE>

<ENTITIES>
{extractedNodes.ToJsonString(GraphitiJsonSerializer.Options)}
</ENTITIES>

<EXISTING ENTITIES>
{existingNodes.ToJsonString(GraphitiJsonSerializer.Options)}
</EXISTING ENTITIES>

Each of the above ENTITIES was extracted from the CURRENT MESSAGE.
For each entity, determine if it is a duplicate of any EXISTING ENTITY.
Entities should only be considered duplicates if they refer to the same real-world object or concept.

NEVER mark entities as duplicates if:
- They are related but distinct.
- They have similar names or purposes but refer to separate instances or concepts.

Task:
ENTITIES contains {unresolvedNodes.Count} entities with IDs 0 through {unresolvedNodes.Count - 1}.
Your response MUST include EXACTLY {unresolvedNodes.Count} resolutions with IDs 0 through {unresolvedNodes.Count - 1}. Do not skip or add IDs.

For every entity, provide:
- id: integer id from ENTITIES
- name: the best full name for the entity
- duplicate_candidate_id: the candidate_id of the EXISTING ENTITY that is the best duplicate match, or -1 if there is no duplicate
""";
    }

    private static JsonObject BuildNodeDedupCandidateContext(EntityNode candidate, int candidateId)
    {
        var context = JsonSerializer.SerializeToNode(candidate.Attributes, GraphitiJsonSerializer.Options) as JsonObject
                      ?? new JsonObject();
        context["candidate_id"] = candidateId;
        context["name"] = candidate.Name;
        context["entity_types"] = ExtractionContextBuilder.BuildStringArray(candidate.Labels);
        context["summary"] = string.IsNullOrEmpty(candidate.Summary)
            ? string.Empty
            : candidate.Summary.Length > 120
                ? candidate.Summary[..120]
                : candidate.Summary;
        return context;
    }

    private static string EntityTypeDescription(
        EntityNode node,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes)
    {
        if (entityTypes is null)
        {
            return "Default Entity Type";
        }

        for (var i = 0; i < node.Labels.Count; i++)
        {
            var label = node.Labels[i];
            if (string.Equals(label, "Entity", StringComparison.Ordinal)
                || !entityTypes.TryGetValue(label, out var definition)
                || string.IsNullOrWhiteSpace(definition.Description))
            {
                continue;
            }

            return definition.Description;
        }

        return "Default Entity Type";
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
        var nodes = new List<EntityNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extractedNode in extractedNodes)
        {
            if (!nodesByExtractedName.TryGetValue(extractedNode.Name, out var resolvedNode)
                || !seen.Add(resolvedNode.Uuid))
            {
                continue;
            }

            nodes.Add(resolvedNode);
        }

        return nodes;
    }

    private static EntityNode MergeExtractedNode(EntityNode existing, EntityNode extracted)
    {
        existing.Labels = MergeLabels(existing.Labels, extracted.Labels);

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

    private static List<string> BuildNodeDedupEmbeddingQueries(List<EntityNode> unresolvedNodes)
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
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddLabels(first, labels, seen);
        AddLabels(second, labels, seen);
        return labels;
    }

    private static void AddLabels(
        List<string> source,
        List<string> target,
        HashSet<string> seen)
    {
        for (var i = 0; i < source.Count; i++)
        {
            var label = source[i];
            if (seen.Add(label))
            {
                target.Add(label);
            }
        }
    }
}
