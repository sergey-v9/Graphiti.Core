namespace Graphiti.Core.CrossEncoder;

/// <summary>Base class for cross-encoder rerankers implementing the indexed ranking helper.</summary>
public abstract class CrossEncoderClient : ICrossEncoderClient
{
    /// <inheritdoc />
    public abstract Task<IReadOnlyList<(string Passage, float Score)>> RankAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<CrossEncoderRank>> RankIndexedAsync(
        string query,
        IReadOnlyList<string> passages,
        CancellationToken cancellationToken = default)
    {
        var ranked = await RankAsync(query, passages, cancellationToken).ConfigureAwait(false);
        return CrossEncoderRankMatcher.MatchIndexed(passages, ranked);
    }
}
