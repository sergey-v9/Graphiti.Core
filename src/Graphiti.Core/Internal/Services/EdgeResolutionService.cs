using Microsoft.Extensions.Logging;

namespace Graphiti.Core.Internal.Services;

internal sealed class EdgeResolutionService(
    Func<IGraphDriver> driverAccessor,
    GraphitiClients clients,
    ILlmClient llmClient,
    ILogger logger)
{
    public static string NormalizeFact(string fact) => GraphitiHelpers.NormalizeEntityKey(fact);

    public Task<List<EntityEdge>> ResolveExtractedEdgesAsync(
        IReadOnlyList<Graphiti.ExtractedEdge> extractedEdges,
        List<EntityNode> nodes,
        EpisodicNode episode,
        string groupId,
        DateTime now,
        CancellationToken cancellationToken) =>
        ResolveExtractedEdgesAsync(
            extractedEdges,
            EntityTypeResolver.BuildNodeNameMap(nodes),
            episode,
            groupId,
            now,
            cancellationToken);

    public async Task<List<EntityEdge>> ResolveExtractedEdgesAsync(
        IReadOnlyList<Graphiti.ExtractedEdge> extractedEdges,
        IReadOnlyDictionary<string, EntityNode> nodesByExtractedName,
        EpisodicNode episode,
        string groupId,
        DateTime now,
        CancellationToken cancellationToken,
        IReadOnlyList<EntityEdge>? existingEdgesOverride = null,
        HashSet<string>? newlyCreatedEdgeUuids = null)
    {
        using var activity = GraphitiTelemetry.StartActivity("Resolution.Edges");
        activity?.SetTag("graphiti.group_id", groupId);
        activity?.SetTag("graphiti.input.edges", extractedEdges.Count);
        activity?.SetTag("graphiti.input.nodes", nodesByExtractedName.Count);
        activity?.SetTag("graphiti.existing_edges.override", existingEdgesOverride is not null);
        activity?.SetTag("graphiti.existing_edges.override_count", existingEdgesOverride?.Count ?? 0);
        var newlyCreatedEdgeStartCount = newlyCreatedEdgeUuids?.Count ?? 0;

        try
        {
            var driver = driverAccessor();
            var result = new List<EntityEdge>();
            var resultUuids = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<(string SourceUuid, string TargetUuid, string NormalizedFact)>();
            var skippedEdges = 0;
            var episodes = new[] { episode };
            foreach (var extracted in extractedEdges)
            {
                if (!nodesByExtractedName.TryGetValue(extracted.SourceName, out var sourceNode)
                    || !nodesByExtractedName.TryGetValue(extracted.TargetName, out var targetNode)
                    || (!extracted.AllowSelfEdge && sourceNode.Uuid == targetNode.Uuid)
                    || string.IsNullOrWhiteSpace(extracted.Fact))
                {
                    skippedEdges++;
                    continue;
                }

                var key = (
                    SourceUuid: sourceNode.Uuid,
                    TargetUuid: targetNode.Uuid,
                    NormalizedFact: NormalizeFact(extracted.Fact));
                if (!seen.Add(key))
                {
                    skippedEdges++;
                    continue;
                }

                var candidate = new EntityEdge
                {
                    SourceNodeUuid = sourceNode.Uuid,
                    TargetNodeUuid = targetNode.Uuid,
                    GroupId = groupId,
                    CreatedAt = now,
                    Name = string.IsNullOrWhiteSpace(extracted.RelationType) ? "RELATES_TO" : extracted.RelationType,
                    Fact = extracted.Fact,
                    Episodes = EpisodeAttribution.MapIndicesToEpisodeUuids(extracted.EpisodeIndices, episodes),
                    ValidAt = extracted.ValidAt,
                    InvalidAt = extracted.InvalidAt,
                    ReferenceTime = extracted.ReferenceTime
                                    ?? EpisodeAttribution.ReferenceTimeForFirstValidIndex(
                                        extracted.EpisodeIndices,
                                        episodes,
                                        episode.ValidAt)
                };

                if (candidate.InvalidAt is not null)
                {
                    candidate.ExpiredAt = now;
                }

                var relatedEdges = await driver.GetEntityEdgesBetweenNodesAsync(
                    sourceNode.Uuid,
                    targetNode.Uuid,
                    cancellationToken).ConfigureAwait(false);
                relatedEdges = EdgeMergeHelpers.MergeEdgeOverrides(
                    relatedEdges,
                    existingEdgesOverride,
                    edge => edge.SourceNodeUuid == sourceNode.Uuid && edge.TargetNodeUuid == targetNode.Uuid);
                var duplicate = FindDuplicateFact(relatedEdges, key.NormalizedFact);
                if (duplicate is not null)
                {
                    AddEpisodesIfMissing(duplicate, candidate.Episodes, episode.Uuid);
                    EdgeMergeHelpers.AddResolvedEdge(result, resultUuids, duplicate);
                    continue;
                }

                var existingEdges = await GetEdgeInvalidationCandidatesAsync(
                    candidate,
                    groupId,
                    relatedEdges,
                    existingEdgesOverride,
                    cancellationToken).ConfigureAwait(false);
                var (resolvedEdge, invalidatedEdges) = await ResolveEdgeWithLlmAsync(
                    candidate,
                    relatedEdges,
                    existingEdges,
                    episode,
                    now,
                    cancellationToken).ConfigureAwait(false);
                if (resolvedEdge.Uuid == candidate.Uuid)
                {
                    newlyCreatedEdgeUuids?.Add(resolvedEdge.Uuid);
                }

                EdgeMergeHelpers.AddResolvedEdge(result, resultUuids, resolvedEdge);
                foreach (var invalidatedEdge in invalidatedEdges)
                {
                    EdgeMergeHelpers.AddResolvedEdge(result, resultUuids, invalidatedEdge);
                }
            }

            activity?.SetTag("graphiti.extraction.skipped_edges", skippedEdges);
            activity?.SetTag("graphiti.result.edges", result.Count);
            activity?.SetTag(
                "graphiti.result.created_edges",
                newlyCreatedEdgeUuids is null ? 0 : newlyCreatedEdgeUuids.Count - newlyCreatedEdgeStartCount);
            GraphitiTelemetry.SetOk(activity);
            return result;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    internal static EntityEdge? FindDuplicateFact(IReadOnlyList<EntityEdge> relatedEdges, string normalizedFact)
    {
        for (var i = 0; i < relatedEdges.Count; i++)
        {
            var edge = relatedEdges[i];
            if (NormalizeFact(edge.Fact) == normalizedFact)
            {
                return edge;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<EntityEdge>> GetEdgeInvalidationCandidatesAsync(
        EntityEdge extractedEdge,
        string groupId,
        IReadOnlyList<EntityEdge> relatedEdges,
        IReadOnlyList<EntityEdge>? existingEdgesOverride,
        CancellationToken cancellationToken)
    {
        var relatedUuids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < relatedEdges.Count; i++)
        {
            relatedUuids.Add(relatedEdges[i].Uuid);
        }

        var searchConfig = SearchConfigRecipes.EdgeHybridSearchRrf;
        searchConfig.Limit = SearchUtilities.RelevantSchemaLimit;
        var searchResults = await SearchEngine.SearchAsync(
            clients,
            extractedEdge.Fact,
            new[] { groupId },
            searchConfig,
            new SearchFilters(),
            driver: driverAccessor(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var overrideLookup = EdgeMergeHelpers.BuildOverrideLookup(existingEdgesOverride, relatedUuids);
        var candidates = new List<EntityEdge>(SearchUtilities.RelevantSchemaLimit);
        var seenUuids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < searchResults.Edges.Count; i++)
        {
            var edge = searchResults.Edges[i];
            if (relatedUuids.Contains(edge.Uuid))
            {
                continue;
            }

            var candidate = overrideLookup.TryGetValue(edge.Uuid, out var overrideEdge)
                ? overrideEdge
                : edge;
            if (seenUuids.Add(candidate.Uuid))
            {
                candidates.Add(candidate);
                if (candidates.Count >= SearchUtilities.RelevantSchemaLimit)
                {
                    return candidates;
                }
            }
        }

        var overrideCandidates = EdgeMergeHelpers.RankOverrideInvalidationCandidates(
            extractedEdge.Fact,
            existingEdgesOverride,
            relatedUuids,
            SearchUtilities.RelevantSchemaLimit);
        for (var i = 0; i < overrideCandidates.Count; i++)
        {
            var candidate = overrideCandidates[i];
            if (seenUuids.Add(candidate.Uuid))
            {
                candidates.Add(candidate);
                if (candidates.Count >= SearchUtilities.RelevantSchemaLimit)
                {
                    break;
                }
            }
        }

        return candidates;
    }

    public async Task<(EntityEdge ResolvedEdge, IReadOnlyList<EntityEdge> InvalidatedEdges)> ResolveEdgeWithLlmAsync(
        EntityEdge extractedEdge,
        IReadOnlyList<EntityEdge> relatedEdges,
        IReadOnlyList<EntityEdge> existingEdges,
        EpisodicNode episode,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (relatedEdges.Count == 0 && existingEdges.Count == 0)
        {
            await ExtractEdgeTimestampsAsync(extractedEdge, episode, now, cancellationToken).ConfigureAwait(false);
            return (extractedEdge, Array.Empty<EntityEdge>());
        }

        var response = await llmClient.GenerateResponseAsync(
            DedupeEdgesPrompts.BuildResolveEdge(DedupeEdgesPrompts.BuildContext(
                extractedEdge,
                relatedEdges,
                existingEdges)),
            responseModel: typeof(Graphiti.EdgeResolutionResponse),
            modelSize: ModelSize.Small,
            promptName: "dedupe_edges.resolve_edge",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var resolvedEdge = TryGetFirstValidIndex(
            EdgeMergeHelpers.ReadIntArray(response, "duplicate_facts"),
            relatedEdges.Count,
            out var duplicateFactIndex)
            ? relatedEdges[duplicateFactIndex]
            : extractedEdge;
        if (resolvedEdge.Uuid != extractedEdge.Uuid)
        {
            AddEpisodesIfMissing(resolvedEdge, extractedEdge.Episodes, episode.Uuid);
        }

        var invalidationCandidates = new List<EntityEdge>();
        var offset = relatedEdges.Count;
        var maxValidIndex = relatedEdges.Count + existingEdges.Count - 1;
        foreach (var index in EdgeMergeHelpers.ReadIntArray(response, "contradicted_facts"))
        {
            if (index < 0 || index > maxValidIndex)
            {
                continue;
            }

            invalidationCandidates.Add(index < offset
                ? relatedEdges[index]
                : existingEdges[index - offset]);
        }

        if (resolvedEdge.InvalidAt is not null && resolvedEdge.ExpiredAt is null)
        {
            resolvedEdge.ExpiredAt = now;
        }

        if (resolvedEdge.Uuid == extractedEdge.Uuid)
        {
            await ExtractEdgeTimestampsAsync(resolvedEdge, episode, now, cancellationToken).ConfigureAwait(false);
        }

        ExpireResolvedEdgeIfLaterCandidateExists(resolvedEdge, invalidationCandidates, now);

        var invalidatedEdges = EdgeMergeHelpers.ResolveEdgeContradictions(resolvedEdge, invalidationCandidates, now);
        return (resolvedEdge, invalidatedEdges);
    }

    private static bool TryGetFirstValidIndex(
        IReadOnlyList<int> indexes,
        int upperBound,
        out int validIndex)
    {
        for (var i = 0; i < indexes.Count; i++)
        {
            var index = indexes[i];
            if (index >= 0 && index < upperBound)
            {
                validIndex = index;
                return true;
            }
        }

        validIndex = 0;
        return false;
    }

    private static void ExpireResolvedEdgeIfLaterCandidateExists(
        EntityEdge resolvedEdge,
        List<EntityEdge> invalidationCandidates,
        DateTime now)
    {
        if (resolvedEdge.ExpiredAt is not null || resolvedEdge.ValidAt is null)
        {
            return;
        }

        var resolvedValidAt = GraphitiHelpers.EnsureUtc(resolvedEdge.ValidAt.Value);
        EntityEdge? earliestLaterCandidate = null;
        var earliestLaterValidAt = default(DateTime);
        for (var i = 0; i < invalidationCandidates.Count; i++)
        {
            var candidate = invalidationCandidates[i];
            if (candidate.ValidAt is null)
            {
                continue;
            }

            var candidateValidAt = GraphitiHelpers.EnsureUtc(candidate.ValidAt.Value);
            if (candidateValidAt <= resolvedValidAt)
            {
                continue;
            }

            if (earliestLaterCandidate is null || candidate.ValidAt.Value < earliestLaterValidAt)
            {
                earliestLaterCandidate = candidate;
                earliestLaterValidAt = candidate.ValidAt.Value;
            }
        }

        if (earliestLaterCandidate is null)
        {
            return;
        }

        resolvedEdge.InvalidAt = earliestLaterCandidate.ValidAt;
        resolvedEdge.ExpiredAt = now;
    }

    private async Task ExtractEdgeTimestampsAsync(
        EntityEdge edge,
        EpisodicNode episode,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (edge.ValidAt is not null || edge.InvalidAt is not null)
        {
            return;
        }

        try
        {
            var response = await llmClient.GenerateTypedResponseAsync<Graphiti.EdgeTimestampResponse>(
                ExtractEdgesPrompts.BuildExtractTimestamps(edge.Fact, edge.ReferenceTime ?? episode.ValidAt),
                modelSize: ModelSize.Small,
                promptName: "extract_edges.extract_timestamps",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            edge.ValidAt = ParseOptionalDate(response.ValidAt);
            edge.InvalidAt = ParseOptionalDate(response.InvalidAt);
            if (edge.InvalidAt is not null && edge.ExpiredAt is null)
            {
                edge.ExpiredAt = now;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            GraphitiLog.TimestampExtractionFailed(logger, exception, edge.Uuid);
        }
    }

    private static DateTime? ParseOptionalDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return GraphitiHelpers.TryParseDbDate(value, out var parsed) ? parsed : null;
    }

    private static void AddEpisodesIfMissing(
        EntityEdge edge,
        List<string> episodeUuids,
        string fallbackEpisodeUuid)
    {
        if (episodeUuids.Count == 0)
        {
            EdgeMergeHelpers.AddEpisodeIfMissing(edge, fallbackEpisodeUuid);
            return;
        }

        for (var i = 0; i < episodeUuids.Count; i++)
        {
            EdgeMergeHelpers.AddEpisodeIfMissing(edge, episodeUuids[i]);
        }
    }
}
