using System.Globalization;
using System.Text.Json.Nodes;

namespace Graphiti.Core.Internal.Helpers;

internal static class EdgeMergeHelpers
{
    internal static Dictionary<string, EntityEdge> BuildOverrideLookup(
        IReadOnlyCollection<EntityEdge>? overrides,
        HashSet<string> excludedUuids)
    {
        var lookup = new Dictionary<string, EntityEdge>(StringComparer.Ordinal);
        if (overrides is null || overrides.Count == 0)
        {
            return lookup;
        }

        foreach (var edge in overrides)
        {
            if (!excludedUuids.Contains(edge.Uuid))
            {
                lookup.TryAdd(edge.Uuid, edge);
            }
        }

        return lookup;
    }

    internal static List<EntityEdge> ResolveEdgeContradictions(
        EntityEdge resolvedEdge,
        IReadOnlyList<EntityEdge> invalidationCandidates,
        Func<DateTime> utcNow)
    {
        var invalidatedEdges = new List<EntityEdge>();
        foreach (var edge in invalidationCandidates)
        {
            var edgeInvalidAt = edge.InvalidAt is null
                ? (DateTime?)null
                : GraphitiHelpers.EnsureUtc(edge.InvalidAt.Value);
            var resolvedValidAt = resolvedEdge.ValidAt is null
                ? (DateTime?)null
                : GraphitiHelpers.EnsureUtc(resolvedEdge.ValidAt.Value);
            var edgeValidAt = edge.ValidAt is null
                ? (DateTime?)null
                : GraphitiHelpers.EnsureUtc(edge.ValidAt.Value);
            var resolvedInvalidAt = resolvedEdge.InvalidAt is null
                ? (DateTime?)null
                : GraphitiHelpers.EnsureUtc(resolvedEdge.InvalidAt.Value);

            if ((edgeInvalidAt is not null && resolvedValidAt is not null && edgeInvalidAt <= resolvedValidAt)
                || (edgeValidAt is not null && resolvedInvalidAt is not null && resolvedInvalidAt <= edgeValidAt))
            {
                continue;
            }

            if (edgeValidAt is not null && resolvedValidAt is not null && edgeValidAt < resolvedValidAt)
            {
                edge.InvalidAt = resolvedEdge.ValidAt;
                edge.ExpiredAt ??= utcNow();
                invalidatedEdges.Add(edge);
            }
        }

        return invalidatedEdges;
    }

    internal static IReadOnlyList<int> ReadIntArray(JsonObject response, string key)
    {
        if (!response.TryGetPropertyValue(key, out var node) || node is not JsonArray array)
        {
            return Array.Empty<int>();
        }

        var result = new List<int>(array.Count);
        foreach (var item in array)
        {
            if (item is null || !TryReadInt(item, out var value))
            {
                continue;
            }

            result.Add(value);
        }

        return result;
    }

    private static bool TryReadInt(JsonNode item, out int value)
    {
        if (item is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out value))
            {
                return true;
            }

            if (jsonValue.TryGetValue<string>(out var text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }

        value = 0;
        return false;
    }

    internal static List<EntityEdge> MergeEdgeOverrides(
        IEnumerable<EntityEdge> source,
        IReadOnlyCollection<EntityEdge>? overrides,
        Func<EntityEdge, bool> predicate)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return CopyEdges(source);
        }

        var overridesByUuid = new Dictionary<string, EntityEdge>(StringComparer.Ordinal);
        foreach (var edge in overrides)
        {
            if (predicate(edge))
            {
                overridesByUuid.TryAdd(edge.Uuid, edge);
            }
        }

        var merged = new List<EntityEdge>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in source)
        {
            if (!seen.Add(edge.Uuid))
            {
                merged.Add(edge);
                continue;
            }

            merged.Add(overridesByUuid.TryGetValue(edge.Uuid, out var overrideEdge)
                ? overrideEdge
                : edge);
        }

        foreach (var edge in overrides)
        {
            if (predicate(edge) && seen.Add(edge.Uuid))
            {
                merged.Add(edge);
            }
        }

        return merged;
    }

    private static List<EntityEdge> CopyEdges(IEnumerable<EntityEdge> source)
    {
        var edges = new List<EntityEdge>();
        foreach (var edge in source)
        {
            edges.Add(edge);
        }

        return edges;
    }

    internal static List<EntityEdge> FilterEdgesByUuid(
        IEnumerable<EntityEdge> edges,
        HashSet<string> uuids)
    {
        if (uuids.Count == 0)
        {
            return new List<EntityEdge>();
        }

        var filtered = new List<EntityEdge>(uuids.Count);
        foreach (var edge in edges)
        {
            if (uuids.Contains(edge.Uuid))
            {
                filtered.Add(edge);
            }
        }

        return filtered;
    }

    internal static void UpsertCanonicalEdges(
        Dictionary<string, EntityEdge> canonicalEdges,
        IEnumerable<EntityEdge> edges)
    {
        foreach (var edge in edges)
        {
            if (canonicalEdges.TryGetValue(edge.Uuid, out var existing))
            {
                MergeCanonicalEdge(existing, edge);
                continue;
            }

            canonicalEdges.Add(edge.Uuid, edge);
        }
    }

    internal static void MergeCanonicalEdge(EntityEdge target, EntityEdge source)
    {
        if (ReferenceEquals(target, source))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(source.GroupId))
        {
            target.GroupId = source.GroupId;
        }

        if (!string.IsNullOrWhiteSpace(source.SourceNodeUuid))
        {
            target.SourceNodeUuid = source.SourceNodeUuid;
        }

        if (!string.IsNullOrWhiteSpace(source.TargetNodeUuid))
        {
            target.TargetNodeUuid = source.TargetNodeUuid;
        }

        if (!string.IsNullOrWhiteSpace(source.Name))
        {
            target.Name = source.Name;
        }

        if (!string.IsNullOrWhiteSpace(source.Fact))
        {
            target.Fact = source.Fact;
        }

        foreach (var episodeUuid in source.Episodes)
        {
            AddEpisodeIfMissing(target, episodeUuid);
        }

        target.FactEmbedding ??= source.FactEmbedding;
        target.ValidAt ??= source.ValidAt;
        target.InvalidAt ??= source.InvalidAt;
        target.ExpiredAt ??= source.ExpiredAt;
        target.ReferenceTime ??= source.ReferenceTime;
        foreach (var pair in source.Attributes)
        {
            target.Attributes[pair.Key] = pair.Value;
        }
    }

    internal static void AddEpisodeIfMissing(EntityEdge edge, string episodeUuid)
    {
        for (var i = 0; i < edge.Episodes.Count; i++)
        {
            if (string.Equals(edge.Episodes[i], episodeUuid, StringComparison.Ordinal))
            {
                return;
            }
        }

        edge.Episodes.Add(episodeUuid);
    }
}
