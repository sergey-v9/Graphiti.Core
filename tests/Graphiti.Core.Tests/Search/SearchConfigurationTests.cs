using System.Text.Json;
using Graphiti.Core;

namespace Graphiti.Core.Tests.Search;

public sealed class SearchConfigurationTests
{
    [Fact]
    public void SearchConfig_UsesPythonDefaults()
    {
        var edgeConfig = new EdgeSearchConfig();
        var nodeConfig = new NodeSearchConfig();
        var episodeConfig = new EpisodeSearchConfig();
        var communityConfig = new CommunitySearchConfig();
        var searchConfig = new SearchConfig();

        Assert.Equal(10, SearchConfiguration.DefaultSearchLimit);
        Assert.Equal(0.6, SearchConfiguration.DefaultMinScore);
        Assert.Equal(0.5, SearchConfiguration.DefaultMmrLambda);
        Assert.Equal(3, SearchConfiguration.MaxSearchDepth);
        Assert.Equal(EdgeReranker.Rrf, edgeConfig.Reranker);
        Assert.Equal(NodeReranker.Rrf, nodeConfig.Reranker);
        Assert.Equal(EpisodeReranker.Rrf, episodeConfig.Reranker);
        Assert.Equal(CommunityReranker.Rrf, communityConfig.Reranker);
        Assert.Equal(0.6, edgeConfig.SimMinScore);
        Assert.Equal(0.5, nodeConfig.MmrLambda);
        Assert.Equal(3, episodeConfig.BfsMaxDepth);
        Assert.Equal(10, searchConfig.Limit);
        Assert.Equal(0, searchConfig.RerankerMinScore);
        Assert.Null(searchConfig.EdgeConfig);
        Assert.Null(searchConfig.NodeConfig);
        Assert.Null(searchConfig.EpisodeConfig);
        Assert.Null(searchConfig.CommunityConfig);
    }

    [Fact]
    public void Enums_ExposePythonWireValues()
    {
        Assert.Equal("cosine_similarity", EdgeSearchMethod.CosineSimilarity.ToWireValue());
        Assert.Equal("breadth_first_search", NodeSearchMethod.Bfs.ToWireValue());
        Assert.Equal("bm25", EpisodeSearchMethod.Bm25.ToWireValue());
        Assert.Equal("reciprocal_rank_fusion", CommunityReranker.Rrf.ToWireValue());
        Assert.Equal("episode_mentions", EdgeReranker.EpisodeMentions.ToWireValue());
        Assert.Equal("cross_encoder", NodeReranker.CrossEncoder.ToWireValue());
    }

