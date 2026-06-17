namespace Graphiti.Core.Drivers;

internal static class TypedEdgeDeletion
{
    public static Task DeleteEdgeAsync(
        Edge edge,
        IGraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return edge switch
        {
            EntityEdge entity => DeleteEdgeAsync<EntityEdge>(entity.Uuid, driver, cancellationToken),
            EpisodicEdge episode => DeleteEdgeAsync<EpisodicEdge>(episode.Uuid, driver, cancellationToken),
            CommunityEdge community => DeleteEdgeAsync<CommunityEdge>(community.Uuid, driver, cancellationToken),
            HasEpisodeEdge hasEpisode => DeleteEdgeAsync<HasEpisodeEdge>(hasEpisode.Uuid, driver, cancellationToken),
            NextEpisodeEdge nextEpisode => DeleteEdgeAsync<NextEpisodeEdge>(nextEpisode.Uuid, driver, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(edge), edge.GetType().Name)
        };
    }

    public static async Task DeleteEdgeAsync<TEdge>(
        string uuid,
        IGraphDriver driver,
        CancellationToken cancellationToken = default)
        where TEdge : Edge
    {
        ArgumentNullException.ThrowIfNull(driver);
        if (driver is ITypedEdgeDeleteGraphDriver typedDriver)
        {
            await typedDriver.DeleteEdgeAsync<TEdge>(uuid, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!await ExistsAsync<TEdge>(uuid, driver, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await driver.DeleteEdgeAsync(uuid, cancellationToken).ConfigureAwait(false);
    }

    public static async Task DeleteEdgesByUuidsAsync<TEdge>(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default)
        where TEdge : Edge
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(uuids);
        if (driver is ITypedEdgeDeleteGraphDriver typedDriver)
        {
            await typedDriver.DeleteEdgesByUuidsAsync<TEdge>(uuids, cancellationToken).ConfigureAwait(false);
            return;
        }

        var typedEdges = await driver.GetEdgesByUuidsAsync<TEdge>(uuids, cancellationToken).ConfigureAwait(false);
        var typedUuids = BuildEdgeUuidList(typedEdges);
        if (typedUuids.Count == 0)
        {
            return;
        }

        await driver.DeleteEdgesByUuidsAsync(typedUuids, cancellationToken).ConfigureAwait(false);
    }

    public static async Task DeleteBaseEdgesByUuidsAsync(
        IGraphDriver driver,
        IEnumerable<string> uuids,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(uuids);
        var uuidList = uuids.TryGetNonEnumeratedCount(out var count)
            ? new List<string>(count)
            : new List<string>();
        foreach (var uuid in uuids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uuidList.Add(uuid);
        }

        if (uuidList.Count == 0)
        {
            return;
        }

        await DeleteEdgesByUuidsAsync<EntityEdge>(driver, uuidList, cancellationToken).ConfigureAwait(false);
        await DeleteEdgesByUuidsAsync<EpisodicEdge>(driver, uuidList, cancellationToken).ConfigureAwait(false);
        await DeleteEdgesByUuidsAsync<CommunityEdge>(driver, uuidList, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ExistsAsync<TEdge>(
        string uuid,
        IGraphDriver driver,
        CancellationToken cancellationToken)
        where TEdge : Edge
    {
        try
        {
            _ = await driver.GetEdgeByUuidAsync<TEdge>(uuid, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (EdgeNotFoundException)
        {
            return false;
        }
    }

    private static List<string> BuildEdgeUuidList<TEdge>(IReadOnlyList<TEdge> edges)
        where TEdge : Edge
    {
        var uuids = new List<string>(edges.Count);
        for (var i = 0; i < edges.Count; i++)
        {
            uuids.Add(edges[i].Uuid);
        }

        return uuids;
    }
}
