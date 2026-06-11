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
                newlyCreatedEdgeUuids: newEdgeUuids).ConfigureAwait(false);
            await _attributeExtractionService.ExtractAttributesFromEdgesAsync(
                entityEdges,
                nodes,
                episode,
                edgeTypes,
                edgeTypeMap,
                cancellationToken).ConfigureAwait(false);
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
    /// Ingests many episodes in one pass with batched extraction and deduplication. Faster than
    /// repeated <c>AddEpisodeAsync</c> calls, but applies cross-episode temporal invalidation less
    /// aggressively.
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

            var knownCandidateNodes = CopyList(await Driver.GetNodesByGroupIdsAsync<EntityNode>(
                new[] { groupId },
                cancellationToken: cancellationToken).ConfigureAwait(false));
            var knownCandidateUuids = BuildEntityNodeUuidSet(knownCandidateNodes);
            var allNodesByUuid = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
            var allEdgesByUuid = new Dictionary<string, EntityEdge>(StringComparer.Ordinal);
            var allEpisodicEdges = new List<EpisodicEdge>();
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

            foreach (var extractedEpisode in extractedEpisodes)
            {
                var episode = extractedEpisode.Episode;
                var nodes = extractedEpisode.Nodes;
                var extractedEdges = extractedEpisode.Edges;
                var attribution = extractedEpisode.Attribution;
                var previousEpisodes = extractedEpisode.PreviousEpisodes;

                var nodeResolution = await _nodeResolutionService.ResolveExtractedNodesAsync(
                    nodes,
                    groupId,
                    episode,
                    previousEpisodes,
                    entityTypes,
                    knownCandidateNodes,
                    cancellationToken).ConfigureAwait(false);
                var resolvedNodes = nodeResolution.Nodes;
                foreach (var node in resolvedNodes)
                {
                    allNodesByUuid[node.Uuid] = node;
                    if (knownCandidateUuids.Add(node.Uuid))
                    {
                        knownCandidateNodes.Add(node);
                    }
                }

                var newEdgeUuids = new HashSet<string>(StringComparer.Ordinal);
                var entityEdges = await _edgeResolutionService.ResolveExtractedEdgesAsync(
                    extractedEdges,
                    nodeResolution.NodesByExtractedName,
                    episode,
                    groupId,
                    now,
                    cancellationToken,
                    CopyDictionaryValues(allEdgesByUuid),
                    newlyCreatedEdgeUuids: newEdgeUuids).ConfigureAwait(false);
                await _attributeExtractionService.ExtractAttributesFromEdgesAsync(
                    entityEdges,
                    resolvedNodes,
                    episode,
                    edgeTypes,
                    edgeTypeMap,
                    cancellationToken).ConfigureAwait(false);
                await _attributeExtractionService.ExtractAttributesFromNodesAsync(
                    resolvedNodes,
                    episode,
                    previousEpisodes,
                    entityTypes,
                    EdgeMergeHelpers.FilterEdgesByUuid(entityEdges, newEdgeUuids),
                    cancellationToken).ConfigureAwait(false);
                await _entitySummaryService.ExtractEntitySummariesAsync(
                    resolvedNodes,
                    episode,
                    previousEpisodes,
                    EdgeMergeHelpers.FilterEdgesByUuid(entityEdges, newEdgeUuids),
                    cancellationToken).ConfigureAwait(false);
                episode.EntityEdges = BuildEntityEdgeUuidList(entityEdges);
                EdgeMergeHelpers.UpsertCanonicalEdges(allEdgesByUuid, entityEdges);
                allEpisodicEdges.AddRange(MaintenanceUtilities.BuildEpisodicEdges(resolvedNodes, episode.Uuid, now, attribution));
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

            var relatedEdges = await Driver.GetEntityEdgesBetweenNodesAsync(
                edge.SourceNodeUuid,
                edge.TargetNodeUuid,
                cancellationToken).ConfigureAwait(false);
            var normalizedFact = EdgeResolutionService.NormalizeFact(edge.Fact);
            var duplicate = EdgeResolutionService.FindDuplicateFact(relatedEdges, normalizedFact);
            var edges = new List<EntityEdge>();
            if (duplicate is not null)
            {
                edge = duplicate;
                edges.Add(edge);
            }
            else
            {
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
                    now,
                    cancellationToken).ConfigureAwait(false);
                edges.Add(resolvedEdge);
                edges.AddRange(invalidatedEdges);
            }

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

    private static List<T> CopyList<T>(IReadOnlyList<T> source)
    {
        var copy = new List<T>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            copy.Add(source[i]);
        }

        return copy;
    }

    private static HashSet<string> BuildEntityNodeUuidSet(List<EntityNode> nodes)
    {
        var uuids = new HashSet<string>(nodes.Count, StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            uuids.Add(nodes[i].Uuid);
        }

        return uuids;
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
}
