using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Graphiti.Core.Internal.Services;

internal sealed class EdgeResolutionService(
    Func<IGraphDriver> driverAccessor,
    GraphitiClients clients,
    ILlmClient llmClient,
    ILogger logger,
    Func<int>? getMaxDegreeOfParallelism = null)
{
    public static string NormalizeFact(string fact) => GraphitiHelpers.NormalizeEntityKey(fact);

    public Task<List<EntityEdge>> ResolveExtractedEdgesAsync(
        IReadOnlyList<Graphiti.ExtractedEdge> extractedEdges,
        List<EntityNode> nodes,
        EpisodicNode episode,
        string groupId,
        DateTime now,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes = null,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap = null) =>
        ResolveExtractedEdgesAsync(
            extractedEdges,
            EntityTypeResolver.BuildNodeNameMap(nodes),
            episode,
            groupId,
            now,
            cancellationToken,
            edgeTypes,
            edgeTypeMap);

    public async Task<List<EntityEdge>> ResolveExtractedEdgesAsync(
        IReadOnlyList<Graphiti.ExtractedEdge> extractedEdges,
        IReadOnlyDictionary<string, EntityNode> nodesByExtractedName,
        EpisodicNode episode,
        string groupId,
        DateTime now,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes = null,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap = null,
        IReadOnlyList<EntityEdge>? existingEdgesOverride = null,
        HashSet<string>? newlyCreatedEdgeUuids = null)
    {
        var candidates = BuildExtractedEdgeCandidates(
            extractedEdges,
            nodesByExtractedName,
            new[] { episode },
            groupId,
            now,
            out _);

        return await ResolveEntityEdgesAsync(
            candidates,
            episode,
            groupId,
            now,
            cancellationToken,
            existingEdgesOverride,
            BuildUniqueNodeList(nodesByExtractedName.Values),
            edgeTypes,
            edgeTypeMap,
            newlyCreatedEdgeUuids,
            inputNodeCount: nodesByExtractedName.Count).ConfigureAwait(false);
    }

    public async Task<List<EntityEdge>> ResolveEntityEdgesAsync(
        IReadOnlyList<EntityEdge> extractedEdges,
        EpisodicNode episode,
        string groupId,
        DateTime now,
        CancellationToken cancellationToken,
        IReadOnlyList<EntityEdge>? existingEdgesOverride = null,
        IReadOnlyList<EntityNode>? nodes = null,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes = null,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap = null,
        HashSet<string>? newlyCreatedEdgeUuids = null,
        Dictionary<string, string>? resolvedEdgeUuidMap = null,
        int? inputNodeCount = null)
    {
        using var activity = GraphitiTelemetry.StartActivity("Resolution.Edges");
        activity?.SetTag("graphiti.group_id", groupId);
        activity?.SetTag("graphiti.input.edges", extractedEdges.Count);
        if (inputNodeCount is { } nodeCount)
        {
            activity?.SetTag("graphiti.input.nodes", nodeCount);
        }

        activity?.SetTag("graphiti.existing_edges.override", existingEdgesOverride is not null);
        activity?.SetTag("graphiti.existing_edges.override_count", existingEdgesOverride?.Count ?? 0);
        var newlyCreatedEdgeStartCount = newlyCreatedEdgeUuids?.Count ?? 0;

        try
        {
            var result = new List<EntityEdge>();
            var resultUuids = new HashSet<string>(StringComparer.Ordinal);
            var seen = new Dictionary<(string SourceUuid, string TargetUuid, string NormalizedFact), EntityEdge>();
            var skippedEdges = 0;
            var nodesByUuid = BuildNodesByUuid(nodes);

            // Python edge_operations.py:439-455 augments uuid_entity_map by DB-fetching any edge
            // endpoint UUID absent from the resolved-node set (scoped by the batch's group_id) before
            // signature resolution, so an override/cross-pair endpoint that is not in `nodes` still
            // contributes its real labels and a custom edge type is not silently lost. Mirror that
            // here; FindEdgeTypeDefinition then falls back to ["Entity"] only when still missing.
            await FetchMissingEndpointNodesAsync(
                extractedEdges,
                nodesByUuid,
                groupId,
                edgeTypeMap,
                cancellationToken).ConfigureAwait(false);

            var attributeSchemaCache = new ConcurrentDictionary<EntityTypeDefinition, StructuredResponseSchema>();

            // Serial preparation pass (mirrors Python resolve_extracted_edges:344-358 fast-path
            // dedup): keep the first occurrence of each (source, target, normalized fact) within the
            // batch, drop later duplicates (attaching only this episode's uuid to the kept edge), and
            // initialise per-edge fields. Ordering is preserved so the concurrent gather and result
            // collection below remain deterministic.
            var prepared = new List<EntityEdge>(extractedEdges.Count);
            foreach (var candidate in extractedEdges)
            {
                if (string.IsNullOrWhiteSpace(candidate.SourceNodeUuid)
                    || string.IsNullOrWhiteSpace(candidate.TargetNodeUuid)
                    || candidate.SourceNodeUuid == candidate.TargetNodeUuid
                    || string.IsNullOrWhiteSpace(candidate.Fact))
                {
                    skippedEdges++;
                    continue;
                }

                var key = (
                    SourceUuid: candidate.SourceNodeUuid,
                    TargetUuid: candidate.TargetNodeUuid,
                    NormalizedFact: NormalizeFact(candidate.Fact));
                if (seen.TryGetValue(key, out var duplicateCandidate))
                {
                    // Python resolve_extracted_edges (edge_operations.py:344-358) keeps the first
                    // occurrence and drops later within-batch duplicates without merging their
                    // episode lists; the kept edge picks up the current episode's uuid during its
                    // own resolution. Attach only the resolution episode here.
                    EdgeMergeHelpers.AddEpisodeIfMissing(duplicateCandidate, episode.Uuid);
                    resolvedEdgeUuidMap?.TryAdd(candidate.Uuid, duplicateCandidate.Uuid);
                    skippedEdges++;
                    continue;
                }

                seen.Add(key, candidate);
                if (string.IsNullOrWhiteSpace(candidate.GroupId))
                {
                    candidate.GroupId = groupId;
                }

                if (candidate.CreatedAt == GraphitiHelpers.DefaultTimestamp)
                {
                    candidate.CreatedAt = now;
                }

                if (candidate.InvalidAt is not null)
                {
                    candidate.ExpiredAt ??= now;
                }

                prepared.Add(candidate);
            }

            // Concurrent resolve pass (mirrors Python's semaphore_gather over resolve_extracted_edge,
            // edge_operations.py:489-509). Each prepared edge runs its independent between-nodes
            // fetch, duplicate/invalidation searches, and LLM resolution. Mutations to existing graph
            // edges shared across candidates (episode attribution, contradiction expiry) are
            // serialised under sharedEdgeMutationLock so real-thread parallelism stays as safe as
            // Python's cooperative single-thread async. SelectAsync preserves input order in the
            // returned array, keeping the result collection deterministic.
            var sharedEdgeMutationLock = new object();
            var outcomes = await ThrottledWork.SelectAsync(
                prepared,
                (candidate, token) => ResolvePreparedEdgeAsync(
                    candidate,
                    episode,
                    groupId,
                    now,
                    sharedEdgeMutationLock,
                    existingEdgesOverride,
                    edgeTypes,
                    edgeTypeMap,
                    nodesByUuid,
                    attributeSchemaCache,
                    token),
                getMaxDegreeOfParallelism?.Invoke() ?? Environment.ProcessorCount,
                cancellationToken).ConfigureAwait(false);

            // Serial collection pass in input order (mirrors edge_operations.py:511-526).
            foreach (var outcome in outcomes)
            {
                resolvedEdgeUuidMap?.TryAdd(outcome.ExtractedEdgeUuid, outcome.ResolvedEdge.Uuid);
                if (outcome.IsNew)
                {
                    newlyCreatedEdgeUuids?.Add(outcome.ResolvedEdge.Uuid);
                }

                EdgeMergeHelpers.AddResolvedEdge(result, resultUuids, outcome.ResolvedEdge);
                foreach (var invalidatedEdge in outcome.InvalidatedEdges)
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

    private async Task<EdgeResolveOutcome> ResolvePreparedEdgeAsync(
        EntityEdge candidate,
        EpisodicNode episode,
        string groupId,
        DateTime now,
        object sharedEdgeMutationLock,
        IReadOnlyList<EntityEdge>? existingEdgesOverride,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap,
        IReadOnlyDictionary<string, EntityNode> nodesByUuid,
        ConcurrentDictionary<EntityTypeDefinition, StructuredResponseSchema> attributeSchemaCache,
        CancellationToken cancellationToken)
    {
        var driver = driverAccessor();
        var normalizedFact = NormalizeFact(candidate.Fact);

        var betweenNodesEdges = await driver.GetEntityEdgesBetweenNodesAsync(
            candidate.SourceNodeUuid,
            candidate.TargetNodeUuid,
            cancellationToken).ConfigureAwait(false);
        // Python edge_operations.py:376-390 appends per-pair override edges onto the
        // between-nodes (valid_edges) list before the duplicate-candidate hybrid search.
        betweenNodesEdges = EdgeMergeHelpers.MergeEdgeOverrides(
            betweenNodesEdges,
            existingEdgesOverride,
            edge => edge.SourceNodeUuid == candidate.SourceNodeUuid && edge.TargetNodeUuid == candidate.TargetNodeUuid);

        // Keep the exact-match scan over the full between-nodes superset for verbatim
        // duplicate detection (Python fast path, edge_operations.py:684-695 operates on the
        // reranked related_edges, but the full superset here is a safe extension that never
        // mislabels a non-duplicate).
        var duplicate = FindDuplicateFact(betweenNodesEdges, normalizedFact);
        if (duplicate is not null)
        {
            // Python resolve_extracted_edge fast path (edge_operations.py:692-694) appends
            // only episode.uuid to the matched existing edge. The edge is shared across
            // candidates, so guard the append under the gather lock.
            RunSharedEdgeMutation(
                sharedEdgeMutationLock,
                () => EdgeMergeHelpers.AddEpisodeIfMissing(duplicate, episode.Uuid));
            return new EdgeResolveOutcome(candidate.Uuid, duplicate, Array.Empty<EntityEdge>(), IsNew: false);
        }

        // Python edge_operations.py:392-405 re-searches the valid_edges via
        // EDGE_HYBRID_SEARCH_RRF (SearchFilters(edge_uuids=[...]), default limit 10,
        // sim_min_score 0.6) and feeds the reranked/truncated result (in relevance order)
        // to the resolve_edge prompt as the EXISTING FACTS / duplicate candidates.
        var relatedEdges = await GetEdgeDuplicateCandidatesAsync(
            candidate,
            groupId,
            betweenNodesEdges,
            existingEdgesOverride,
            cancellationToken).ConfigureAwait(false);

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
            cancellationToken,
            edgeTypes,
            edgeTypeMap,
            nodesByUuid,
            attributeSchemaCache,
            sharedEdgeMutationLock: sharedEdgeMutationLock).ConfigureAwait(false);

        return new EdgeResolveOutcome(
            candidate.Uuid,
            resolvedEdge,
            invalidatedEdges,
            IsNew: resolvedEdge.Uuid == candidate.Uuid);
    }

    private readonly record struct EdgeResolveOutcome(
        string ExtractedEdgeUuid,
        EntityEdge ResolvedEdge,
        IReadOnlyList<EntityEdge> InvalidatedEdges,
        bool IsNew);

    internal static List<EntityEdge> BuildExtractedEdgeCandidates(
        IReadOnlyList<Graphiti.ExtractedEdge> extractedEdges,
        IReadOnlyDictionary<string, EntityNode> nodesByExtractedName,
        IReadOnlyList<EpisodicNode> episodes,
        string groupId,
        DateTime now,
        out int skippedEdges)
    {
        skippedEdges = 0;
        var candidates = new List<EntityEdge>(extractedEdges.Count);
        var seen = new HashSet<(string SourceUuid, string TargetUuid, string NormalizedFact)>();
        if (episodes.Count == 0)
        {
            skippedEdges = extractedEdges.Count;
            return candidates;
        }

        var fallbackEpisode = episodes[0];
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
                // Python uses name=edge_data.relation_type with no fallback (edge_operations.py:302);
                // relation_type is a required field, and ExtractEdges/BuildCombinedEdges now skip any
                // edge lacking one, so there is no empty value to fabricate a "RELATES_TO" name from.
                Name = extracted.RelationType,
                Fact = extracted.Fact,
                Episodes = EpisodeAttribution.MapIndicesToEpisodeUuids(extracted.EpisodeIndices, episodes),
                ValidAt = extracted.ValidAt,
                InvalidAt = extracted.InvalidAt,
                ReferenceTime = extracted.ReferenceTime
                                ?? EpisodeAttribution.ReferenceTimeForFirstValidIndex(
                                    extracted.EpisodeIndices,
                                    episodes,
                                    fallbackEpisode.ValidAt)
            };
            if (candidate.InvalidAt is not null)
            {
                candidate.ExpiredAt = now;
            }

            candidates.Add(candidate);
        }

        return candidates;
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

    public async Task<IReadOnlyList<EntityEdge>> GetEdgeDuplicateCandidatesAsync(
        EntityEdge extractedEdge,
        string groupId,
        IReadOnlyList<EntityEdge> betweenNodesEdges,
        IReadOnlyList<EntityEdge>? existingEdgesOverride,
        CancellationToken cancellationToken)
    {
        // Python edge_operations.py:392-405 builds the duplicate-candidate list by re-searching the
        // valid_edges (between-nodes edges plus per-pair overrides) via EDGE_HYBRID_SEARCH_RRF with
        // SearchFilters(edge_uuids=[valid_edges uuids]). The reranked, limit-10 result (in relevance
        // order) is what the resolve_edge prompt sees as EXISTING FACTS.
        var validEdgeUuids = new List<string>(betweenNodesEdges.Count);
        var validEdgeUuidSet = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < betweenNodesEdges.Count; i++)
        {
            var uuid = betweenNodesEdges[i].Uuid;
            if (validEdgeUuidSet.Add(uuid))
            {
                validEdgeUuids.Add(uuid);
            }
        }

        // Python applies the edge_uuids filter whenever it is not None (search_filters.py:132), so an
        // empty valid_edges list yields zero matches. The shared C# CompiledSearchFilter skips an
        // empty edge_uuids filter (treating it as no filter), which would instead run an unfiltered
        // search; short-circuit here to preserve Python's zero-result semantics.
        if (validEdgeUuids.Count == 0)
        {
            return Array.Empty<EntityEdge>();
        }

        var searchConfig = SearchConfigRecipes.EdgeHybridSearchRrf;
        searchConfig.Limit = SearchUtilities.RelevantSchemaLimit;
        var searchResults = await SearchEngine.SearchAsync(
            clients,
            extractedEdge.Fact,
            new[] { groupId },
            searchConfig,
            new SearchFilters { EdgeUuids = validEdgeUuids },
            driver: driverAccessor(),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Benign override object-substitution: when a search-returned UUID matches an override edge,
        // surface the richer override instance (it may carry attributes/timestamps not yet indexed).
        var overrideLookup = EdgeMergeHelpers.BuildOverrideLookup(
            existingEdgesOverride,
            new HashSet<string>(StringComparer.Ordinal));
        var candidates = new List<EntityEdge>(searchResults.Edges.Count);
        var seenUuids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < searchResults.Edges.Count; i++)
        {
            var edge = searchResults.Edges[i];
            var candidate = overrideLookup.TryGetValue(edge.Uuid, out var overrideEdge)
                ? overrideEdge
                : edge;
            if (seenUuids.Add(candidate.Uuid))
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
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

        // Python edge_operations.py:407-418 runs the invalidation search with a plain
        // SearchFilters() and never injects existing_edges_override into the invalidation
        // candidate list (overrides are merged only into the duplicate/related path,
        // edge_operations.py:376-390). The benign override object-substitution above is kept
        // because it only swaps richer override instances in for search-returned UUIDs.
        return candidates;
    }

    public async Task<(EntityEdge ResolvedEdge, IReadOnlyList<EntityEdge> InvalidatedEdges)> ResolveEdgeWithLlmAsync(
        EntityEdge extractedEdge,
        IReadOnlyList<EntityEdge> relatedEdges,
        IReadOnlyList<EntityEdge> existingEdges,
        EpisodicNode episode,
        DateTime now,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes = null,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap = null,
        IReadOnlyDictionary<string, EntityNode>? nodesByUuid = null,
        ConcurrentDictionary<EntityTypeDefinition, StructuredResponseSchema>? attributeSchemaCache = null,
        IReadOnlyList<EntityNode>? nodes = null,
        object? sharedEdgeMutationLock = null)
    {
        nodesByUuid ??= BuildNodesByUuid(nodes);
        attributeSchemaCache ??= new ConcurrentDictionary<EntityTypeDefinition, StructuredResponseSchema>();

        if (relatedEdges.Count == 0 && existingEdges.Count == 0)
        {
            await ExtractEdgeAttributesAsync(
                extractedEdge,
                episode,
                edgeTypes,
                edgeTypeMap,
                nodesByUuid,
                attributeSchemaCache,
                clearWhenNoSchema: false,
                cancellationToken).ConfigureAwait(false);
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
            // Python resolve_extracted_edge LLM path (edge_operations.py:751-752) appends only
            // episode.uuid to the matched existing (duplicate) edge. This mutates a shared graph
            // edge, so guard the append when running under the concurrent gather.
            RunSharedEdgeMutation(
                sharedEdgeMutationLock,
                () => EdgeMergeHelpers.AddEpisodeIfMissing(resolvedEdge, episode.Uuid));
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

        await ExtractEdgeAttributesAsync(
            resolvedEdge,
            episode,
            edgeTypes,
            edgeTypeMap,
            nodesByUuid,
            attributeSchemaCache,
            clearWhenNoSchema: true,
            cancellationToken).ConfigureAwait(false);

        if (resolvedEdge.Uuid == extractedEdge.Uuid)
        {
            await ExtractEdgeTimestampsAsync(resolvedEdge, episode, now, cancellationToken).ConfigureAwait(false);
        }

        // Expiry of the resolved edge and contradiction handling write invalid_at/expired_at on the
        // resolved edge and on shared invalidation-candidate edges (Python edge_operations.py:820-840
        // and resolve_edge_contradictions:538-573). Serialise under the gather lock to keep these
        // in-place mutations atomic; the returned invalidated set is still collected in input order.
        List<EntityEdge> invalidatedEdges;
        if (sharedEdgeMutationLock is null)
        {
            ExpireResolvedEdgeIfLaterCandidateExists(resolvedEdge, invalidationCandidates, now);
            invalidatedEdges = EdgeMergeHelpers.ResolveEdgeContradictions(resolvedEdge, invalidationCandidates, now);
        }
        else
        {
            lock (sharedEdgeMutationLock)
            {
                ExpireResolvedEdgeIfLaterCandidateExists(resolvedEdge, invalidationCandidates, now);
                invalidatedEdges = EdgeMergeHelpers.ResolveEdgeContradictions(resolvedEdge, invalidationCandidates, now);
            }
        }

        return (resolvedEdge, invalidatedEdges);
    }

    private static void RunSharedEdgeMutation(object? sharedEdgeMutationLock, Action mutation)
    {
        if (sharedEdgeMutationLock is null)
        {
            mutation();
            return;
        }

        lock (sharedEdgeMutationLock)
        {
            mutation();
        }
    }

    private async Task ExtractEdgeAttributesAsync(
        EntityEdge edge,
        EpisodicNode episode,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap,
        IReadOnlyDictionary<string, EntityNode> nodesByUuid,
        ConcurrentDictionary<EntityTypeDefinition, StructuredResponseSchema> attributeSchemaCache,
        bool clearWhenNoSchema,
        CancellationToken cancellationToken)
    {
        if (edgeTypes is null || edgeTypes.Count == 0)
        {
            if (clearWhenNoSchema)
            {
                edge.Attributes.Clear();
            }

            return;
        }

        var edgeType = EntityTypeResolver.FindEdgeTypeDefinition(
            edge,
            nodesByUuid,
            edgeTypes,
            edgeTypeMap);
        if (edgeType is null || edgeType.Attributes.Count == 0)
        {
            if (clearWhenNoSchema)
            {
                edge.Attributes.Clear();
            }

            return;
        }

        var attributeSchema = attributeSchemaCache.GetOrAdd(
            edgeType,
            static type => ExtractionContextBuilder.BuildAttributeResponseSchema(
                type,
                "EdgeAttributeResponse"));

        var response = await llmClient.GenerateResponseAsync(
            ExtractEdgesPrompts.BuildExtractAttributes(
                edge.Fact,
                episode.ValidAt,
                edge.Attributes),
            responseSchema: attributeSchema,
            modelSize: ModelSize.Small,
            groupId: edge.GroupId,
            promptName: "extract_edges.extract_attributes",
            attributeExtraction: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        edge.Attributes = AttributeMerger.ReplaceExtractedAttributes(
            edge.Attributes,
            edgeType,
            response);
    }

    private static Dictionary<string, EntityNode> BuildNodesByUuid(IReadOnlyList<EntityNode>? nodes)
    {
        var nodesByUuid = new Dictionary<string, EntityNode>(nodes?.Count ?? 0, StringComparer.Ordinal);
        if (nodes is null)
        {
            return nodesByUuid;
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            nodesByUuid.TryAdd(nodes[i].Uuid, nodes[i]);
        }

        return nodesByUuid;
    }

    /// <summary>
    /// DB-fetches edge endpoint nodes that are referenced by the extracted edges but absent from the
    /// resolved-node set, adding them to <paramref name="nodesByUuid"/>. Mirrors Python
    /// <c>resolve_extracted_edges</c> (edge_operations.py:439-455): it only matters for node-signature
    /// resolution, so the fetch is skipped when there is no edge-type map to match against. The lookup
    /// is scoped by the batch's group_id (the first edge's group_id, like Python). Endpoints still
    /// missing after the fetch are handled by <c>FindEdgeTypeDefinition</c>'s ["Entity"] fallback.
    /// </summary>
    private async Task FetchMissingEndpointNodesAsync(
        IReadOnlyList<EntityEdge> extractedEdges,
        Dictionary<string, EntityNode> nodesByUuid,
        string groupId,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap,
        CancellationToken cancellationToken)
    {
        if (edgeTypeMap is null || edgeTypeMap.Count == 0 || extractedEdges.Count == 0)
        {
            return;
        }

        HashSet<string>? missing = null;
        foreach (var edge in extractedEdges)
        {
            if (!string.IsNullOrWhiteSpace(edge.SourceNodeUuid) && !nodesByUuid.ContainsKey(edge.SourceNodeUuid))
            {
                (missing ??= new HashSet<string>(StringComparer.Ordinal)).Add(edge.SourceNodeUuid);
            }

            if (!string.IsNullOrWhiteSpace(edge.TargetNodeUuid) && !nodesByUuid.ContainsKey(edge.TargetNodeUuid))
            {
                (missing ??= new HashSet<string>(StringComparer.Ordinal)).Add(edge.TargetNodeUuid);
            }
        }

        if (missing is null)
        {
            return;
        }

        // Python scopes the lookup by the first edge's group_id (edge_operations.py:450).
        var edgeGroupId = string.IsNullOrWhiteSpace(extractedEdges[0].GroupId)
            ? groupId
            : extractedEdges[0].GroupId;
        var fetched = await driverAccessor()
            .GetNodesByUuidsAsync<EntityNode>(missing, edgeGroupId, cancellationToken)
            .ConfigureAwait(false);
        for (var i = 0; i < fetched.Count; i++)
        {
            nodesByUuid.TryAdd(fetched[i].Uuid, fetched[i]);
        }
    }

    private static List<EntityNode> BuildUniqueNodeList(IEnumerable<EntityNode> nodes)
    {
        var result = new List<EntityNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (seen.Add(node.Uuid))
            {
                result.Add(node);
            }
        }

        return result;
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
                ExtractEdgesPrompts.BuildExtractTimestamps(edge.Fact, episode.ValidAt),
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
}
