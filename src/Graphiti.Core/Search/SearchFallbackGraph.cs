namespace Graphiti.Core.Search;

internal static class SearchFallbackGraph
{
    internal static async Task<TraversalGraph> LoadTraversalGraphAsync(
        IGraphDriver driver,
        IReadOnlyList<string>? groupIds,
        CancellationToken cancellationToken)
    {
        var entityEdges = await GetAllEntityEdgesAsync(
            driver,
            groupIds,
            withEmbeddings: false,
            cancellationToken).ConfigureAwait(false);
        var episodicEdges = await GetAllEpisodicEdgesAsync(driver, groupIds, cancellationToken).ConfigureAwait(false);
        var entityNodes = await GetAllEntityNodesAsync(
            driver,
            groupIds,
            withEmbeddings: false,
            cancellationToken).ConfigureAwait(false);
        var episodes = await GetAllEpisodesAsync(driver, groupIds, cancellationToken).ConfigureAwait(false);
        var nodeGroupIds = new Dictionary<string, string>(
            entityNodes.Count + episodes.Count,
            StringComparer.Ordinal);
        foreach (var node in entityNodes)
        {
            nodeGroupIds.TryAdd(node.Uuid, node.GroupId);
        }

        foreach (var episode in episodes)
        {
            nodeGroupIds.TryAdd(episode.Uuid, episode.GroupId);
        }

        return new TraversalGraph(entityEdges, episodicEdges, nodeGroupIds);
    }

