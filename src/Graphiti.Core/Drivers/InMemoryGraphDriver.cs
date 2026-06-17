using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graphiti.Core.Drivers;

/// <summary>
/// An in-process graph driver that stores all nodes and edges in memory with secondary indexes for fast
/// lookups. It implements both persistence (<see cref="GraphDriverBase"/>) and search
/// (<see cref="ISearchGraphDriver"/>), making it ideal for tests, examples, and ephemeral graphs.
/// All mutating operations are guarded by a lock; the driver can be cloned to snapshot its state.
/// </summary>
public sealed class InMemoryGraphDriver : GraphDriverBase, ISearchGraphDriver, ITypedNodeDeleteGraphDriver
{
    private readonly Lock _gate;
    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Edge> _edges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _nodeUuidsByGroup = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _entityNodeUuidsByGroup = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _episodicNodeUuidsByGroup = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _communityNodeUuidsByGroup = new(StringComparer.Ordinal);
    private readonly Dictionary<(string GroupId, string Name), HashSet<string>> _sagaNodeUuidsByGroupAndName = new();
    private readonly Dictionary<string, HashSet<string>> _edgeUuidsByGroup = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _incidentEdgeUuidsByNodeUuid = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _entityEdgeUuidsByNodeUuid = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _entityEdgeUuidsBySourceNodeUuid = new(StringComparer.Ordinal);
    private readonly Dictionary<(string SourceNodeUuid, string TargetNodeUuid), HashSet<string>> _entityEdgeUuidsByEndpoints = new();
    private readonly Dictionary<string, HashSet<string>> _episodicEdgeUuidsBySourceNodeUuid = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _episodicEdgeUuidsByTargetNodeUuid = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _communityEdgeUuidsByTargetNodeUuid = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _hasEpisodeEdgeUuidsBySagaUuid = new(StringComparer.Ordinal);

    /// <summary>Creates an empty in-memory driver for the optional named database.</summary>
    public InMemoryGraphDriver(string database = "") : base(GraphProvider.InMemory, database)
    {
        _gate = new Lock();
    }

    private InMemoryGraphDriver(
        string database,
        Dictionary<string, Node> nodes,
        Dictionary<string, Edge> edges,
        Lock gate,
        Dictionary<string, HashSet<string>> nodeUuidsByGroup,
        Dictionary<string, HashSet<string>> entityNodeUuidsByGroup,
        Dictionary<string, HashSet<string>> episodicNodeUuidsByGroup,
        Dictionary<string, HashSet<string>> communityNodeUuidsByGroup,
        Dictionary<(string GroupId, string Name), HashSet<string>> sagaNodeUuidsByGroupAndName,
        Dictionary<string, HashSet<string>> edgeUuidsByGroup,
        Dictionary<string, HashSet<string>> incidentEdgeUuidsByNodeUuid,
        Dictionary<string, HashSet<string>> entityEdgeUuidsByNodeUuid,
        Dictionary<string, HashSet<string>> entityEdgeUuidsBySourceNodeUuid,
        Dictionary<(string SourceNodeUuid, string TargetNodeUuid), HashSet<string>> entityEdgeUuidsByEndpoints,
        Dictionary<string, HashSet<string>> episodicEdgeUuidsBySourceNodeUuid,
        Dictionary<string, HashSet<string>> episodicEdgeUuidsByTargetNodeUuid,
        Dictionary<string, HashSet<string>> communityEdgeUuidsByTargetNodeUuid,
        Dictionary<string, HashSet<string>> hasEpisodeEdgeUuidsBySagaUuid) : base(GraphProvider.InMemory, database)
    {
        _nodes = nodes;
        _edges = edges;
        _gate = gate;
        _nodeUuidsByGroup = nodeUuidsByGroup;
        _entityNodeUuidsByGroup = entityNodeUuidsByGroup;
        _episodicNodeUuidsByGroup = episodicNodeUuidsByGroup;
        _communityNodeUuidsByGroup = communityNodeUuidsByGroup;
        _sagaNodeUuidsByGroupAndName = sagaNodeUuidsByGroupAndName;
        _edgeUuidsByGroup = edgeUuidsByGroup;
        _incidentEdgeUuidsByNodeUuid = incidentEdgeUuidsByNodeUuid;
        _entityEdgeUuidsByNodeUuid = entityEdgeUuidsByNodeUuid;
        _entityEdgeUuidsBySourceNodeUuid = entityEdgeUuidsBySourceNodeUuid;
        _entityEdgeUuidsByEndpoints = entityEdgeUuidsByEndpoints;
        _episodicEdgeUuidsBySourceNodeUuid = episodicEdgeUuidsBySourceNodeUuid;
        _episodicEdgeUuidsByTargetNodeUuid = episodicEdgeUuidsByTargetNodeUuid;
        _communityEdgeUuidsByTargetNodeUuid = communityEdgeUuidsByTargetNodeUuid;
        _hasEpisodeEdgeUuidsBySagaUuid = hasEpisodeEdgeUuidsBySagaUuid;
    }

    /// <inheritdoc />
    public override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override IGraphDriver Clone(string database) => new InMemoryGraphDriver(
        database,
        _nodes,
        _edges,
        _gate,
        _nodeUuidsByGroup,
        _entityNodeUuidsByGroup,
        _episodicNodeUuidsByGroup,
        _communityNodeUuidsByGroup,
        _sagaNodeUuidsByGroupAndName,
        _edgeUuidsByGroup,
        _incidentEdgeUuidsByNodeUuid,
        _entityEdgeUuidsByNodeUuid,
        _entityEdgeUuidsBySourceNodeUuid,
        _entityEdgeUuidsByEndpoints,
        _episodicEdgeUuidsBySourceNodeUuid,
        _episodicEdgeUuidsByTargetNodeUuid,
        _communityEdgeUuidsByTargetNodeUuid,
        _hasEpisodeEdgeUuidsBySagaUuid);

