using System.Globalization;
using System.Text.Json.Nodes;

namespace Graphiti.Core.Internal.Helpers;

internal static class EdgeMergeHelpers
{
    internal static IReadOnlyList<EntityEdge> RankOverrideInvalidationCandidates(
        string fact,
        IReadOnlyList<EntityEdge>? existingEdgesOverride,
        HashSet<string> excludedUuids,
        int limit)
    {
        if (existingEdgesOverride is null || existingEdgesOverride.Count == 0 || limit <= 0)
        {
            return Array.Empty<EntityEdge>();
        }

        return SearchUtilities
            .TopByScore(
                existingEdgesOverride.Where(edge => !excludedUuids.Contains(edge.Uuid)),
                edge => SearchUtilities.TextScore(fact, $"{edge.Name} {edge.Fact}"),
                limit,
                minScore: 0,
                includeMinScore: false)
            .Select(candidate => candidate.Item)
            .ToList();
    }

    internal static List<EntityEdge> ResolveEdgeContradictions(
        EntityEdge resolvedEdge,
        IReadOnlyList<EntityEdge> invalidationCandidates,
        DateTime now)
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
                edge.ExpiredAt ??= now;
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
        IReadOnlyList<EntityEdge>? overrides,
        Func<EntityEdge, bool> predicate)
    {
        var merged = source.ToList();
        if (overrides is null || overrides.Count == 0)
        {
            return merged;
        }

        var seen = merged.Select(edge => edge.Uuid).ToHashSet(StringComparer.Ordinal);
        foreach (var edge in overrides)
        {
            if (predicate(edge) && seen.Add(edge.Uuid))
            {
                merged.Add(edge);
            }
        }

        return merged;
    }

    internal static void AddResolvedEdge(
        List<EntityEdge> edges,
        HashSet<string> seenUuids,
        EntityEdge edge)
    {
        if (seenUuids.Add(edge.Uuid))
        {
            edges.Add(edge);
        }
    }

    internal static List<EntityEdge> FilterEdgesByUuid(
        IEnumerable<EntityEdge> edges,
        HashSet<string> uuids)
    {
        if (uuids.Count == 0)
        {
            return new List<EntityEdge>();
        }

        return edges
            .Where(edge => uuids.Contains(edge.Uuid))
            .ToList();
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

    private static void MergeCanonicalEdge(EntityEdge target, EntityEdge source)
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
            if (!target.Episodes.Contains(episodeUuid, StringComparer.Ordinal))
            {
                target.Episodes.Add(episodeUuid);
            }
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
}
