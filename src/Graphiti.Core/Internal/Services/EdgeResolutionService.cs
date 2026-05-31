using System.Text.Json.Nodes;
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
            foreach (var extracted in extractedEdges)
            {
                if (!nodesByExtractedName.TryGetValue(extracted.SourceName, out var sourceNode)
                    || !nodesByExtractedName.TryGetValue(extracted.TargetName, out var targetNode)
                    || sourceNode.Uuid == targetNode.Uuid
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
                    Episodes = new List<string> { episode.Uuid },
                    ValidAt = extracted.ValidAt,
                    InvalidAt = extracted.InvalidAt,
                    ReferenceTime = episode.ValidAt
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
                var duplicate = relatedEdges.FirstOrDefault(edge => NormalizeFact(edge.Fact) == key.NormalizedFact);
                if (duplicate is not null)
                {
                    if (!duplicate.Episodes.Contains(episode.Uuid, StringComparer.Ordinal))
                    {
                        duplicate.Episodes.Add(episode.Uuid);
                    }

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

    public async Task<IReadOnlyList<EntityEdge>> GetEdgeInvalidationCandidatesAsync(
        EntityEdge extractedEdge,
        string groupId,
        IReadOnlyList<EntityEdge> relatedEdges,
        IReadOnlyList<EntityEdge>? existingEdgesOverride,
        CancellationToken cancellationToken)
    {
        var relatedUuids = relatedEdges.Select(edge => edge.Uuid).ToHashSet(StringComparer.Ordinal);
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
        var graphCandidates = searchResults.Edges
            .Where(edge => !relatedUuids.Contains(edge.Uuid))
            .ToList();
        var overrideCandidates = EdgeMergeHelpers.RankOverrideInvalidationCandidates(
            extractedEdge.Fact,
            existingEdgesOverride,
            relatedUuids,
            SearchUtilities.RelevantSchemaLimit);

        return graphCandidates
            .Concat(overrideCandidates)
            .DistinctBy(edge => edge.Uuid, StringComparer.Ordinal)
            .Take(SearchUtilities.RelevantSchemaLimit)
            .ToList();
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
            new[]
            {
                new Message("system", "Resolve duplicate and contradictory facts."),
                new Message("user", BuildEdgeResolutionContext(extractedEdge, relatedEdges, existingEdges))
            },
            responseModel: typeof(Graphiti.EdgeResolutionResponse),
            modelSize: ModelSize.Small,
            promptName: "dedupe_edges.resolve_edge",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var duplicateFactIds = EdgeMergeHelpers.ReadIntArray(response, "duplicate_facts")
            .Where(index => index >= 0 && index < relatedEdges.Count)
            .ToList();
        var resolvedEdge = duplicateFactIds.Count > 0 ? relatedEdges[duplicateFactIds[0]] : extractedEdge;
        if (resolvedEdge.Uuid != extractedEdge.Uuid
            && !resolvedEdge.Episodes.Contains(episode.Uuid, StringComparer.Ordinal))
        {
            resolvedEdge.Episodes.Add(episode.Uuid);
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

        if (resolvedEdge.ExpiredAt is null)
        {
            foreach (var candidate in invalidationCandidates
                         .OrderBy(candidate => candidate.ValidAt is null)
                         .ThenBy(candidate => candidate.ValidAt))
            {
                var candidateValidAt = candidate.ValidAt is null
                    ? (DateTime?)null
                    : GraphitiHelpers.EnsureUtc(candidate.ValidAt.Value);
                var resolvedValidAt = resolvedEdge.ValidAt is null
                    ? (DateTime?)null
                    : GraphitiHelpers.EnsureUtc(resolvedEdge.ValidAt.Value);
                if (candidateValidAt is not null
                    && resolvedValidAt is not null
                    && candidateValidAt > resolvedValidAt)
                {
                    resolvedEdge.InvalidAt = candidate.ValidAt;
                    resolvedEdge.ExpiredAt = now;
                    break;
                }
            }
        }

        var invalidatedEdges = EdgeMergeHelpers.ResolveEdgeContradictions(resolvedEdge, invalidationCandidates, now);
        return (resolvedEdge, invalidatedEdges);
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
                new[]
                {
                    new Message("system", "Extract temporal validity timestamps for the fact."),
                    new Message("user", new JsonObject
                    {
                        ["fact"] = edge.Fact,
                        ["reference_time"] = GraphitiHelpers.EnsureUtc(episode.ValidAt).ToString("O")
                    }.ToJsonString(GraphitiJsonSerializer.Options))
                },
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

    private static string BuildEdgeResolutionContext(
        EntityEdge extractedEdge,
        IReadOnlyList<EntityEdge> relatedEdges,
        IReadOnlyList<EntityEdge> existingEdges)
    {
        var offset = relatedEdges.Count;
        var existingFacts = new JsonArray();
        for (var i = 0; i < relatedEdges.Count; i++)
        {
            existingFacts.Add(new JsonObject
            {
                ["idx"] = i,
                ["fact"] = relatedEdges[i].Fact
            });
        }

        var invalidationCandidates = new JsonArray();
        for (var i = 0; i < existingEdges.Count; i++)
        {
            invalidationCandidates.Add(new JsonObject
            {
                ["idx"] = offset + i,
                ["fact"] = existingEdges[i].Fact
            });
        }

        return new JsonObject
        {
            ["existing_edges"] = existingFacts,
            ["new_edge"] = extractedEdge.Fact,
            ["edge_invalidation_candidates"] = invalidationCandidates
        }.ToJsonString(GraphitiJsonSerializer.Options);
    }

    private static DateTime? ParseOptionalDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return GraphitiHelpers.TryParseDbDate(value, out var parsed) ? parsed : null;
    }
}
