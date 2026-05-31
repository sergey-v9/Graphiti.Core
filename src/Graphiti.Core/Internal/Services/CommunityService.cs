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
            var edges = await GetEntityEdgesForCommunityAsync(driver, resolvedGroupIds, cancellationToken).ConfigureAwait(false);
            var clusters = CommunityClustering.BuildClusters(nodes, edges);
            var now = UtcNow();
            var builtCommunities = await SelectThrottledAsync(
                clusters,
                (cluster, token) => BuildCommunityAsync(cluster, now, token),
                cancellationToken).ConfigureAwait(false);
            var communities = builtCommunities
                .Select(result => result.Community)
                .ToList();
            var communityEdges = builtCommunities
                .SelectMany(result => result.CommunityEdges)
                .ToList();

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
        foreach (var node in nodes.GroupBy(node => node.Uuid, StringComparer.Ordinal).Select(group => group.First()))
        {
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

        await Node.DeleteByUuidsAsync(
            driver,
            existing.Select(community => community.Uuid),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<string>> ResolveCommunityGroupIdsAsync(
        IGraphDriver driver,
        IReadOnlyList<string>? groupIds,
        CancellationToken cancellationToken)
    {
        if (groupIds is { Count: > 0 })
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
        return nodes.ToList();
    }

    private static async Task<IReadOnlyList<EntityEdge>> GetEntityEdgesForCommunityAsync(
        IGraphDriver driver,
        IReadOnlyList<string> groupIds,
        CancellationToken cancellationToken)
    {
        return await driver.GetEdgesByGroupIdsAsync<EntityEdge>(
            groupIds,
            cancellationToken: cancellationToken).ConfigureAwait(false);
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
        IReadOnlyList<EntityNode> cluster,
        CancellationToken cancellationToken)
    {
        var summaries = cluster
            .Select(DeterministicCommunityText.BuildNodeSummary)
            .Where(summary => !string.IsNullOrWhiteSpace(summary))
            .ToList();
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

            var merged = new List<string>(summaries.Count / 2 + (oddOneOut is null ? 0 : 1));
            for (var i = 0; i < summaries.Count / 2; i++)
            {
                merged.Add(await GeneratePairSummaryAsync(
                    summaries[i],
                    summaries[i + summaries.Count / 2],
                    cancellationToken).ConfigureAwait(false));
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
        try
        {
            var response = await llmClient.GenerateTypedResponseAsync<Graphiti.CommunitySummaryResponse>(
                new[]
                {
                    new Message("system", "Summarize this cluster of related entities as a concise community."),
                    new Message("user", deterministicSummary)
                },
                promptName: "summarize_nodes.summarize_pair",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return response.Summary is { Length: > 0 } summary
                ? TextUtilities.TruncateAtSentence(summary, TextUtilities.MaxSummaryChars) ?? summary
                : deterministicSummary;
        }
        catch (NotImplementedException)
        {
            return deterministicSummary;
        }
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
        try
        {
            var response = await llmClient.GenerateTypedResponseAsync<Graphiti.CommunityNameResponse>(
                new[]
                {
                    new Message("system", "Name this entity community in five words or fewer."),
                    new Message("user", summary)
                },
                promptName: "summarize_nodes.summary_description",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return response.Description is { Length: > 0 } name
                ? name
                : deterministicName;
        }
        catch (NotImplementedException)
        {
            return deterministicName;
        }
    }

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

        var newSummary = await GeneratePairSummaryAsync(
            DeterministicCommunityText.BuildNodeSummary(entity),
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
        var neighborUuids = edges
            .Select(edge => edge.SourceNodeUuid == entity.Uuid ? edge.TargetNodeUuid : edge.SourceNodeUuid)
            .Where(uuid => !string.Equals(uuid, entity.Uuid, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (neighborUuids.Count == 0)
        {
            return (null, false);
        }

        var neighbors = await EntityNode.GetByUuidsAsync(driver, neighborUuids, entity.GroupId, cancellationToken).ConfigureAwait(false);
        if (neighbors.Count == 0)
        {
            return (null, false);
        }

        var neighborCommunities = await driver.GetCommunitiesByNodesAsync(neighbors, cancellationToken).ConfigureAwait(false);
        var community = neighborCommunities
            .GroupBy(candidate => candidate.Uuid, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .FirstOrDefault();

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
