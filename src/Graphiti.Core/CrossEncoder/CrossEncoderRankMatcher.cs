namespace Graphiti.Core.CrossEncoder;

internal static class CrossEncoderRankMatcher
{
    public static IReadOnlyList<CrossEncoderRank> MatchIndexed(
        IReadOnlyList<string> passages,
        IReadOnlyList<(string Passage, float Score)> ranked)
    {
        var indexesByPassage = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        for (var index = 0; index < passages.Count; index++)
        {
            if (!indexesByPassage.TryGetValue(passages[index], out var indexes))
            {
                indexes = new Queue<int>();
                indexesByPassage[passages[index]] = indexes;
            }

            indexes.Enqueue(index);
        }

        var matched = new List<CrossEncoderRank>(ranked.Count);
        foreach (var item in ranked)
        {
            if (indexesByPassage.TryGetValue(item.Passage, out var indexes) && indexes.Count > 0)
            {
                var index = indexes.Dequeue();
                matched.Add(new CrossEncoderRank(index, passages[index], item.Score));
            }
        }

        return matched;
    }
}