    /// <inheritdoc />
    public override Task<IReadOnlyList<string>> GetEntityGroupIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<string>>(MaterializeSortedNonEmptyGroupIds(_entityNodeUuidsByGroup));
        }
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<string>> GetCommunityGroupIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<string>>(MaterializeSortedNonEmptyGroupIds(_communityNodeUuidsByGroup));
        }
    }

    /// <inheritdoc />
    public override Task SaveNodeAsync(Node node, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var clone = CloneNode(node);
            if (_nodes.TryGetValue(clone.Uuid, out var existing))
            {
                RemoveNodeIndexes(existing);
            }

            _nodes[clone.Uuid] = clone;
            AddNodeIndexes(clone);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task SaveEdgeAsync(Edge edge, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var clone = CloneEdge(edge);
            if (_edges.TryGetValue(clone.Uuid, out var existing))
            {
                RemoveEdgeIndexes(existing);
            }

            _edges[clone.Uuid] = clone;
            AddEdgeIndexes(clone);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task DeleteNodeAsync(string uuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_nodes.Remove(uuid, out var node))
            {
                RemoveNodeIndexes(node);
            }

            var edgeUuids = MaterializeUuids(GetIndexedUuids(_incidentEdgeUuidsByNodeUuid, uuid));
            for (var i = 0; i < edgeUuids.Count; i++)
            {
                RemoveEdgeByUuid(edgeUuids[i]);
            }
        }

        return Task.CompletedTask;
    }

    Task ITypedNodeDeleteGraphDriver.DeleteNodeAsync<TNode>(
        string uuid,
        CancellationToken cancellationToken) =>
        DeleteNodeByUuidAsync<TNode>(uuid, cancellationToken);

    Task ITypedNodeDeleteGraphDriver.DeleteNodesByGroupIdAsync<TNode>(
        string groupId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        cancellationToken.ThrowIfCancellationRequested();
        List<string> uuids;
        lock (_gate)
        {
            uuids = BuildTypedNodeUuidsByGroup<TNode>(groupId);
        }

        return DeleteNodesByUuidsTypedAsync<TNode>(uuids, batchSize, cancellationToken);
    }

    Task ITypedNodeDeleteGraphDriver.DeleteNodesByUuidsAsync<TNode>(
        IEnumerable<string> uuids,
        int batchSize,
        CancellationToken cancellationToken) =>
        DeleteNodesByUuidsTypedAsync<TNode>(uuids, batchSize, cancellationToken);

    private async Task DeleteNodesByUuidsTypedAsync<TNode>(
        IEnumerable<string> uuids,
        int batchSize,
        CancellationToken cancellationToken)
        where TNode : Node
    {
        ArgumentNullException.ThrowIfNull(uuids);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        cancellationToken.ThrowIfCancellationRequested();
        var uuidList = MaterializeUuids(uuids);
        for (var batchStart = 0; batchStart < uuidList.Count; batchStart += batchSize)
        {
            var batchEnd = batchStart + Math.Min(batchSize, uuidList.Count - batchStart);
            for (var i = batchStart; i < batchEnd; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DeleteNodeByUuidAsync<TNode>(uuidList[i], cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private Task DeleteNodeByUuidAsync<TNode>(
        string uuid,
        CancellationToken cancellationToken)
        where TNode : Node
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_nodes.TryGetValue(uuid, out var node) || node is not TNode)
            {
                return Task.CompletedTask;
            }

            _nodes.Remove(uuid);
            RemoveNodeIndexes(node);
            var edgeUuids = MaterializeUuids(GetIndexedUuids(_incidentEdgeUuidsByNodeUuid, uuid));
            for (var i = 0; i < edgeUuids.Count; i++)
            {
                RemoveEdgeByUuid(edgeUuids[i]);
            }
        }

        return Task.CompletedTask;
    }

    private void RemoveEdgeByUuid(string uuid)
    {
        if (_edges.Remove(uuid, out var edge))
        {
            RemoveEdgeIndexes(edge);
        }
    }

    /// <inheritdoc />
    public override Task DeleteNodesByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        cancellationToken.ThrowIfCancellationRequested();
        List<string> uuids;
        lock (_gate)
        {
            uuids = MaterializeUuids(GetIndexedUuids(_nodeUuidsByGroup, groupId));
        }

        return DeleteNodesByUuidsAsync(uuids, batchSize, cancellationToken);
    }

    /// <inheritdoc />
    public override async Task DeleteNodesByUuidsAsync(IEnumerable<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        cancellationToken.ThrowIfCancellationRequested();
        var uuidList = MaterializeUuids(uuids);
        for (var batchStart = 0; batchStart < uuidList.Count; batchStart += batchSize)
        {
            var batchEnd = batchStart + Math.Min(batchSize, uuidList.Count - batchStart);
            for (var i = batchStart; i < batchEnd; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DeleteNodeAsync(uuidList[i], cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public override Task DeleteEdgeAsync(string uuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            RemoveEdgeByUuid(uuid);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task DeleteEdgesByUuidsAsync(IEnumerable<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        cancellationToken.ThrowIfCancellationRequested();
        var uuidList = MaterializeUuids(uuids);
        if (uuidList.Count == 0)
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            for (var i = 0; i < uuidList.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RemoveEdgeByUuid(uuidList[i]);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task ClearDataAsync(IReadOnlyList<string>? groupIds = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (groupIds is null)
            {
                _nodes.Clear();
                _edges.Clear();
                ClearIndexes();
            }
            else
            {
                var nodeUuids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var groupId in groupIds)
                {
                    foreach (var nodeUuid in GetIndexedUuids(_nodeUuidsByGroup, groupId))
                    {
                        nodeUuids.Add(nodeUuid);
                    }
                }

                if (nodeUuids.Count == 0)
                {
                    return Task.CompletedTask;
                }

                var edgeUuids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var nodeUuid in nodeUuids)
                {
                    foreach (var edgeUuid in GetIndexedUuids(_incidentEdgeUuidsByNodeUuid, nodeUuid))
                    {
                        edgeUuids.Add(edgeUuid);
                    }
                }

                foreach (var nodeUuid in nodeUuids)
                {
                    if (_nodes.Remove(nodeUuid, out var node))
                    {
                        RemoveNodeIndexes(node);
                    }
                }

                foreach (var edgeUuid in edgeUuids)
                {
                    RemoveEdgeByUuid(edgeUuid);
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task<TNode> GetNodeByUuidAsync<TNode>(string uuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_nodes.TryGetValue(uuid, out var node) && node is TNode typed)
            {
                return Task.FromResult((TNode)CloneNode(typed));
            }
        }

        throw new NodeNotFoundException(uuid);
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<TNode>> GetNodesByUuidsAsync<TNode>(
        IEnumerable<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        cancellationToken.ThrowIfCancellationRequested();
        var uuidList = MaterializeDistinctUuids(uuids, cancellationToken);
        if (uuidList.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<TNode>>(Array.Empty<TNode>());
        }

        lock (_gate)
        {
            var nodes = new List<TNode>(uuidList.Count);
            foreach (var uuid in uuidList)
            {
                if (_nodes.TryGetValue(uuid, out var node)
                    && node is TNode typed
                    && (groupId is null || string.Equals(node.GroupId, groupId, StringComparison.Ordinal)))
                {
                    nodes.Add((TNode)CloneNode(typed));
                }
            }

            return Task.FromResult<IReadOnlyList<TNode>>(nodes);
        }
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<TNode>> GetNodesByGroupIdsAsync<TNode>(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        cancellationToken.ThrowIfCancellationRequested();
        var groupList = MaterializeDistinctUuids(groupIds, cancellationToken);
        lock (_gate)
        {
            var candidates = GetNodesFromIndex<TNode>(groupList, allWhenNoGroups: false);
            candidates.Sort(static (left, right) => string.CompareOrdinal(right.Uuid, left.Uuid));
            var nodes = ProjectNodesByUuidOrder(candidates, limit, uuidCursor, withEmbeddings);
            return Task.FromResult<IReadOnlyList<TNode>>(nodes);
        }
    }

    /// <inheritdoc />
    public override Task<T> GetEdgeByUuidAsync<T>(string uuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_edges.TryGetValue(uuid, out var edge) && edge is T typed)
            {
                return Task.FromResult((T)CloneEdge(typed));
            }
        }

        throw new EdgeNotFoundException(uuid);
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<T>> GetEdgesByUuidsAsync<T>(IEnumerable<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        cancellationToken.ThrowIfCancellationRequested();
        var uuidList = MaterializeDistinctUuids(uuids, cancellationToken);
        if (uuidList.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
        }

        lock (_gate)
        {
            var edges = new List<T>(uuidList.Count);
            foreach (var uuid in uuidList)
            {
                if (_edges.TryGetValue(uuid, out var edge) && edge is T typed)
                {
                    edges.Add((T)CloneEdge(typed));
                }
            }

            return Task.FromResult<IReadOnlyList<T>>(edges);
        }
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<T>> GetEdgesByGroupIdsAsync<T>(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        cancellationToken.ThrowIfCancellationRequested();
        var groupList = MaterializeDistinctUuids(groupIds, cancellationToken);
        lock (_gate)
        {
            var candidates = GetEdgesFromIndex<T>(groupList, allWhenNoGroups: false);
            candidates.Sort(static (left, right) => string.CompareOrdinal(right.Uuid, left.Uuid));
            var edges = ProjectEdgesByUuidOrder(candidates, limit, uuidCursor, withEmbeddings);
            return Task.FromResult<IReadOnlyList<T>>(edges);
        }
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesBetweenNodesAsync(
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var edgeUuids = MaterializeSortedUuids(GetIndexedUuids(
                _entityEdgeUuidsByEndpoints,
                (sourceNodeUuid, targetNodeUuid)));
            var edges = new List<EntityEdge>(edgeUuids.Count);
            foreach (var uuid in edgeUuids)
            {
                if (_edges.TryGetValue(uuid, out var edge) && edge is EntityEdge entityEdge)
                {
                    edges.Add((EntityEdge)CloneEdge(entityEdge));
                }
            }

            return Task.FromResult<IReadOnlyList<EntityEdge>>(edges);
        }
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesByNodeUuidAsync(
        string nodeUuid,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var edgeUuids = MaterializeSortedUuids(GetIndexedUuids(_entityEdgeUuidsByNodeUuid, nodeUuid));
            var edges = new List<EntityEdge>(edgeUuids.Count);
            foreach (var uuid in edgeUuids)
            {
                if (_edges.TryGetValue(uuid, out var edge) && edge is EntityEdge entityEdge)
                {
                    edges.Add((EntityEdge)CloneEdge(entityEdge));
                }
            }

            return Task.FromResult<IReadOnlyList<EntityEdge>>(edges);
        }
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<EpisodicNode>> GetEpisodesByEntityNodeUuidAsync(
        string entityNodeUuid,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var edgeUuids = MaterializeSortedUuids(GetIndexedUuids(
                _episodicEdgeUuidsByTargetNodeUuid,
                entityNodeUuid));
            var episodeUuids = new HashSet<string>(StringComparer.Ordinal);
            var episodes = new List<EpisodicNode>();
            foreach (var edgeUuid in edgeUuids)
            {
                if (!_edges.TryGetValue(edgeUuid, out var edge)
                    || edge is not EpisodicEdge episodicEdge
                    || !episodeUuids.Add(episodicEdge.SourceNodeUuid)
                    || !_nodes.TryGetValue(episodicEdge.SourceNodeUuid, out var node)
                    || node is not EpisodicNode episode)
                {
                    continue;
                }

                episodes.Add((EpisodicNode)CloneNode(episode));
            }

            return Task.FromResult<IReadOnlyList<EpisodicNode>>(episodes);
        }
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(
        DateTime referenceTime,
        int lastN,
        IReadOnlyList<string>? groupIds = null,
        EpisodeType? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var referenceUtc = GraphitiHelpers.EnsureUtc(referenceTime);
        lock (_gate)
        {
            if (lastN <= 0)
            {
                return Task.FromResult<IReadOnlyList<EpisodicNode>>(Array.Empty<EpisodicNode>());
            }

            var candidates = groupIds is { Count: > 0 }
                ? GetNodesFromIndex<EpisodicNode>(groupIds, allWhenNoGroups: false)
                : GetNodesFromIndex<EpisodicNode>(null, allWhenNoGroups: true);

            HashSet<string>? sagaEpisodeUuids = null;
            if (saga is not null)
            {
                if (groupIds is not { Count: > 0 })
                {
                    return Task.FromResult<IReadOnlyList<EpisodicNode>>(Array.Empty<EpisodicNode>());
                }

                var sagaNode = FindStoredSagaByName(groupIds[0], saga);

                if (sagaNode is null)
                {
                    return Task.FromResult<IReadOnlyList<EpisodicNode>>(Array.Empty<EpisodicNode>());
                }

                sagaEpisodeUuids = MaterializeSagaEpisodeUuids(sagaNode.Uuid);
            }

            var episodes = new List<IndexedEpisode>(candidates.Count);
            for (var i = 0; i < candidates.Count; i++)
            {
                var episode = candidates[i];
                if (source is not null && episode.Source != source.Value)
                {
                    continue;
                }

                if (sagaEpisodeUuids is not null && !sagaEpisodeUuids.Contains(episode.Uuid))
                {
                    continue;
                }

                if (GraphitiHelpers.EnsureUtc(episode.ValidAt) > referenceUtc)
                {
                    continue;
                }

                episodes.Add(new IndexedEpisode(episode, i));
            }

            if (episodes.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<EpisodicNode>>(Array.Empty<EpisodicNode>());
            }

            episodes.Sort(CompareEpisodeValidAtNewestFirst);
            var take = Math.Min(lastN, episodes.Count);
            var results = new List<EpisodicNode>(take);
            for (var i = take - 1; i >= 0; i--)
            {
                results.Add((EpisodicNode)CloneNode(episodes[i].Episode));
            }

            return Task.FromResult<IReadOnlyList<EpisodicNode>>(results);
        }
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<EntityNode>> GetMentionedNodesAsync(
        IReadOnlyList<EpisodicNode> episodes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var episodeUuids = new List<string>(episodes.Count);
        var seenEpisodeUuids = new HashSet<string>(episodes.Count, StringComparer.Ordinal);
        foreach (var episode in episodes)
        {
            if (seenEpisodeUuids.Add(episode.Uuid))
            {
                episodeUuids.Add(episode.Uuid);
            }
        }

        lock (_gate)
        {
            var nodeUuids = new HashSet<string>(StringComparer.Ordinal);
            var nodes = new List<EntityNode>();
            foreach (var episodeUuid in episodeUuids)
            {
                var edgeUuids = MaterializeSortedUuids(GetIndexedUuids(
                    _episodicEdgeUuidsBySourceNodeUuid,
                    episodeUuid));
                foreach (var edgeUuid in edgeUuids)
                {
                    if (!_edges.TryGetValue(edgeUuid, out var edge)
                        || edge is not EpisodicEdge episodicEdge
                        || !nodeUuids.Add(episodicEdge.TargetNodeUuid)
                        || !_nodes.TryGetValue(episodicEdge.TargetNodeUuid, out var node)
                        || node is not EntityNode entityNode)
                    {
                        continue;
                    }

                    nodes.Add((EntityNode)CloneNode(entityNode));
                }
            }

            return Task.FromResult<IReadOnlyList<EntityNode>>(nodes);
        }
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<CommunityNode>> GetCommunitiesByNodesAsync(
        IReadOnlyList<EntityNode> nodes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nodeUuids = new List<string>(nodes.Count);
        var seenNodeUuids = new HashSet<string>(nodes.Count, StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (seenNodeUuids.Add(node.Uuid))
            {
                nodeUuids.Add(node.Uuid);
            }
        }

        lock (_gate)
        {
            var communityUuids = new HashSet<string>(StringComparer.Ordinal);
            var communities = new List<CommunityNode>();
            foreach (var nodeUuid in nodeUuids)
            {
                var edgeUuids = MaterializeSortedUuids(GetIndexedUuids(
                    _communityEdgeUuidsByTargetNodeUuid,
                    nodeUuid));
                foreach (var edgeUuid in edgeUuids)
                {
                    if (!_edges.TryGetValue(edgeUuid, out var edge)
                        || edge is not CommunityEdge communityEdge
                        || !communityUuids.Add(communityEdge.SourceNodeUuid)
                        || !_nodes.TryGetValue(communityEdge.SourceNodeUuid, out var node)
                        || node is not CommunityNode communityNode)
                    {
                        continue;
                    }

                    communities.Add((CommunityNode)CloneNode(communityNode));
                }
            }

            return Task.FromResult<IReadOnlyList<CommunityNode>>(communities);
        }
    }

    /// <inheritdoc />
    public override Task<SagaNode?> FindSagaByNameAsync(string name, string groupId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var saga = FindStoredSagaByName(groupId, name);
            return Task.FromResult(saga is null ? null : (SagaNode)CloneNode(saga));
        }
    }

    /// <inheritdoc />
    public override Task<string?> GetSagaPreviousEpisodeUuidAsync(
        string sagaUuid,
        string currentEpisodeUuid,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var seenEpisodeUuids = new HashSet<string>(StringComparer.Ordinal);
            EpisodicNode? previous = null;
            foreach (var edgeUuid in GetIndexedUuids(_hasEpisodeEdgeUuidsBySagaUuid, sagaUuid))
            {
                if (!_edges.TryGetValue(edgeUuid, out var edge)
                    || edge is not HasEpisodeEdge hasEpisodeEdge
                    || string.Equals(hasEpisodeEdge.TargetNodeUuid, currentEpisodeUuid, StringComparison.Ordinal)
                    || !seenEpisodeUuids.Add(hasEpisodeEdge.TargetNodeUuid)
                    || !_nodes.TryGetValue(hasEpisodeEdge.TargetNodeUuid, out var node)
                    || node is not EpisodicNode episode)
                {
                    continue;
                }

                if (previous is null
                    || episode.ValidAt > previous.ValidAt
                    || (episode.ValidAt == previous.ValidAt && episode.CreatedAt > previous.CreatedAt))
                {
                    previous = episode;
                }
            }

            return Task.FromResult(previous?.Uuid);
        }
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<SagaEpisodeContent>> GetSagaEpisodeContentsAsync(
        string sagaUuid,
        DateTime? since = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var take = Math.Max(0, limit);
            if (take == 0)
            {
                return Task.FromResult<IReadOnlyList<SagaEpisodeContent>>(Array.Empty<SagaEpisodeContent>());
            }

            var seenEpisodeUuids = new HashSet<string>(StringComparer.Ordinal);
            var episodes = new List<IndexedEpisode>();
            var index = 0;
            foreach (var edgeUuid in GetIndexedUuids(_hasEpisodeEdgeUuidsBySagaUuid, sagaUuid))
            {
                if (!_edges.TryGetValue(edgeUuid, out var edge)
                    || edge is not HasEpisodeEdge hasEpisodeEdge
                    || !seenEpisodeUuids.Add(hasEpisodeEdge.TargetNodeUuid)
                    || !_nodes.TryGetValue(hasEpisodeEdge.TargetNodeUuid, out var node)
                    || node is not EpisodicNode episode)
                {
                    continue;
                }

                if (since is not null && episode.CreatedAt <= since.Value)
                {
                    index++;
                    continue;
                }

                episodes.Add(new IndexedEpisode(episode, index));
                index++;
            }

            if (episodes.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<SagaEpisodeContent>>(Array.Empty<SagaEpisodeContent>());
            }

            episodes.Sort(since is null
                ? CompareEpisodeNewestFirst
                : CompareEpisodeOldestFirst);

            take = Math.Min(take, episodes.Count);
            var results = new List<SagaEpisodeContent>(take);
            if (since is null)
            {
                for (var i = take - 1; i >= 0; i--)
                {
                    var episode = episodes[i].Episode;
                    if (!string.IsNullOrEmpty(episode.Content))
                    {
                        results.Add(new SagaEpisodeContent(episode.Content, episode.ValidAt));
                    }
                }
            }
            else
            {
                for (var i = 0; i < take; i++)
                {
                    var episode = episodes[i].Episode;
                    if (!string.IsNullOrEmpty(episode.Content))
                    {
                        results.Add(new SagaEpisodeContent(episode.Content, episode.ValidAt));
                    }
                }
            }

            return Task.FromResult<IReadOnlyList<SagaEpisodeContent>>(results);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var compiledFilter = CompiledSearchFilter.Compile(searchFilter);
        List<EntityNode> candidates;
        lock (_gate)
        {
            candidates = GetNodesFromIndex<EntityNode>(groupIds, allWhenNoGroups: true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var ranked = Bm25TextScorer.Rank(
            candidates,
            node => SearchFilterMatcher.NodeMatches(node, compiledFilter),
            EntityNodeFulltextText,
            query,
            limit);
        return Task.FromResult<IReadOnlyList<SearchHit<EntityNode>>>(ProjectNodeSearchHits(ranked));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var compiledFilter = CompiledSearchFilter.Compile(searchFilter);
        List<EntityNode> candidates;
        lock (_gate)
        {
            candidates = GetNodesFromIndex<EntityNode>(groupIds, allWhenNoGroups: true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var scorer = SearchUtilities.CreateCosineSimilarityScorer(searchVector);
        var ranked = SearchUtilities.TopByScore(
            candidates,
            node => SearchFilterMatcher.NodeMatches(node, compiledFilter),
            node => scorer.Score(node.NameEmbedding),
            limit,
            minScore,
            includeMinScore: false);
        return Task.FromResult<IReadOnlyList<SearchHit<EntityNode>>>(ProjectNodeSearchHits(ranked));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var compiledFilter = CompiledSearchFilter.Compile(searchFilter);
        List<EntityEdge> candidates;
        Dictionary<string, EntityNode> nodesByUuid;
        lock (_gate)
        {
            candidates = GetEdgesFromIndex<EntityEdge>(groupIds, allWhenNoGroups: true);
            nodesByUuid = EntityNodeLookup();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var ranked = Bm25TextScorer.Rank(
            candidates,
            edge => SearchFilterMatcher.EdgeMatches(edge, compiledFilter, nodesByUuid),
            EntityEdgeFulltextText,
            query,
            limit);
        return Task.FromResult<IReadOnlyList<SearchHit<EntityEdge>>>(ProjectEdgeSearchHits(ranked));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        string? sourceNodeUuid = null,
        string? targetNodeUuid = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var compiledFilter = CompiledSearchFilter.Compile(searchFilter);
        List<EntityEdge> candidates;
        Dictionary<string, EntityNode> nodesByUuid;
        lock (_gate)
        {
            candidates = FilterEdgesByEndpoint(
                GetEdgesFromIndex<EntityEdge>(groupIds, allWhenNoGroups: true),
                sourceNodeUuid,
                targetNodeUuid);
            nodesByUuid = EntityNodeLookup();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var scorer = SearchUtilities.CreateCosineSimilarityScorer(searchVector);
        var ranked = SearchUtilities.TopByScore(
            candidates,
            edge => SearchFilterMatcher.EdgeMatches(edge, compiledFilter, nodesByUuid),
            edge => scorer.Score(edge.FactEmbedding),
            limit,
            minScore,
            includeMinScore: false);
        return Task.FromResult<IReadOnlyList<SearchHit<EntityEdge>>>(ProjectEdgeSearchHits(ranked));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesBfsAsync(
        IReadOnlyList<string>? originNodeUuids,
        SearchFilters searchFilter,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (originNodeUuids is null || originNodeUuids.Count == 0 || maxDepth < 1)
        {
            return Task.FromResult<IReadOnlyList<SearchHit<EntityNode>>>(Array.Empty<SearchHit<EntityNode>>());
        }

        var compiledFilter = CompiledSearchFilter.Compile(searchFilter);
        List<EntityNode> candidates;
        TraversalGraph graph;
        lock (_gate)
        {
            candidates = GetNodesFromIndex<EntityNode>(groupIds, allWhenNoGroups: true);
            graph = BuildTraversalGraph(groupIds);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var candidateByUuid = BuildNodeCandidateLookup(candidates, compiledFilter);
        IReadOnlyList<SearchHit<EntityNode>> results =
            BuildNodeBfsHits(originNodeUuids, maxDepth, limit, graph, candidateByUuid);
        return Task.FromResult(results);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesBfsAsync(
        IReadOnlyList<string>? originNodeUuids,
        SearchFilters searchFilter,
        int maxDepth,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (originNodeUuids is null || originNodeUuids.Count == 0 || maxDepth < 1)
        {
            return Task.FromResult<IReadOnlyList<SearchHit<EntityEdge>>>(Array.Empty<SearchHit<EntityEdge>>());
        }

        var compiledFilter = CompiledSearchFilter.Compile(searchFilter);
        List<EntityEdge> candidates;
        TraversalGraph graph;
        Dictionary<string, EntityNode> nodesByUuid;
        lock (_gate)
        {
            candidates = GetEdgesFromIndex<EntityEdge>(groupIds, allWhenNoGroups: true);
            graph = BuildTraversalGraph(groupIds);
            nodesByUuid = EntityNodeLookup();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var candidateByUuid = BuildEdgeCandidateLookup(candidates, compiledFilter, nodesByUuid);
        IReadOnlyList<SearchHit<EntityEdge>> results =
            BuildEdgeBfsHits(originNodeUuids, maxDepth, limit, graph, candidateByUuid);
        return Task.FromResult(results);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<EpisodicNode>>> SearchEpisodesFulltextAsync(
        string query,
        SearchFilters searchFilter,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = searchFilter;
        List<EpisodicNode> candidates;
        lock (_gate)
        {
            candidates = GetNodesFromIndex<EpisodicNode>(groupIds, allWhenNoGroups: true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var ranked = Bm25TextScorer.Rank(
            candidates,
            EpisodeFulltextText,
            query,
            limit);
        return Task.FromResult<IReadOnlyList<SearchHit<EpisodicNode>>>(ProjectNodeSearchHits(ranked));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesFulltextAsync(
        string query,
        IReadOnlyList<string>? groupIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<CommunityNode> candidates;
        lock (_gate)
        {
            candidates = GetNodesFromIndex<CommunityNode>(groupIds, allWhenNoGroups: true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var ranked = Bm25TextScorer.Rank(
            candidates,
            CommunityFulltextText,
            query,
            limit);
        return Task.FromResult<IReadOnlyList<SearchHit<CommunityNode>>>(ProjectNodeSearchHits(ranked));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesByEmbeddingAsync(
        IReadOnlyList<float> searchVector,
        IReadOnlyList<string>? groupIds,
        int limit,
        float minScore,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        List<CommunityNode> candidates;
        lock (_gate)
        {
            candidates = GetNodesFromIndex<CommunityNode>(groupIds, allWhenNoGroups: true);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var scorer = SearchUtilities.CreateCosineSimilarityScorer(searchVector);
        var ranked = SearchUtilities.TopByScore(
            candidates,
            node => scorer.Score(node.NameEmbedding),
            limit,
            minScore,
            includeMinScore: false);
        return Task.FromResult<IReadOnlyList<SearchHit<CommunityNode>>>(ProjectNodeSearchHits(ranked));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchRank>> RankNodeDistanceAsync(
        IReadOnlyList<string> nodeUuids,
        string centerNodeUuid,
        float minScore = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HashSet<string> adjacentNodeUuids;
        lock (_gate)
        {
            adjacentNodeUuids = BuildAdjacentNodeLookup(centerNodeUuid);
        }

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<SearchRank> results = BuildNodeDistanceRanks(
            nodeUuids,
            centerNodeUuid,
            adjacentNodeUuids,
            minScore);
        return Task.FromResult(results);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchRank>> RankNodeEpisodeMentionsAsync(
        IReadOnlyList<string> nodeUuids,
        float minScore = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<string, int> mentionCounts;
        lock (_gate)
        {
            mentionCounts = BuildMentionCountLookup();
        }

        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<SearchRank> results = BuildEpisodeMentionRanks(nodeUuids, mentionCounts, minScore);
        return Task.FromResult(results);
    }

    internal IReadOnlyList<Node> SnapshotNodes()
    {
        lock (_gate)
        {
            var nodes = new List<Node>(_nodes.Count);
            foreach (var node in _nodes.Values)
            {
                nodes.Add(CloneNode(node));
            }

            return nodes;
        }
    }

    internal IReadOnlyList<Edge> SnapshotEdges()
    {
        lock (_gate)
        {
            var edges = new List<Edge>(_edges.Count);
            foreach (var edge in _edges.Values)
            {
                edges.Add(CloneEdge(edge));
            }

            return edges;
        }
    }

    private void AddNodeIndexes(Node node)
    {
        AddToIndex(_nodeUuidsByGroup, node.GroupId, node.Uuid);
        switch (node)
        {
            case EntityNode:
                AddToIndex(_entityNodeUuidsByGroup, node.GroupId, node.Uuid);
                break;
            case EpisodicNode:
                AddToIndex(_episodicNodeUuidsByGroup, node.GroupId, node.Uuid);
                break;
            case CommunityNode:
                AddToIndex(_communityNodeUuidsByGroup, node.GroupId, node.Uuid);
                break;
            case SagaNode saga:
                AddToIndex(_sagaNodeUuidsByGroupAndName, (saga.GroupId, saga.Name), saga.Uuid);
                break;
        }
    }

    private void RemoveNodeIndexes(Node node)
    {
        RemoveFromIndex(_nodeUuidsByGroup, node.GroupId, node.Uuid);
        switch (node)
        {
            case EntityNode:
                RemoveFromIndex(_entityNodeUuidsByGroup, node.GroupId, node.Uuid);
                break;
            case EpisodicNode:
                RemoveFromIndex(_episodicNodeUuidsByGroup, node.GroupId, node.Uuid);
                break;
            case CommunityNode:
                RemoveFromIndex(_communityNodeUuidsByGroup, node.GroupId, node.Uuid);
                break;
            case SagaNode saga:
                RemoveFromIndex(_sagaNodeUuidsByGroupAndName, (saga.GroupId, saga.Name), saga.Uuid);
                break;
        }
    }

    private void AddEdgeIndexes(Edge edge)
    {
        AddToIndex(_edgeUuidsByGroup, edge.GroupId, edge.Uuid);
        AddToIndex(_incidentEdgeUuidsByNodeUuid, edge.SourceNodeUuid, edge.Uuid);
        AddToIndex(_incidentEdgeUuidsByNodeUuid, edge.TargetNodeUuid, edge.Uuid);

        switch (edge)
        {
            case EntityEdge:
                AddToIndex(_entityEdgeUuidsByNodeUuid, edge.SourceNodeUuid, edge.Uuid);
                AddToIndex(_entityEdgeUuidsByNodeUuid, edge.TargetNodeUuid, edge.Uuid);
                AddToIndex(_entityEdgeUuidsBySourceNodeUuid, edge.SourceNodeUuid, edge.Uuid);
                AddToIndex(_entityEdgeUuidsByEndpoints, (edge.SourceNodeUuid, edge.TargetNodeUuid), edge.Uuid);
                break;
            case EpisodicEdge:
                AddToIndex(_episodicEdgeUuidsBySourceNodeUuid, edge.SourceNodeUuid, edge.Uuid);
                AddToIndex(_episodicEdgeUuidsByTargetNodeUuid, edge.TargetNodeUuid, edge.Uuid);
                break;
            case CommunityEdge:
                AddToIndex(_communityEdgeUuidsByTargetNodeUuid, edge.TargetNodeUuid, edge.Uuid);
                break;
            case HasEpisodeEdge:
                AddToIndex(_hasEpisodeEdgeUuidsBySagaUuid, edge.SourceNodeUuid, edge.Uuid);
                break;
        }
    }

    private void RemoveEdgeIndexes(Edge edge)
    {
        RemoveFromIndex(_edgeUuidsByGroup, edge.GroupId, edge.Uuid);
        RemoveFromIndex(_incidentEdgeUuidsByNodeUuid, edge.SourceNodeUuid, edge.Uuid);
        RemoveFromIndex(_incidentEdgeUuidsByNodeUuid, edge.TargetNodeUuid, edge.Uuid);

        switch (edge)
        {
            case EntityEdge:
                RemoveFromIndex(_entityEdgeUuidsByNodeUuid, edge.SourceNodeUuid, edge.Uuid);
                RemoveFromIndex(_entityEdgeUuidsByNodeUuid, edge.TargetNodeUuid, edge.Uuid);
                RemoveFromIndex(_entityEdgeUuidsBySourceNodeUuid, edge.SourceNodeUuid, edge.Uuid);
                RemoveFromIndex(_entityEdgeUuidsByEndpoints, (edge.SourceNodeUuid, edge.TargetNodeUuid), edge.Uuid);
                break;
            case EpisodicEdge:
                RemoveFromIndex(_episodicEdgeUuidsBySourceNodeUuid, edge.SourceNodeUuid, edge.Uuid);
                RemoveFromIndex(_episodicEdgeUuidsByTargetNodeUuid, edge.TargetNodeUuid, edge.Uuid);
                break;
            case CommunityEdge:
                RemoveFromIndex(_communityEdgeUuidsByTargetNodeUuid, edge.TargetNodeUuid, edge.Uuid);
                break;
            case HasEpisodeEdge:
                RemoveFromIndex(_hasEpisodeEdgeUuidsBySagaUuid, edge.SourceNodeUuid, edge.Uuid);
                break;
        }
    }

    private void ClearIndexes()
    {
        _nodeUuidsByGroup.Clear();
        _entityNodeUuidsByGroup.Clear();
        _episodicNodeUuidsByGroup.Clear();
        _communityNodeUuidsByGroup.Clear();
        _sagaNodeUuidsByGroupAndName.Clear();
        _edgeUuidsByGroup.Clear();
        _incidentEdgeUuidsByNodeUuid.Clear();
        _entityEdgeUuidsByNodeUuid.Clear();
        _entityEdgeUuidsBySourceNodeUuid.Clear();
        _entityEdgeUuidsByEndpoints.Clear();
        _episodicEdgeUuidsBySourceNodeUuid.Clear();
        _episodicEdgeUuidsByTargetNodeUuid.Clear();
        _communityEdgeUuidsByTargetNodeUuid.Clear();
        _hasEpisodeEdgeUuidsBySagaUuid.Clear();
    }

    private List<TNode> GetNodesFromIndex<TNode>(
        IEnumerable<string>? groupIds,
        bool allWhenNoGroups)
        where TNode : Node
    {
        var index = GetNodeGroupIndex<TNode>();
        var uuids = GetUuidsByGroups(index, groupIds, allWhenNoGroups);
        var nodes = new List<TNode>(uuids.Count);
        foreach (var uuid in uuids)
        {
            if (_nodes.TryGetValue(uuid, out var node) && node is TNode typed)
            {
                nodes.Add(typed);
            }
        }

        return nodes;
    }

    private List<TEdge> GetEdgesFromIndex<TEdge>(
        IEnumerable<string>? groupIds,
        bool allWhenNoGroups)
        where TEdge : Edge
    {
        var uuids = GetUuidsByGroups(_edgeUuidsByGroup, groupIds, allWhenNoGroups);
        var edges = new List<TEdge>(uuids.Count);
        foreach (var uuid in uuids)
        {
            if (_edges.TryGetValue(uuid, out var edge) && edge is TEdge typed)
            {
                edges.Add(typed);
            }
        }

        return edges;
    }

    private static List<EntityEdge> FilterEdgesByEndpoint(
        List<EntityEdge> candidates,
        string? sourceNodeUuid,
        string? targetNodeUuid)
    {
        var results = new List<EntityEdge>(candidates.Count);
        for (var i = 0; i < candidates.Count; i++)
        {
            var edge = candidates[i];
            if ((sourceNodeUuid is null || edge.SourceNodeUuid == sourceNodeUuid)
                && (targetNodeUuid is null || edge.TargetNodeUuid == targetNodeUuid))
            {
                results.Add(edge);
            }
        }

        return results;
    }

    private static List<SearchHit<TNode>> ProjectNodeSearchHits<TNode>(
        IReadOnlyList<(TNode Item, float Score)> ranked)
        where TNode : Node
    {
        var results = new List<SearchHit<TNode>>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            results.Add(new SearchHit<TNode>((TNode)CloneNode(ranked[i].Item), ranked[i].Score));
        }

        return results;
    }

    private static List<SearchHit<TEdge>> ProjectEdgeSearchHits<TEdge>(
        IReadOnlyList<(TEdge Item, float Score)> ranked)
        where TEdge : Edge
    {
        var results = new List<SearchHit<TEdge>>(ranked.Count);
        for (var i = 0; i < ranked.Count; i++)
        {
            results.Add(new SearchHit<TEdge>((TEdge)CloneEdge(ranked[i].Item), ranked[i].Score));
        }

        return results;
    }

    private SagaNode? FindStoredSagaByName(string groupId, string name)
    {
        var sagaUuids = MaterializeSortedUuids(GetIndexedUuids(_sagaNodeUuidsByGroupAndName, (groupId, name)));
        foreach (var uuid in sagaUuids)
        {
            if (_nodes.TryGetValue(uuid, out var node) && node is SagaNode saga)
            {
                return saga;
            }
        }

        return null;
    }

    private SagaNode? FindFirstStoredSagaByName(string name)
    {
        foreach (var pair in _sagaNodeUuidsByGroupAndName)
        {
            if (!string.Equals(pair.Key.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var uuid in pair.Value)
            {
                if (_nodes.TryGetValue(uuid, out var node) && node is SagaNode saga)
                {
                    return saga;
                }
            }
        }

        return null;
    }

    private HashSet<string> MaterializeSagaEpisodeUuids(string sagaUuid)
    {
        var episodeUuids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edgeUuid in GetIndexedUuids(_hasEpisodeEdgeUuidsBySagaUuid, sagaUuid))
        {
            if (_edges.TryGetValue(edgeUuid, out var edge) && edge is HasEpisodeEdge hasEpisodeEdge)
            {
                episodeUuids.Add(hasEpisodeEdge.TargetNodeUuid);
            }
        }

        return episodeUuids;
    }

    private Dictionary<string, List<TEdge>> BuildEdgesBySource<TEdge>(
        IReadOnlyList<string>? groupIds)
        where TEdge : Edge
    {
        var candidates = GetEdgesFromIndex<TEdge>(groupIds, allWhenNoGroups: true);
        candidates.Sort(static (left, right) => string.CompareOrdinal(left.Uuid, right.Uuid));
        var result = new Dictionary<string, List<TEdge>>(StringComparer.Ordinal);
        for (var i = 0; i < candidates.Count; i++)
        {
            var edge = candidates[i];
            if (!result.TryGetValue(edge.SourceNodeUuid, out var edges))
            {
                edges = new List<TEdge>();
                result[edge.SourceNodeUuid] = edges;
            }

            edges.Add(edge);
        }

        return result;
    }

    private Dictionary<string, HashSet<string>> GetNodeGroupIndex<TNode>()
        where TNode : Node
    {
        if (typeof(TNode) == typeof(EntityNode))
        {
            return _entityNodeUuidsByGroup;
        }

        if (typeof(TNode) == typeof(EpisodicNode))
        {
            return _episodicNodeUuidsByGroup;
        }

        if (typeof(TNode) == typeof(CommunityNode))
        {
            return _communityNodeUuidsByGroup;
        }

        return _nodeUuidsByGroup;
    }

    private List<string> BuildTypedNodeUuidsByGroup<TNode>(string groupId)
        where TNode : Node
    {
        var uuids = GetIndexedUuids(GetNodeGroupIndex<TNode>(), groupId);
        var results = new List<string>();
        foreach (var uuid in uuids)
        {
            if (_nodes.TryGetValue(uuid, out var node) && node is TNode)
            {
                results.Add(uuid);
            }
        }

        return results;
    }

    private static List<string> GetUuidsByGroups(
        Dictionary<string, HashSet<string>> index,
        IEnumerable<string>? groupIds,
        bool allWhenNoGroups)
    {
        if (groupIds is null)
        {
            return allWhenNoGroups ? MaterializeAllIndexedUuids(index) : [];
        }

        var groups = MaterializeDistinctUuids(groupIds, CancellationToken.None);
        if (groups.Count == 0)
        {
            return allWhenNoGroups ? MaterializeAllIndexedUuids(index) : [];
        }

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var groupId in groups)
        {
            foreach (var uuid in GetIndexedUuids(index, groupId))
            {
                if (seen.Add(uuid))
                {
                    results.Add(uuid);
                }
            }
        }

        return results;
    }

    private static List<string> MaterializeAllIndexedUuids(Dictionary<string, HashSet<string>> index)
    {
        var capacity = 0;
        foreach (var pair in index)
        {
            capacity += pair.Value.Count;
        }

        var results = new List<string>(capacity);
        foreach (var pair in index)
        {
            foreach (var uuid in pair.Value)
            {
                results.Add(uuid);
            }
        }

        return results;
    }

    private static List<string> MaterializeSortedNonEmptyGroupIds(Dictionary<string, HashSet<string>> index)
    {
        var groupIds = new List<string>(index.Count);
        foreach (var pair in index)
        {
            if (pair.Value.Count > 0 && !string.IsNullOrEmpty(pair.Key))
            {
                groupIds.Add(pair.Key);
            }
        }

        groupIds.Sort(StringComparer.Ordinal);
        return groupIds;
    }

    private static List<TNode> ProjectNodesByUuidOrder<TNode>(
        List<TNode> candidates,
        int? limit,
        string? uuidCursor,
        bool withEmbeddings)
        where TNode : Node
    {
        var capacity = BoundedCapacity(candidates.Count, limit);
        if (capacity == 0)
        {
            return [];
        }

        var nodes = new List<TNode>(capacity);
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(uuidCursor) && string.CompareOrdinal(candidate.Uuid, uuidCursor) >= 0)
            {
                continue;
            }

            nodes.Add(ProjectNodeEmbedding((TNode)CloneNode(candidate), withEmbeddings));
            if (limit is not null && nodes.Count == limit.Value)
            {
                break;
            }
        }

        return nodes;
    }

    private static List<TEdge> ProjectEdgesByUuidOrder<TEdge>(
        List<TEdge> candidates,
        int? limit,
        string? uuidCursor,
        bool withEmbeddings)
        where TEdge : Edge
    {
        var capacity = BoundedCapacity(candidates.Count, limit);
        if (capacity == 0)
        {
            return [];
        }

        var edges = new List<TEdge>(capacity);
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(uuidCursor) && string.CompareOrdinal(candidate.Uuid, uuidCursor) >= 0)
            {
                continue;
            }

            edges.Add(ProjectEdgeEmbedding((TEdge)CloneEdge(candidate), withEmbeddings));
            if (limit is not null && edges.Count == limit.Value)
            {
                break;
            }
        }

        return edges;
    }

    private static int BoundedCapacity(int count, int? limit) =>
        limit is null
            ? count
            : Math.Min(count, Math.Max(0, limit.Value));

    private static void AddToIndex<TKey>(
        Dictionary<TKey, HashSet<string>> index,
        TKey key,
        string uuid)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out var uuids))
        {
            uuids = new HashSet<string>(StringComparer.Ordinal);
            index[key] = uuids;
        }

        uuids.Add(uuid);
    }

    private static void RemoveFromIndex<TKey>(
        Dictionary<TKey, HashSet<string>> index,
        TKey key,
        string uuid)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out var uuids))
        {
            return;
        }

        uuids.Remove(uuid);
        if (uuids.Count == 0)
        {
            index.Remove(key);
        }
    }

    private readonly struct IndexedEpisode(EpisodicNode episode, int index)
    {
        public EpisodicNode Episode { get; } = episode;

        public int Index { get; } = index;
    }

    private static int CompareEpisodeValidAtNewestFirst(IndexedEpisode left, IndexedEpisode right)
    {
        var validAt = right.Episode.ValidAt.CompareTo(left.Episode.ValidAt);
        return validAt != 0 ? validAt : left.Index.CompareTo(right.Index);
    }

    private static int CompareEpisodeNewestFirst(IndexedEpisode left, IndexedEpisode right)
    {
        var validAt = right.Episode.ValidAt.CompareTo(left.Episode.ValidAt);
        if (validAt != 0)
        {
            return validAt;
        }

        var createdAt = right.Episode.CreatedAt.CompareTo(left.Episode.CreatedAt);
        return createdAt != 0 ? createdAt : left.Index.CompareTo(right.Index);
    }

    private static int CompareEpisodeOldestFirst(IndexedEpisode left, IndexedEpisode right)
    {
        var validAt = left.Episode.ValidAt.CompareTo(right.Episode.ValidAt);
        if (validAt != 0)
        {
            return validAt;
        }

        var createdAt = left.Episode.CreatedAt.CompareTo(right.Episode.CreatedAt);
        return createdAt != 0 ? createdAt : left.Index.CompareTo(right.Index);
    }

    private static IEnumerable<string> GetIndexedUuids<TKey>(
        Dictionary<TKey, HashSet<string>> index,
        TKey key)
        where TKey : notnull =>
        index.TryGetValue(key, out var uuids)
            ? uuids
            : Array.Empty<string>();

    private static List<string> MaterializeUuids(IEnumerable<string> uuids)
    {
        var capacity = uuids.TryGetNonEnumeratedCount(out var count) ? count : 0;
        var results = capacity == 0 ? new List<string>() : new List<string>(capacity);
        foreach (var uuid in uuids)
        {
            results.Add(uuid);
        }

        return results;
    }

    private static List<string> MaterializeDistinctUuids(
        IEnumerable<string> uuids,
        CancellationToken cancellationToken)
    {
        var capacity = uuids.TryGetNonEnumeratedCount(out var count) ? count : 0;
        var results = capacity == 0 ? new List<string>() : new List<string>(capacity);
        var seen = capacity == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(capacity, StringComparer.Ordinal);
        foreach (var uuid in uuids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (seen.Add(uuid))
            {
                results.Add(uuid);
            }
        }

        return results;
    }

    private static List<string> MaterializeSortedUuids(IEnumerable<string> uuids)
    {
        var capacity = uuids.TryGetNonEnumeratedCount(out var count) ? count : 0;
        var results = capacity == 0 ? new List<string>() : new List<string>(capacity);
        foreach (var uuid in uuids)
        {
            results.Add(uuid);
        }

        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private static bool GroupMatches(string groupId, IReadOnlyList<string>? groupIds) =>
        groupIds is null || groupIds.Count == 0 || groupIds.Contains(groupId, StringComparer.Ordinal);

    private static string EntityNodeFulltextText(EntityNode node) =>
        $"{node.Name} {node.Summary} {node.GroupId}";

    private static string EntityEdgeFulltextText(EntityEdge edge) =>
        $"{edge.Name} {edge.Fact} {edge.GroupId}";

    private static string EpisodeFulltextText(EpisodicNode episode) =>
        $"{episode.Content} {episode.Source.ToWireValue()} {episode.SourceDescription} {episode.GroupId}";

    private static string CommunityFulltextText(CommunityNode community) =>
        $"{community.Name} {community.GroupId}";

    private TraversalGraph BuildTraversalGraph(IReadOnlyList<string>? groupIds) =>
        new(
            BuildEdgesBySource<EntityEdge>(groupIds),
            BuildEdgesBySource<EpisodicEdge>(groupIds),
            BuildNodeGroupLookup());

    private Dictionary<string, string> BuildNodeGroupLookup()
    {
        var groupIdsByUuid = new Dictionary<string, string>(_nodes.Count, StringComparer.Ordinal);
        foreach (var node in _nodes.Values)
        {
            groupIdsByUuid.Add(node.Uuid, node.GroupId);
        }

        return groupIdsByUuid;
    }

    private Dictionary<string, EntityNode> EntityNodeLookup()
    {
        var nodes = GetNodesFromIndex<EntityNode>(null, allWhenNoGroups: true);
        var nodesByUuid = new Dictionary<string, EntityNode>(nodes.Count, StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            nodesByUuid.Add(nodes[i].Uuid, nodes[i]);
        }

        return nodesByUuid;
    }

    private static Dictionary<string, EntityNode> BuildNodeCandidateLookup(
        List<EntityNode> candidates,
        CompiledSearchFilter compiledFilter)
    {
        var candidateByUuid = new Dictionary<string, EntityNode>(candidates.Count, StringComparer.Ordinal);
        for (var i = 0; i < candidates.Count; i++)
        {
            var node = candidates[i];
            if (SearchFilterMatcher.NodeMatches(node, compiledFilter))
            {
                candidateByUuid.Add(node.Uuid, node);
            }
        }

        return candidateByUuid;
    }

    private static Dictionary<string, EntityEdge> BuildEdgeCandidateLookup(
        List<EntityEdge> candidates,
        CompiledSearchFilter compiledFilter,
        IReadOnlyDictionary<string, EntityNode> nodesByUuid)
    {
        var candidateByUuid = new Dictionary<string, EntityEdge>(candidates.Count, StringComparer.Ordinal);
        for (var i = 0; i < candidates.Count; i++)
        {
            var edge = candidates[i];
            if (SearchFilterMatcher.EdgeMatches(edge, compiledFilter, nodesByUuid))
            {
                candidateByUuid.Add(edge.Uuid, edge);
            }
        }

        return candidateByUuid;
    }

    private static List<SearchHit<EntityNode>> BuildNodeBfsHits(
        IReadOnlyList<string> originNodeUuids,
        int maxDepth,
        int limit,
        TraversalGraph graph,
        Dictionary<string, EntityNode> candidateByUuid)
    {
        var results = new List<SearchHit<EntityNode>>(ResultCapacity(limit, candidateByUuid.Count));
        if (limit <= 0)
        {
            return results;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in TraverseBreadthFirst(originNodeUuids, maxDepth, graph))
        {
            if (step.TargetNodeUuid is null
                || !step.TargetMatchesOriginGroup
                || !candidateByUuid.TryGetValue(step.TargetNodeUuid, out var node)
                || !seen.Add(step.TargetNodeUuid))
            {
                continue;
            }

            results.Add(new SearchHit<EntityNode>((EntityNode)CloneNode(node), 1f / step.Depth));
            if (results.Count >= limit)
            {
                break;
            }
        }

        return results;
    }

    private static List<SearchHit<EntityEdge>> BuildEdgeBfsHits(
        IReadOnlyList<string> originNodeUuids,
        int maxDepth,
        int limit,
        TraversalGraph graph,
        Dictionary<string, EntityEdge> candidateByUuid)
    {
        var results = new List<SearchHit<EntityEdge>>(ResultCapacity(limit, candidateByUuid.Count));
        if (limit <= 0)
        {
            return results;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in TraverseBreadthFirst(originNodeUuids, maxDepth, graph))
        {
            if (step.Edge is null
                || !candidateByUuid.TryGetValue(step.Edge.Uuid, out var edge)
                || !seen.Add(step.Edge.Uuid))
            {
                continue;
            }

            results.Add(new SearchHit<EntityEdge>((EntityEdge)CloneEdge(edge), 1f / step.Depth));
            if (results.Count >= limit)
            {
                break;
            }
        }

        return results;
    }

    private HashSet<string> BuildAdjacentNodeLookup(string centerNodeUuid)
    {
        var adjacentNodeUuids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edgeUuid in GetIndexedUuids(_entityEdgeUuidsByNodeUuid, centerNodeUuid))
        {
            if (!_edges.TryGetValue(edgeUuid, out var stored) || stored is not EntityEdge edge)
            {
                continue;
            }

            adjacentNodeUuids.Add(edge.SourceNodeUuid == centerNodeUuid
                ? edge.TargetNodeUuid
                : edge.SourceNodeUuid);
        }

        return adjacentNodeUuids;
    }

    private static List<SearchRank> BuildNodeDistanceRanks(
        IReadOnlyList<string> nodeUuids,
        string centerNodeUuid,
        HashSet<string> adjacentNodeUuids,
        float minScore)
    {
        var centerRanks = new List<SearchRank>(1);
        var adjacentRanks = new List<SearchRank>(nodeUuids.Count);
        var distantRanks = new List<SearchRank>(nodeUuids.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < nodeUuids.Count; i++)
        {
            var uuid = nodeUuids[i];
            if (!seen.Add(uuid))
            {
                continue;
            }

            var score = NodeDistanceScore(adjacentNodeUuids, uuid, centerNodeUuid);
            if (score < minScore)
            {
                continue;
            }

            AddNodeDistanceRank(centerRanks, adjacentRanks, distantRanks, uuid, score);
        }

        var results = new List<SearchRank>(
            centerRanks.Count + adjacentRanks.Count + distantRanks.Count);
        results.AddRange(centerRanks);
        results.AddRange(adjacentRanks);
        results.AddRange(distantRanks);
        return results;
    }

    private Dictionary<string, int> BuildMentionCountLookup()
    {
        var mentionCounts = new Dictionary<string, int>(_episodicEdgeUuidsByTargetNodeUuid.Count, StringComparer.Ordinal);
        foreach (var pair in _episodicEdgeUuidsByTargetNodeUuid)
        {
            mentionCounts[pair.Key] = pair.Value.Count;
        }

        return mentionCounts;
    }

    private static List<SearchRank> BuildEpisodeMentionRanks(
        IReadOnlyList<string> nodeUuids,
        Dictionary<string, int> mentionCounts,
        float minScore)
    {
        var ranks = new List<(SearchRank Rank, int Index)>(nodeUuids.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < nodeUuids.Count; i++)
        {
            var uuid = nodeUuids[i];
            if (!seen.Add(uuid))
            {
                continue;
            }

            var mentions = mentionCounts.GetValueOrDefault(uuid);
            var score = mentions > 0 ? mentions : float.PositiveInfinity;
            if (score >= minScore)
            {
                ranks.Add((new SearchRank(uuid, score), i));
            }
        }

        ranks.Sort(static (left, right) =>
        {
            var scoreComparison = left.Rank.Score.CompareTo(right.Rank.Score);
            return scoreComparison != 0
                ? scoreComparison
                : left.Index.CompareTo(right.Index);
        });

        var results = new List<SearchRank>(ranks.Count);
        for (var i = 0; i < ranks.Count; i++)
        {
            results.Add(ranks[i].Rank);
        }

        return results;
    }

    private static int ResultCapacity(int limit, int maximum) =>
        limit <= 0 ? 0 : Math.Min(limit, maximum);

    private static float NodeDistanceScore(
        HashSet<string> adjacentNodeUuids,
        string nodeUuid,
        string centerNodeUuid)
    {
        if (nodeUuid == centerNodeUuid)
        {
            return 10;
        }

        return adjacentNodeUuids.Contains(nodeUuid) ? 1 : 0;
    }

    private static void AddNodeDistanceRank(
        List<SearchRank> centerRanks,
        List<SearchRank> adjacentRanks,
        List<SearchRank> distantRanks,
        string uuid,
        float score)
    {
        var rank = new SearchRank(uuid, score);
        if (score >= 10)
        {
            centerRanks.Add(rank);
        }
        else if (score >= 1)
        {
            adjacentRanks.Add(rank);
        }
        else
        {
            distantRanks.Add(rank);
        }
    }

    private static IEnumerable<TraversalStep> TraverseBreadthFirst(
        IReadOnlyList<string> originNodeUuids,
        int maxDepth,
        TraversalGraph graph)
    {
        var queue = new Queue<(string NodeUuid, int Depth, string OriginGroupId)>();
        var visited = new HashSet<(string NodeUuid, string OriginGroupId)>();

        for (var i = 0; i < originNodeUuids.Count; i++)
        {
            var origin = originNodeUuids[i];
            if (string.IsNullOrEmpty(origin))
            {
                continue;
            }

            if (!graph.NodeGroupIdsByUuid.TryGetValue(origin, out var originGroupId))
            {
                continue;
            }

            queue.Enqueue((origin, 0, originGroupId));
            visited.Add((origin, originGroupId));
        }

        while (queue.Count > 0)
        {
            var (nodeUuid, depth, originGroupId) = queue.Dequeue();
            if (depth >= maxDepth)
            {
                continue;
            }

            var nextDepth = depth + 1;
            if (graph.EpisodicEdgesBySource.TryGetValue(nodeUuid, out var episodicEdges))
            {
                foreach (var edge in episodicEdges)
                {
                    if (visited.Add((edge.TargetNodeUuid, originGroupId)))
                    {
                        queue.Enqueue((edge.TargetNodeUuid, nextDepth, originGroupId));
                    }

                    graph.NodeGroupIdsByUuid.TryGetValue(edge.TargetNodeUuid, out var targetGroupId);
                    yield return new TraversalStep(null, edge.TargetNodeUuid, originGroupId, targetGroupId, nextDepth);
                }
            }

            if (!graph.EntityEdgesBySource.TryGetValue(nodeUuid, out var entityEdges))
            {
                continue;
            }

            foreach (var edge in entityEdges)
            {
                if (visited.Add((edge.TargetNodeUuid, originGroupId)))
                {
                    queue.Enqueue((edge.TargetNodeUuid, nextDepth, originGroupId));
                }

                graph.NodeGroupIdsByUuid.TryGetValue(edge.TargetNodeUuid, out var targetGroupId);
                yield return new TraversalStep(edge, edge.TargetNodeUuid, originGroupId, targetGroupId, nextDepth);
            }
        }
    }

    private static Node CloneNode(Node node) =>
        node switch
        {
            EntityNode entity => new EntityNode
            {
                Uuid = entity.Uuid,
                Name = entity.Name,
                GroupId = entity.GroupId,
                Labels = CopyList(entity.Labels),
                CreatedAt = entity.CreatedAt,
                NameEmbedding = CopyNullableList(entity.NameEmbedding),
                Summary = entity.Summary,
                Attributes = CloneDictionary(entity.Attributes)
            },
            EpisodicNode episode => new EpisodicNode
            {
                Uuid = episode.Uuid,
                Name = episode.Name,
                GroupId = episode.GroupId,
                Labels = CopyList(episode.Labels),
                CreatedAt = episode.CreatedAt,
                Source = episode.Source,
                SourceDescription = episode.SourceDescription,
                Content = episode.Content,
                ValidAt = episode.ValidAt,
                EntityEdges = CopyList(episode.EntityEdges),
                EpisodeMetadata = episode.EpisodeMetadata is null ? null : CloneDictionary(episode.EpisodeMetadata)
            },
            CommunityNode community => new CommunityNode
            {
                Uuid = community.Uuid,
                Name = community.Name,
                GroupId = community.GroupId,
                Labels = CopyList(community.Labels),
                CreatedAt = community.CreatedAt,
                NameEmbedding = CopyNullableList(community.NameEmbedding),
                Summary = community.Summary
            },
            SagaNode saga => new SagaNode
            {
                Uuid = saga.Uuid,
                Name = saga.Name,
                GroupId = saga.GroupId,
                Labels = CopyList(saga.Labels),
                CreatedAt = saga.CreatedAt,
                Summary = saga.Summary,
                FirstEpisodeUuid = saga.FirstEpisodeUuid,
                LastEpisodeUuid = saga.LastEpisodeUuid,
                LastSummarizedAt = saga.LastSummarizedAt,
                LastSummarizedEpisodeValidAt = saga.LastSummarizedEpisodeValidAt
            },
            _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType().Name)
        };

    private static TNode ProjectNodeEmbedding<TNode>(TNode node, bool withEmbeddings)
        where TNode : Node
    {
        if (withEmbeddings)
        {
            return node;
        }

        if (node is EntityNode entity)
        {
            entity.NameEmbedding = null;
        }

        return node;
    }

    private static Edge CloneEdge(Edge edge) =>
        edge switch
        {
            EntityEdge entity => new EntityEdge
            {
                Uuid = entity.Uuid,
                GroupId = entity.GroupId,
                SourceNodeUuid = entity.SourceNodeUuid,
                TargetNodeUuid = entity.TargetNodeUuid,
                CreatedAt = entity.CreatedAt,
                Name = entity.Name,
                Fact = entity.Fact,
                FactEmbedding = CopyNullableList(entity.FactEmbedding),
                Episodes = CopyList(entity.Episodes),
                ExpiredAt = entity.ExpiredAt,
                ValidAt = entity.ValidAt,
                InvalidAt = entity.InvalidAt,
                ReferenceTime = entity.ReferenceTime,
                Attributes = CloneDictionary(entity.Attributes)
            },
            EpisodicEdge episodic => CopyBase(new EpisodicEdge(), episodic),
            CommunityEdge community => CopyBase(new CommunityEdge(), community),
            HasEpisodeEdge hasEpisode => CopyBase(new HasEpisodeEdge(), hasEpisode),
            NextEpisodeEdge nextEpisode => CopyBase(new NextEpisodeEdge(), nextEpisode),
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge.GetType().Name)
        };

    private static TEdge ProjectEdgeEmbedding<TEdge>(TEdge edge, bool withEmbeddings)
        where TEdge : Edge
    {
        if (withEmbeddings)
        {
            return edge;
        }

        if (edge is EntityEdge entity)
        {
            entity.FactEmbedding = null;
        }

        return edge;
    }

    private static T CopyBase<T>(T target, Edge source) where T : Edge
    {
        target.Uuid = source.Uuid;
        target.GroupId = source.GroupId;
        target.SourceNodeUuid = source.SourceNodeUuid;
        target.TargetNodeUuid = source.TargetNodeUuid;
        target.CreatedAt = source.CreatedAt;
        return target;
    }

    private static List<T> CopyList<T>(IReadOnlyList<T> source)
    {
        var copy = new List<T>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            copy.Add(source[i]);
        }

        return copy;
    }

    private static List<T>? CopyNullableList<T>(IReadOnlyList<T>? source) =>
        source is null ? null : CopyList(source);

    private static Dictionary<string, object?> CloneDictionary(IDictionary<string, object?> source)
    {
        var clone = new Dictionary<string, object?>(source.Count, StringComparer.Ordinal);
        foreach (var pair in source)
        {
            clone[pair.Key] = CloneMetadataValue(pair.Value);
        }

        return clone;
    }

    private static object? CloneMetadataValue(object? value)
    {
        if (value is null || IsImmutableScalar(value))
        {
            return value;
        }

        return value switch
        {
            JsonNode node => node.DeepClone(),
            JsonElement element => element.Clone(),
            IDictionary<string, object?> dictionary => CloneDictionary(dictionary),
            IEnumerable<object?> values => CloneMetadataValues(values),
            _ => CloneJsonCompatibleValue(value)
        };
    }

    private static List<object?> CloneMetadataValues(IEnumerable<object?> values)
    {
        var clone = values is ICollection<object?> collection
            ? new List<object?>(collection.Count)
            : [];

        foreach (var value in values)
        {
            clone.Add(CloneMetadataValue(value));
        }

        return clone;
    }

    private static object? CloneJsonCompatibleValue(object value)
    {
        var node = JsonSerializer.SerializeToNode(value, GraphitiJsonSerializer.Options);
        return ConvertJsonNode(node);
    }

    private static object? ConvertJsonNode(JsonNode? node) =>
        node switch
        {
            null => null,
            JsonObject jsonObject => ConvertJsonObject(jsonObject),
            JsonArray jsonArray => ConvertJsonArray(jsonArray),
            JsonValue jsonValue => ConvertJsonValue(jsonValue),
            _ => node.ToJsonString(GraphitiJsonSerializer.Options)
        };

    private static Dictionary<string, object?> ConvertJsonObject(JsonObject jsonObject)
    {
        var dictionary = new Dictionary<string, object?>(jsonObject.Count, StringComparer.Ordinal);
        foreach (var pair in jsonObject)
        {
            dictionary[pair.Key] = ConvertJsonNode(pair.Value);
        }

        return dictionary;
    }

    private static List<object?> ConvertJsonArray(JsonArray jsonArray)
    {
        var values = new List<object?>(jsonArray.Count);
        foreach (var item in jsonArray)
        {
            values.Add(ConvertJsonNode(item));
        }

        return values;
    }

    private static object? ConvertJsonValue(JsonValue value)
    {
        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }

        if (value.TryGetValue<long>(out var integer))
        {
            return integer;
        }

        if (value.TryGetValue<decimal>(out var decimalValue))
        {
            return decimalValue;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        return value.DeepClone();
    }

    private static bool IsImmutableScalar(object value)
    {
        var type = value.GetType();
        return type.IsEnum
            || value is string
                or bool
                or char
                or byte
                or sbyte
                or short
                or ushort
                or int
                or uint
                or long
                or ulong
                or float
                or double
                or decimal
                or DateTime
                or DateTimeOffset
                or Guid
                or TimeSpan;
    }

    private sealed record TraversalGraph(
        IReadOnlyDictionary<string, List<EntityEdge>> EntityEdgesBySource,
        IReadOnlyDictionary<string, List<EpisodicEdge>> EpisodicEdgesBySource,
        IReadOnlyDictionary<string, string> NodeGroupIdsByUuid);

    private sealed record TraversalStep(
        EntityEdge? Edge,
        string? TargetNodeUuid,
        string OriginGroupId,
        string? TargetGroupId,
        int Depth)
    {
        public bool TargetMatchesOriginGroup =>
            string.Equals(TargetGroupId, OriginGroupId, StringComparison.Ordinal);
    }
}
