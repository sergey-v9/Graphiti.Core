namespace Graphiti.Core;

public sealed partial class Graphiti
{
    /// <summary>
    /// Rebuilds communities for the given groups: clears existing communities, clusters entities by
    /// their relationships, summarizes each cluster, and persists the resulting community nodes and
    /// membership edges.
    /// </summary>
    /// <param name="groupIds">Graph partitions to rebuild; all default when omitted.</param>
    /// <param name="driver">Optional driver override.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The community nodes and their membership edges.</returns>
    public Task<(IReadOnlyList<CommunityNode> Communities, IReadOnlyList<CommunityEdge> CommunityEdges)> BuildCommunitiesAsync(
        IReadOnlyList<string>? groupIds = null,
        IGraphDriver? driver = null,
        CancellationToken cancellationToken = default) =>
        _communityService.BuildCommunitiesAsync(groupIds, driver, cancellationToken);
}
