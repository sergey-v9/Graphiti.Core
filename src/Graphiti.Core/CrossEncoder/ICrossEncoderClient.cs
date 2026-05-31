namespace Graphiti.Core.CrossEncoder;

/// <summary>
/// Abstraction over a cross-encoder reranker that scores how relevant each passage is to a query,
/// used to reorder hybrid search candidates.
/// </summary>
public interface ICrossEncoderClient
{
    /// <summary>Scores each passage against the query and returns them ordered by relevance.</summary>
    Task<IReadOnlyList<(string Passage, float Score)>> RankAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ranks passages and returns results that retain each passage's original index, so callers can
    /// map scores back to their source items even when passages repeat.
    /// </summary>
    async Task<IReadOnlyList<CrossEncoderRank>> RankIndexedAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken = default)
    {
        var ranked = await RankAsync(query, passages, cancellationToken).ConfigureAwait(false);
        return CrossEncoderRankMatcher.MatchIndexed(passages, ranked);
    }
}
