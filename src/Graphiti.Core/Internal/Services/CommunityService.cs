using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Graphiti.Core.Internal.Services;

internal sealed class CommunityService(
    Func<IGraphDriver> driverAccessor,
    ILlmClient llmClient,
    IEmbedderClient embedder,
    ILogger logger,
    TimeProvider timeProvider,
    Func<int> getMaxDegreeOfParallelism)
{
    public async Task<(IReadOnlyList<CommunityNode> Communities, IReadOnlyList<CommunityEdge> CommunityEdges)> BuildCommunitiesAsync(
        IReadOnlyList<string>? groupIds = null,
        IGraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = GraphitiTelemetry.StartActivity("BuildCommunities");
        GraphitiTelemetry.SetGroupIds(activity, groupIds);
        GraphitiLog.BuildingCommunities(logger, groupIds?.Count ?? 0);

        try
        {
            driver ??= driverAccessor();
            await RemoveCommunitiesAsync(driver, cancellationToken).ConfigureAwait(false);

            var resolvedGroupIds = await ResolveCommunityGroupIdsAsync(driver, groupIds, cancellationToken).ConfigureAwait(false);
            var nodes = await GetEntityNodesForCommunityAsync(driver, resolvedGroupIds, cancellationToken).ConfigureAwait(false);
            var edges = await GetEntityEdgesForCommunityAsync(driver, nodes, cancellationToken).ConfigureAwait(false);
            var clusters = CommunityClustering.BuildClusters(nodes, edges);
            var now = UtcNow();
            var builtCommunities = await SelectThrottledAsync(
                clusters,
                (cluster, token) => BuildCommunityAsync(cluster, now, token),
                cancellationToken).ConfigureAwait(false);
            var communities = new List<CommunityNode>(builtCommunities.Length);
            var communityEdgeCapacity = 0;
            foreach (var result in builtCommunities)
            {
                communityEdgeCapacity += result.CommunityEdges.Count;
            }

            var communityEdges = new List<CommunityEdge>(communityEdgeCapacity);
            foreach (var result in builtCommunities)
            {
                communities.Add(result.Community);
                foreach (var edge in result.CommunityEdges)
                {
                    communityEdges.Add(edge);
                }
            }

            await ThrottledWork.ForEachAsync(
                communities,
                (community, token) => community.SaveAsync(driver, token),
                getMaxDegreeOfParallelism(),
                cancellationToken).ConfigureAwait(false);
            await ThrottledWork.ForEachAsync(
                communityEdges,
                (edge, token) => edge.SaveAsync(driver, token),
                getMaxDegreeOfParallelism(),
                cancellationToken).ConfigureAwait(false);

            activity?.SetTag("graphiti.result.communities", communities.Count);
            activity?.SetTag("graphiti.result.community_edges", communityEdges.Count);
            GraphitiLog.CommunitiesBuilt(logger, communities.Count, communityEdges.Count);
            GraphitiTelemetry.SetOk(activity);
            return (communities, communityEdges);
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    public async Task<(IReadOnlyList<CommunityNode> Communities, IReadOnlyList<CommunityEdge> CommunityEdges)> UpdateCommunitiesForNodesAsync(
        IReadOnlyList<EntityNode> nodes,
        IGraphDriver driver,
        CancellationToken cancellationToken)
    {
        var communities = new List<CommunityNode>();
        var communityEdges = new List<CommunityEdge>();
        var seenNodeUuids = new HashSet<string>(nodes.Count, StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (!seenNodeUuids.Add(node.Uuid))
            {
                continue;
            }

            var (updatedCommunities, newCommunityEdges) = await UpdateCommunityAsync(node, driver, cancellationToken).ConfigureAwait(false);
            communities.AddRange(updatedCommunities);
            communityEdges.AddRange(newCommunityEdges);
        }

        return (communities, communityEdges);
    }

    private async Task<CommunityBuildResult> BuildCommunityAsync(
        List<EntityNode> cluster,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var summary = await GenerateCommunitySummaryAsync(cluster, cancellationToken).ConfigureAwait(false);
        var name = await GenerateCommunityNameAsync(summary, cluster, cancellationToken).ConfigureAwait(false);
        var community = new CommunityNode
        {
            Name = name,
            GroupId = cluster[0].GroupId,
            Labels = new List<string> { "Community" },
            CreatedAt = now,
            Summary = summary
        };
        await community.GenerateNameEmbeddingAsync(embedder, cancellationToken).ConfigureAwait(false);
        return new CommunityBuildResult(
            community,
            MaintenanceUtilities.BuildCommunityEdges(cluster, community, now));
    }

    private static async Task RemoveCommunitiesAsync(
        IGraphDriver driver,
        CancellationToken cancellationToken)
    {
        var existing = await GetCommunityNodesAsync(driver, cancellationToken).ConfigureAwait(false);
        if (existing.Count == 0)
        {
            return;
        }

        var communityUuids = new string[existing.Count];
        for (var i = 0; i < existing.Count; i++)
        {
            communityUuids[i] = existing[i].Uuid;
        }

        await Node.DeleteByUuidsAsync(driver, communityUuids, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> ResolveCommunityGroupIdsAsync(
        IGraphDriver driver,
        IReadOnlyList<string>? groupIds,
        CancellationToken cancellationToken)
    {
        if (groupIds is not null)
        {
            return groupIds;
        }

        return driver is GraphDriverBase graphDriver
            ? await graphDriver.GetEntityGroupIdsAsync(cancellationToken).ConfigureAwait(false)
            : new[] { driver.DefaultGroupId };
    }

    private static async Task<List<EntityNode>> GetEntityNodesForCommunityAsync(
        IGraphDriver driver,
        IReadOnlyList<string> groupIds,
        CancellationToken cancellationToken)
    {
        var nodes = await driver.GetNodesByGroupIdsAsync<EntityNode>(
            groupIds,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var result = new List<EntityNode>(nodes.Count);
        foreach (var node in nodes)
        {
            result.Add(node);
        }

        return result;
    }

    private static async Task<IReadOnlyList<EntityEdge>> GetEntityEdgesForCommunityAsync(
        IGraphDriver driver,
        List<EntityNode> nodes,
        CancellationToken cancellationToken)
    {
        if (nodes.Count == 0)
        {
            return Array.Empty<EntityEdge>();
        }

        var selectedNodeUuids = new HashSet<string>(nodes.Count, StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            selectedNodeUuids.Add(node.Uuid);
        }

        var edges = new List<EntityEdge>();
        var seenEdgeUuids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var incidentEdges = await driver.GetEntityEdgesByNodeUuidAsync(
                node.Uuid,
                cancellationToken).ConfigureAwait(false);
            foreach (var edge in incidentEdges)
            {
                if (!selectedNodeUuids.Contains(edge.SourceNodeUuid)
                    || !selectedNodeUuids.Contains(edge.TargetNodeUuid)
                    || !seenEdgeUuids.Add(edge.Uuid))
                {
                    continue;
                }

                edges.Add(edge);
            }
        }

        return edges;
    }

    private static async Task<IReadOnlyList<CommunityNode>> GetCommunityNodesAsync(
        IGraphDriver driver,
        CancellationToken cancellationToken)
    {
        var groupIds = driver is GraphDriverBase graphDriver
            ? await graphDriver.GetCommunityGroupIdsAsync(cancellationToken).ConfigureAwait(false)
            : new[] { driver.DefaultGroupId };
        if (groupIds.Count == 0)
        {
            return Array.Empty<CommunityNode>();
        }

        return await driver.GetNodesByGroupIdsAsync<CommunityNode>(
            groupIds,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GenerateCommunitySummaryAsync(
        List<EntityNode> cluster,
        CancellationToken cancellationToken)
    {
        var summaries = new List<string>(cluster.Count);
        foreach (var node in cluster)
        {
            // Seed the pairwise reduction with the RAW entity summary, not name-prefixed
            // text or a filtered subset. Python build_community
            // (community_operations.py:177) uses `entity.summary` directly, including
            // empty strings; for a single-node cluster this seed is persisted verbatim
            // via truncate_at_sentence (:199).
            summaries.Add(node.Summary);
        }
        if (summaries.Count == 0)
        {
            return string.Empty;
        }

        while (summaries.Count > 1)
        {
            string? oddOneOut = null;
            if (summaries.Count % 2 == 1)
            {
                oddOneOut = summaries[^1];
                summaries.RemoveAt(summaries.Count - 1);
            }

            var pairCount = summaries.Count / 2;
            var pairIndexes = new List<int>(pairCount);
            for (var i = 0; i < pairCount; i++)
            {
                pairIndexes.Add(i);
            }

            var pairSummaries = await ThrottledWork.SelectAsync(
                pairIndexes,
                (index, token) => GeneratePairSummaryAsync(
                    summaries[index],
                    summaries[index + pairCount],
                    token),
                getMaxDegreeOfParallelism(),
                cancellationToken).ConfigureAwait(false);

            var merged = new List<string>(pairSummaries.Length + (oddOneOut is null ? 0 : 1));
            for (var i = 0; i < pairSummaries.Length; i++)
            {
                merged.Add(pairSummaries[i]);
            }

            if (oddOneOut is not null)
            {
                merged.Add(oddOneOut);
            }

            summaries = merged;
        }

        return TextUtilities.TruncateAtSentence(summaries[0], TextUtilities.MaxSummaryChars) ?? summaries[0];
    }

    private async Task<string> GeneratePairSummaryAsync(
        string left,
        string right,
        CancellationToken cancellationToken)
    {
        var deterministicSummary = DeterministicCommunityText.BuildCommunitySummary(new[] { left, right });
        var response = await llmClient.GenerateTypedResponseAsync<Graphiti.CommunitySummaryResponse>(
            SummarizeNodesPrompts.BuildSummarizePair(left, right),
            promptName: "summarize_nodes.summarize_pair",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (response.Summary is { Length: > 0 } summary)
        {
            return TextUtilities.TruncateAtSentence(summary, TextUtilities.MaxSummaryChars) ?? summary;
        }

        return ShouldUseDeterministicLlmFallback()
            ? deterministicSummary
            : throw new InvalidOperationException("LLM did not return a community summary.");
    }

    private async Task<string> GenerateCommunityNameAsync(
        string summary,
        IReadOnlyList<EntityNode> cluster,
        CancellationToken cancellationToken,
        string? fallbackName = null)
    {
        var deterministicName = string.IsNullOrWhiteSpace(fallbackName)
            ? DeterministicCommunityText.BuildCommunityName(cluster)
            : fallbackName;
        var response = await llmClient.GenerateTypedResponseAsync<Graphiti.CommunityNameResponse>(
            SummarizeNodesPrompts.BuildSummaryDescription(summary),
            promptName: "summarize_nodes.summary_description",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (response.Description is { Length: > 0 } name)
        {
            return name;
        }

        return ShouldUseDeterministicLlmFallback()
            ? deterministicName
            : throw new InvalidOperationException("LLM did not return a community name.");
    }

    private bool ShouldUseDeterministicLlmFallback() => llmClient is NoOpLlmClient;

    private async Task<(IReadOnlyList<CommunityNode> Communities, IReadOnlyList<CommunityEdge> CommunityEdges)> UpdateCommunityAsync(
        EntityNode entity,
        IGraphDriver driver,
        CancellationToken cancellationToken)
    {
        var (community, isNewMember) = await DetermineEntityCommunityAsync(driver, entity, cancellationToken).ConfigureAwait(false);
        if (community is null)
        {
            return (Array.Empty<CommunityNode>(), Array.Empty<CommunityEdge>());
        }

        // Pair the RAW entity summary with the existing community summary, matching
        // Python update_community (community_operations.py:336):
        // summarize_pair(llm_client, (entity.summary, community.summary)).
        var newSummary = await GeneratePairSummaryAsync(
            entity.Summary,
            community.Summary,
            cancellationToken).ConfigureAwait(false);
        var newName = await GenerateCommunityNameAsync(
            newSummary,
            new[] { entity },
            cancellationToken,
            community.Name).ConfigureAwait(false);

        community.Summary = newSummary;
        community.Name = newName;
        await community.GenerateNameEmbeddingAsync(embedder, cancellationToken).ConfigureAwait(false);

        var communityEdges = new List<CommunityEdge>();
        if (isNewMember)
        {
            var communityEdge = MaintenanceUtilities.BuildCommunityEdges(
                new[] { entity },
                community,
                UtcNow())[0];
            await communityEdge.SaveAsync(driver, cancellationToken).ConfigureAwait(false);
            communityEdges.Add(communityEdge);
        }

        await community.SaveAsync(driver, cancellationToken).ConfigureAwait(false);
        return (new[] { community }, communityEdges);
    }

    private static async Task<(CommunityNode? Community, bool IsNewMember)> DetermineEntityCommunityAsync(
        IGraphDriver driver,
        EntityNode entity,
        CancellationToken cancellationToken)
    {
        var existingCommunities = await driver.GetCommunitiesByNodesAsync(new[] { entity }, cancellationToken).ConfigureAwait(false);
        if (existingCommunities.Count > 0)
        {
            return (existingCommunities[0], false);
        }

        var edges = await driver.GetEntityEdgesByNodeUuidAsync(entity.Uuid, cancellationToken).ConfigureAwait(false);
        var neighborUuids = new List<string>(edges.Count);
        var seenNeighborUuids = new HashSet<string>(edges.Count, StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            var neighborUuid = string.Equals(edge.SourceNodeUuid, entity.Uuid, StringComparison.Ordinal)
                ? edge.TargetNodeUuid
                : edge.SourceNodeUuid;
            if (!string.Equals(neighborUuid, entity.Uuid, StringComparison.Ordinal)
                && seenNeighborUuids.Add(neighborUuid))
            {
                neighborUuids.Add(neighborUuid);
            }
        }

        if (neighborUuids.Count == 0)
        {
            return (null, false);
        }

        var neighbors = await EntityNode.GetByUuidsAsync(driver, neighborUuids, entity.GroupId, cancellationToken).ConfigureAwait(false);
        if (neighbors.Count == 0)
        {
            return (null, false);
        }

        var neighborsByUuid = new Dictionary<string, EntityNode>(neighbors.Count, StringComparer.Ordinal);
        foreach (var neighbor in neighbors)
        {
            neighborsByUuid.TryAdd(neighbor.Uuid, neighbor);
        }

        var communityCounts = new Dictionary<string, (CommunityNode Community, int Count)>(StringComparer.Ordinal);
        var communityOrder = new List<string>(neighbors.Count);
        var singleNeighbor = new EntityNode[1];
        foreach (var neighborUuid in neighborUuids)
        {
            if (!neighborsByUuid.TryGetValue(neighborUuid, out var neighbor))
            {
                continue;
            }

            singleNeighbor[0] = neighbor;
            var neighborCommunities = await driver.GetCommunitiesByNodesAsync(singleNeighbor, cancellationToken).ConfigureAwait(false);
            foreach (var candidate in neighborCommunities)
            {
                if (communityCounts.TryGetValue(candidate.Uuid, out var existing))
                {
                    communityCounts[candidate.Uuid] = (existing.Community, existing.Count + 1);
                    continue;
                }

                communityCounts.Add(candidate.Uuid, (candidate, 1));
                communityOrder.Add(candidate.Uuid);
            }
        }

        CommunityNode? community = null;
        var bestCount = 0;
        foreach (var communityUuid in communityOrder)
        {
            var candidate = communityCounts[communityUuid];
            if (community is null || candidate.Count > bestCount)
            {
                community = candidate.Community;
                bestCount = candidate.Count;
            }
        }

        return community is null ? (null, false) : (community, true);
    }

    private Task<TResult[]> SelectThrottledAsync<TSource, TResult>(
        List<TSource> items,
        Func<TSource, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken) =>
        ThrottledWork.SelectAsync(items, operation, getMaxDegreeOfParallelism(), cancellationToken);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record CommunityBuildResult(
        CommunityNode Community,
        IReadOnlyList<CommunityEdge> CommunityEdges);

}