    internal static IEnumerable<TraversalStep> TraverseBreadthFirst(
        IReadOnlyList<string> originNodeUuids,
        int maxDepth,
        TraversalGraph graph)
    {
        var entityEdgesBySource = BuildEntityEdgesBySource(graph.EntityEdges);
        var episodicEdgesBySource = BuildEpisodicEdgesBySource(graph.EpisodicEdges);
        var queue = new Queue<(string NodeUuid, int Depth, string OriginGroupId)>();
        var visited = new HashSet<(string NodeUuid, string OriginGroupId)>();

        foreach (var origin in originNodeUuids)
        {
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
            if (episodicEdgesBySource.TryGetValue(nodeUuid, out var episodicEdges))
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

            if (!entityEdgesBySource.TryGetValue(nodeUuid, out var entityEdges))
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

    internal static async Task<IReadOnlyList<EntityEdge>> GetAllEntityEdgesAsync(
        IGraphDriver driver,
        IReadOnlyList<string>? groupIds,
        bool withEmbeddings,
        CancellationToken cancellationToken)
    {
        if (driver is InMemoryGraphDriver memory && (groupIds is null || groupIds.Count == 0))
        {
            return ProjectEntityEdges(memory.SnapshotEdges(), withEmbeddings);
        }

        var resolvedGroupIds = await ResolveEntitySearchGroupIdsAsync(
            driver,
            groupIds,
            cancellationToken).ConfigureAwait(false);
        if (resolvedGroupIds.Count == 0)
        {
            return Array.Empty<EntityEdge>();
        }

        return await driver.GetEdgesByGroupIdsAsync<EntityEdge>(
            resolvedGroupIds,
            withEmbeddings: withEmbeddings,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<IReadOnlyDictionary<string, EntityNode>> LoadEdgeEndpointNodeLookupAsync(
        IGraphDriver driver,
        IReadOnlyList<EntityEdge> edges,
        CompiledSearchFilter searchFilter,
        CancellationToken cancellationToken)
    {
        if (!searchFilter.RequiresEndpointNodeLookup || edges.Count == 0)
        {
            return new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        }

        var nodeUuids = BuildEndpointNodeUuidList(edges);
        if (nodeUuids.Count == 0)
        {
            return new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        }

        var nodes = await driver.GetNodesByUuidsAsync<EntityNode>(
            nodeUuids,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return BuildNodeLookup(nodes);
    }

    internal static async Task<IReadOnlyList<EpisodicEdge>> GetAllEpisodicEdgesAsync(
        IGraphDriver driver,
        IReadOnlyList<string>? groupIds,
        CancellationToken cancellationToken)
    {
        if (driver is InMemoryGraphDriver memory && (groupIds is null || groupIds.Count == 0))
        {
            return ProjectEpisodicEdges(memory.SnapshotEdges());
        }

        var resolvedGroupIds = await ResolveEntitySearchGroupIdsAsync(
            driver,
            groupIds,
            cancellationToken).ConfigureAwait(false);
        if (resolvedGroupIds.Count == 0)
        {
            return Array.Empty<EpisodicEdge>();
        }

        return await driver.GetEdgesByGroupIdsAsync<EpisodicEdge>(
            resolvedGroupIds,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<IReadOnlyList<EntityNode>> GetAllEntityNodesAsync(
        IGraphDriver driver,
        IReadOnlyList<string>? groupIds,
        bool withEmbeddings,
        CancellationToken cancellationToken)
    {
        if (driver is InMemoryGraphDriver memory && (groupIds is null || groupIds.Count == 0))
        {
            return ProjectEntityNodes(memory.SnapshotNodes(), withEmbeddings);
        }

        var resolvedGroupIds = await ResolveEntitySearchGroupIdsAsync(
            driver,
            groupIds,
            cancellationToken).ConfigureAwait(false);
        if (resolvedGroupIds.Count == 0)
        {
            return Array.Empty<EntityNode>();
        }

        return await driver.GetNodesByGroupIdsAsync<EntityNode>(
            resolvedGroupIds,
            withEmbeddings: withEmbeddings,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<IReadOnlyList<EpisodicNode>> GetAllEpisodesAsync(
        IGraphDriver driver,
        IReadOnlyList<string>? groupIds,
        CancellationToken cancellationToken)
    {
        if (driver is InMemoryGraphDriver memory && (groupIds is null || groupIds.Count == 0))
        {
            return ProjectEpisodes(memory.SnapshotNodes());
        }

        var resolvedGroupIds = await ResolveEntitySearchGroupIdsAsync(
            driver,
            groupIds,
            cancellationToken).ConfigureAwait(false);
        if (resolvedGroupIds.Count == 0)
        {
            return Array.Empty<EpisodicNode>();
        }

        return await driver.GetNodesByGroupIdsAsync<EpisodicNode>(
            resolvedGroupIds,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<IReadOnlyList<CommunityNode>> GetAllCommunityNodesAsync(
        IGraphDriver driver,
        IReadOnlyList<string>? groupIds,
        bool withEmbeddings,
        CancellationToken cancellationToken)
    {
        if (driver is InMemoryGraphDriver memory && (groupIds is null || groupIds.Count == 0))
        {
            return ProjectCommunities(memory.SnapshotNodes(), withEmbeddings);
        }

        var resolvedGroupIds = await ResolveCommunitySearchGroupIdsAsync(
            driver,
            groupIds,
            cancellationToken).ConfigureAwait(false);
        if (resolvedGroupIds.Count == 0)
        {
            return Array.Empty<CommunityNode>();
        }

        return await driver.GetNodesByGroupIdsAsync<CommunityNode>(
            resolvedGroupIds,
            withEmbeddings: withEmbeddings,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static float NodeDistanceScore(
        IReadOnlyList<EntityEdge> graphEdges,
        string centerNodeUuid,
        string nodeUuid)
    {
        if (nodeUuid == centerNodeUuid)
        {
            return 10;
        }

        for (var i = 0; i < graphEdges.Count; i++)
        {
            var edge = graphEdges[i];
            if ((edge.SourceNodeUuid == centerNodeUuid && edge.TargetNodeUuid == nodeUuid) ||
                (edge.TargetNodeUuid == centerNodeUuid && edge.SourceNodeUuid == nodeUuid))
            {
                return 1;
            }
        }

        return 0;
    }

    private static Dictionary<string, List<EntityEdge>> BuildEntityEdgesBySource(
        IReadOnlyList<EntityEdge> edges)
    {
        var edgesBySource = new Dictionary<string, List<EntityEdge>>(StringComparer.Ordinal);
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (!edgesBySource.TryGetValue(edge.SourceNodeUuid, out var sourceEdges))
            {
                sourceEdges = new List<EntityEdge>();
                edgesBySource.Add(edge.SourceNodeUuid, sourceEdges);
            }

            sourceEdges.Add(edge);
        }

        return edgesBySource;
    }

    private static Dictionary<string, List<EpisodicEdge>> BuildEpisodicEdgesBySource(
        IReadOnlyList<EpisodicEdge> edges)
    {
        var edgesBySource = new Dictionary<string, List<EpisodicEdge>>(StringComparer.Ordinal);
        for (var i = 0; i < edges.Count; i++)
        {
            var edge = edges[i];
            if (!edgesBySource.TryGetValue(edge.SourceNodeUuid, out var sourceEdges))
            {
                sourceEdges = new List<EpisodicEdge>();
                edgesBySource.Add(edge.SourceNodeUuid, sourceEdges);
            }

            sourceEdges.Add(edge);
        }

        return edgesBySource;
    }

    private static List<EntityEdge> ProjectEntityEdges(
        IReadOnlyList<Edge> snapshot,
        bool withEmbeddings)
    {
        var edges = new List<EntityEdge>(snapshot.Count);
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i] is EntityEdge edge)
            {
                edges.Add(ProjectEdgeEmbedding(edge, withEmbeddings));
            }
        }

        return edges;
    }

    private static List<EpisodicEdge> ProjectEpisodicEdges(IReadOnlyList<Edge> snapshot)
    {
        var edges = new List<EpisodicEdge>(snapshot.Count);
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i] is EpisodicEdge edge)
            {
                edges.Add(edge);
            }
        }

        return edges;
    }

    private static List<EntityNode> ProjectEntityNodes(
        IReadOnlyList<Node> snapshot,
        bool withEmbeddings)
    {
        var nodes = new List<EntityNode>(snapshot.Count);
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i] is EntityNode node)
            {
                nodes.Add(ProjectNodeEmbedding(node, withEmbeddings));
            }
        }

        return nodes;
    }

    private static List<EpisodicNode> ProjectEpisodes(IReadOnlyList<Node> snapshot)
    {
        var episodes = new List<EpisodicNode>(snapshot.Count);
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i] is EpisodicNode episode)
            {
                episodes.Add(episode);
            }
        }

        return episodes;
    }

