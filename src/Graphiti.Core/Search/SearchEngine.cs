using System.Diagnostics;

namespace Graphiti.Core.Search;

/// <summary>
/// The hybrid search engine. Runs the configured per-type retrieval methods (cosine, BM25, BFS),
/// merges and reranks candidates (RRF / MMR / node-distance / episode-mentions / cross-encoder), and
/// returns a combined <see cref="SearchResults"/>. This is the shared implementation behind the
/// <see cref="Graphiti"/> search methods.
/// </summary>
public static class SearchEngine
{
    /// <summary>
    /// Executes a hybrid search described by <paramref name="config"/> against the graph.
    /// </summary>
    /// <param name="clients">Client bundle providing the driver, embedder, and cross-encoder.</param>
    /// <param name="query">Natural-language query. An empty query yields empty results.</param>
    /// <param name="groupIds">Graph partitions to search; <c>null</c>/empty searches all.</param>
    /// <param name="config">Search methods, rerankers, and limits.</param>
    /// <param name="searchFilter">Filters constraining candidates.</param>
    /// <param name="centerNodeUuid">Optional node used for node-distance reranking.</param>
    /// <param name="bfsOriginNodeUuids">Optional origin nodes for breadth-first methods.</param>
    /// <param name="queryVector">Optional precomputed query embedding to avoid re-embedding.</param>
    /// <param name="driver">Optional driver override; defaults to <paramref name="clients"/>'s driver.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    public static async Task<SearchResults> SearchAsync(
        GraphitiClients clients,
        string query,
        IReadOnlyList<string>? groupIds,
        SearchConfig config,
        SearchFilters searchFilter,
        string? centerNodeUuid = null,
        IReadOnlyList<string>? bfsOriginNodeUuids = null,
        IReadOnlyList<float>? queryVector = null,
        IGraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        SearchConfigValidator.Validate(config);
        GraphitiHelpers.ValidateGroupIds(groupIds);
        using var activity = GraphitiTelemetry.StartActivity("SearchEngine.Search");
        activity?.SetTag("graphiti.query.length", query.Length);
        activity?.SetTag("graphiti.limit", config.Limit);
        activity?.SetTag("graphiti.has_center_node", centerNodeUuid is not null);
        activity?.SetTag("graphiti.search.edge", config.EdgeConfig is not null);
        activity?.SetTag("graphiti.search.node", config.NodeConfig is not null);
        activity?.SetTag("graphiti.search.episode", config.EpisodeConfig is not null);
        activity?.SetTag("graphiti.search.community", config.CommunityConfig is not null);
        activity?.SetTag("graphiti.query_vector.provided", queryVector is not null);
        GraphitiTelemetry.SetGroupIds(activity, groupIds);

