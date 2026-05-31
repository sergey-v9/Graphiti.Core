namespace Graphiti.Core.CrossEncoder;

/// <summary>
/// A cross-encoder that scores passages with a simple deterministic lexical overlap metric rather
/// than a model. Used as the default reranker so the library works without an external service.
/// </summary>
public sealed class IdentityCrossEncoderClient : CrossEncoderClient
{
    /// <inheritdoc />
    public override Task<IReadOnlyList<(string Passage, float Score)>> RankAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scorer = SearchUtilities.CreateTextScorer(query);

        var scored = new List<(string Passage, float Score, int Index)>(passages.Count);
        for (var i = 0; i < passages.Count; i++)
        {
            var passage = passages[i];
            scored.Add((passage, scorer.Score(passage), i));
        }

        scored.Sort(CompareScoredPassages);

        var results = new List<(string Passage, float Score)>(scored.Count);
        for (var i = 0; i < scored.Count; i++)
        {
            var item = scored[i];
            results.Add((item.Passage, item.Score));
        }

        return Task.FromResult<IReadOnlyList<(string Passage, float Score)>>(results);
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<CrossEncoderRank>> RankIndexedAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scorer = SearchUtilities.CreateTextScorer(query);

        var results = new List<CrossEncoderRank>(passages.Count);
        for (var i = 0; i < passages.Count; i++)
        {
            var passage = passages[i];
            results.Add(new CrossEncoderRank(i, passage, scorer.Score(passage)));
        }

        results.Sort(CompareIndexedRanks);
        return Task.FromResult<IReadOnlyList<CrossEncoderRank>>(results);
    }

    private static int CompareScoredPassages(
        (string Passage, float Score, int Index) left,
        (string Passage, float Score, int Index) right)
    {
        var scoreComparison = right.Score.CompareTo(left.Score);
        return scoreComparison != 0
            ? scoreComparison
            : left.Index.CompareTo(right.Index);
    }

    private static int CompareIndexedRanks(CrossEncoderRank left, CrossEncoderRank right)
    {
        var scoreComparison = right.Score.CompareTo(left.Score);
        return scoreComparison != 0
            ? scoreComparison
            : left.Index.CompareTo(right.Index);
    }
}
