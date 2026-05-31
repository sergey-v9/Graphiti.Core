using System.Text.Json;
using System.Text.Json.Nodes;

namespace Graphiti.Core.Drivers;

/// <summary>
/// An in-process graph driver that stores all nodes and edges in memory with secondary indexes for fast
/// lookups. It implements both persistence (<see cref="GraphDriverBase"/>) and search
/// (<see cref="ISearchGraphDriver"/>), making it ideal for tests, examples, and ephemeral graphs.
/// All mutating operations are guarded by a lock; the driver can be cloned to snapshot its state.
/// </summary>
public sealed class InMemoryGraphDriver : GraphDriverBase, ISearchGraphDriver
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

    public override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public override Task CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

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

    public override Task<IReadOnlyList<string>> GetEntityGroupIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<string> groupIds = _entityNodeUuidsByGroup
                .Where(pair => pair.Value.Count > 0 && !string.IsNullOrEmpty(pair.Key))
                .Select(pair => pair.Key)
                .Order(StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(groupIds);
        }
    }

    public override Task<IReadOnlyList<string>> GetCommunityGroupIdsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<string> groupIds = _communityNodeUuidsByGroup
                .Where(pair => pair.Value.Count > 0 && !string.IsNullOrEmpty(pair.Key))
                .Select(pair => pair.Key)
                .Order(StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(groupIds);
        }
    }

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

    public override Task DeleteNodeAsync(string uuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_nodes.Remove(uuid, out var node))
            {
                RemoveNodeIndexes(node);
            }

            foreach (var edgeUuid in GetIndexedUuids(_incidentEdgeUuidsByNodeUuid, uuid).ToList())
            {
                RemoveEdgeByUuid(edgeUuid);
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

    public override Task DeleteNodesByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        cancellationToken.ThrowIfCancellationRequested();
        List<string> uuids;
        lock (_gate)
        {
            uuids = GetIndexedUuids(_nodeUuidsByGroup, groupId).ToList();
        }

        return DeleteNodesByUuidsAsync(uuids, batchSize, cancellationToken);
    }

    public override async Task DeleteNodesByUuidsAsync(IEnumerable<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var batch in uuids.ToList().Chunk(batchSize))
        {
            foreach (var uuid in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DeleteNodeAsync(uuid, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public override Task DeleteEdgeAsync(string uuid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            RemoveEdgeByUuid(uuid);
        }

        return Task.CompletedTask;
    }

    public override Task DeleteEdgesByUuidsAsync(IEnumerable<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        cancellationToken.ThrowIfCancellationRequested();
        var uuidList = uuids.ToList();
        if (uuidList.Count == 0)
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            foreach (var uuid in uuidList)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RemoveEdgeByUuid(uuid);
            }
        }

        return Task.CompletedTask;
    }

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
                var groupSet = groupIds.ToHashSet(StringComparer.Ordinal);
                var nodeUuids = groupSet
                    .SelectMany(groupId => GetIndexedUuids(_nodeUuidsByGroup, groupId))
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var nodeUuid in nodeUuids)
                {
                    if (_nodes.Remove(nodeUuid, out var node))
                    {
                        RemoveNodeIndexes(node);
                    }
                }

                foreach (var edgeUuid in nodeUuids
                             .SelectMany(nodeUuid => GetIndexedUuids(_incidentEdgeUuidsByNodeUuid, nodeUuid))
                             .Distinct(StringComparer.Ordinal)
                             .ToList())
                {
                    RemoveEdgeByUuid(edgeUuid);
                }
            }
        }

        return Task.CompletedTask;
    }

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

    public override Task<IReadOnlyList<TNode>> GetNodesByUuidsAsync<TNode>(
        IEnumerable<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uuidSet = uuids.ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            IReadOnlyList<TNode> nodes = uuidSet
                .Select(uuid => _nodes.GetValueOrDefault(uuid))
                .OfType<TNode>()
                .Where(node => groupId is null || node.GroupId == groupId)
                .Select(node => (TNode)CloneNode(node))
                .ToList();
            return Task.FromResult(nodes);
        }
    }

    public override Task<IReadOnlyList<TNode>> GetNodesByGroupIdsAsync<TNode>(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var groupSet = groupIds.ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            IEnumerable<TNode> query = GetNodesFromIndex<TNode>(groupSet, allWhenNoGroups: false);

            if (!string.IsNullOrEmpty(uuidCursor))
            {
                query = query.Where(node => string.CompareOrdinal(node.Uuid, uuidCursor) < 0);
            }

            query = query.OrderByDescending(node => node.Uuid);
            if (limit is not null)
            {
                query = query.Take(limit.Value);
            }

            IReadOnlyList<TNode> nodes = query
                .Select(node => ProjectNodeEmbedding((TNode)CloneNode(node), withEmbeddings))
                .ToList();
            return Task.FromResult(nodes);
        }
    }

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

    public override Task<IReadOnlyList<T>> GetEdgesByUuidsAsync<T>(IEnumerable<string> uuids, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uuidSet = uuids.ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            IReadOnlyList<T> edges = uuidSet
                .Select(uuid => _edges.GetValueOrDefault(uuid))
                .OfType<T>()
                .Select(edge => (T)CloneEdge(edge))
                .ToList();
            return Task.FromResult(edges);
        }
    }

    public override Task<IReadOnlyList<T>> GetEdgesByGroupIdsAsync<T>(
        IEnumerable<string> groupIds,
        int? limit = null,
        string? uuidCursor = null,
        bool withEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var groupSet = groupIds.ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            IEnumerable<T> query = GetEdgesFromIndex<T>(groupSet, allWhenNoGroups: false);

            if (!string.IsNullOrEmpty(uuidCursor))
            {
                query = query.Where(edge => string.CompareOrdinal(edge.Uuid, uuidCursor) < 0);
            }

            query = query.OrderByDescending(edge => edge.Uuid);
            if (limit is not null)
            {
                query = query.Take(limit.Value);
            }

            IReadOnlyList<T> edges = query
                .Select(edge => ProjectEdgeEmbedding((T)CloneEdge(edge), withEmbeddings))
                .ToList();
            return Task.FromResult(edges);
        }
    }

    public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesBetweenNodesAsync(
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<EntityEdge> edges = GetIndexedUuids(
                    _entityEdgeUuidsByEndpoints,
                    (sourceNodeUuid, targetNodeUuid))
                .Order(StringComparer.Ordinal)
                .Select(uuid => _edges.GetValueOrDefault(uuid))
                .OfType<EntityEdge>()
                .Select(edge => (EntityEdge)CloneEdge(edge))
                .ToList();
            return Task.FromResult(edges);
        }
    }

    public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesByNodeUuidAsync(
        string nodeUuid,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<EntityEdge> edges = GetIndexedUuids(_entityEdgeUuidsByNodeUuid, nodeUuid)
                .Order(StringComparer.Ordinal)
                .Select(uuid => _edges.GetValueOrDefault(uuid))
                .OfType<EntityEdge>()
                .Select(edge => (EntityEdge)CloneEdge(edge))
                .ToList();
            return Task.FromResult(edges);
        }
    }

    public override Task<IReadOnlyList<EpisodicNode>> GetEpisodesByEntityNodeUuidAsync(
        string entityNodeUuid,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var episodeUuids = GetIndexedUuids(_episodicEdgeUuidsByTargetNodeUuid, entityNodeUuid)
                .Select(uuid => _edges.GetValueOrDefault(uuid))
                .OfType<EpisodicEdge>()
                .Select(edge => edge.SourceNodeUuid)
                .ToHashSet(StringComparer.Ordinal);

            IReadOnlyList<EpisodicNode> episodes = episodeUuids
                .Select(uuid => _nodes.GetValueOrDefault(uuid))
                .OfType<EpisodicNode>()
                .Select(episode => (EpisodicNode)CloneNode(episode))
                .ToList();

            return Task.FromResult(episodes);
        }
    }

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
            IEnumerable<EpisodicNode> episodes = groupIds is { Count: > 0 }
                ? GetNodesFromIndex<EpisodicNode>(groupIds, allWhenNoGroups: false)
                : GetNodesFromIndex<EpisodicNode>(null, allWhenNoGroups: true);

            if (source is not null)
            {
                episodes = episodes.Where(episode => episode.Source == source);
            }

            if (!string.IsNullOrEmpty(saga))
            {
                var sagaNode = (groupIds is { Count: > 0 }
                        ? groupIds.SelectMany(groupId => GetIndexedUuids(_sagaNodeUuidsByGroupAndName, (groupId, saga)))
                        : _sagaNodeUuidsByGroupAndName
                            .Where(pair => string.Equals(pair.Key.Name, saga, StringComparison.Ordinal))
                            .SelectMany(pair => pair.Value))
                    .Select(uuid => _nodes.GetValueOrDefault(uuid))
                    .OfType<SagaNode>()
                    .FirstOrDefault();

                if (sagaNode is null)
                {
                    return Task.FromResult<IReadOnlyList<EpisodicNode>>(Array.Empty<EpisodicNode>());
                }

                var sagaEpisodeUuids = GetIndexedUuids(_hasEpisodeEdgeUuidsBySagaUuid, sagaNode.Uuid)
                    .Select(uuid => _edges.GetValueOrDefault(uuid))
                    .OfType<HasEpisodeEdge>()
                    .Select(edge => edge.TargetNodeUuid)
                    .ToHashSet(StringComparer.Ordinal);
                episodes = episodes.Where(episode => sagaEpisodeUuids.Contains(episode.Uuid));
            }

            IReadOnlyList<EpisodicNode> results = episodes
                .Where(episode => GraphitiHelpers.EnsureUtc(episode.ValidAt) <= referenceUtc)
                .OrderByDescending(episode => episode.ValidAt)
                .Take(lastN)
                .Reverse()
                .Select(episode => (EpisodicNode)CloneNode(episode))
                .ToList();

            return Task.FromResult(results);
        }
    }

    public override Task<IReadOnlyList<EntityNode>> GetMentionedNodesAsync(
        IReadOnlyList<EpisodicNode> episodes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var episodeUuids = episodes.Select(episode => episode.Uuid).ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            var nodeUuids = episodeUuids
                .SelectMany(episodeUuid => GetIndexedUuids(_episodicEdgeUuidsBySourceNodeUuid, episodeUuid))
                .Select(uuid => _edges.GetValueOrDefault(uuid))
                .OfType<EpisodicEdge>()
                .Select(edge => edge.TargetNodeUuid)
                .ToHashSet(StringComparer.Ordinal);

            IReadOnlyList<EntityNode> nodes = nodeUuids
                .Select(uuid => _nodes.GetValueOrDefault(uuid))
                .OfType<EntityNode>()
                .Select(node => (EntityNode)CloneNode(node))
                .ToList();

            return Task.FromResult(nodes);
        }
    }

    public override Task<IReadOnlyList<CommunityNode>> GetCommunitiesByNodesAsync(
        IReadOnlyList<EntityNode> nodes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nodeUuids = nodes.Select(node => node.Uuid).ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            var communityUuids = nodeUuids
                .SelectMany(nodeUuid => GetIndexedUuids(_communityEdgeUuidsByTargetNodeUuid, nodeUuid))
                .Select(uuid => _edges.GetValueOrDefault(uuid))
                .OfType<CommunityEdge>()
                .Select(edge => edge.SourceNodeUuid)
                .ToHashSet(StringComparer.Ordinal);

            IReadOnlyList<CommunityNode> communities = communityUuids
                .Select(uuid => _nodes.GetValueOrDefault(uuid))
                .OfType<CommunityNode>()
                .Select(node => (CommunityNode)CloneNode(node))
                .ToList();

            return Task.FromResult(communities);
        }
    }

    public override Task<SagaNode?> FindSagaByNameAsync(string name, string groupId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var saga = GetIndexedUuids(_sagaNodeUuidsByGroupAndName, (groupId, name))
                .Order(StringComparer.Ordinal)
                .Select(uuid => _nodes.GetValueOrDefault(uuid))
                .OfType<SagaNode>()
                .FirstOrDefault();
            return Task.FromResult(saga is null ? null : (SagaNode)CloneNode(saga));
        }
    }

    public override Task<string?> GetSagaPreviousEpisodeUuidAsync(
        string sagaUuid,
        string currentEpisodeUuid,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var episodeUuids = GetIndexedUuids(_hasEpisodeEdgeUuidsBySagaUuid, sagaUuid)
                .Select(uuid => _edges.GetValueOrDefault(uuid))
                .OfType<HasEpisodeEdge>()
                .Where(edge => edge.TargetNodeUuid != currentEpisodeUuid)
                .Select(edge => edge.TargetNodeUuid)
                .ToHashSet(StringComparer.Ordinal);

            var previous = episodeUuids
                .Select(uuid => _nodes.GetValueOrDefault(uuid))
                .OfType<EpisodicNode>()
                .OrderByDescending(episode => episode.ValidAt)
                .ThenByDescending(episode => episode.CreatedAt)
                .FirstOrDefault();

            return Task.FromResult(previous?.Uuid);
        }
    }

    public override Task<IReadOnlyList<SagaEpisodeContent>> GetSagaEpisodeContentsAsync(
        string sagaUuid,
        DateTime? since = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var episodeUuids = GetIndexedUuids(_hasEpisodeEdgeUuidsBySagaUuid, sagaUuid)
                .Select(uuid => _edges.GetValueOrDefault(uuid))
                .OfType<HasEpisodeEdge>()
                .Select(edge => edge.TargetNodeUuid)
                .ToHashSet(StringComparer.Ordinal);

            IEnumerable<EpisodicNode> query = episodeUuids
                .Select(uuid => _nodes.GetValueOrDefault(uuid))
                .OfType<EpisodicNode>();

            if (since is not null)
            {
                query = query
                    .Where(episode => episode.CreatedAt > since.Value)
                    .OrderBy(episode => episode.ValidAt)
                    .ThenBy(episode => episode.CreatedAt);
            }
            else
            {
                query = query
                    .OrderByDescending(episode => episode.ValidAt)
                    .ThenByDescending(episode => episode.CreatedAt)
                    .Take(limit)
                    .Reverse();
            }

            IReadOnlyList<SagaEpisodeContent> results = query
                .Take(limit)
                .Where(episode => !string.IsNullOrEmpty(episode.Content))
                .Select(episode => new SagaEpisodeContent(episode.Content, episode.ValidAt))
                .ToList();

            return Task.FromResult(results);
        }
    }

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
        IReadOnlyList<SearchHit<EntityNode>> results = Bm25TextScorer
            .Rank(
                candidates.Where(node => SearchFilterMatcher.NodeMatches(node, compiledFilter)),
                EntityNodeFulltextText,
                query,
                limit)
            .Select(hit => new SearchHit<EntityNode>((EntityNode)CloneNode(hit.Item), hit.Score))
            .ToList();
        return Task.FromResult(results);
    }

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
        IReadOnlyList<SearchHit<EntityNode>> results = SearchUtilities
            .TopByScore(
                candidates.Where(node => SearchFilterMatcher.NodeMatches(node, compiledFilter)),
                node => scorer.Score(node.NameEmbedding),
                limit,
                minScore,
                includeMinScore: false)
            .Select(hit => new SearchHit<EntityNode>((EntityNode)CloneNode(hit.Item), hit.Score))
            .ToList();
        return Task.FromResult(results);
    }

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
        IReadOnlyList<SearchHit<EntityEdge>> results = Bm25TextScorer
            .Rank(
                candidates.Where(edge => SearchFilterMatcher.EdgeMatches(edge, compiledFilter, nodesByUuid)),
                EntityEdgeFulltextText,
                query,
                limit)
            .Select(hit => new SearchHit<EntityEdge>((EntityEdge)CloneEdge(hit.Item), hit.Score))
            .ToList();
        return Task.FromResult(results);
    }

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
            candidates = GetEdgesFromIndex<EntityEdge>(groupIds, allWhenNoGroups: true)
                .Where(edge => sourceNodeUuid is null || edge.SourceNodeUuid == sourceNodeUuid)
                .Where(edge => targetNodeUuid is null || edge.TargetNodeUuid == targetNodeUuid)
                .ToList();
            nodesByUuid = EntityNodeLookup();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var scorer = SearchUtilities.CreateCosineSimilarityScorer(searchVector);
        IReadOnlyList<SearchHit<EntityEdge>> results = SearchUtilities
            .TopByScore(
                candidates.Where(edge => SearchFilterMatcher.EdgeMatches(edge, compiledFilter, nodesByUuid)),
                edge => scorer.Score(edge.FactEmbedding),
                limit,
                minScore,
                includeMinScore: false)
            .Select(hit => new SearchHit<EntityEdge>((EntityEdge)CloneEdge(hit.Item), hit.Score))
            .ToList();
        return Task.FromResult(results);
    }

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
        IReadOnlyList<SearchHit<EpisodicNode>> results = Bm25TextScorer
            .Rank(
                candidates,
                EpisodeFulltextText,
                query,
                limit)
            .Select(hit => new SearchHit<EpisodicNode>((EpisodicNode)CloneNode(hit.Item), hit.Score))
            .ToList();
        return Task.FromResult(results);
    }

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
        IReadOnlyList<SearchHit<CommunityNode>> results = Bm25TextScorer
            .Rank(
                candidates,
                CommunityFulltextText,
                query,
                limit)
            .Select(hit => new SearchHit<CommunityNode>((CommunityNode)CloneNode(hit.Item), hit.Score))
            .ToList();
        return Task.FromResult(results);
    }

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
        IReadOnlyList<SearchHit<CommunityNode>> results = SearchUtilities
            .TopByScore(
                candidates,
                node => scorer.Score(node.NameEmbedding),
                limit,
                minScore,
                includeMinScore: false)
            .Select(hit => new SearchHit<CommunityNode>((CommunityNode)CloneNode(hit.Item), hit.Score))
            .ToList();
        return Task.FromResult(results);
    }

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
            return _nodes.Values.Select(CloneNode).ToList();
        }
    }

    internal IReadOnlyList<Edge> SnapshotEdges()
    {
        lock (_gate)
        {
            return _edges.Values.Select(CloneEdge).ToList();
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
        return GetUuidsByGroups(index, groupIds, allWhenNoGroups)
            .Select(uuid => _nodes.GetValueOrDefault(uuid))
            .OfType<TNode>()
            .ToList();
    }

    private List<TEdge> GetEdgesFromIndex<TEdge>(
        IEnumerable<string>? groupIds,
        bool allWhenNoGroups)
        where TEdge : Edge =>
        GetUuidsByGroups(_edgeUuidsByGroup, groupIds, allWhenNoGroups)
            .Select(uuid => _edges.GetValueOrDefault(uuid))
            .OfType<TEdge>()
            .ToList();

    private Dictionary<string, List<TEdge>> BuildEdgesBySource<TEdge>(
        IReadOnlyList<string>? groupIds)
        where TEdge : Edge
    {
        var result = new Dictionary<string, List<TEdge>>(StringComparer.Ordinal);
        foreach (var edge in GetEdgesFromIndex<TEdge>(groupIds, allWhenNoGroups: true)
                     .OrderBy(edge => edge.Uuid, StringComparer.Ordinal))
        {
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

    private static IEnumerable<string> GetUuidsByGroups(
        Dictionary<string, HashSet<string>> index,
        IEnumerable<string>? groupIds,
        bool allWhenNoGroups)
    {
        if (groupIds is null)
        {
            return allWhenNoGroups
                ? index.Values.SelectMany(uuids => uuids)
                : Array.Empty<string>();
        }

        var groupSet = groupIds.ToHashSet(StringComparer.Ordinal);
        if (groupSet.Count == 0)
        {
            return allWhenNoGroups
                ? index.Values.SelectMany(uuids => uuids)
                : Array.Empty<string>();
        }

        return groupSet.SelectMany(groupId => GetIndexedUuids(index, groupId)).Distinct(StringComparer.Ordinal);
    }

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

    private static IEnumerable<string> GetIndexedUuids<TKey>(
        Dictionary<TKey, HashSet<string>> index,
        TKey key)
        where TKey : notnull =>
        index.TryGetValue(key, out var uuids)
            ? uuids
            : Array.Empty<string>();

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
            _nodes.Values.ToDictionary(node => node.Uuid, node => node.GroupId, StringComparer.Ordinal));

    private Dictionary<string, EntityNode> EntityNodeLookup() =>
        GetNodesFromIndex<EntityNode>(null, allWhenNoGroups: true)
            .ToDictionary(node => node.Uuid, StringComparer.Ordinal);

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

        foreach (var origin in originNodeUuids.Where(origin => !string.IsNullOrEmpty(origin)))
        {
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
                Labels = entity.Labels.ToList(),
                CreatedAt = entity.CreatedAt,
                NameEmbedding = entity.NameEmbedding?.ToList(),
                Summary = entity.Summary,
                Attributes = CloneDictionary(entity.Attributes)
            },
            EpisodicNode episode => new EpisodicNode
            {
                Uuid = episode.Uuid,
                Name = episode.Name,
                GroupId = episode.GroupId,
                Labels = episode.Labels.ToList(),
                CreatedAt = episode.CreatedAt,
                Source = episode.Source,
                SourceDescription = episode.SourceDescription,
                Content = episode.Content,
                ValidAt = episode.ValidAt,
                EntityEdges = episode.EntityEdges.ToList(),
                EpisodeMetadata = episode.EpisodeMetadata is null ? null : CloneDictionary(episode.EpisodeMetadata)
            },
            CommunityNode community => new CommunityNode
            {
                Uuid = community.Uuid,
                Name = community.Name,
                GroupId = community.GroupId,
                Labels = community.Labels.ToList(),
                CreatedAt = community.CreatedAt,
                NameEmbedding = community.NameEmbedding?.ToList(),
                Summary = community.Summary
            },
            SagaNode saga => new SagaNode
            {
                Uuid = saga.Uuid,
                Name = saga.Name,
                GroupId = saga.GroupId,
                Labels = saga.Labels.ToList(),
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
                FactEmbedding = entity.FactEmbedding?.ToList(),
                Episodes = entity.Episodes.ToList(),
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
            IEnumerable<object?> values => values.Select(CloneMetadataValue).ToList(),
            _ => CloneJsonCompatibleValue(value)
        };
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
            JsonObject jsonObject => jsonObject.ToDictionary(
                pair => pair.Key,
                pair => ConvertJsonNode(pair.Value),
                StringComparer.Ordinal),
            JsonArray jsonArray => jsonArray.Select(ConvertJsonNode).ToList(),
            JsonValue jsonValue => ConvertJsonValue(jsonValue),
            _ => node.ToJsonString(GraphitiJsonSerializer.Options)
        };

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
