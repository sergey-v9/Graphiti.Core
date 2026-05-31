namespace Graphiti.Core;

public sealed partial class Graphiti
{
    /// <summary>
    /// Convenience hybrid search that returns the most relevant facts (entity edges) for a query.
    /// When <paramref name="centerNodeUuid"/> is supplied, results are reranked by graph distance to
    /// that node; otherwise reciprocal rank fusion is used.
    /// </summary>
    /// <param name="query">Natural-language search query.</param>
    /// <param name="centerNodeUuid">Optional node to bias results toward by graph proximity.</param>
    /// <param name="groupIds">Graph partitions to search; all default when omitted.</param>
    /// <param name="numResults">Maximum number of facts to return.</param>
    /// <param name="searchFilter">Optional filters constraining candidate edges.</param>
    /// <param name="driver">Optional driver override.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task<IReadOnlyList<EntityEdge>> SearchAsync(
        string query,
        string? centerNodeUuid = null,
        IReadOnlyList<string>? groupIds = null,
        int numResults = SearchConfiguration.DefaultSearchLimit,
        SearchFilters? searchFilter = null,
        IGraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = GraphitiTelemetry.StartActivity("SearchEdges");
        activity?.SetTag("graphiti.query.length", query.Length);
        activity?.SetTag("graphiti.limit", numResults);
        GraphitiTelemetry.SetGroupIds(activity, groupIds);
        GraphitiLog.SearchingEdges(_logger, query.Length, numResults);

