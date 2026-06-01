namespace Graphiti.Core;

public sealed partial class Graphiti
{
    /// <summary>
    /// Removes an episode and cleans up the graph elements it solely produced: mention edges, and any
    /// entities or facts that were only supported by this episode. Data still referenced by other
    /// episodes is retained.
    /// </summary>
    public async Task RemoveEpisodeAsync(string episodeUuid, CancellationToken cancellationToken = default)
    {
        using var activity = GraphitiTelemetry.StartActivity("RemoveEpisode");
        activity?.SetTag("graphiti.episode.uuid", episodeUuid);

        try
        {
            var episode = await EpisodicNode.GetByUuidAsync(Driver, episodeUuid, cancellationToken).ConfigureAwait(false);
            var edges = await EntityEdge.GetByUuidsAsync(Driver, episode.EntityEdges, cancellationToken).ConfigureAwait(false);
            var edgesToDelete = new List<EntityEdge>();
            var edgesToSave = new List<EntityEdge>();
            var retainedEdgeNodeUuids = new HashSet<string>(StringComparer.Ordinal);

            foreach (var edge in edges)
            {
                var remainingEpisodes = RemoveEpisodeUuid(edge.Episodes, episode.Uuid);
                if (remainingEpisodes.Count == 0)
                {
                    edgesToDelete.Add(edge);
                    continue;
                }

                retainedEdgeNodeUuids.Add(edge.SourceNodeUuid);
                retainedEdgeNodeUuids.Add(edge.TargetNodeUuid);
                if (remainingEpisodes.Count != edge.Episodes.Count)
                {
                    edge.Episodes = remainingEpisodes;
                    edgesToSave.Add(edge);
                }
            }

            var nodes = await Driver.GetMentionedNodesAsync(new[] { episode }, cancellationToken).ConfigureAwait(false);
            var nodesToDelete = new List<EntityNode>();

            foreach (var node in nodes)
            {
                if (retainedEdgeNodeUuids.Contains(node.Uuid))
                {
                    continue;
                }

                var episodesMentioningNode = await Driver.GetEpisodesByEntityNodeUuidAsync(node.Uuid, cancellationToken).ConfigureAwait(false);
                if (episodesMentioningNode.Count == 1)
                {
                    nodesToDelete.Add(node);
                }
            }

            foreach (var edge in edgesToSave)
            {
                await edge.SaveAsync(Driver, cancellationToken).ConfigureAwait(false);
            }

            var edgeUuidsToDelete = BuildEdgeUuidList(edgesToDelete);
            var nodeUuidsToDelete = BuildNodeUuidList(nodesToDelete);
            var sagaRepair = await RepairSagaEpisodeDeletionAsync(
                episode,
                _timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken).ConfigureAwait(false);

            await Edge.DeleteByUuidsAsync(Driver, edgeUuidsToDelete, cancellationToken).ConfigureAwait(false);
            await Node.DeleteByUuidsAsync(Driver, nodeUuidsToDelete, cancellationToken: cancellationToken).ConfigureAwait(false);
            await episode.DeleteAsync(Driver, cancellationToken).ConfigureAwait(false);
            activity?.SetTag("graphiti.deleted.nodes", nodesToDelete.Count);
            activity?.SetTag("graphiti.deleted.edges", edgesToDelete.Count);
            activity?.SetTag("graphiti.updated.edges", edgesToSave.Count);
            activity?.SetTag("graphiti.updated.sagas", sagaRepair.UpdatedSagas);
            activity?.SetTag("graphiti.deleted.saga_edges", sagaRepair.DeletedSagaEdges);
            activity?.SetTag("graphiti.created.next_episode_edges", sagaRepair.CreatedNextEpisodeEdges);
            GraphitiTelemetry.SetOk(activity);
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private async Task<SagaEpisodeDeletionRepairResult> RepairSagaEpisodeDeletionAsync(
        EpisodicNode episode,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var groupIds = new[] { episode.GroupId };
        var hasEpisodeEdges = await HasEpisodeEdge.GetByGroupIdsAsync(
            Driver,
            groupIds,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var removedMembershipEdges = FilterEdgesByTarget(hasEpisodeEdges, episode.Uuid);
        if (removedMembershipEdges.Count == 0)
        {
            return SagaEpisodeDeletionRepairResult.Empty;
        }

        var nextEpisodeEdges = await NextEpisodeEdge.GetByGroupIdsAsync(
            Driver,
            groupIds,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        BuildNextEpisodeDeletionState(
            nextEpisodeEdges,
            episode.Uuid,
            out var nextEdgesToDelete,
            out var existingNextPairs);

        var updatedSagas = 0;
        var createdNextEpisodeEdges = 0;
        var affectedSagaUuids = BuildAffectedSagaUuidList(removedMembershipEdges);
        foreach (var sagaUuid in affectedSagaUuids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var saga = await SagaNode.GetByUuidAsync(Driver, sagaUuid, cancellationToken).ConfigureAwait(false);
            var remainingEpisodeUuids = BuildRemainingEpisodeUuidSet(
                hasEpisodeEdges,
                sagaUuid,
                episode.Uuid);
            var remainingEpisodes = remainingEpisodeUuids.Count == 0
                ? Array.Empty<EpisodicNode>()
                : await EpisodicNode.GetByUuidsAsync(
                    Driver,
                    remainingEpisodeUuids,
                    cancellationToken).ConfigureAwait(false);
            var orderedEpisodes = SortEpisodesForSagaBounds(remainingEpisodes);

            saga.FirstEpisodeUuid = orderedEpisodes.Count == 0 ? null : orderedEpisodes[0].Uuid;
            saga.LastEpisodeUuid = orderedEpisodes.Count == 0 ? null : orderedEpisodes[^1].Uuid;
            await saga.SaveAsync(Driver, cancellationToken).ConfigureAwait(false);
            updatedSagas++;

            BuildNextEpisodeRepairCandidates(
                nextEpisodeEdges,
                episode.Uuid,
                remainingEpisodeUuids,
                out var incoming,
                out var outgoing);
            foreach (var predecessor in incoming)
            {
                foreach (var successor in outgoing)
                {
                    if (string.Equals(predecessor.SourceNodeUuid, successor.TargetNodeUuid, StringComparison.Ordinal)
                        || !existingNextPairs.Add((predecessor.SourceNodeUuid, successor.TargetNodeUuid)))
                    {
                        continue;
                    }

                    await new NextEpisodeEdge
                    {
                        SourceNodeUuid = predecessor.SourceNodeUuid,
                        TargetNodeUuid = successor.TargetNodeUuid,
                        GroupId = episode.GroupId,
                        CreatedAt = now
                    }.SaveAsync(Driver, cancellationToken).ConfigureAwait(false);
                    createdNextEpisodeEdges++;
                }
            }
        }

        var sagaEdgeUuidsToDelete = BuildSagaEdgeDeleteUuidList(removedMembershipEdges, nextEdgesToDelete);
        await Edge.DeleteByUuidsAsync(Driver, sagaEdgeUuidsToDelete, cancellationToken).ConfigureAwait(false);

        return new SagaEpisodeDeletionRepairResult(
            updatedSagas,
            sagaEdgeUuidsToDelete.Count,
            createdNextEpisodeEdges);
    }

    private static List<string> RemoveEpisodeUuid(
        List<string> episodeUuids,
        string episodeUuid)
    {
        var remainingEpisodes = new List<string>(episodeUuids.Count);
        for (var i = 0; i < episodeUuids.Count; i++)
        {
            if (!string.Equals(episodeUuids[i], episodeUuid, StringComparison.Ordinal))
            {
                remainingEpisodes.Add(episodeUuids[i]);
            }
        }

        return remainingEpisodes;
    }

    private static List<string> BuildEdgeUuidList(List<EntityEdge> edges)
    {
        var edgeUuids = new List<string>(edges.Count);
        for (var i = 0; i < edges.Count; i++)
        {
            edgeUuids.Add(edges[i].Uuid);
        }

        return edgeUuids;
    }

    private static List<string> BuildNodeUuidList(List<EntityNode> nodes)
    {
        var nodeUuids = new List<string>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++)
        {
            nodeUuids.Add(nodes[i].Uuid);
        }

        return nodeUuids;
    }

    private static List<HasEpisodeEdge> FilterEdgesByTarget(
        IReadOnlyList<HasEpisodeEdge> edges,
        string targetNodeUuid)
    {
        var matches = new List<HasEpisodeEdge>(edges.Count);
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (string.Equals(edge.TargetNodeUuid, targetNodeUuid, StringComparison.Ordinal))
            {
                matches.Add(edge);
            }
        }

        return matches;
    }

    private static void BuildNextEpisodeDeletionState(
        IReadOnlyList<NextEpisodeEdge> nextEpisodeEdges,
        string episodeUuid,
        out List<NextEpisodeEdge> nextEdgesToDelete,
        out HashSet<(string SourceNodeUuid, string TargetNodeUuid)> existingNextPairs)
    {
        nextEdgesToDelete = new List<NextEpisodeEdge>(nextEpisodeEdges.Count);
        existingNextPairs = new HashSet<(string SourceNodeUuid, string TargetNodeUuid)>();
        for (var i = 0; i < nextEpisodeEdges.Count; i++)
        {
            var edge = nextEpisodeEdges[i];
            if (string.Equals(edge.SourceNodeUuid, episodeUuid, StringComparison.Ordinal)
                || string.Equals(edge.TargetNodeUuid, episodeUuid, StringComparison.Ordinal))
            {
                nextEdgesToDelete.Add(edge);
            }
            else
            {
                existingNextPairs.Add((edge.SourceNodeUuid, edge.TargetNodeUuid));
            }
        }
    }

    private static List<string> BuildAffectedSagaUuidList(List<HasEpisodeEdge> removedMembershipEdges)
    {
        var sagaUuids = new List<string>(removedMembershipEdges.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < removedMembershipEdges.Count; i++)
        {
            var sagaUuid = removedMembershipEdges[i].SourceNodeUuid;
            if (seen.Add(sagaUuid))
            {
                sagaUuids.Add(sagaUuid);
            }
        }

        return sagaUuids;
    }

    private static HashSet<string> BuildRemainingEpisodeUuidSet(
        IReadOnlyList<HasEpisodeEdge> hasEpisodeEdges,
        string sagaUuid,
        string removedEpisodeUuid)
    {
        var remainingEpisodeUuids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < hasEpisodeEdges.Count; i++)
        {
            var edge = hasEpisodeEdges[i];
            if (string.Equals(edge.SourceNodeUuid, sagaUuid, StringComparison.Ordinal)
                && !string.Equals(edge.TargetNodeUuid, removedEpisodeUuid, StringComparison.Ordinal))
            {
                remainingEpisodeUuids.Add(edge.TargetNodeUuid);
            }
        }

        return remainingEpisodeUuids;
    }

    private static List<EpisodicNode> SortEpisodesForSagaBounds(IReadOnlyList<EpisodicNode> episodes)
    {
        var sorted = new List<EpisodicNode>(episodes.Count);
        for (var i = 0; i < episodes.Count; i++)
        {
            sorted.Add(episodes[i]);
        }

        sorted.Sort(CompareEpisodesForSagaBounds);
        return sorted;
    }

    private static int CompareEpisodesForSagaBounds(EpisodicNode left, EpisodicNode right)
    {
        var validAt = left.ValidAt.CompareTo(right.ValidAt);
        if (validAt != 0)
        {
            return validAt;
        }

        var createdAt = left.CreatedAt.CompareTo(right.CreatedAt);
        return createdAt != 0
            ? createdAt
            : string.CompareOrdinal(left.Uuid, right.Uuid);
    }

    private static void BuildNextEpisodeRepairCandidates(
        IReadOnlyList<NextEpisodeEdge> nextEpisodeEdges,
        string removedEpisodeUuid,
        HashSet<string> remainingEpisodeUuids,
        out List<NextEpisodeEdge> incoming,
        out List<NextEpisodeEdge> outgoing)
    {
        incoming = new List<NextEpisodeEdge>(nextEpisodeEdges.Count);
        outgoing = new List<NextEpisodeEdge>(nextEpisodeEdges.Count);
        for (var i = 0; i < nextEpisodeEdges.Count; i++)
        {
            var edge = nextEpisodeEdges[i];
            if (string.Equals(edge.TargetNodeUuid, removedEpisodeUuid, StringComparison.Ordinal)
                && remainingEpisodeUuids.Contains(edge.SourceNodeUuid))
            {
                incoming.Add(edge);
            }

            if (string.Equals(edge.SourceNodeUuid, removedEpisodeUuid, StringComparison.Ordinal)
                && remainingEpisodeUuids.Contains(edge.TargetNodeUuid))
            {
                outgoing.Add(edge);
            }
        }
    }

    private static List<string> BuildSagaEdgeDeleteUuidList(
        List<HasEpisodeEdge> removedMembershipEdges,
        List<NextEpisodeEdge> nextEdgesToDelete)
    {
        var edgeUuids = new List<string>(removedMembershipEdges.Count + nextEdgesToDelete.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddEdgeUuids(removedMembershipEdges, seen, edgeUuids);
        AddEdgeUuids(nextEdgesToDelete, seen, edgeUuids);
        return edgeUuids;
    }

    private static void AddEdgeUuids<T>(
        List<T> edges,
        HashSet<string> seen,
        List<string> edgeUuids)
        where T : Edge
    {
        for (var i = 0; i < edges.Count; i++)
        {
            var uuid = edges[i].Uuid;
            if (seen.Add(uuid))
            {
                edgeUuids.Add(uuid);
            }
        }
    }

    private readonly record struct SagaEpisodeDeletionRepairResult(
        int UpdatedSagas,
        int DeletedSagaEdges,
        int CreatedNextEpisodeEdges)
    {
        public static SagaEpisodeDeletionRepairResult Empty => default;
    }
}
