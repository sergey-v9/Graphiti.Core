namespace Graphiti.Core.Maintenance;

/// <summary>
/// Groups entity nodes into communities by clustering the entity graph (label-propagation style) over
/// the provided nodes and edges. The resulting clusters back the community-building maintenance flow.
/// </summary>
internal static class CommunityClustering
{
    private const int MinimumMaxIterations = 100;

    /// <summary>Partitions the nodes into communities based on their connecting edges.</summary>
    public static List<List<EntityNode>> BuildClusters(
        IReadOnlyList<EntityNode> nodes,
        IReadOnlyList<EntityEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        var nodesByGroup = new Dictionary<string, List<EntityNode>>(StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (!nodesByGroup.TryGetValue(node.GroupId, out var groupNodes))
            {
                groupNodes = new List<EntityNode>();
                nodesByGroup.Add(node.GroupId, groupNodes);
            }

            groupNodes.Add(node);
        }

        var groupIds = new List<string>(nodesByGroup.Keys);
        groupIds.Sort(StringComparer.Ordinal);
        var clusters = new List<List<EntityNode>>(nodes.Count);
        for (var i = 0; i < groupIds.Count; i++)
        {
            clusters.AddRange(BuildClustersForGroup(nodesByGroup[groupIds[i]], edges));
        }

        return clusters;
    }

    private static List<List<EntityNode>> BuildClustersForGroup(
        List<EntityNode> nodes,
        IReadOnlyList<EntityEdge> edges)
    {
        if (nodes.Count == 0)
        {
            return new List<List<EntityNode>>();
        }

        var distinctNodes = new List<EntityNode>(nodes.Count);
        var indexByUuid = new Dictionary<string, int>(nodes.Count, StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (indexByUuid.TryAdd(node.Uuid, distinctNodes.Count))
            {
                distinctNodes.Add(node);
            }
        }

        var adjacency = BuildAdjacency(indexByUuid, edges);
        var communities = LabelPropagate(adjacency);
        var clustersByCommunity = new Dictionary<int, List<EntityNode>>();
        var communityOrder = new List<int>();

        for (var index = 0; index < distinctNodes.Count; index++)
        {
            var community = communities[index];
            if (!clustersByCommunity.TryGetValue(community, out var cluster))
            {
                cluster = new List<EntityNode>();
                clustersByCommunity[community] = cluster;
                communityOrder.Add(community);
            }

            cluster.Add(distinctNodes[index]);
        }

        var clusters = new List<List<EntityNode>>(clustersByCommunity.Count);
        foreach (var community in communityOrder)
        {
            clusters.Add(clustersByCommunity[community]);
        }

        return clusters;
    }

    private static Neighbor[][] BuildAdjacency(
        Dictionary<string, int> indexByUuid,
        IReadOnlyList<EntityEdge> edges)
    {
        var builders = new Dictionary<int, int>[indexByUuid.Count];
        for (var index = 0; index < builders.Length; index++)
        {
            builders[index] = new Dictionary<int, int>();
        }

        foreach (var edge in edges)
        {
            if (!indexByUuid.TryGetValue(edge.SourceNodeUuid, out var sourceIndex)
                || !indexByUuid.TryGetValue(edge.TargetNodeUuid, out var targetIndex))
            {
                continue;
            }

            IncrementNeighbor(builders[sourceIndex], targetIndex);
            IncrementNeighbor(builders[targetIndex], sourceIndex);
        }

        var adjacency = new Neighbor[builders.Length][];
        for (var index = 0; index < builders.Length; index++)
        {
            adjacency[index] = BuildSortedNeighbors(builders[index]);
        }

        return adjacency;
    }

    private static int[] LabelPropagate(IReadOnlyList<Neighbor[]> adjacency)
    {
        var current = new int[adjacency.Count];
        for (var i = 0; i < current.Length; i++)
        {
            current[i] = i;
        }

        var next = new int[adjacency.Count];
        var communityWeights = new int[adjacency.Count];
        var touchedCommunities = new int[adjacency.Count];
        var maxIterations = Math.Max(MinimumMaxIterations, adjacency.Count * adjacency.Count);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var changed = false;

            for (var nodeIndex = 0; nodeIndex < adjacency.Count; nodeIndex++)
            {
                var touchedCount = 0;
                foreach (var neighbor in adjacency[nodeIndex])
                {
                    var community = current[neighbor.NodeIndex];
                    if (communityWeights[community] == 0)
                    {
                        touchedCommunities[touchedCount++] = community;
                    }

                    communityWeights[community] += neighbor.EdgeCount;
                }

                var selectedCommunity = current[nodeIndex];
                if (touchedCount > 0)
                {
                    var bestCommunity = touchedCommunities[0];
                    var bestWeight = communityWeights[bestCommunity];
                    for (var i = 1; i < touchedCount; i++)
                    {
                        var candidateCommunity = touchedCommunities[i];
                        var candidateWeight = communityWeights[candidateCommunity];
                        if (candidateWeight > bestWeight
                            || (candidateWeight == bestWeight && candidateCommunity > bestCommunity))
                        {
                            bestCommunity = candidateCommunity;
                            bestWeight = candidateWeight;
                        }
                    }

                    selectedCommunity = bestWeight > 1
                        ? bestCommunity
                        : Math.Max(bestCommunity, current[nodeIndex]);
                }

                next[nodeIndex] = selectedCommunity;
                changed |= selectedCommunity != current[nodeIndex];

                for (var i = 0; i < touchedCount; i++)
                {
                    communityWeights[touchedCommunities[i]] = 0;
                }
            }

            if (!changed)
            {
                return current;
            }

            (current, next) = (next, current);
        }

        return current;
    }

    private static Neighbor[] BuildSortedNeighbors(Dictionary<int, int> neighbors)
    {
        if (neighbors.Count == 0)
        {
            return [];
        }

        var sorted = new Neighbor[neighbors.Count];
        var index = 0;
        foreach (var pair in neighbors)
        {
            sorted[index++] = new Neighbor(pair.Key, pair.Value);
        }

        Array.Sort(sorted, CompareNeighbors);
        return sorted;
    }

    private static int CompareNeighbors(Neighbor left, Neighbor right) =>
        left.NodeIndex.CompareTo(right.NodeIndex);

    private static void IncrementNeighbor(Dictionary<int, int> neighbors, int index)
    {
        neighbors.TryGetValue(index, out var count);
        neighbors[index] = count + 1;
    }

    private readonly record struct Neighbor(int NodeIndex, int EdgeCount);
}
