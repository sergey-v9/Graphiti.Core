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
}
