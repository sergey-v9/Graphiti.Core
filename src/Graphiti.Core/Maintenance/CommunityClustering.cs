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

        return nodes
            .GroupBy(node => node.GroupId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => BuildClustersForGroup(group.ToList(), edges))
            .ToList();
    }

    private static List<List<EntityNode>> BuildClustersForGroup(
        List<EntityNode> nodes,
        IReadOnlyList<EntityEdge> edges)
    {
        if (nodes.Count == 0)
        {
            return new List<List<EntityNode>>();
        }

        var distinctNodes = nodes
            .GroupBy(node => node.Uuid, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var indexByUuid = distinctNodes
            .Select((node, index) => (node.Uuid, index))
            .ToDictionary(item => item.Uuid, item => item.index, StringComparer.Ordinal);
        var adjacency = BuildAdjacency(indexByUuid, edges);
        var communities = LabelPropagate(adjacency);
        var clustersByCommunity = new Dictionary<int, List<EntityNode>>();

        for (var index = 0; index < distinctNodes.Length; index++)
        {
            var community = communities[index];
            if (!clustersByCommunity.TryGetValue(community, out var cluster))
            {
                cluster = new List<EntityNode>();
                clustersByCommunity[community] = cluster;
            }

            cluster.Add(distinctNodes[index]);
        }

        return clustersByCommunity.Values
            .Select(cluster => cluster
                .OrderBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(node => node.Uuid, StringComparer.Ordinal)
                .ToList())
            .OrderBy(cluster => cluster[0].Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(cluster => cluster[0].Uuid, StringComparer.Ordinal)
            .ToList();
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
            adjacency[index] = builders[index]
                .OrderBy(pair => pair.Key)
                .Select(pair => new Neighbor(pair.Key, pair.Value))
                .ToArray();
        }

        return adjacency;
    }

    private static int[] LabelPropagate(IReadOnlyList<Neighbor[]> adjacency)
    {
        var current = Enumerable.Range(0, adjacency.Count).ToArray();
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

    private static void IncrementNeighbor(Dictionary<int, int> neighbors, int index)
    {
        neighbors.TryGetValue(index, out var count);
        neighbors[index] = count + 1;
    }

    private readonly record struct Neighbor(int NodeIndex, int EdgeCount);
}
