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
                var remainingEpisodes = edge.Episodes
                    .Where(uuid => !string.Equals(uuid, episode.Uuid, StringComparison.Ordinal))
                    .ToList();
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

            var sagaRepair = await RepairSagaEpisodeDeletionAsync(
                episode,
                _timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken).ConfigureAwait(false);

            await Edge.DeleteByUuidsAsync(Driver, edgesToDelete.Select(edge => edge.Uuid), cancellationToken).ConfigureAwait(false);
            await Node.DeleteByUuidsAsync(Driver, nodesToDelete.Select(node => node.Uuid), cancellationToken: cancellationToken).ConfigureAwait(false);
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
        var removedMembershipEdges = hasEpisodeEdges
            .Where(edge => string.Equals(edge.TargetNodeUuid, episode.Uuid, StringComparison.Ordinal))
            .ToList();
        if (removedMembershipEdges.Count == 0)
        {
            return SagaEpisodeDeletionRepairResult.Empty;
        }

        var nextEpisodeEdges = await NextEpisodeEdge.GetByGroupIdsAsync(
            Driver,
            groupIds,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var nextEdgesToDelete = nextEpisodeEdges
            .Where(edge =>
                string.Equals(edge.SourceNodeUuid, episode.Uuid, StringComparison.Ordinal)
                || string.Equals(edge.TargetNodeUuid, episode.Uuid, StringComparison.Ordinal))
            .ToList();
        var existingNextPairs = nextEpisodeEdges
            .Where(edge =>
                !string.Equals(edge.SourceNodeUuid, episode.Uuid, StringComparison.Ordinal)
                && !string.Equals(edge.TargetNodeUuid, episode.Uuid, StringComparison.Ordinal))
            .Select(edge => (edge.SourceNodeUuid, edge.TargetNodeUuid))
            .ToHashSet();

        var updatedSagas = 0;
        var createdNextEpisodeEdges = 0;
        foreach (var sagaUuid in removedMembershipEdges
                     .Select(edge => edge.SourceNodeUuid)
                     .Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var saga = await SagaNode.GetByUuidAsync(Driver, sagaUuid, cancellationToken).ConfigureAwait(false);
            var remainingMemberships = hasEpisodeEdges
                .Where(edge =>
                    string.Equals(edge.SourceNodeUuid, sagaUuid, StringComparison.Ordinal)
                    && !string.Equals(edge.TargetNodeUuid, episode.Uuid, StringComparison.Ordinal))
                .ToList();
            var remainingEpisodeUuids = remainingMemberships
                .Select(edge => edge.TargetNodeUuid)
                .ToHashSet(StringComparer.Ordinal);
            var remainingEpisodes = remainingEpisodeUuids.Count == 0
                ? Array.Empty<EpisodicNode>()
                : await EpisodicNode.GetByUuidsAsync(
                    Driver,
                    remainingEpisodeUuids,
                    cancellationToken).ConfigureAwait(false);
            var orderedEpisodes = remainingEpisodes
                .OrderBy(remainingEpisode => remainingEpisode.ValidAt)
                .ThenBy(remainingEpisode => remainingEpisode.CreatedAt)
                .ThenBy(remainingEpisode => remainingEpisode.Uuid, StringComparer.Ordinal)
                .ToList();

            saga.FirstEpisodeUuid = orderedEpisodes.FirstOrDefault()?.Uuid;
            saga.LastEpisodeUuid = orderedEpisodes.LastOrDefault()?.Uuid;
            await saga.SaveAsync(Driver, cancellationToken).ConfigureAwait(false);
            updatedSagas++;

            var incoming = nextEpisodeEdges
                .Where(edge =>
                    string.Equals(edge.TargetNodeUuid, episode.Uuid, StringComparison.Ordinal)
                    && remainingEpisodeUuids.Contains(edge.SourceNodeUuid))
                .ToList();
            var outgoing = nextEpisodeEdges
                .Where(edge =>
                    string.Equals(edge.SourceNodeUuid, episode.Uuid, StringComparison.Ordinal)
                    && remainingEpisodeUuids.Contains(edge.TargetNodeUuid))
                .ToList();
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

        var sagaEdgeUuidsToDelete = removedMembershipEdges
            .Select(edge => edge.Uuid)
            .Concat(nextEdgesToDelete.Select(edge => edge.Uuid))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        await Edge.DeleteByUuidsAsync(Driver, sagaEdgeUuidsToDelete, cancellationToken).ConfigureAwait(false);

        return new SagaEpisodeDeletionRepairResult(
            updatedSagas,
            sagaEdgeUuidsToDelete.Count,
            createdNextEpisodeEdges);
    }

    private readonly record struct SagaEpisodeDeletionRepairResult(
        int UpdatedSagas,
        int DeletedSagaEdges,
        int CreatedNextEpisodeEdges)
    {
        public static SagaEpisodeDeletionRepairResult Empty => default;
    }
}
