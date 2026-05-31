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
        IReadOnlyList<(string Passage, float Score)> results = passages
            .Select(passage => (passage, scorer.Score(passage)))
            .OrderByDescending(item => item.Item2)
            .ToList();
        return Task.FromResult(results);
    }

    public override Task<IReadOnlyList<CrossEncoderRank>> RankIndexedAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scorer = SearchUtilities.CreateTextScorer(query);
        IReadOnlyList<CrossEncoderRank> results = passages
            .Select((passage, index) => new CrossEncoderRank(
                index,
                passage,
                scorer.Score(passage)))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .ToList();
        return Task.FromResult(results);
    }
}
