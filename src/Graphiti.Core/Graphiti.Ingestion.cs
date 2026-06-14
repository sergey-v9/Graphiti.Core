namespace Graphiti.Core;

public sealed partial class Graphiti
{
    /// <summary>
    /// Ingests a single episode and integrates it into the graph. Extracts entities and facts from
    /// <paramref name="episodeBody"/>, resolves them against existing graph data (deduplicating
    /// nodes and edges), invalidates facts the new information contradicts, persists everything, and
    /// optionally updates communities. This is the primary write operation of Graphiti.
    /// </summary>
    /// <param name="name">Display name for the episode.</param>
    /// <param name="episodeBody">Raw content to ingest, interpreted according to <paramref name="source"/>.</param>
    /// <param name="sourceDescription">Free-text description of where the content came from.</param>
    /// <param name="referenceTime">Event time at which the content became true.</param>
    /// <param name="source">The kind of content in <paramref name="episodeBody"/>.</param>
    /// <param name="groupId">Graph partition to write to; the default group is used when omitted.</param>
    /// <param name="uuid">Optional explicit episode UUID.</param>
    /// <param name="updateCommunities">Whether to update community membership after ingestion.</param>
    /// <param name="entityTypes">Custom entity type ontology to guide extraction.</param>
    /// <param name="excludedEntityTypes">Entity type names to exclude from extraction.</param>
    /// <param name="previousEpisodeUuids">Explicit prior episodes to use as context, instead of the recent window.</param>
    /// <param name="edgeTypes">Custom edge (fact) type ontology to guide extraction.</param>
    /// <param name="edgeTypeMap">Allowed edge types per (source type, target type) pair.</param>
    /// <param name="customExtractionInstructions">Extra natural-language guidance appended to extraction prompts.</param>
    /// <param name="saga">Optional saga to associate the episode with (name or saga reference).</param>
    /// <param name="sagaPreviousEpisodeUuid">Explicit predecessor episode within the saga.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The episode plus the nodes, edges, and communities created or updated.</returns>
    public async Task<AddEpisodeResults> AddEpisodeAsync(
        string name,
        string episodeBody,
        string sourceDescription,
        DateTime referenceTime,
        EpisodeType source = EpisodeType.Message,
        string? groupId = null,
        string? uuid = null,
        bool updateCommunities = false,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes = null,
        IReadOnlyList<string>? excludedEntityTypes = null,
        IReadOnlyList<string>? previousEpisodeUuids = null,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes = null,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap = null,
        string? customExtractionInstructions = null,
        object? saga = null,
        string? sagaPreviousEpisodeUuid = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = GraphitiTelemetry.StartActivity("AddEpisode");
        activity?.SetTag("graphiti.episode.source", source.ToString());
        activity?.SetTag("graphiti.episode_body.length", episodeBody.Length);

        try
        {
            var now = UtcNow();
            await using var driverScope = UseGroupDriver(groupId, out groupId);
            GraphitiHelpers.ValidateEntityTypes(entityTypes);
            GraphitiHelpers.ValidateExcludedEntityTypes(excludedEntityTypes, entityTypes);
            activity?.SetTag("graphiti.group_id", groupId);
            GraphitiLog.AddingEpisode(_logger, groupId, source, episodeBody.Length);

            var previousEpisodes = previousEpisodeUuids is null
                ? await RetrieveEpisodesAsync(referenceTime, SearchUtilities.RelevantSchemaLimit, new[] { groupId }, source, cancellationToken: cancellationToken).ConfigureAwait(false)
                : await EpisodicNode.GetByUuidsAsync(Driver, previousEpisodeUuids, cancellationToken).ConfigureAwait(false);

            EpisodicNode episode;
            if (uuid is not null)
            {
                episode = await EpisodicNode.GetByUuidAsync(Driver, uuid, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                episode = CreateEpisode(name, episodeBody, sourceDescription, referenceTime, source, groupId, now, null);
            }

            var (nodes, extractedEdges, attribution) = await _episodeGraphExtractor.ExtractEpisodeGraphAsync(
                episode,
                previousEpisodes,
                entityTypes,
                excludedEntityTypes,
                edgeTypes,
                edgeTypeMap,
                customExtractionInstructions,
                cancellationToken).ConfigureAwait(false);

            var nodeResolution = await _nodeResolutionService.ResolveExtractedNodesAsync(
                nodes,
                groupId,
                episode,
                previousEpisodes,
                entityTypes,
                existingNodesOverride: null,
                cancellationToken).ConfigureAwait(false);
            nodes = nodeResolution.Nodes;
            var newEdgeUuids = new HashSet<string>(StringComparer.Ordinal);
            var entityEdges = await _edgeResolutionService.ResolveExtractedEdgesAsync(
                extractedEdges,
                nodeResolution.NodesByExtractedName,
                episode,
                groupId,
                now,
                cancellationToken,
                edgeTypes: edgeTypes,
                edgeTypeMap: edgeTypeMap,
                newlyCreatedEdgeUuids: newEdgeUuids).ConfigureAwait(false);
            await _attributeExtractionService.ExtractAttributesFromNodesAsync(
                nodes,
                episode,
                previousEpisodes,
                entityTypes,
                EdgeMergeHelpers.FilterEdgesByUuid(entityEdges, newEdgeUuids),
                cancellationToken).ConfigureAwait(false);
            await _entitySummaryService.ExtractEntitySummariesAsync(
                nodes,
                episode,
                previousEpisodes,
                EdgeMergeHelpers.FilterEdgesByUuid(entityEdges, newEdgeUuids),
                cancellationToken).ConfigureAwait(false);
            var episodicEdges = CopyList(
                MaintenanceUtilities.BuildEpisodicEdges(nodes, episode.Uuid, now, attribution));

            episode.EntityEdges = BuildEntityEdgeUuidList(entityEdges);
            if (!_storeRawEpisodeContent)
            {
                episode.Content = string.Empty;
            }

            await SaveBulkWithTelemetryAsync(
                "add_episode.graph",
                groupId,
                new[] { episode },
                episodicEdges,
                nodes,
                entityEdges,
                cancellationToken).ConfigureAwait(false);
            await _sagaService.AssociateAsync(
                saga,
                groupId,
                now,
                episode,
                sagaPreviousEpisodeUuid,
                cancellationToken).ConfigureAwait(false);

            var communities = new List<CommunityNode>();
            var communityEdges = new List<CommunityEdge>();
            if (updateCommunities)
            {
                var updateResults = await _communityService.UpdateCommunitiesForNodesAsync(nodes, Driver, cancellationToken).ConfigureAwait(false);
                communities.AddRange(updateResults.Communities);
                communityEdges.AddRange(updateResults.CommunityEdges);
            }

            var result = new AddEpisodeResults
            {
                Episode = episode,
                EpisodicEdges = episodicEdges,
                Nodes = nodes,
                Edges = entityEdges,
                Communities = communities,
                CommunityEdges = communityEdges
            };
            activity?.SetTag("graphiti.episode.uuid", episode.Uuid);
            activity?.SetTag("graphiti.result.nodes", result.Nodes.Count);
            activity?.SetTag("graphiti.result.edges", result.Edges.Count);
            activity?.SetTag("graphiti.result.episodic_edges", result.EpisodicEdges.Count);
            GraphitiLog.EpisodeAdded(
                _logger,
                groupId,
                result.Episode.Uuid,
                result.Nodes.Count,
                result.Edges.Count,
                result.EpisodicEdges.Count);
            GraphitiTelemetry.SetOk(activity);
            return result;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    /// <summary>
    /// Ingests a single episode using an <see cref="AddEpisodeOptions"/> bundle instead of the long
    /// positional parameter list. This is purely ergonomic: it delegates to the parameter-list
    /// <see cref="AddEpisodeAsync(string, string, string, DateTime, EpisodeType, string?, string?, bool, IReadOnlyDictionary{string, EntityTypeDefinition}?, IReadOnlyList{string}?, IReadOnlyList{string}?, IReadOnlyDictionary{string, EntityTypeDefinition}?, IReadOnlyDictionary{ValueTuple{string, string}, IReadOnlyList{string}}?, string?, object?, string?, CancellationToken)"/>
    /// overload with no change in ingestion, temporal, or wire behavior.
    /// </summary>
    /// <param name="options">The episode content and extraction options.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The episode plus the nodes, edges, and communities created or updated.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    public Task<AddEpisodeResults> AddEpisodeAsync(
        AddEpisodeOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        return AddEpisodeAsync(
            options.Name,
            options.EpisodeBody,
            options.SourceDescription,
            options.ReferenceTime,
            options.Source,
            options.GroupId,
            options.Uuid,
            options.UpdateCommunities,
            options.EntityTypes,
            options.ExcludedEntityTypes,
            options.PreviousEpisodeUuids,
            options.EdgeTypes,
            options.EdgeTypeMap,
            options.CustomExtractionInstructions,
            options.Saga,
            options.SagaPreviousEpisodeUuid,
            cancellationToken);
    }

    /// <summary>
    /// Ingests many episodes in one pass with batched extraction and deduplication. Faster than
    /// repeated <c>AddEpisodeAsync</c> calls, and in the C# port resolved facts from earlier
    /// episodes in the same batch can participate in later episodes' dedupe and temporal
    /// invalidation.
    /// </summary>
    /// <param name="bulkEpisodes">The episodes to ingest.</param>
    /// <param name="groupId">Graph partition to write to; the default group is used when omitted.</param>
    /// <param name="entityTypes">Optional entity ontology used for extraction and attribute hydration.</param>
    /// <param name="excludedEntityTypes">Entity type names to suppress during extraction.</param>
    /// <param name="edgeTypes">Optional edge ontology used for edge type and attribute extraction.</param>
    /// <param name="edgeTypeMap">Allowed edge types by source/target entity type pair.</param>
    /// <param name="customExtractionInstructions">Additional extraction instructions sent to the LLM.</param>
    /// <param name="saga">Optional saga descriptor or node to associate with the ingested episodes.</param>
    /// <param name="cancellationToken">Token used to cancel extraction, persistence, and maintenance work.</param>
    public async Task<AddBulkEpisodeResults> AddEpisodeBulkAsync(
        IReadOnlyList<RawEpisode> bulkEpisodes,
        string? groupId = null,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes = null,
        IReadOnlyList<string>? excludedEntityTypes = null,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes = null,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap = null,
        string? customExtractionInstructions = null,
        object? saga = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = GraphitiTelemetry.StartActivity("AddEpisodeBulk");
        activity?.SetTag("graphiti.episodes.count", bulkEpisodes.Count);

        try
        {
            var now = UtcNow();
            await using var driverScope = UseGroupDriver(groupId, out groupId);
            GraphitiHelpers.ValidateEntityTypes(entityTypes);
            GraphitiHelpers.ValidateExcludedEntityTypes(excludedEntityTypes, entityTypes);
            activity?.SetTag("graphiti.group_id", groupId);
            GraphitiLog.AddingEpisodeBulk(_logger, groupId, bulkEpisodes.Count);
            var episodes = new List<EpisodicNode>(bulkEpisodes.Count);
            foreach (var raw in bulkEpisodes)
            {
                episodes.Add(await GetOrCreateEpisodeAsync(raw, groupId, now, cancellationToken).ConfigureAwait(false));
            }

            await SaveBulkWithTelemetryAsync(
                "add_episode_bulk.episodes",
                groupId,
                episodes,
                Array.Empty<EpisodicEdge>(),
                Array.Empty<EntityNode>(),
                Array.Empty<EntityEdge>(),
                cancellationToken).ConfigureAwait(false);

            var extractedEpisodes = await SelectThrottledAsync(
                episodes,
                async (episode, token) => await ExtractBulkEpisodeAsync(
                    episode,
                    groupId,
                    entityTypes,
                    excludedEntityTypes,
                    edgeTypes,
                    edgeTypeMap,
                    customExtractionInstructions,
                    token).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);

            var nodeBatch = await DedupeBulkNodesAsync(
                extractedEpisodes,
                groupId,
                entityTypes,
                cancellationToken).ConfigureAwait(false);
            var allEpisodicEdges = BuildBulkEpisodicEdges(extractedEpisodes, nodeBatch, now);
            var edgeCandidatesByEpisode = BuildBulkEdgeCandidates(
                extractedEpisodes,
                nodeBatch,
                groupId,
                now);
            foreach (var edges in edgeCandidatesByEpisode.Values)
            {
                MaintenanceUtilities.ResolveEdgePointers(edges, nodeBatch.UuidMap);
            }

            var edgesByEpisode = await DedupeBulkEdgesAsync(
                edgeCandidatesByEpisode,
                extractedEpisodes,
                edgeTypes,
                now,
                cancellationToken).ConfigureAwait(false);
            var finalNodeBatch = await ResolveBulkNodesFinalAsync(
                nodeBatch.NodesByEpisode,
                extractedEpisodes,
                groupId,
                entityTypes,
                cancellationToken).ConfigureAwait(false);

            MaintenanceUtilities.ResolveEdgePointers(allEpisodicEdges, finalNodeBatch.UuidMap);
            foreach (var edges in edgesByEpisode.Values)
            {
                MaintenanceUtilities.ResolveEdgePointers(edges, finalNodeBatch.UuidMap);
            }

            var allNodesByUuid = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
            var allEdgesByUuid = new Dictionary<string, EntityEdge>(StringComparer.Ordinal);
            var finalEdgeUuidPairs = new List<UuidMapPair>();
            var resolvedEdgesByEpisode = new Dictionary<string, List<EntityEdge>>(StringComparer.Ordinal);
            var seenFinalEdgeUuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var extractedEpisode in extractedEpisodes)
            {
                var episode = extractedEpisode.Episode;
                var previousEpisodes = extractedEpisode.PreviousEpisodes;
                var resolvedNodes = finalNodeBatch.NodesByEpisode.TryGetValue(episode.Uuid, out var episodeNodes)
                    ? episodeNodes
                    : new List<EntityNode>();
                var extractedEdges = edgesByEpisode.TryGetValue(episode.Uuid, out var episodeEdges)
                    ? episodeEdges
                    : new List<EntityEdge>();
                var uniqueExtractedEdges = new List<EntityEdge>();
                for (var i = 0; i < extractedEdges.Count; i++)
                {
                    var edge = extractedEdges[i];
                    if (seenFinalEdgeUuids.Add(edge.Uuid))
                    {
                        uniqueExtractedEdges.Add(edge);
                    }
                }

                var newEdgeUuids = new HashSet<string>(StringComparer.Ordinal);
                var edgeUuidMap = new Dictionary<string, string>(StringComparer.Ordinal);
                var entityEdges = await _edgeResolutionService.ResolveEntityEdgesAsync(
                    uniqueExtractedEdges,
                    episode,
                    groupId,
                    now,
                    cancellationToken,
                    CopyDictionaryValues(allEdgesByUuid),
                    finalNodeBatch.AllNodes,
                    edgeTypes,
                    edgeTypeMap,
                    newlyCreatedEdgeUuids: newEdgeUuids,
                    resolvedEdgeUuidMap: edgeUuidMap,
                    inputNodeCount: finalNodeBatch.AllNodes.Count).ConfigureAwait(false);
                foreach (var pair in edgeUuidMap)
                {
                    finalEdgeUuidPairs.Add(new UuidMapPair(pair.Key, pair.Value));
                }

                resolvedEdgesByEpisode[episode.Uuid] = entityEdges;
                await _attributeExtractionService.ExtractAttributesFromNodesAsync(
                    resolvedNodes,
                    episode,
                    previousEpisodes,
                    entityTypes,
                    EdgeMergeHelpers.FilterEdgesByUuid(entityEdges, newEdgeUuids),
                    cancellationToken).ConfigureAwait(false);
                // Bulk summaries must NOT append edge facts. Python _resolve_nodes_and_edges_bulk
                // (graphiti.py:875-886) calls extract_attributes_from_nodes with no edges argument
                // (edges defaults to None -> _build_edges_by_node returns {} -> nodes with short
                // summaries keep them verbatim). Pass an empty edge collection to match.
                await _entitySummaryService.ExtractEntitySummariesAsync(
                    resolvedNodes,
                    episode,
                    previousEpisodes,
                    Array.Empty<EntityEdge>(),
                    cancellationToken).ConfigureAwait(false);
                EdgeMergeHelpers.UpsertCanonicalEdges(allEdgesByUuid, entityEdges);
                for (var i = 0; i < resolvedNodes.Count; i++)
                {
                    allNodesByUuid[resolvedNodes[i].Uuid] = resolvedNodes[i];
                }
            }

            var finalEdgeUuidMap = BuildDirectedUuidMap(finalEdgeUuidPairs);
            foreach (var extractedEpisode in extractedEpisodes)
            {
                var episode = extractedEpisode.Episode;
                var currentEpisodeEdges = edgesByEpisode.TryGetValue(episode.Uuid, out var episodeEdges)
                    ? episodeEdges
                    : new List<EntityEdge>();
                var entityEdgeUuids = BuildEntityEdgeUuidList(currentEpisodeEdges, finalEdgeUuidMap);
                if (resolvedEdgesByEpisode.TryGetValue(episode.Uuid, out var resolvedEpisodeEdges))
                {
                    AddResolvedEntityEdgeUuids(
                        entityEdgeUuids,
                        resolvedEpisodeEdges,
                        currentEpisodeEdges,
                        finalEdgeUuidMap);
                }

                episode.EntityEdges = entityEdgeUuids;
            }

            if (!_storeRawEpisodeContent)
            {
                foreach (var episode in episodes)
                {
                    episode.Content = string.Empty;
                }
            }

            var allEdges = CopyDictionaryValues(allEdgesByUuid);
            await SaveBulkWithTelemetryAsync(
                "add_episode_bulk.graph",
                groupId,
                episodes,
                allEpisodicEdges,
                allNodesByUuid.Values,
                allEdges,
                cancellationToken).ConfigureAwait(false);

            if (saga is not null)
            {
                await _sagaService.AssociateBulkAsync(
                    saga,
                    groupId,
                    now,
                    BuildStableValidAtOrder(episodes),
                    cancellationToken).ConfigureAwait(false);
            }

            var result = new AddBulkEpisodeResults
            {
                Episodes = episodes,
                EpisodicEdges = allEpisodicEdges,
                Nodes = CopyDictionaryValues(allNodesByUuid),
                Edges = allEdges,
                Communities = new List<CommunityNode>(),
                CommunityEdges = new List<CommunityEdge>()
            };
            activity?.SetTag("graphiti.result.nodes", result.Nodes.Count);
            activity?.SetTag("graphiti.result.edges", result.Edges.Count);
            activity?.SetTag("graphiti.result.episodic_edges", result.EpisodicEdges.Count);
            GraphitiLog.EpisodeBulkAdded(
                _logger,
                groupId,
                result.Episodes.Count,
                result.Nodes.Count,
                result.Edges.Count,
                result.EpisodicEdges.Count);
            GraphitiTelemetry.SetOk(activity);
            return result;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private async Task<BulkEpisodeExtraction> ExtractBulkEpisodeAsync(
        EpisodicNode episode,
        string groupId,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes,
        IReadOnlyList<string>? excludedEntityTypes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap,
        string? customExtractionInstructions,
        CancellationToken cancellationToken)
    {
        var previousEpisodes = await RetrieveEpisodesAsync(
            episode.ValidAt,
            MaintenanceUtilities.EpisodeWindowLength,
            new[] { groupId },
            source: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var (nodes, edges, attribution) = await _episodeGraphExtractor.ExtractEpisodeGraphAsync(
            episode,
            previousEpisodes,
            entityTypes,
            excludedEntityTypes,
            edgeTypes,
            edgeTypeMap,
            customExtractionInstructions,
            cancellationToken).ConfigureAwait(false);

        return new BulkEpisodeExtraction(episode, previousEpisodes, nodes, edges, attribution);
    }

    private async Task<BulkNodeDedupeResult> DedupeBulkNodesAsync(
        IReadOnlyList<BulkEpisodeExtraction> extractedEpisodes,
        string groupId,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes,
        CancellationToken cancellationToken)
    {
        var firstPassResults = await SelectThrottledAsync(
            extractedEpisodes.ToList(),
            async (extraction, token) =>
            {
                // Do NOT widen the candidate pool with the whole group. Python
                // dedupe_nodes_bulk first pass (bulk_utils.py:389-400) calls
                // resolve_extracted_nodes with no existing_nodes_override, so candidates come
                // solely from the driver's per-name semantic search
                // (node_operations.py:407-450). Pass null to mirror the FINAL pass and rely on
                // NodeResolutionService's ISearchGraphDriver search for candidates.
                var resolution = await _nodeResolutionService.ResolveExtractedNodesAsync(
                    extraction.Nodes,
                    groupId,
                    extraction.Episode,
                    extraction.PreviousEpisodes,
                    entityTypes,
                    existingNodesOverride: null,
                    token).ConfigureAwait(false);
                return new BulkNodeFirstPass(extraction, resolution);
            },
            cancellationToken).ConfigureAwait(false);

        var canonicalNodes = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        var uuidPairs = new List<UuidMapPair>();
        foreach (var firstPass in firstPassResults)
        {
            foreach (var pair in firstPass.Resolution.UuidMap)
            {
                uuidPairs.Add(new UuidMapPair(pair.Key, pair.Value));
            }

            uuidPairs.AddRange(firstPass.Resolution.DuplicatePairs);
        }

        foreach (var firstPass in firstPassResults)
        {
            foreach (var node in firstPass.Resolution.Nodes)
            {
                if (canonicalNodes.Count == 0)
                {
                    canonicalNodes[node.Uuid] = node;
                    continue;
                }

                if (canonicalNodes.ContainsKey(node.Uuid))
                {
                    continue;
                }

                var normalized = GraphitiHelpers.NormalizeEntityKey(node.Name);
                var exactMatch = FindCanonicalNodeByNormalizedName(canonicalNodes.Values, normalized);
                if (exactMatch is not null)
                {
                    if (!string.Equals(node.Uuid, exactMatch.Uuid, StringComparison.Ordinal))
                    {
                        uuidPairs.Add(new UuidMapPair(node.Uuid, exactMatch.Uuid));
                    }

                    continue;
                }

                var deterministic = EntityNodeDeduplicator.Resolve(
                    new List<EntityNode> { node },
                    CopyDictionaryValues(canonicalNodes),
                    NodeResolutionService.MergeExtractedNode);
                var resolved = deterministic.Nodes.Count == 0 ? node : deterministic.Nodes[0];
                if (string.Equals(resolved.Uuid, node.Uuid, StringComparison.Ordinal))
                {
                    canonicalNodes[node.Uuid] = node;
                    continue;
                }

                canonicalNodes.TryAdd(resolved.Uuid, resolved);
                uuidPairs.Add(new UuidMapPair(node.Uuid, resolved.Uuid));
            }
        }

        var compressedMap = BuildDirectedUuidMap(uuidPairs);
        var nodesByEpisode = new Dictionary<string, List<EntityNode>>(StringComparer.Ordinal);
        var nodesByExtractedNameByEpisode = new Dictionary<string, Dictionary<string, EntityNode>>(StringComparer.Ordinal);
        var attributionByEpisode = new Dictionary<string, Dictionary<string, IReadOnlyList<int>>>(StringComparer.Ordinal);
        foreach (var firstPass in firstPassResults)
        {
            var episodeUuid = firstPass.Extraction.Episode.Uuid;
            var dedupedNodes = new List<EntityNode>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in firstPass.Resolution.Nodes)
            {
                var canonicalUuid = ResolveUuid(node.Uuid, compressedMap);
                if (!seen.Add(canonicalUuid))
                {
                    continue;
                }

                dedupedNodes.Add(canonicalNodes.TryGetValue(canonicalUuid, out var canonicalNode)
                    ? canonicalNode
                    : node);
            }

            nodesByEpisode[episodeUuid] = dedupedNodes;
            var nodesByExtractedName = new Dictionary<string, EntityNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var extractedNode in firstPass.Extraction.Nodes)
            {
                if (!firstPass.Resolution.NodesByExtractedName.TryGetValue(extractedNode.Name, out var resolvedNode))
                {
                    continue;
                }

                var canonicalUuid = ResolveUuid(resolvedNode.Uuid, compressedMap);
                nodesByExtractedName[extractedNode.Name] = canonicalNodes.TryGetValue(canonicalUuid, out var canonicalNode)
                    ? canonicalNode
                    : resolvedNode;
            }

            nodesByExtractedNameByEpisode[episodeUuid] = nodesByExtractedName;
            attributionByEpisode[episodeUuid] = firstPass.Extraction.Attribution;
        }

        return new BulkNodeDedupeResult(
            nodesByEpisode,
            nodesByExtractedNameByEpisode,
            attributionByEpisode,
            compressedMap);
    }

    private static List<EpisodicEdge> BuildBulkEpisodicEdges(
        IReadOnlyList<BulkEpisodeExtraction> extractedEpisodes,
        BulkNodeDedupeResult nodeBatch,
        DateTime now)
    {
        var episodicEdges = new List<EpisodicEdge>();
        foreach (var extraction in extractedEpisodes)
        {
            if (!nodeBatch.NodesByEpisode.TryGetValue(extraction.Episode.Uuid, out var nodes))
            {
                continue;
            }

            nodeBatch.AttributionByEpisode.TryGetValue(extraction.Episode.Uuid, out var attribution);
            episodicEdges.AddRange(MaintenanceUtilities.BuildEpisodicEdges(
                nodes,
                extraction.Episode.Uuid,
                now,
                attribution));
        }

        return episodicEdges;
    }

    private static Dictionary<string, List<EntityEdge>> BuildBulkEdgeCandidates(
        IReadOnlyList<BulkEpisodeExtraction> extractedEpisodes,
        BulkNodeDedupeResult nodeBatch,
        string groupId,
        DateTime now)
    {
        var edgesByEpisode = new Dictionary<string, List<EntityEdge>>(StringComparer.Ordinal);
        foreach (var extraction in extractedEpisodes)
        {
            if (!nodeBatch.NodesByExtractedNameByEpisode.TryGetValue(
                    extraction.Episode.Uuid,
                    out var nodesByExtractedName))
            {
                edgesByEpisode[extraction.Episode.Uuid] = new List<EntityEdge>();
                continue;
            }

            edgesByEpisode[extraction.Episode.Uuid] = EdgeResolutionService.BuildExtractedEdgeCandidates(
                extraction.Edges,
                nodesByExtractedName,
                new[] { extraction.Episode },
                groupId,
                now,
                out _);
        }

        return edgesByEpisode;
    }

    private async Task<Dictionary<string, List<EntityEdge>>> DedupeBulkEdgesAsync(
        Dictionary<string, List<EntityEdge>> extractedEdgesByEpisode,
        IReadOnlyList<BulkEpisodeExtraction> extractedEpisodes,
        IReadOnlyDictionary<string, EntityTypeDefinition>? edgeTypes,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var allEdges = new List<EntityEdge>();
        foreach (var edges in extractedEdgesByEpisode.Values)
        {
            allEdges.AddRange(edges);
        }

        await MaintenanceUtilities.CreateEntityEdgeEmbeddingsAsync(
            Embedder,
            allEdges,
            cancellationToken).ConfigureAwait(false);

        var duplicatePairs = new List<UuidMapPair>();
        foreach (var extraction in extractedEpisodes)
        {
            if (!extractedEdgesByEpisode.TryGetValue(extraction.Episode.Uuid, out var edges))
            {
                continue;
            }

            for (var i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                var candidates = FindBulkEdgeDedupeCandidates(edge, allEdges);
                if (candidates.Count == 0)
                {
                    continue;
                }

                var normalizedFact = EdgeResolutionService.NormalizeFact(edge.Fact);
                var exactDuplicate = EdgeResolutionService.FindDuplicateFact(candidates, normalizedFact);
                if (exactDuplicate is not null)
                {
                    duplicatePairs.Add(new UuidMapPair(edge.Uuid, exactDuplicate.Uuid));
                    continue;
                }

                var (resolvedEdge, _) = await _edgeResolutionService.ResolveEdgeWithLlmAsync(
                    edge,
                    candidates,
                    candidates,
                    extraction.Episode,
                    cancellationToken,
                    edgeTypes: edgeTypes).ConfigureAwait(false);
                if (!string.Equals(resolvedEdge.Uuid, edge.Uuid, StringComparison.Ordinal))
                {
                    duplicatePairs.Add(new UuidMapPair(edge.Uuid, resolvedEdge.Uuid));
                }
            }
        }

        var compressedMap = CompressUuidMap(duplicatePairs);
        var edgeByUuid = new Dictionary<string, EntityEdge>(StringComparer.Ordinal);
        foreach (var edge in allEdges)
        {
            edgeByUuid.TryAdd(edge.Uuid, edge);
        }

        foreach (var edge in allEdges)
        {
            var canonicalUuid = ResolveUuid(edge.Uuid, compressedMap);
            if (!string.Equals(canonicalUuid, edge.Uuid, StringComparison.Ordinal)
                && edgeByUuid.TryGetValue(canonicalUuid, out var canonicalEdge))
            {
                foreach (var episodeUuid in edge.Episodes)
                {
                    EdgeMergeHelpers.AddEpisodeIfMissing(canonicalEdge, episodeUuid);
                }
            }
        }

        var edgesByEpisode = new Dictionary<string, List<EntityEdge>>(StringComparer.Ordinal);
        foreach (var extraction in extractedEpisodes)
        {
            var episodeUuid = extraction.Episode.Uuid;
            var sourceEdges = extractedEdgesByEpisode.TryGetValue(episodeUuid, out var edges)
                ? edges
                : new List<EntityEdge>();
            var mappedEdges = new List<EntityEdge>(sourceEdges.Count);
            foreach (var edge in sourceEdges)
            {
                var canonicalUuid = ResolveUuid(edge.Uuid, compressedMap);
                mappedEdges.Add(edgeByUuid.TryGetValue(canonicalUuid, out var canonicalEdge)
                    ? canonicalEdge
                    : edge);
            }

            edgesByEpisode[episodeUuid] = mappedEdges;
        }

        return edgesByEpisode;
    }

    private async Task<FinalBulkNodeResolution> ResolveBulkNodesFinalAsync(
        IReadOnlyDictionary<string, List<EntityNode>> nodesByEpisode,
        IReadOnlyList<BulkEpisodeExtraction> extractedEpisodes,
        string groupId,
        IReadOnlyDictionary<string, EntityTypeDefinition>? entityTypes,
        CancellationToken cancellationToken)
    {
        var nodesByUuid = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        foreach (var nodes in nodesByEpisode.Values)
        {
            foreach (var node in nodes)
            {
                nodesByUuid.TryAdd(node.Uuid, node);
            }
        }

        var uniqueNodesByEpisode = new Dictionary<string, List<EntityNode>>(StringComparer.Ordinal);
        var seenNodeUuids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extraction in extractedEpisodes)
        {
            var episodeUuid = extraction.Episode.Uuid;
            var uniqueNodes = new List<EntityNode>();
            if (nodesByEpisode.TryGetValue(episodeUuid, out var nodes))
            {
                foreach (var node in nodes)
                {
                    if (seenNodeUuids.Add(node.Uuid))
                    {
                        uniqueNodes.Add(node);
                    }
                }
            }

            uniqueNodesByEpisode[episodeUuid] = uniqueNodes;
        }

        var resolutionResults = await SelectThrottledAsync(
            extractedEpisodes.ToList(),
            async (extraction, token) =>
            {
                var nodes = uniqueNodesByEpisode[extraction.Episode.Uuid];
                var resolution = await _nodeResolutionService.ResolveExtractedNodesAsync(
                    nodes,
                    groupId,
                    extraction.Episode,
                    extraction.PreviousEpisodes,
                    entityTypes,
                    existingNodesOverride: null,
                    token).ConfigureAwait(false);
                return new BulkNodeFirstPass(extraction, resolution);
            },
            cancellationToken).ConfigureAwait(false);

        var uuidPairs = new List<UuidMapPair>();
        var finalNodesByUuid = new Dictionary<string, EntityNode>(nodesByUuid, StringComparer.Ordinal);
        foreach (var result in resolutionResults)
        {
            foreach (var pair in result.Resolution.UuidMap)
            {
                uuidPairs.Add(new UuidMapPair(pair.Key, pair.Value));
            }

            foreach (var node in result.Resolution.Nodes)
            {
                finalNodesByUuid[node.Uuid] = node;
            }
        }

        var uuidMap = BuildDirectedUuidMap(uuidPairs);
        var updatedNodesByEpisode = new Dictionary<string, List<EntityNode>>(StringComparer.Ordinal);
        foreach (var pair in uniqueNodesByEpisode)
        {
            var nodes = new List<EntityNode>(pair.Value.Count);
            foreach (var node in pair.Value)
            {
                var resolvedUuid = ResolveUuid(node.Uuid, uuidMap);
                nodes.Add(finalNodesByUuid.TryGetValue(resolvedUuid, out var resolvedNode)
                    ? resolvedNode
                    : node);
            }

            updatedNodesByEpisode[pair.Key] = nodes;
        }

        return new FinalBulkNodeResolution(
            updatedNodesByEpisode,
            CopyDictionaryValues(finalNodesByUuid),
            uuidMap);
    }

    /// <summary>
    /// Adds a single fact directly as a source-entity → edge → target-entity triplet, bypassing LLM
    /// extraction. The nodes and edge are deduplicated against existing graph data, embeddings are
    /// generated as needed, and contradicted facts are invalidated.
    /// </summary>
    /// <param name="sourceNode">The source entity of the fact.</param>
    /// <param name="edge">The fact (entity edge) connecting the two entities.</param>
    /// <param name="targetNode">The target entity of the fact.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task<AddTripletResults> AddTripletAsync(
        EntityNode sourceNode,
        EntityEdge edge,
        EntityNode targetNode,
        CancellationToken cancellationToken = default)
    {
        using var activity = GraphitiTelemetry.StartActivity("AddTriplet");
        activity?.SetTag("graphiti.edge.name", edge.Name);
        activity?.SetTag("graphiti.source_node.uuid", sourceNode.Uuid);
        activity?.SetTag("graphiti.target_node.uuid", targetNode.Uuid);
        try
        {
            var now = UtcNow();

            if (sourceNode.NameEmbedding is null)
            {
                await sourceNode.GenerateNameEmbeddingAsync(Embedder, cancellationToken).ConfigureAwait(false);
            }

            if (targetNode.NameEmbedding is null)
            {
                await targetNode.GenerateNameEmbeddingAsync(Embedder, cancellationToken).ConfigureAwait(false);
            }

            if (edge.FactEmbedding is null)
            {
                await edge.GenerateEmbeddingAsync(Embedder, cancellationToken).ConfigureAwait(false);
            }

            var resolvedSource = await _nodeResolutionService.ResolveTripletNodeAsync(sourceNode, cancellationToken).ConfigureAwait(false);
            var resolvedTarget = await _nodeResolutionService.ResolveTripletNodeAsync(targetNode, cancellationToken).ConfigureAwait(false);

            edge.SourceNodeUuid = resolvedSource.Uuid;
            edge.TargetNodeUuid = resolvedTarget.Uuid;
            try
            {
                var existingEdge = await EntityEdge.GetByUuidAsync(Driver, edge.Uuid, cancellationToken).ConfigureAwait(false);
                if (existingEdge.SourceNodeUuid != edge.SourceNodeUuid || existingEdge.TargetNodeUuid != edge.TargetNodeUuid)
                {
                    edge.Uuid = GraphitiHelpers.NewUuid();
                }
            }
            catch (EdgeNotFoundException)
            {
            }

            var betweenNodesEdges = await Driver.GetEntityEdgesBetweenNodesAsync(
                edge.SourceNodeUuid,
                edge.TargetNodeUuid,
                cancellationToken).ConfigureAwait(false);

            var relatedEdges = await _edgeResolutionService.GetEdgeDuplicateCandidatesAsync(
                edge,
                edge.GroupId,
                betweenNodesEdges,
                null,
                cancellationToken).ConfigureAwait(false);

            var edges = new List<EntityEdge>();
            var existingEdges = await _edgeResolutionService.GetEdgeInvalidationCandidatesAsync(
                edge,
                edge.GroupId,
                relatedEdges,
                null,
                cancellationToken).ConfigureAwait(false);
            var syntheticEpisode = new EpisodicNode
            {
                Name = string.Empty,
                Source = EpisodeType.Text,
                SourceDescription = string.Empty,
                Content = string.Empty,
                GroupId = edge.GroupId,
                CreatedAt = now,
                ValidAt = edge.ValidAt ?? now
            };
            var (resolvedEdge, invalidatedEdges) = await _edgeResolutionService.ResolveEdgeWithLlmAsync(
                edge,
                relatedEdges,
                existingEdges,
                syntheticEpisode,
                cancellationToken,
                nodes: new[] { resolvedSource, resolvedTarget }).ConfigureAwait(false);
            edges.Add(resolvedEdge);
            edges.AddRange(invalidatedEdges);

            await SaveBulkWithTelemetryAsync(
                "add_triplet.graph",
                edge.GroupId,
                Array.Empty<EpisodicNode>(),
                Array.Empty<EpisodicEdge>(),
                new[] { resolvedSource, resolvedTarget },
                edges,
                cancellationToken).ConfigureAwait(false);

            var result = new AddTripletResults
            {
                Nodes = new List<EntityNode> { resolvedSource, resolvedTarget },
                Edges = edges
            };
            GraphitiTelemetry.SetOk(activity);
            return result;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private static EpisodicNode CreateEpisode(
        string name,
        string episodeBody,
        string sourceDescription,
        DateTime referenceTime,
        EpisodeType source,
        string groupId,
        DateTime now,
        string? uuid)
    {
        return new EpisodicNode
        {
            Uuid = uuid ?? GraphitiHelpers.NewUuid(),
            Name = name,
            GroupId = groupId,
            Labels = new List<string>(),
            Source = source,
            Content = episodeBody,
            SourceDescription = sourceDescription,
            CreatedAt = now,
            ValidAt = GraphitiHelpers.EnsureUtc(referenceTime),
            EntityEdges = new List<string>()
        };
    }

    private async Task<EpisodicNode> GetOrCreateEpisodeAsync(
        RawEpisode raw,
        string groupId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (raw.Uuid is not null)
        {
            return await EpisodicNode.GetByUuidAsync(Driver, raw.Uuid, cancellationToken).ConfigureAwait(false);
        }

        return CreateEpisode(
            raw.Name,
            raw.Content,
            raw.SourceDescription,
            raw.ReferenceTime,
            raw.Source,
            groupId,
            now,
            raw.Uuid);
    }

    private sealed record BulkEpisodeExtraction(
        EpisodicNode Episode,
        IReadOnlyList<EpisodicNode> PreviousEpisodes,
        List<EntityNode> Nodes,
        List<ExtractedEdge> Edges,
        Dictionary<string, IReadOnlyList<int>> Attribution);

    private sealed record BulkNodeFirstPass(
        BulkEpisodeExtraction Extraction,
        EntityNodeResolution Resolution);

    private sealed record BulkNodeDedupeResult(
        Dictionary<string, List<EntityNode>> NodesByEpisode,
        Dictionary<string, Dictionary<string, EntityNode>> NodesByExtractedNameByEpisode,
        Dictionary<string, Dictionary<string, IReadOnlyList<int>>> AttributionByEpisode,
        Dictionary<string, string> UuidMap);

    private sealed record FinalBulkNodeResolution(
        Dictionary<string, List<EntityNode>> NodesByEpisode,
        List<EntityNode> AllNodes,
        Dictionary<string, string> UuidMap);

    private static List<T> CopyList<T>(IReadOnlyList<T> source)
    {
        var copy = new List<T>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            copy.Add(source[i]);
        }

        return copy;
    }

    private static EntityNode? FindCanonicalNodeByNormalizedName(
        IEnumerable<EntityNode> nodes,
        string normalizedName)
    {
        foreach (var node in nodes)
        {
            if (GraphitiHelpers.NormalizeEntityKey(node.Name) == normalizedName)
            {
                return node;
            }
        }

        return null;
    }

    private static List<string> BuildEntityEdgeUuidList(List<EntityEdge> edges)
    {
        var uuids = new List<string>(edges.Count);
        for (var i = 0; i < edges.Count; i++)
        {
            uuids.Add(edges[i].Uuid);
        }

        return uuids;
    }

    private static List<string> BuildEntityEdgeUuidList(
        List<EntityEdge> edges,
        IReadOnlyDictionary<string, string> uuidMap)
    {
        var uuids = new List<string>(edges.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < edges.Count; i++)
        {
            var uuid = ResolveUuid(edges[i].Uuid, uuidMap);
            if (seen.Add(uuid))
            {
                uuids.Add(uuid);
            }
        }

        return uuids;
    }

    private static void AddResolvedEntityEdgeUuids(
        List<string> uuids,
        List<EntityEdge> edges,
        List<EntityEdge> currentEpisodeEdges,
        IReadOnlyDictionary<string, string> uuidMap)
    {
        var seen = new HashSet<string>(uuids, StringComparer.Ordinal);
        var invalidAtValuesForCurrentEpisode = new HashSet<DateTime>();
        for (var i = 0; i < currentEpisodeEdges.Count; i++)
        {
            if (currentEpisodeEdges[i].ValidAt is { } validAt)
            {
                invalidAtValuesForCurrentEpisode.Add(GraphitiHelpers.EnsureUtc(validAt));
            }
        }

        for (var i = 0; i < edges.Count; i++)
        {
            if (edges[i].ExpiredAt is not null
                && edges[i].InvalidAt is { } invalidAt
                && !invalidAtValuesForCurrentEpisode.Contains(GraphitiHelpers.EnsureUtc(invalidAt)))
            {
                continue;
            }

            var uuid = ResolveUuid(edges[i].Uuid, uuidMap);
            if (seen.Add(uuid))
            {
                uuids.Add(uuid);
            }
        }
    }

    private static List<TValue> CopyDictionaryValues<TKey, TValue>(Dictionary<TKey, TValue> source)
        where TKey : notnull
    {
        var values = new List<TValue>(source.Count);
        foreach (var value in source.Values)
        {
            values.Add(value);
        }

        return values;
    }

    private static List<EpisodicNode> BuildStableValidAtOrder(List<EpisodicNode> episodes)
    {
        var indexed = new List<IndexedEpisode>(episodes.Count);
        for (var i = 0; i < episodes.Count; i++)
        {
            indexed.Add(new IndexedEpisode(episodes[i], i));
        }

        indexed.Sort(static (left, right) =>
        {
            var result = left.Episode.ValidAt.CompareTo(right.Episode.ValidAt);
            return result != 0 ? result : left.Index.CompareTo(right.Index);
        });

        var sorted = new List<EpisodicNode>(indexed.Count);
        for (var i = 0; i < indexed.Count; i++)
        {
            sorted.Add(indexed[i].Episode);
        }

        return sorted;
    }

    private readonly record struct IndexedEpisode(EpisodicNode Episode, int Index);

    private static List<EntityEdge> FindBulkEdgeDedupeCandidates(
        EntityEdge edge,
        List<EntityEdge> allEdges)
    {
        var candidates = new List<EntityEdge>();
        for (var i = 0; i < allEdges.Count; i++)
        {
            var candidate = allEdges[i];
            if (string.Equals(candidate.Uuid, edge.Uuid, StringComparison.Ordinal)
                || !string.Equals(candidate.SourceNodeUuid, edge.SourceNodeUuid, StringComparison.Ordinal)
                || !string.Equals(candidate.TargetNodeUuid, edge.TargetNodeUuid, StringComparison.Ordinal))
            {
                continue;
            }

            if (FactsHaveWordOverlap(edge.Fact, candidate.Fact)
                || SearchUtilities.CalculateCosineSimilarity(edge.FactEmbedding, candidate.FactEmbedding) >= 0.6f)
            {
                candidates.Add(candidate);
            }
        }

        return candidates;
    }

    private static bool FactsHaveWordOverlap(string left, string right)
    {
        var leftWords = left.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (leftWords.Length == 0)
        {
            return false;
        }

        var words = new HashSet<string>(leftWords, StringComparer.OrdinalIgnoreCase);
        foreach (var word in right.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (words.Contains(word))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, string> BuildDirectedUuidMap(IEnumerable<UuidMapPair> pairs)
    {
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);

        string Find(string uuid)
        {
            if (!parent.TryGetValue(uuid, out _))
            {
                parent[uuid] = uuid;
            }

            var root = uuid;
            while (!string.Equals(parent[root], root, StringComparison.Ordinal))
            {
                root = parent[root];
            }

            while (!string.Equals(parent[uuid], root, StringComparison.Ordinal))
            {
                var next = parent[uuid];
                parent[uuid] = root;
                uuid = next;
            }

            return root;
        }

        foreach (var pair in pairs)
        {
            parent.TryAdd(pair.SourceUuid, pair.SourceUuid);
            parent.TryAdd(pair.TargetUuid, pair.TargetUuid);
            parent[Find(pair.SourceUuid)] = Find(pair.TargetUuid);
        }

        var keys = parent.Keys.ToList();
        var uuidMap = new Dictionary<string, string>(keys.Count, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            uuidMap[key] = Find(key);
        }

        return uuidMap;
    }

    private static Dictionary<string, string> CompressUuidMap(IEnumerable<UuidMapPair> pairs)
    {
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);

        string Find(string uuid)
        {
            if (!parent.TryGetValue(uuid, out _))
            {
                parent[uuid] = uuid;
            }

            if (!string.Equals(parent[uuid], uuid, StringComparison.Ordinal))
            {
                parent[uuid] = Find(parent[uuid]);
            }

            return parent[uuid];
        }

        void Union(string left, string right)
        {
            var leftRoot = Find(left);
            var rightRoot = Find(right);
            if (string.Equals(leftRoot, rightRoot, StringComparison.Ordinal))
            {
                return;
            }

            if (string.CompareOrdinal(leftRoot, rightRoot) < 0)
            {
                parent[rightRoot] = leftRoot;
            }
            else
            {
                parent[leftRoot] = rightRoot;
            }
        }

        foreach (var pair in pairs)
        {
            Union(pair.SourceUuid, pair.TargetUuid);
        }

        var keys = parent.Keys.ToList();
        var uuidMap = new Dictionary<string, string>(keys.Count, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            uuidMap[key] = Find(key);
        }

        return uuidMap;
    }

    private static string ResolveUuid(string uuid, IReadOnlyDictionary<string, string> uuidMap) =>
        uuidMap.TryGetValue(uuid, out var resolvedUuid) ? resolvedUuid : uuid;
}

/// <summary>
/// Options bundle for <see cref="Graphiti.AddEpisodeAsync(AddEpisodeOptions, CancellationToken)"/>.
/// Carries the same inputs as the positional-parameter overload of <c>AddEpisodeAsync</c>; property
/// defaults mirror that overload's parameter defaults exactly, so the two paths are behaviorally
/// identical.
/// </summary>
public sealed class AddEpisodeOptions
{
    /// <summary>Display name for the episode.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Raw content to ingest, interpreted according to <see cref="Source"/>.</summary>
    public string EpisodeBody { get; set; } = string.Empty;

