namespace Graphiti.Core.Internal.Helpers;

internal static class EpisodeAttribution
{
    internal static readonly IReadOnlyList<int> FirstEpisodeIndex = Array.AsReadOnly(new[] { 0 });

    internal static IReadOnlyList<int> NormalizeIndices(
        IReadOnlyList<int>? episodeIndices,
        int episodeCount)
    {
        if (episodeCount <= 0)
        {
            return Array.Empty<int>();
        }

        if (episodeIndices is null)
        {
            return AllEpisodeIndices(episodeCount);
        }

        var valid = new List<int>(episodeIndices.Count);
        for (var i = 0; i < episodeIndices.Count; i++)
        {
            var index = episodeIndices[i];
            if ((uint)index < (uint)episodeCount)
            {
                valid.Add(index);
            }
        }

        return valid.Count == 0 ? AllEpisodeIndices(episodeCount) : valid;
    }

    internal static List<string> MapIndicesToEpisodeUuids(
        IReadOnlyList<int>? episodeIndices,
        IReadOnlyList<EpisodicNode> episodes)
    {
        var indices = NormalizeIndices(episodeIndices, episodes.Count);
        var episodeUuids = new List<string>(indices.Count);
        for (var i = 0; i < indices.Count; i++)
        {
            episodeUuids.Add(episodes[indices[i]].Uuid);
        }

        return episodeUuids;
    }

    internal static DateTime ReferenceTimeForFirstIndex(
        IReadOnlyList<int>? episodeIndices,
        IReadOnlyList<EpisodicNode> episodes,
        DateTime fallback)
    {
        if (episodeIndices is null || episodeIndices.Count == 0)
        {
            return fallback;
        }

        var index = episodeIndices[0];
        if ((uint)index < (uint)episodes.Count)
        {
            return episodes[index].ValidAt;
        }

        return fallback;
    }

    internal static Dictionary<string, IReadOnlyList<int>> RemapNodeIndexMap(
        IReadOnlyList<EntityNode> extractedNodes,
        IReadOnlyDictionary<string, IReadOnlyList<int>> extractedAttribution,
        IReadOnlyDictionary<string, EntityNode> nodesByExtractedName)
    {
        var merged = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var sourceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < extractedNodes.Count; i++)
        {
            var extractedNode = extractedNodes[i];
            if (!nodesByExtractedName.TryGetValue(extractedNode.Name, out var resolvedNode)
                || !extractedAttribution.TryGetValue(extractedNode.Uuid, out var indices))
            {
                continue;
            }

            if (!merged.TryGetValue(resolvedNode.Uuid, out var resolvedIndices))
            {
                resolvedIndices = new List<int>(indices.Count);
                merged[resolvedNode.Uuid] = resolvedIndices;
            }

            sourceCounts[resolvedNode.Uuid] = sourceCounts.GetValueOrDefault(resolvedNode.Uuid) + 1;
            for (var index = 0; index < indices.Count; index++)
            {
                resolvedIndices.Add(indices[index]);
            }
        }

        var result = new Dictionary<string, IReadOnlyList<int>>(merged.Count, StringComparer.Ordinal);
        foreach (var pair in merged)
        {
            if (sourceCounts[pair.Key] > 1)
            {
                pair.Value.Sort();
                result[pair.Key] = DistinctSorted(pair.Value);
                continue;
            }

            result[pair.Key] = pair.Value;
        }

        return result;
    }

    private static IReadOnlyList<int> AllEpisodeIndices(int episodeCount)
    {
        if (episodeCount == 1)
        {
            return FirstEpisodeIndex;
        }

        var indices = new int[episodeCount];
        for (var i = 0; i < episodeCount; i++)
        {
            indices[i] = i;
        }

        return indices;
    }

    private static List<int> DistinctSorted(List<int> sorted)
    {
        if (sorted.Count < 2)
        {
            return sorted;
        }

        var write = 1;
        for (var read = 1; read < sorted.Count; read++)
        {
            if (sorted[read] == sorted[write - 1])
            {
                continue;
            }

            sorted[write++] = sorted[read];
        }

        if (write < sorted.Count)
        {
            sorted.RemoveRange(write, sorted.Count - write);
        }

        return sorted;
    }
}