    private static List<CommunityNode> ProjectCommunities(
        IReadOnlyList<Node> snapshot,
        bool withEmbeddings)
    {
        var communities = new List<CommunityNode>(snapshot.Count);
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i] is CommunityNode community)
            {
                communities.Add(ProjectCommunityEmbedding(community, withEmbeddings));
            }
        }

        return communities;
    }

    private static List<string> BuildEndpointNodeUuidList(IReadOnlyList<EntityEdge> edges)
    {
        var nodeUuids = new List<string>(edges.Count * 2);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < edges.Count; i++)
        {
            AddEndpointNodeUuid(edges[i].SourceNodeUuid, seen, nodeUuids);
        }

        for (var i = 0; i < edges.Count; i++)
        {
            AddEndpointNodeUuid(edges[i].TargetNodeUuid, seen, nodeUuids);
        }

        return nodeUuids;
    }

    private static void AddEndpointNodeUuid(
        string? nodeUuid,
        HashSet<string> seen,
        List<string> nodeUuids)
    {
        if (!string.IsNullOrEmpty(nodeUuid) && seen.Add(nodeUuid))
        {
            nodeUuids.Add(nodeUuid);
        }
    }

    private static Dictionary<string, EntityNode> BuildNodeLookup(IReadOnlyList<EntityNode> nodes)
    {
        var nodesByUuid = new Dictionary<string, EntityNode>(nodes.Count, StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            nodesByUuid.Add(nodes[i].Uuid, nodes[i]);
        }

        return nodesByUuid;
    }

    private static Task<IReadOnlyList<string>> ResolveEntitySearchGroupIdsAsync(
        IGraphDriver driver,
        IReadOnlyList<string>? groupIds,
        CancellationToken cancellationToken)
    {
        if (groupIds is { Count: > 0 })
        {
            return Task.FromResult(groupIds);
        }

        return driver is GraphDriverBase graphDriver
            ? graphDriver.GetEntityGroupIdsAsync(cancellationToken)
            : Task.FromResult<IReadOnlyList<string>>(new[] { driver.DefaultGroupId });
    }

    private static Task<IReadOnlyList<string>> ResolveCommunitySearchGroupIdsAsync(
        IGraphDriver driver,
        IReadOnlyList<string>? groupIds,
        CancellationToken cancellationToken)
    {
        if (groupIds is { Count: > 0 })
        {
            return Task.FromResult(groupIds);
        }

        return driver is GraphDriverBase graphDriver
            ? graphDriver.GetCommunityGroupIdsAsync(cancellationToken)
            : Task.FromResult<IReadOnlyList<string>>(new[] { driver.DefaultGroupId });
    }

    private static EntityNode ProjectNodeEmbedding(EntityNode node, bool withEmbeddings)
    {
        if (!withEmbeddings)
        {
            node.NameEmbedding = null;
        }

        return node;
    }

    private static CommunityNode ProjectCommunityEmbedding(CommunityNode node, bool withEmbeddings)
    {
        if (!withEmbeddings)
        {
            node.NameEmbedding = null;
        }

        return node;
    }

    private static EntityEdge ProjectEdgeEmbedding(EntityEdge edge, bool withEmbeddings)
    {
        if (!withEmbeddings)
        {
            edge.FactEmbedding = null;
        }

        return edge;
    }

    internal sealed record TraversalGraph(
        IReadOnlyList<EntityEdge> EntityEdges,
        IReadOnlyList<EpisodicEdge> EpisodicEdges,
        IReadOnlyDictionary<string, string> NodeGroupIdsByUuid);

    internal sealed record TraversalStep(
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