    /// <summary>Free-text description of where the content came from.</summary>
    public string SourceDescription { get; set; } = string.Empty;

    /// <summary>Event time at which the content became true.</summary>
    public DateTime ReferenceTime { get; set; } = GraphitiHelpers.DefaultTimestamp;

    /// <summary>The kind of content in <see cref="EpisodeBody"/>.</summary>
    public EpisodeType Source { get; set; } = EpisodeType.Message;

    /// <summary>Graph partition to write to; the default group is used when omitted.</summary>
    public string? GroupId { get; set; }

    /// <summary>Optional explicit episode UUID.</summary>
    public string? Uuid { get; set; }

    /// <summary>Whether to update community membership after ingestion.</summary>
    public bool UpdateCommunities { get; set; }

    /// <summary>Custom entity type ontology to guide extraction.</summary>
    public IReadOnlyDictionary<string, EntityTypeDefinition>? EntityTypes { get; set; }

    /// <summary>Entity type names to exclude from extraction.</summary>
    public IReadOnlyList<string>? ExcludedEntityTypes { get; set; }

    /// <summary>Explicit prior episodes to use as context, instead of the recent window.</summary>
    public IReadOnlyList<string>? PreviousEpisodeUuids { get; set; }

    /// <summary>Custom edge (fact) type ontology to guide extraction.</summary>
    public IReadOnlyDictionary<string, EntityTypeDefinition>? EdgeTypes { get; set; }

    /// <summary>Allowed edge types per (source type, target type) pair.</summary>
    public IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? EdgeTypeMap { get; set; }

    /// <summary>Extra natural-language guidance appended to extraction prompts.</summary>
    public string? CustomExtractionInstructions { get; set; }

    /// <summary>
    /// Optional saga to associate the episode with. Accepts a saga <b>name</b> (<see cref="string"/>)
    /// or an existing <c>SagaNode</c>; any other runtime type throws <see cref="ArgumentException"/>
    /// during ingestion. For the common by-name case prefer the type-safe <see cref="SagaName"/>.
    /// </summary>
    public object? Saga { get; set; }

    /// <summary>
    /// Type-safe convenience for associating the episode with a saga by name. Setting this assigns
    /// <see cref="Saga"/>; reading returns the value only when <see cref="Saga"/> holds a string
    /// (null when it holds a <c>SagaNode</c> or is unset).
    /// </summary>
    public string? SagaName
    {
        get => Saga as string;
        set => Saga = value;
    }

    /// <summary>Explicit predecessor episode within the saga.</summary>
    public string? SagaPreviousEpisodeUuid { get; set; }
}
