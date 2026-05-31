namespace Graphiti.Core.Internal.Helpers;

internal static class EntityTypeResolver
{
    internal static EntityTypeDefinition? FindEdgeTypeDefinition(
        EntityEdge edge,
        Dictionary<string, EntityNode> nodesByUuid,
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

        if (!nodesByUuid.TryGetValue(edge.SourceNodeUuid, out var source)
            || !nodesByUuid.TryGetValue(edge.TargetNodeUuid, out var target))
        {
            return null;
        }

        return EdgeTypeAllowedForNodePair(edge.Name, source, target, edgeTypeMap) ? edgeType : null;
    }

    internal static Dictionary<string, List<EntityEdge>> BuildEdgesByNode(IReadOnlyList<EntityEdge> edges)
    {
        var edgesByNode = new Dictionary<string, List<EntityEdge>>(StringComparer.Ordinal);
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
        foreach (var label in node.Labels.Where(label => !string.Equals(label, "Entity", StringComparison.OrdinalIgnoreCase)))
        {
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
        EntityNode source,
        EntityNode target,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>> edgeTypeMap)
    {
        var sourceLabels = EffectiveEntityLabels(source);
        var targetLabels = EffectiveEntityLabels(target);
        foreach (var pair in edgeTypeMap)
        {
            if (!sourceLabels.Contains(pair.Key.SourceType)
                || !targetLabels.Contains(pair.Key.TargetType))
            {
                continue;
            }

            if (pair.Value.Any(type => string.Equals(type, edgeName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> EffectiveEntityLabels(EntityNode node)
    {
        var labels = node.Labels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        labels.Add("Entity");
        return labels;
    }

    private static EntityTypeDefinition? FindTypeDefinition(
        string typeName,
        IReadOnlyDictionary<string, EntityTypeDefinition> typeDefinitions)
    {
        if (typeDefinitions.TryGetValue(typeName, out var direct))
        {
            return direct;
        }

        var matched = typeDefinitions.FirstOrDefault(pair =>
            string.Equals(pair.Key, typeName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(pair.Value.Name, typeName, StringComparison.OrdinalIgnoreCase));
        return matched.Value;
    }
}
