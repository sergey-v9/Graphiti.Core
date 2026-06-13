namespace Graphiti.Core.Internal.Helpers;

internal static class EntityTypeResolver
{
    internal static EntityTypeDefinition? FindEdgeTypeDefinition(
        EntityEdge edge,
        IReadOnlyDictionary<string, EntityNode> nodesByUuid,
        IReadOnlyDictionary<string, EntityTypeDefinition> edgeTypes,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>>? edgeTypeMap)
    {
        var edgeType = FindTypeDefinition(edge.Name, edgeTypes);
        if (edgeType is null)
        {
            return null;
        }

        if (edgeTypeMap is null || edgeTypeMap.Count == 0)
        {
            return edgeType;
        }

        // Python edge_operations.py:464-469 treats an endpoint missing from uuid_entity_map (even after
        // the DB fetch in resolve_extracted_edges:447-455) as labels=['Entity'] rather than dropping the
        // custom edge type. Mirror that: a null node behaves as an Entity-only node, so a signature whose
        // relevant side is "Entity" still matches.
        nodesByUuid.TryGetValue(edge.SourceNodeUuid, out var source);
        nodesByUuid.TryGetValue(edge.TargetNodeUuid, out var target);

        return EdgeTypeAllowedForNodePair(edge.Name, source, target, edgeTypeMap) ? edgeType : null;
    }

    internal static Dictionary<string, List<EntityEdge>> BuildEdgesByNode(IReadOnlyList<EntityEdge> edges)
    {
        var edgesByNode = new Dictionary<string, List<EntityEdge>>(edges.Count * 2, StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!edgesByNode.TryGetValue(edge.SourceNodeUuid, out var sourceEdges))
            {
                sourceEdges = new List<EntityEdge>();
                edgesByNode[edge.SourceNodeUuid] = sourceEdges;
            }

            if (!edgesByNode.TryGetValue(edge.TargetNodeUuid, out var targetEdges))
            {
                targetEdges = new List<EntityEdge>();
                edgesByNode[edge.TargetNodeUuid] = targetEdges;
            }

            sourceEdges.Add(edge);
            targetEdges.Add(edge);
        }

        return edgesByNode;
    }

    internal static EntityTypeDefinition? FindEntityTypeDefinition(
        EntityNode node,
        IReadOnlyDictionary<string, EntityTypeDefinition> entityTypes)
    {
        for (var i = 0; i < node.Labels.Count; i++)
        {
            var label = node.Labels[i];
            if (string.Equals(label, "Entity", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var matched = FindTypeDefinition(label, entityTypes);
            if (matched is not null)
            {
                return matched;
            }
        }

        return null;
    }

    internal static Dictionary<string, EntityNode> BuildNodeNameMap(IReadOnlyList<EntityNode> nodes)
    {
        var nodesByName = new Dictionary<string, EntityNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            nodesByName.TryAdd(node.Name, node);
        }

        return nodesByName;
    }

    private static bool EdgeTypeAllowedForNodePair(
        string edgeName,
        EntityNode? source,
        EntityNode? target,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>> edgeTypeMap)
    {
        foreach (var pair in edgeTypeMap)
        {
            if (!NodeHasEffectiveLabel(source, pair.Key.SourceType)
                || !NodeHasEffectiveLabel(target, pair.Key.TargetType))
            {
                continue;
            }

            if (ContainsEdgeTypeName(pair.Value, edgeName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NodeHasEffectiveLabel(EntityNode? node, string label)
    {
        // "Entity" is appended to every node's labels in Python (source_node.labels + ['Entity']),
        // and a node missing from uuid_entity_map is treated as ['Entity'] only — so a null node
        // matches only the "Entity" label.
        if (string.Equals(label, "Entity", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (node is null)
        {
            return false;
        }

        for (var i = 0; i < node.Labels.Count; i++)
        {
            if (string.Equals(node.Labels[i], label, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsEdgeTypeName(IReadOnlyList<string> edgeTypeNames, string edgeName)
    {
        for (var i = 0; i < edgeTypeNames.Count; i++)
        {
            if (string.Equals(edgeTypeNames[i], edgeName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static EntityTypeDefinition? FindTypeDefinition(
        string typeName,
        IReadOnlyDictionary<string, EntityTypeDefinition> typeDefinitions)
    {
        if (typeDefinitions.TryGetValue(typeName, out var direct))
        {
            return direct;
        }

        foreach (var pair in typeDefinitions)
        {
            if (string.Equals(pair.Key, typeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(pair.Value.Name, typeName, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