    [Fact]
    public void SearchConfigJson_UsesPythonSnakeCaseAndStringEnums()
    {
        var config = new SearchConfig
        {
            EdgeConfig = new EdgeSearchConfig
            {
                SearchMethods = { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity },
                Reranker = EdgeReranker.CrossEncoder,
                SimMinScore = 0.7,
                MmrLambda = 0.25,
                BfsMaxDepth = 2
            },
            Limit = 5,
            RerankerMinScore = 0.1
        };

        var json = JsonSerializer.Serialize(config, GraphitiJsonSerializer.Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var edgeConfig = root.GetProperty("edge_config");

        Assert.False(root.TryGetProperty("edgeConfig", out _));
        Assert.Equal(new[] { "bm25", "cosine_similarity" }, edgeConfig.GetProperty("search_methods").EnumerateArray().Select(item => item.GetString()!));
        Assert.Equal("cross_encoder", edgeConfig.GetProperty("reranker").GetString());
        Assert.Equal(0.7, edgeConfig.GetProperty("sim_min_score").GetDouble());
        Assert.Equal(0.25, edgeConfig.GetProperty("mmr_lambda").GetDouble());
        Assert.Equal(2, edgeConfig.GetProperty("bfs_max_depth").GetInt32());
        Assert.Equal(5, root.GetProperty("limit").GetInt32());
        Assert.Equal(0.1, root.GetProperty("reranker_min_score").GetDouble());
    }

    [Fact]
    public void SearchConfigJson_ReadsPythonSnakeCaseAndStringEnums()
    {
        const string json = """
            {
              "edge_config": {
                "search_methods": ["bm25", "breadth_first_search"],
                "reranker": "node_distance",
                "sim_min_score": 0.65,
                "mmr_lambda": 0.2,
                "bfs_max_depth": 3
              },
              "limit": 8,
              "reranker_min_score": 0.4
            }
            """;

        var config = JsonSerializer.Deserialize<SearchConfig>(json, GraphitiJsonSerializer.Options)!;

        Assert.Equal(new[] { EdgeSearchMethod.Bm25, EdgeSearchMethod.Bfs }, config.EdgeConfig!.SearchMethods);
        Assert.Equal(EdgeReranker.NodeDistance, config.EdgeConfig.Reranker);
        Assert.Equal(0.65, config.EdgeConfig.SimMinScore);
        Assert.Equal(0.2, config.EdgeConfig.MmrLambda);
        Assert.Equal(3, config.EdgeConfig.BfsMaxDepth);
        Assert.Equal(8, config.Limit);
        Assert.Equal(0.4, config.RerankerMinScore);
    }

    [Fact]
    public void SearchFiltersJson_UsesPythonSnakeCaseAndComparisonOperators()
    {
        var date = new DateTime(2026, 5, 27, 12, 0, 0, DateTimeKind.Utc);
        var filters = new SearchFilters
        {
            NodeLabels = new List<string> { "Person" },
            EdgeTypes = new List<string> { "KNOWS" },
            EdgeUuids = new List<string> { "edge-1" },
            ValidAt = new List<List<DateFilter>> { new() { new DateFilter(ComparisonOperator.GreaterThanEqual, date) } },
            PropertyFilters = new List<PropertyFilter> { new("confidence", ComparisonOperator.NotEquals, 0.2) }
        };

        var json = JsonSerializer.Serialize(filters, GraphitiJsonSerializer.Options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.False(root.TryGetProperty("nodeLabels", out _));
        Assert.Equal("Person", root.GetProperty("node_labels")[0].GetString());
        Assert.Equal("KNOWS", root.GetProperty("edge_types")[0].GetString());
        Assert.Equal("edge-1", root.GetProperty("edge_uuids")[0].GetString());
        Assert.Equal(">=", root.GetProperty("valid_at")[0][0].GetProperty("comparison_operator").GetString());
        Assert.Equal("confidence", root.GetProperty("property_filters")[0].GetProperty("property_name").GetString());
        Assert.Equal("<>", root.GetProperty("property_filters")[0].GetProperty("comparison_operator").GetString());
    }

    [Fact]
    public void SearchFiltersJson_ReadsPythonSnakeCaseAndComparisonOperators()
    {
        const string json = """
            {
              "node_labels": ["Person"],
              "edge_types": ["KNOWS"],
              "edge_uuids": ["edge-1"],
              "valid_at": [[{"date": "2026-05-27T12:00:00Z", "comparison_operator": ">="}]],
              "property_filters": [{"property_name": "confidence", "property_value": 0.2, "comparison_operator": "<>"}]
            }
            """;

        var filters = JsonSerializer.Deserialize<SearchFilters>(json, GraphitiJsonSerializer.Options)!;

        Assert.Equal(new[] { "Person" }, filters.NodeLabels);
        Assert.Equal(new[] { "KNOWS" }, filters.EdgeTypes);
        Assert.Equal(new[] { "edge-1" }, filters.EdgeUuids);
        Assert.Equal(ComparisonOperator.GreaterThanEqual, filters.ValidAt![0][0].ComparisonOperator);
        Assert.Equal(new DateTime(2026, 5, 27, 12, 0, 0, DateTimeKind.Utc), filters.ValidAt[0][0].Date!.Value);
        Assert.Equal("confidence", filters.PropertyFilters![0].PropertyName);
        Assert.Equal(ComparisonOperator.NotEquals, filters.PropertyFilters[0].ComparisonOperator);
        Assert.Equal(0.2, Assert.IsType<double>(filters.PropertyFilters[0].PropertyValue));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("""{"nested":1}""")]
    [InlineData("[1]")]
    public void SearchFiltersJson_RejectsUnsupportedPropertyFilterValues(string propertyValue)
    {
        var json = $$"""
            {
              "property_filters": [
                {
                  "property_name": "confidence",
                  "property_value": {{propertyValue}},
                  "comparison_operator": "="
                }
              ]
            }
            """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SearchFilters>(json, GraphitiJsonSerializer.Options));
    }

    [Fact]
    public void SearchJson_RejectsNumericAndUnknownEnumValues()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EdgeSearchConfig>(
            """{"search_methods":[1]}""",
            GraphitiJsonSerializer.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EdgeSearchConfig>(
            """{"reranker":"unknown"}""",
            GraphitiJsonSerializer.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SearchFilters>(
            """{"valid_at":[[{"comparison_operator":1}]]}""",
            GraphitiJsonSerializer.Options));
    }

    [Fact]
    public void Recipes_MatchPythonCombinedMmrRecipe()
    {
        var config = SearchConfigRecipes.CombinedHybridSearchMmr;

        Assert.Equal(new[] { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity }, config.EdgeConfig!.SearchMethods);
        Assert.Equal(EdgeReranker.Mmr, config.EdgeConfig.Reranker);
        Assert.Equal(1, config.EdgeConfig.MmrLambda);
        Assert.Equal(new[] { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity }, config.NodeConfig!.SearchMethods);
        Assert.Equal(NodeReranker.Mmr, config.NodeConfig.Reranker);
        Assert.Equal(1, config.NodeConfig.MmrLambda);
        Assert.Equal(new[] { EpisodeSearchMethod.Bm25 }, config.EpisodeConfig!.SearchMethods);
        Assert.Equal(EpisodeReranker.Rrf, config.EpisodeConfig.Reranker);
        Assert.Equal(new[] { CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity }, config.CommunityConfig!.SearchMethods);
        Assert.Equal(CommunityReranker.Mmr, config.CommunityConfig.Reranker);
        Assert.Equal(1, config.CommunityConfig.MmrLambda);
        Assert.Equal(10, config.Limit);
    }

    [Fact]
    public void Recipes_MatchPythonCrossEncoderLimitsAndMethods()
    {
        var edgeConfig = SearchConfigRecipes.EdgeHybridSearchCrossEncoder;
        var nodeConfig = SearchConfigRecipes.NodeHybridSearchCrossEncoder;
        var communityConfig = SearchConfigRecipes.CommunityHybridSearchCrossEncoder;

        Assert.Equal(new[] { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity, EdgeSearchMethod.Bfs }, edgeConfig.EdgeConfig!.SearchMethods);
        Assert.Equal(EdgeReranker.CrossEncoder, edgeConfig.EdgeConfig.Reranker);
        Assert.Equal(10, edgeConfig.Limit);
        Assert.Equal(new[] { NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity, NodeSearchMethod.Bfs }, nodeConfig.NodeConfig!.SearchMethods);
        Assert.Equal(NodeReranker.CrossEncoder, nodeConfig.NodeConfig.Reranker);
        Assert.Equal(10, nodeConfig.Limit);
        Assert.Equal(new[] { CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity }, communityConfig.CommunityConfig!.SearchMethods);
        Assert.Equal(CommunityReranker.CrossEncoder, communityConfig.CommunityConfig.Reranker);
        Assert.Equal(3, communityConfig.Limit);
    }

    [Fact]
    public void Recipes_ReturnFreshInstances()
    {
        var first = SearchConfigRecipes.EdgeHybridSearchRrf;
        var second = SearchConfigRecipes.EdgeHybridSearchRrf;

        first.Limit = 99;
        first.EdgeConfig!.SearchMethods.Clear();

        Assert.Equal(10, second.Limit);
        Assert.Equal(new[] { EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity }, second.EdgeConfig!.SearchMethods);
    }

    [Fact]
    public async Task SearchEngine_RejectsInvalidSearchConfigBeforeExecuting()
    {
        var clients = new GraphitiClients(
            new InMemoryGraphDriver(),
            new NoOpLlmClient(),
            new HashEmbedder(2),
            new IdentityCrossEncoderClient());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SearchEngine.SearchAsync(
                clients,
                "query",
                groupIds: null,
                new SearchConfig { Limit = 0 },
                new SearchFilters()));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public async Task SearchEngine_RejectsInvalidMmrLambda(double mmrLambda)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SearchEngine.NodeSearchAsync(
                new InMemoryGraphDriver(),
                new IdentityCrossEncoderClient(),
                "query",
                new[] { 1f, 0f },
                groupIds: null,
                new NodeSearchConfig { MmrLambda = mmrLambda },
                new SearchFilters(),
                limit: 10));
    }

    [Fact]
    public async Task SearchEngine_RejectsUndefinedSearchMethodValues()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SearchEngine.EdgeSearchAsync(
                new InMemoryGraphDriver(),
                new IdentityCrossEncoderClient(),
                "query",
                new[] { 1f, 0f },
                groupIds: null,
                new EdgeSearchConfig { SearchMethods = { (EdgeSearchMethod)999 } },
                new SearchFilters(),
                limit: 10));
    }

    [Fact]
    public async Task SearchEngine_RejectsUndefinedRerankerValues()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SearchEngine.NodeSearchAsync(
                new InMemoryGraphDriver(),
                new IdentityCrossEncoderClient(),
                "query",
                new[] { 1f, 0f },
                groupIds: null,
                new NodeSearchConfig { Reranker = (NodeReranker)999 },
                new SearchFilters(),
                limit: 10));
    }

    [Fact]
    public void SearchResults_MergeConcatenatesAllResultLists()
    {
        var first = new SearchResults
        {
            Edges = { new EntityEdge { Uuid = "edge-1" } },
            EdgeRerankerScores = { 0.9 },
            Nodes = { new EntityNode { Uuid = "node-1" } },
            NodeRerankerScores = { 0.8 },
            Episodes = { new EpisodicNode { Uuid = "episode-1" } },
            EpisodeRerankerScores = { 0.7 },
            Communities = { new CommunityNode { Uuid = "community-1" } },
            CommunityRerankerScores = { 0.6 }
        };
        var second = new SearchResults
        {
            Edges = { new EntityEdge { Uuid = "edge-2" } },
            EdgeRerankerScores = { 0.5 },
            Nodes = { new EntityNode { Uuid = "node-2" } },
            NodeRerankerScores = { 0.4 },
            Episodes = { new EpisodicNode { Uuid = "episode-2" } },
            EpisodeRerankerScores = { 0.3 },
            Communities = { new CommunityNode { Uuid = "community-2" } },
            CommunityRerankerScores = { 0.2 }
        };

        var merged = SearchResults.Merge(new[] { first, second });

        Assert.Equal(new[] { "edge-1", "edge-2" }, merged.Edges.Select(edge => edge.Uuid));
        Assert.Equal(new[] { 0.9, 0.5 }, merged.EdgeRerankerScores);
        Assert.Equal(new[] { "node-1", "node-2" }, merged.Nodes.Select(node => node.Uuid));
        Assert.Equal(new[] { 0.8, 0.4 }, merged.NodeRerankerScores);
        Assert.Equal(new[] { "episode-1", "episode-2" }, merged.Episodes.Select(episode => episode.Uuid));
        Assert.Equal(new[] { 0.7, 0.3 }, merged.EpisodeRerankerScores);
        Assert.Equal(new[] { "community-1", "community-2" }, merged.Communities.Select(community => community.Uuid));
        Assert.Equal(new[] { 0.6, 0.2 }, merged.CommunityRerankerScores);
    }

    [Fact]
    public void SearchResults_MergeReturnsEmptyResultsForEmptyInput()
    {
        var merged = SearchResults.Merge(Array.Empty<SearchResults>());

        Assert.Empty(merged.Edges);
        Assert.Empty(merged.EdgeRerankerScores);
        Assert.Empty(merged.Nodes);
        Assert.Empty(merged.NodeRerankerScores);
        Assert.Empty(merged.Episodes);
        Assert.Empty(merged.EpisodeRerankerScores);
        Assert.Empty(merged.Communities);
        Assert.Empty(merged.CommunityRerankerScores);
    }
}