        try
        {
            var searchConfig = centerNodeUuid is null
                ? SearchConfigRecipes.EdgeHybridSearchRrf
                : SearchConfigRecipes.EdgeHybridSearchNodeDistance;
            searchConfig.Limit = numResults;

            var results = await SearchEngine.SearchAsync(
                Clients,
                query,
                groupIds,
                searchConfig,
                searchFilter ?? new SearchFilters(),
                centerNodeUuid,
                driver: driver,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            activity?.SetTag("graphiti.result.edges", results.Edges.Count);
            GraphitiLog.EdgeSearchCompleted(_logger, results.Edges.Count);
            GraphitiTelemetry.SetOk(activity);
            return results.Edges;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    /// <summary>
    /// Full hybrid search driven by an explicit <paramref name="config"/>, returning a combined
    /// <see cref="SearchResults"/> of edges, nodes, episodes, and communities according to the
    /// configured search methods and rerankers.
    /// </summary>
    /// <param name="query">Natural-language search query.</param>
    /// <param name="config">Search configuration selecting methods, rerankers, and limits.</param>
    /// <param name="groupIds">Graph partitions to search; all default when omitted.</param>
    /// <param name="centerNodeUuid">Optional node used for node-distance reranking.</param>
    /// <param name="bfsOriginNodeUuids">Optional origin nodes for breadth-first traversal methods.</param>
    /// <param name="searchFilter">Optional filters constraining candidates.</param>
    /// <param name="queryVector">Optional precomputed query embedding to avoid re-embedding.</param>
    /// <param name="driver">Optional driver override.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task<SearchResults> SearchAsync(
        string query,
        SearchConfig config,
        IReadOnlyList<string>? groupIds = null,
        string? centerNodeUuid = null,
        IReadOnlyList<string>? bfsOriginNodeUuids = null,
        SearchFilters? searchFilter = null,
        IReadOnlyList<float>? queryVector = null,
        IGraphDriver? driver = null,
        CancellationToken cancellationToken = default) =>
        await SearchAdvancedAsync(
            query,
            config,
            groupIds,
            centerNodeUuid,
            bfsOriginNodeUuids,
            searchFilter,
            queryVector,
            driver,
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Full hybrid graph search returning edges, nodes, episodes, and communities. When
    /// <paramref name="config"/> is omitted this uses the Python <c>search_</c> default recipe:
    /// combined hybrid search with cross-encoder reranking.
    /// </summary>
    /// <param name="query">Natural-language search query.</param>
    /// <param name="config">Optional search configuration; defaults to combined hybrid cross-encoder search.</param>
    /// <param name="groupIds">Graph partitions to search; all default when omitted.</param>
    /// <param name="centerNodeUuid">Optional node used for node-distance reranking.</param>
    /// <param name="bfsOriginNodeUuids">Optional origin nodes for breadth-first traversal methods.</param>
    /// <param name="searchFilter">Optional filters constraining candidates.</param>
    /// <param name="queryVector">Optional precomputed query embedding to avoid re-embedding.</param>
    /// <param name="driver">Optional driver override.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public async Task<SearchResults> SearchAdvancedAsync(
        string query,
        SearchConfig? config = null,
        IReadOnlyList<string>? groupIds = null,
        string? centerNodeUuid = null,
        IReadOnlyList<string>? bfsOriginNodeUuids = null,
        SearchFilters? searchFilter = null,
        IReadOnlyList<float>? queryVector = null,
        IGraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        config ??= SearchConfigRecipes.CombinedHybridSearchCrossEncoder;
        using var activity = GraphitiTelemetry.StartActivity("Search");
        activity?.SetTag("graphiti.query.length", query.Length);
        activity?.SetTag("graphiti.limit", config.Limit);
        activity?.SetTag("graphiti.has_center_node", centerNodeUuid is not null);
        activity?.SetTag("graphiti.query_vector.provided", queryVector is not null);
        GraphitiTelemetry.SetGroupIds(activity, groupIds);
        GraphitiLog.Searching(_logger, query.Length, config.Limit);

        try
        {
            var results = await SearchEngine.SearchAsync(
                Clients,
                query,
                groupIds,
                config,
                searchFilter ?? new SearchFilters(),
                centerNodeUuid,
                bfsOriginNodeUuids,
                queryVector,
                driver,
                cancellationToken).ConfigureAwait(false);
            activity?.SetTag("graphiti.result.nodes", results.Nodes.Count);
            activity?.SetTag("graphiti.result.edges", results.Edges.Count);
            activity?.SetTag("graphiti.result.episodes", results.Episodes.Count);
            activity?.SetTag("graphiti.result.communities", results.Communities.Count);
            GraphitiLog.SearchCompleted(
                _logger,
                results.Nodes.Count,
                results.Edges.Count,
                results.Episodes.Count,
                results.Communities.Count);
            GraphitiTelemetry.SetOk(activity);
            return results;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    /// <summary>
    /// Returns the entities and facts attributed to the given episodes, packaged as
    /// <see cref="SearchResults"/>. Useful for inspecting what a specific episode contributed.
    /// </summary>
    public async Task<SearchResults> GetNodesAndEdgesByEpisodeAsync(
        IReadOnlyList<string> episodeUuids,
        CancellationToken cancellationToken = default)
    {
        using var activity = GraphitiTelemetry.StartActivity("GetNodesAndEdgesByEpisode");
        activity?.SetTag("graphiti.episodes.count", episodeUuids.Count);

        try
        {
            var episodes = await EpisodicNode.GetByUuidsAsync(Driver, episodeUuids, cancellationToken).ConfigureAwait(false);
            var edges = new List<EntityEdge>();
            foreach (var episode in episodes)
            {
                edges.AddRange(await EntityEdge.GetByUuidsAsync(
                    Driver,
                    episode.EntityEdges,
                    cancellationToken).ConfigureAwait(false));
            }

            var nodes = await Driver.GetMentionedNodesAsync(episodes, cancellationToken).ConfigureAwait(false);
            var results = new SearchResults
            {
                Edges = edges,
                Nodes = nodes.ToList()
            };
            activity?.SetTag("graphiti.result.nodes", results.Nodes.Count);
            activity?.SetTag("graphiti.result.edges", results.Edges.Count);
            GraphitiTelemetry.SetOk(activity);
            return results;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }
}