        try
        {
            driver ??= clients.Driver;
            if (string.IsNullOrWhiteSpace(query))
            {
                GraphitiTelemetry.SetOk(activity);
                return new SearchResults();
            }

            groupIds = groupIds is { Count: > 0 } && !groupIds.SequenceEqual(new[] { string.Empty })
                ? groupIds
                : null;

            var needsQueryVector =
                config.EdgeConfig?.SearchMethods.Contains(EdgeSearchMethod.CosineSimilarity) == true ||
                config.NodeConfig?.SearchMethods.Contains(NodeSearchMethod.CosineSimilarity) == true ||
                CommunityUsesCosine(config.CommunityConfig) ||
                config.EdgeConfig?.Reranker == EdgeReranker.Mmr ||
                config.NodeConfig?.Reranker == NodeReranker.Mmr ||
                config.CommunityConfig?.Reranker == CommunityReranker.Mmr;

            IReadOnlyList<float>? effectiveQueryVector;
            if (needsQueryVector)
            {
                effectiveQueryVector = queryVector is not null
                    ? EmbeddingVectorValidation.MaterializeSingle(
                        queryVector,
                        clients.Embedder.EmbeddingDimension,
                        "search query vector")
                    : EmbeddingVectorValidation.MaterializeSingle(
                        await clients.Embedder
                            .CreateAsync(NormalizeQueryForEmbedding(query), cancellationToken)
                            .ConfigureAwait(false),
                        clients.Embedder.EmbeddingDimension,
                        "search query");
            }
            else
            {
                effectiveQueryVector = null;
            }

            var results = new SearchResults();
            using var scopeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var scopeCancellationToken = scopeCancellation.Token;

            var scopeTasks = new List<Task>(4);
            Task<IReadOnlyList<(EntityEdge Item, float Score)>>? edgeTask = null;
            Task<IReadOnlyList<(EntityNode Item, float Score)>>? nodeTask = null;
            Task<IReadOnlyList<(EpisodicNode Item, float Score)>>? episodeTask = null;
            Task<IReadOnlyList<(CommunityNode Item, float Score)>>? communityTask = null;

            if (config.EdgeConfig is not null)
            {
                edgeTask = EdgeSearchAsync(
                    driver,
                    clients.CrossEncoder,
                    query,
                    effectiveQueryVector,
                    groupIds,
                    config.EdgeConfig,
                    searchFilter,
                    config.Limit,
                    config.RerankerMinScore,
                    centerNodeUuid,
                    bfsOriginNodeUuids,
                    scopeCancellationToken);
                scopeTasks.Add(edgeTask);
            }

            if (config.NodeConfig is not null)
            {
                nodeTask = NodeSearchAsync(
                    driver,
                    clients.CrossEncoder,
                    query,
                    effectiveQueryVector,
                    groupIds,
                    config.NodeConfig,
                    searchFilter,
                    config.Limit,
                    config.RerankerMinScore,
                    centerNodeUuid,
                    bfsOriginNodeUuids,
                    scopeCancellationToken);
                scopeTasks.Add(nodeTask);
            }

            if (config.EpisodeConfig is not null)
            {
                episodeTask = EpisodeSearchAsync(
                    driver,
                    clients.CrossEncoder,
                    query,
                    groupIds,
                    config.EpisodeConfig,
                    config.Limit,
                    config.RerankerMinScore,
                    searchFilter,
                    scopeCancellationToken);
                scopeTasks.Add(episodeTask);
            }

            if (config.CommunityConfig is not null)
            {
                communityTask = CommunitySearchAsync(
                    driver,
                    clients.CrossEncoder,
                    query,
                    effectiveQueryVector,
                    groupIds,
                    config.CommunityConfig,
                    config.Limit,
                    config.RerankerMinScore,
                    scopeCancellationToken);
                scopeTasks.Add(communityTask);
            }

            if (scopeTasks.Count > 0)
            {
                await AwaitSearchScopesAsync(scopeTasks, scopeCancellation).ConfigureAwait(false);
            }

            if (edgeTask is not null)
            {
                var ranked = await edgeTask.ConfigureAwait(false);
                (results.Edges, results.EdgeRerankerScores) = SearchResultComposer.SplitRankedResults(ranked);
            }

            if (nodeTask is not null)
            {
                var ranked = await nodeTask.ConfigureAwait(false);
                (results.Nodes, results.NodeRerankerScores) = SearchResultComposer.SplitRankedResults(ranked);
            }

            if (episodeTask is not null)
            {
                var ranked = await episodeTask.ConfigureAwait(false);
                (results.Episodes, results.EpisodeRerankerScores) = SearchResultComposer.SplitRankedResults(ranked);
            }

            if (communityTask is not null)
            {
                var ranked = await communityTask.ConfigureAwait(false);
                (results.Communities, results.CommunityRerankerScores) = SearchResultComposer.SplitRankedResults(ranked);
            }

            activity?.SetTag("graphiti.result.nodes", results.Nodes.Count);
            activity?.SetTag("graphiti.result.edges", results.Edges.Count);
            activity?.SetTag("graphiti.result.episodes", results.Episodes.Count);
            activity?.SetTag("graphiti.result.communities", results.Communities.Count);
            GraphitiTelemetry.SetOk(activity);
            return results;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private static string NormalizeQueryForEmbedding(string query) =>
        query.Replace('\n', ' ');

    private static ISearchGraphDriver CreateSearchDriver(
        IGraphDriver driver,
        IReadOnlyList<string>? rankGroupIds,
        bool materializeEmbeddingsForFulltext = false) =>
        driver as ISearchGraphDriver
        ?? new MaterializingSearchGraphDriver(
            driver,
            rankGroupIds,
            materializeEmbeddingsForFulltext);

    private static async Task AwaitSearchScopesAsync(
        List<Task> tasks,
        CancellationTokenSource cancellationSource)
    {
        var remaining = new List<Task>(tasks);
        while (remaining.Count > 0)
        {
            var completed = await Task.WhenAny(remaining).ConfigureAwait(false);
            if (completed.IsFaulted)
            {
                cancellationSource.Cancel();
                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch
                {
                }

                await completed.ConfigureAwait(false);
            }

            remaining.Remove(completed);
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task<(
        IReadOnlyList<(TItem Item, float Score)> First,
        IReadOnlyList<(TItem Item, float Score)> Second)> AwaitOptionalSearchMethodsAsync<TItem>(
        Task<List<(TItem Item, float Score)>>? firstTask,
        Task<List<(TItem Item, float Score)>>? secondTask,
        CancellationTokenSource cancellationSource)
    {
        if (firstTask is null)
        {
            return (
                EmptyRanked<TItem>(),
                secondTask is null
                    ? EmptyRanked<TItem>()
                    : await secondTask.ConfigureAwait(false));
        }

        if (secondTask is null)
        {
            return (
                await firstTask.ConfigureAwait(false),
                EmptyRanked<TItem>());
        }

        await AwaitSearchPairAsync(firstTask, secondTask, cancellationSource).ConfigureAwait(false);
        return (
            await firstTask.ConfigureAwait(false),
            await secondTask.ConfigureAwait(false));
    }

    private static (TItem Item, float Score)[] EmptyRanked<TItem>() =>
        Array.Empty<(TItem Item, float Score)>();

    private static async Task AwaitSearchPairAsync(
        Task firstTask,
        Task secondTask,
        CancellationTokenSource cancellationSource)
    {
        var completed = await Task.WhenAny(firstTask, secondTask).ConfigureAwait(false);
        if (completed.IsFaulted)
        {
            cancellationSource.Cancel();
            try
            {
                await Task.WhenAll(firstTask, secondTask).ConfigureAwait(false);
            }
            catch
            {
            }

            await completed.ConfigureAwait(false);
        }

        await Task.WhenAll(firstTask, secondTask).ConfigureAwait(false);
    }

    public static async Task<IReadOnlyList<(EntityEdge Item, float Score)>> EdgeSearchAsync(
        IGraphDriver driver,
        ICrossEncoderClient crossEncoder,
        string query,
        IReadOnlyList<float>? queryVector,
        IReadOnlyList<string>? groupIds,
        EdgeSearchConfig config,
        SearchFilters searchFilter,
        int limit,
        double rerankerMinScore = 0,
        string? centerNodeUuid = null,
        IReadOnlyList<string>? bfsOriginNodeUuids = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartSearchScopeActivity(
            "EdgeSearch",
            "edge",
            query,
            groupIds,
            limit,
            rerankerMinScore);

        try
        {
            SetEdgeSearchActivityTags(activity, config);
            var ranked = await EdgeSearchCoreAsync(
                driver,
                crossEncoder,
                query,
                queryVector,
                groupIds,
                config,
                searchFilter,
                limit,
                rerankerMinScore,
                centerNodeUuid,
                bfsOriginNodeUuids,
                cancellationToken).ConfigureAwait(false);
            activity?.SetTag("graphiti.result.count", ranked.Count);
            GraphitiTelemetry.SetOk(activity);
            return ranked;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private static async Task<IReadOnlyList<(EntityEdge Item, float Score)>> EdgeSearchCoreAsync(
        IGraphDriver driver,
        ICrossEncoderClient crossEncoder,
        string query,
        IReadOnlyList<float>? queryVector,
        IReadOnlyList<string>? groupIds,
        EdgeSearchConfig config,
        SearchFilters searchFilter,
        int limit,
        double rerankerMinScore = 0,
        string? centerNodeUuid = null,
        IReadOnlyList<string>? bfsOriginNodeUuids = null,
        CancellationToken cancellationToken = default)
    {
        SearchConfigValidator.Validate(config);
        SearchConfigValidator.ValidateLimit(limit);
        GraphitiHelpers.ValidateGroupIds(groupIds);
        var minScore = (float)rerankerMinScore;
        var hasBm25 = config.SearchMethods.Contains(EdgeSearchMethod.Bm25);
        var hasCosine = config.SearchMethods.Contains(EdgeSearchMethod.CosineSimilarity);
        var needsEmbeddings = hasCosine || config.Reranker == EdgeReranker.Mmr;
        var searchDriver = CreateSearchDriver(driver, groupIds, needsEmbeddings);
        using var methodCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var methodCancellationToken = methodCancellation.Token;
        Task<List<(EntityEdge Item, float Score)>>? textTask = hasBm25
            ? SearchRetrievalRunner.GetEdgeFulltextRankedAsync(
                searchDriver,
                query,
                groupIds,
                searchFilter,
                limit * 2,
                methodCancellationToken)
            : null;
        Task<List<(EntityEdge Item, float Score)>>? vectorTask = hasCosine && queryVector is not null
            ? SearchRetrievalRunner.GetEdgeVectorRankedAsync(
                searchDriver,
                queryVector,
                groupIds,
                searchFilter,
                limit * 2,
                (float)config.SimMinScore,
                methodCancellationToken)
            : null;
        var (textRanked, vectorRanked) = await AwaitOptionalSearchMethodsAsync(
            textTask,
            vectorTask,
            methodCancellation).ConfigureAwait(false);
        IReadOnlyList<(EntityEdge Item, float Score)> bfsRanked = config.SearchMethods.Contains(EdgeSearchMethod.Bfs)
            ? await SearchRetrievalRunner.EdgeBfsSearchAsync(
                searchDriver,
                EdgeBfsOrigins(bfsOriginNodeUuids, textRanked, vectorRanked),
                config.BfsMaxDepth,
                groupIds,
                searchFilter,
                limit * 2,
                cancellationToken).ConfigureAwait(false)
            : EmptyRanked<EntityEdge>();

        var fusionLimit = config.Reranker == EdgeReranker.Rrf ? limit : int.MaxValue;
        var rankedLists = new IReadOnlyList<(EntityEdge Item, float Score)>[] { textRanked, vectorRanked, bfsRanked };
        var ranked = config.Reranker is EdgeReranker.Rrf or EdgeReranker.NodeDistance or EdgeReranker.EpisodeMentions
            ? SearchResultComposer.FuseRanks(rankedLists, edge => edge.Uuid, fusionLimit, minScore)
            : SearchResultComposer.MergeRankedCandidates(rankedLists, edge => edge.Uuid);

        if (config.Reranker == EdgeReranker.Rrf)
        {
            return SearchResultComposer.LimitRanked(ranked, limit);
        }

        if (config.Reranker == EdgeReranker.CrossEncoder && ranked.Count > 0)
        {
            ranked = await SearchResultComposer.ApplyCrossEncoderRerankerAsync(
                crossEncoder,
                query,
                ranked,
                edge => edge.Fact,
                minScore,
                cancellationToken).ConfigureAwait(false);
        }
        else if (config.Reranker == EdgeReranker.Mmr && queryVector is not null)
        {
            ranked = SearchResultComposer.ApplyMmrReranker(
                ranked,
                queryVector,
                edge => edge.FactEmbedding,
                limit,
                (float)config.MmrLambda,
                minScore);
        }
        else if (config.Reranker == EdgeReranker.NodeDistance)
        {
            if (string.IsNullOrEmpty(centerNodeUuid))
            {
                throw new SearchRerankerException("No center node provided for Node Distance reranker");
            }

            ranked = await SearchResultComposer.ApplyEdgeNodeDistanceRerankerAsync(
                searchDriver,
                ranked,
                centerNodeUuid,
                minScore,
                cancellationToken).ConfigureAwait(false);
        }
        else if (config.Reranker == EdgeReranker.EpisodeMentions)
        {
            ranked = SearchResultComposer.SortByEpisodeMentions(ranked);
        }

        return SearchResultComposer.LimitRanked(ranked, limit);
    }

    public static async Task<IReadOnlyList<(EntityNode Item, float Score)>> NodeSearchAsync(
        IGraphDriver driver,
        ICrossEncoderClient crossEncoder,
        string query,
        IReadOnlyList<float>? queryVector,
        IReadOnlyList<string>? groupIds,
        NodeSearchConfig config,
        SearchFilters searchFilter,
        int limit,
        double rerankerMinScore = 0,
        string? centerNodeUuid = null,
        IReadOnlyList<string>? bfsOriginNodeUuids = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartSearchScopeActivity(
            "NodeSearch",
            "node",
            query,
            groupIds,
            limit,
            rerankerMinScore);

        try
        {
            SetNodeSearchActivityTags(activity, config);
            var ranked = await NodeSearchCoreAsync(
                driver,
                crossEncoder,
                query,
                queryVector,
                groupIds,
                config,
                searchFilter,
                limit,
                rerankerMinScore,
                centerNodeUuid,
                bfsOriginNodeUuids,
                cancellationToken).ConfigureAwait(false);
            activity?.SetTag("graphiti.result.count", ranked.Count);
            GraphitiTelemetry.SetOk(activity);
            return ranked;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private static async Task<IReadOnlyList<(EntityNode Item, float Score)>> NodeSearchCoreAsync(
        IGraphDriver driver,
        ICrossEncoderClient crossEncoder,
        string query,
        IReadOnlyList<float>? queryVector,
        IReadOnlyList<string>? groupIds,
        NodeSearchConfig config,
        SearchFilters searchFilter,
        int limit,
        double rerankerMinScore = 0,
        string? centerNodeUuid = null,
        IReadOnlyList<string>? bfsOriginNodeUuids = null,
        CancellationToken cancellationToken = default)
    {
        SearchConfigValidator.Validate(config);
        SearchConfigValidator.ValidateLimit(limit);
        GraphitiHelpers.ValidateGroupIds(groupIds);
        var minScore = (float)rerankerMinScore;
        var hasBm25 = config.SearchMethods.Contains(NodeSearchMethod.Bm25);
        var hasCosine = config.SearchMethods.Contains(NodeSearchMethod.CosineSimilarity);
        var needsEmbeddings = hasCosine || config.Reranker == NodeReranker.Mmr;
        var searchDriver = CreateSearchDriver(driver, groupIds, needsEmbeddings);
        using var methodCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var methodCancellationToken = methodCancellation.Token;
        Task<List<(EntityNode Item, float Score)>>? textTask = hasBm25
            ? SearchRetrievalRunner.GetNodeFulltextRankedAsync(
                searchDriver,
                query,
                groupIds,
                searchFilter,
                limit * 2,
                methodCancellationToken)
            : null;
        Task<List<(EntityNode Item, float Score)>>? vectorTask = hasCosine && queryVector is not null
            ? SearchRetrievalRunner.GetNodeVectorRankedAsync(
                searchDriver,
                queryVector,
                groupIds,
                searchFilter,
                limit * 2,
                (float)config.SimMinScore,
                methodCancellationToken)
            : null;
        var (textRanked, vectorRanked) = await AwaitOptionalSearchMethodsAsync(
            textTask,
            vectorTask,
            methodCancellation).ConfigureAwait(false);
        IReadOnlyList<(EntityNode Item, float Score)> bfsRanked = config.SearchMethods.Contains(NodeSearchMethod.Bfs)
            ? await SearchRetrievalRunner.NodeBfsSearchAsync(
                searchDriver,
                NodeBfsOrigins(bfsOriginNodeUuids, textRanked, vectorRanked),
                config.BfsMaxDepth,
                groupIds,
                searchFilter,
                limit * 2,
                cancellationToken).ConfigureAwait(false)
            : EmptyRanked<EntityNode>();

        var fusionLimit = config.Reranker == NodeReranker.Rrf ? limit : int.MaxValue;
        var rankedLists = new IReadOnlyList<(EntityNode Item, float Score)>[] { textRanked, vectorRanked, bfsRanked };
        var ranked = config.Reranker is NodeReranker.Rrf or NodeReranker.NodeDistance or NodeReranker.EpisodeMentions
            ? SearchResultComposer.FuseRanks(rankedLists, node => node.Uuid, fusionLimit, minScore)
            : SearchResultComposer.MergeRankedCandidates(rankedLists, node => node.Uuid);

        if (config.Reranker == NodeReranker.Rrf)
        {
            return SearchResultComposer.LimitRanked(ranked, limit);
        }

        if (config.Reranker == NodeReranker.CrossEncoder && ranked.Count > 0)
        {
            ranked = await SearchResultComposer.ApplyCrossEncoderRerankerAsync(
                crossEncoder,
                query,
                ranked,
                node => node.Name,
                minScore,
                cancellationToken).ConfigureAwait(false);
        }
        else if (config.Reranker == NodeReranker.Mmr && queryVector is not null)
        {
            ranked = SearchResultComposer.ApplyMmrReranker(
                ranked,
                queryVector,
                node => node.NameEmbedding,
                limit,
                (float)config.MmrLambda,
                minScore);
        }
        else if (config.Reranker == NodeReranker.NodeDistance)
        {
            if (string.IsNullOrEmpty(centerNodeUuid))
            {
                throw new SearchRerankerException("No center node provided for Node Distance reranker");
            }

            ranked = await SearchResultComposer.ApplyNodeDistanceRerankerAsync(
                searchDriver,
                ranked,
                centerNodeUuid,
                minScore,
                cancellationToken).ConfigureAwait(false);
        }
        else if (config.Reranker == NodeReranker.EpisodeMentions)
        {
            ranked = await SearchResultComposer.ApplyNodeEpisodeMentionsRerankerAsync(
                searchDriver,
                ranked,
                minScore,
                cancellationToken).ConfigureAwait(false);
        }

        return SearchResultComposer.LimitRanked(ranked, limit);
    }

    public static async Task<IReadOnlyList<(EpisodicNode Item, float Score)>> EpisodeSearchAsync(
        IGraphDriver driver,
        ICrossEncoderClient crossEncoder,
        string query,
        IReadOnlyList<string>? groupIds,
        EpisodeSearchConfig config,
        int limit,
        double rerankerMinScore = 0,
        SearchFilters? searchFilter = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartSearchScopeActivity(
            "EpisodeSearch",
            "episode",
            query,
            groupIds,
            limit,
            rerankerMinScore);

        try
        {
            SetEpisodeSearchActivityTags(activity, config);
            var ranked = await EpisodeSearchCoreAsync(
                driver,
                crossEncoder,
                query,
                groupIds,
                config,
                limit,
                rerankerMinScore,
                searchFilter,
                cancellationToken).ConfigureAwait(false);
            activity?.SetTag("graphiti.result.count", ranked.Count);
            GraphitiTelemetry.SetOk(activity);
            return ranked;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private static async Task<IReadOnlyList<(EpisodicNode Item, float Score)>> EpisodeSearchCoreAsync(
        IGraphDriver driver,
        ICrossEncoderClient crossEncoder,
        string query,
        IReadOnlyList<string>? groupIds,
        EpisodeSearchConfig config,
        int limit,
        double rerankerMinScore = 0,
        SearchFilters? searchFilter = null,
        CancellationToken cancellationToken = default)
    {
        SearchConfigValidator.Validate(config);
        SearchConfigValidator.ValidateLimit(limit);
        GraphitiHelpers.ValidateGroupIds(groupIds);
        searchFilter ??= new SearchFilters();
        var searchDriver = CreateSearchDriver(driver, groupIds);
        var textRanked = await SearchRetrievalRunner.GetEpisodeFulltextRankedAsync(
            searchDriver,
            query,
            groupIds,
            searchFilter,
            limit * 2,
            cancellationToken).ConfigureAwait(false);
        var fusionLimit = config.Reranker == EpisodeReranker.Rrf ? limit : int.MaxValue;
        var rankedLists = new IReadOnlyList<(EpisodicNode Item, float Score)>[] { textRanked };
        var minScore = (float)rerankerMinScore;
        var ranked = SearchResultComposer.FuseRanks(rankedLists, episode => episode.Uuid, fusionLimit, minScore);

        if (config.Reranker == EpisodeReranker.Rrf)
        {
            return SearchResultComposer.LimitRanked(ranked, limit);
        }

        if (config.Reranker == EpisodeReranker.CrossEncoder && ranked.Count > 0)
        {
            ranked = await SearchResultComposer.ApplyCrossEncoderRerankerAsync(
                crossEncoder,
                query,
                ranked,
                episode => episode.Content,
                minScore,
                cancellationToken).ConfigureAwait(false);
        }

        return SearchResultComposer.LimitRanked(ranked, limit);
    }

    public static async Task<IReadOnlyList<(CommunityNode Item, float Score)>> CommunitySearchAsync(
        IGraphDriver driver,
        ICrossEncoderClient crossEncoder,
        string query,
        IReadOnlyList<float>? queryVector,
        IReadOnlyList<string>? groupIds,
        CommunitySearchConfig config,
        int limit,
        double rerankerMinScore = 0,
        CancellationToken cancellationToken = default)
    {
        using var activity = StartSearchScopeActivity(
            "CommunitySearch",
            "community",
            query,
            groupIds,
            limit,
            rerankerMinScore);

        try
        {
            SetCommunitySearchActivityTags(activity, config);
            var ranked = await CommunitySearchCoreAsync(
                driver,
                crossEncoder,
                query,
                queryVector,
                groupIds,
                config,
                limit,
                rerankerMinScore,
                cancellationToken).ConfigureAwait(false);
            activity?.SetTag("graphiti.result.count", ranked.Count);
            GraphitiTelemetry.SetOk(activity);
            return ranked;
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    private static async Task<IReadOnlyList<(CommunityNode Item, float Score)>> CommunitySearchCoreAsync(
        IGraphDriver driver,
        ICrossEncoderClient crossEncoder,
        string query,
        IReadOnlyList<float>? queryVector,
        IReadOnlyList<string>? groupIds,
        CommunitySearchConfig config,
        int limit,
        double rerankerMinScore = 0,
        CancellationToken cancellationToken = default)
    {
        SearchConfigValidator.Validate(config);
        SearchConfigValidator.ValidateLimit(limit);
        GraphitiHelpers.ValidateGroupIds(groupIds);
        var minScore = (float)rerankerMinScore;
        var hasCosine = CommunityUsesCosine(config);
        var searchDriver = CreateSearchDriver(
            driver,
            groupIds,
            hasCosine || config.Reranker == CommunityReranker.Mmr);
        using var methodCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var methodCancellationToken = methodCancellation.Token;
        Task<List<(CommunityNode Item, float Score)>> textTask = SearchRetrievalRunner.GetCommunityFulltextRankedAsync(
            searchDriver,
            query,
            groupIds,
            limit * 2,
            methodCancellationToken);
        Task<List<(CommunityNode Item, float Score)>>? vectorTask = hasCosine && queryVector is not null
            ? SearchRetrievalRunner.GetCommunityVectorRankedAsync(
                searchDriver,
                queryVector,
                groupIds,
                limit * 2,
                (float)config.SimMinScore,
                methodCancellationToken)
            : null;
        var (textRanked, vectorRanked) = await AwaitOptionalSearchMethodsAsync(
            textTask,
            vectorTask,
            methodCancellation).ConfigureAwait(false);

        var fusionLimit = config.Reranker == CommunityReranker.Rrf ? limit : int.MaxValue;
        var rankedLists = new IReadOnlyList<(CommunityNode Item, float Score)>[] { textRanked, vectorRanked };
        var ranked = config.Reranker == CommunityReranker.Rrf
            ? SearchResultComposer.FuseRanks(
                rankedLists,
                community => community.Uuid,
                fusionLimit,
                minScore)
            : SearchResultComposer.MergeRankedCandidates(rankedLists, community => community.Uuid);

        if (config.Reranker == CommunityReranker.Rrf)
        {
            return SearchResultComposer.LimitRanked(ranked, limit);
        }

        if (config.Reranker == CommunityReranker.CrossEncoder && ranked.Count > 0)
        {
            ranked = await SearchResultComposer.ApplyCrossEncoderRerankerAsync(
                crossEncoder,
                query,
                ranked,
                community => community.Name,
                minScore,
                cancellationToken).ConfigureAwait(false);
        }
        else if (config.Reranker == CommunityReranker.Mmr && queryVector is not null)
        {
            ranked = SearchResultComposer.ApplyMmrReranker(
                ranked,
                queryVector,
                node => node.NameEmbedding,
                limit,
                (float)config.MmrLambda,
                minScore);
        }

        return SearchResultComposer.LimitRanked(ranked, limit);
    }

    private static bool CommunityUsesCosine(CommunitySearchConfig? config) =>
        config is not null
        && (config.SearchMethods.Count == 0 || config.SearchMethods.Contains(CommunitySearchMethod.CosineSimilarity));

    private static IReadOnlyList<string> EdgeBfsOrigins(
        IReadOnlyList<string>? explicitOrigins,
        IReadOnlyList<(EntityEdge Item, float Score)> textRanked,
        IReadOnlyList<(EntityEdge Item, float Score)> vectorRanked) =>
        explicitOrigins ?? DistinctBfsOrigins(textRanked, vectorRanked, static edge => edge.SourceNodeUuid);

    private static IReadOnlyList<string> NodeBfsOrigins(
        IReadOnlyList<string>? explicitOrigins,
        IReadOnlyList<(EntityNode Item, float Score)> textRanked,
        IReadOnlyList<(EntityNode Item, float Score)> vectorRanked) =>
        explicitOrigins ?? DistinctBfsOrigins(textRanked, vectorRanked, static node => node.Uuid);

    private static List<string> DistinctBfsOrigins<TItem>(
        IReadOnlyList<(TItem Item, float Score)> textRanked,
        IReadOnlyList<(TItem Item, float Score)> vectorRanked,
        Func<TItem, string> originSelector)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var origins = new List<string>();
        AddBfsOrigins(textRanked, originSelector, seen, origins);
        AddBfsOrigins(vectorRanked, originSelector, seen, origins);
        return origins;
    }

    private static void AddBfsOrigins<TItem>(
        IReadOnlyList<(TItem Item, float Score)> ranked,
        Func<TItem, string> originSelector,
        HashSet<string> seen,
        List<string> origins)
    {
        foreach (var (item, _) in ranked)
        {
            var origin = originSelector(item);
            if (seen.Add(origin))
            {
                origins.Add(origin);
            }
        }
    }

    private static Activity? StartSearchScopeActivity(
        string operation,
        string scope,
        string? query,
        IReadOnlyList<string>? groupIds,
        int limit,
        double rerankerMinScore)
    {
        var activity = GraphitiTelemetry.StartActivity($"SearchEngine.{operation}");
        activity?.SetTag("graphiti.search.scope", scope);
        activity?.SetTag("graphiti.query.length", query?.Length ?? 0);
        activity?.SetTag("graphiti.limit", limit);
        activity?.SetTag("graphiti.reranker.min_score", rerankerMinScore);
        GraphitiTelemetry.SetGroupIds(activity, groupIds);
        return activity;
    }

    private static void SetEdgeSearchActivityTags(Activity? activity, EdgeSearchConfig config)
    {
        activity?.SetTag("graphiti.search.methods", string.Join(",", config.SearchMethods.Select(method => method.ToWireValue())));
        activity?.SetTag("graphiti.search.reranker", config.Reranker.ToWireValue());
        activity?.SetTag("graphiti.search.sim_min_score", config.SimMinScore);
        activity?.SetTag("graphiti.search.mmr_lambda", config.MmrLambda);
        activity?.SetTag("graphiti.search.bfs_max_depth", config.BfsMaxDepth);
    }

    private static void SetNodeSearchActivityTags(Activity? activity, NodeSearchConfig config)
    {
        activity?.SetTag("graphiti.search.methods", string.Join(",", config.SearchMethods.Select(method => method.ToWireValue())));
        activity?.SetTag("graphiti.search.reranker", config.Reranker.ToWireValue());
        activity?.SetTag("graphiti.search.sim_min_score", config.SimMinScore);
        activity?.SetTag("graphiti.search.mmr_lambda", config.MmrLambda);
        activity?.SetTag("graphiti.search.bfs_max_depth", config.BfsMaxDepth);
    }

    private static void SetEpisodeSearchActivityTags(Activity? activity, EpisodeSearchConfig config)
    {
        activity?.SetTag("graphiti.search.methods", string.Join(",", config.SearchMethods.Select(method => method.ToWireValue())));
        activity?.SetTag("graphiti.search.reranker", config.Reranker.ToWireValue());
        activity?.SetTag("graphiti.search.sim_min_score", config.SimMinScore);
        activity?.SetTag("graphiti.search.mmr_lambda", config.MmrLambda);
        activity?.SetTag("graphiti.search.bfs_max_depth", config.BfsMaxDepth);
    }

    private static void SetCommunitySearchActivityTags(Activity? activity, CommunitySearchConfig config)
    {
        activity?.SetTag("graphiti.search.methods", string.Join(",", config.SearchMethods.Select(method => method.ToWireValue())));
        activity?.SetTag("graphiti.search.reranker", config.Reranker.ToWireValue());
        activity?.SetTag("graphiti.search.sim_min_score", config.SimMinScore);
        activity?.SetTag("graphiti.search.mmr_lambda", config.MmrLambda);
        activity?.SetTag("graphiti.search.bfs_max_depth", config.BfsMaxDepth);
    }
}
