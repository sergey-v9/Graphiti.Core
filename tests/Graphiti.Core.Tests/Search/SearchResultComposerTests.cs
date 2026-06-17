namespace Graphiti.Core.Tests.Search;

public class SearchResultComposerTests
{
    [Fact]
    public void FuseRanks_IgnoresInputScoresAndKeepsFirstSeenTieOrder()
    {
        var firstA = new ComposerCandidate("a", "first-a");
        var secondA = new ComposerCandidate("a", "second-a");
        var b = new ComposerCandidate("b", "b");
        var c = new ComposerCandidate("c", "c");
        var rankedLists = new IReadOnlyList<(ComposerCandidate Item, float Score)>[]
        {
            new[] { (firstA, -100f), (b, 100f) },
            new[] { (c, 100f), (secondA, -100f) }
        };

        var fused = SearchResultComposer.FuseRanks(
            rankedLists,
            candidate => candidate.Key,
            limit: 10);

        Assert.Equal(new[] { "a", "c", "b" }, fused.Select(item => item.Item.Key));
        Assert.Equal("first-a", fused[0].Item.Label);
        Assert.Equal(new[] { 1.5f, 1f, 0.5f }, fused.Select(item => item.Score));
    }

    [Fact]
    public void FuseRanks_FiltersByInclusiveMinimumScore()
    {
        var rankedLists = new IReadOnlyList<(ComposerCandidate Item, float Score)>[]
        {
            new[]
            {
                (new ComposerCandidate("a", "a"), 0f),
                (new ComposerCandidate("b", "b"), 100f)
            },
            new[]
            {
                (new ComposerCandidate("c", "c"), 100f)
            }
        };

        var fused = SearchResultComposer.FuseRanks(
            rankedLists,
            candidate => candidate.Key,
            limit: 10,
            minScore: 1f);

        Assert.Equal(new[] { "a", "c" }, fused.Select(item => item.Item.Key));
        Assert.Equal(new[] { 1f, 1f }, fused.Select(item => item.Score));
    }

    [Fact]
    public void FuseRanks_DirectSingleListUsesRrfScoresAndLimit()
    {
        var a = new ComposerCandidate("a", "a");
        var b = new ComposerCandidate("b", "b");
        var c = new ComposerCandidate("c", "c");

        var fused = SearchResultComposer.FuseRanks(
            new[] { (a, 100f), (b, -100f), (c, 50f) },
            candidate => candidate.Key,
            limit: 2,
            minScore: 0.5f);

        Assert.Equal(new[] { "a", "b" }, fused.Select(item => item.Item.Key));
        Assert.Equal(new[] { 1f, 0.5f }, fused.Select(item => item.Score));
    }

    [Fact]
    public void FuseRanks_DirectTwoListsIgnoresInputScoresAndKeepsFirstSeenTieOrder()
    {
        var firstA = new ComposerCandidate("a", "first-a");
        var secondA = new ComposerCandidate("a", "second-a");
        var b = new ComposerCandidate("b", "b");
        var c = new ComposerCandidate("c", "c");

        var fused = SearchResultComposer.FuseRanks(
            new[] { (firstA, -100f), (b, 100f) },
            new[] { (c, 100f), (secondA, -100f) },
            candidate => candidate.Key,
            limit: 10);

        Assert.Equal(new[] { "a", "c", "b" }, fused.Select(item => item.Item.Key));
        Assert.Equal("first-a", fused[0].Item.Label);
        Assert.Equal(new[] { 1.5f, 1f, 0.5f }, fused.Select(item => item.Score));
    }

    [Fact]
    public void FuseRanks_DirectListsMatchEnumerableComposition()
    {
        var firstA = new ComposerCandidate("a", "first-a");
        var secondA = new ComposerCandidate("a", "second-a");
        var b = new ComposerCandidate("b", "b");
        var c = new ComposerCandidate("c", "c");
        var d = new ComposerCandidate("d", "d");
        var first = new[] { (firstA, -100f), (b, 100f) };
        var second = new[] { (c, 100f), (secondA, -100f) };
        var third = new[] { (d, 50f), (b, 10f) };

        var expected = SearchResultComposer.FuseRanks(
            new IReadOnlyList<(ComposerCandidate Item, float Score)>[] { first, second, third },
            candidate => candidate.Key,
            limit: 10,
            minScore: 0.75f);
        var actual = SearchResultComposer.FuseRanks(
            first,
            second,
            third,
            candidate => candidate.Key,
            limit: 10,
            minScore: 0.75f);

        Assert.Equal(expected.Select(item => item.Item.Key), actual.Select(item => item.Item.Key));
        Assert.Equal(expected.Select(item => item.Item.Label), actual.Select(item => item.Item.Label));
        Assert.Equal(expected.Select(item => item.Score), actual.Select(item => item.Score));
    }

    [Fact]
    public void MergeRankedCandidates_KeepsFirstItemAndMaxScoreWithStableTies()
    {
        var firstA = new ComposerCandidate("a", "first-a");
        var secondA = new ComposerCandidate("a", "second-a");
        var b = new ComposerCandidate("b", "b");
        var c = new ComposerCandidate("c", "c");
        var rankedLists = new IReadOnlyList<(ComposerCandidate Item, float Score)>[]
        {
            new[] { (firstA, 0.4f), (b, 0.8f) },
            new[] { (secondA, 0.9f), (c, 0.8f) }
        };

        var merged = SearchResultComposer.MergeRankedCandidates(
            rankedLists,
            candidate => candidate.Key);

        Assert.Equal(new[] { "a", "b", "c" }, merged.Select(item => item.Item.Key));
        Assert.Equal("first-a", merged[0].Item.Label);
        Assert.Equal(new[] { 0.9f, 0.8f, 0.8f }, merged.Select(item => item.Score));
    }

    [Fact]
    public void MergeRankedCandidates_DirectTwoListsKeepsFirstItemAndMaxScoreWithStableTies()
    {
        var firstA = new ComposerCandidate("a", "first-a");
        var secondA = new ComposerCandidate("a", "second-a");
        var b = new ComposerCandidate("b", "b");
        var c = new ComposerCandidate("c", "c");

        var merged = SearchResultComposer.MergeRankedCandidates(
            new[] { (firstA, 0.4f), (b, 0.8f) },
            new[] { (secondA, 0.9f), (c, 0.8f) },
            candidate => candidate.Key);

        Assert.Equal(new[] { "a", "b", "c" }, merged.Select(item => item.Item.Key));
        Assert.Equal("first-a", merged[0].Item.Label);
        Assert.Equal(new[] { 0.9f, 0.8f, 0.8f }, merged.Select(item => item.Score));
    }

    [Fact]
    public void MergeRankedCandidates_DirectListsMatchEnumerableComposition()
    {
        var firstA = new ComposerCandidate("a", "first-a");
        var secondA = new ComposerCandidate("a", "second-a");
        var b = new ComposerCandidate("b", "b");
        var c = new ComposerCandidate("c", "c");
        var d = new ComposerCandidate("d", "d");
        var first = new[] { (firstA, 0.4f), (b, 0.8f) };
        var second = new[] { (secondA, 0.9f), (c, 0.8f) };
        var third = new[] { (d, 0.8f), (b, 1f) };

        var expected = SearchResultComposer.MergeRankedCandidates(
            new IReadOnlyList<(ComposerCandidate Item, float Score)>[] { first, second, third },
            candidate => candidate.Key);
        var actual = SearchResultComposer.MergeRankedCandidates(
            first,
            second,
            third,
            candidate => candidate.Key);

        Assert.Equal(expected.Select(item => item.Item.Key), actual.Select(item => item.Item.Key));
        Assert.Equal(expected.Select(item => item.Item.Label), actual.Select(item => item.Item.Label));
        Assert.Equal(expected.Select(item => item.Score), actual.Select(item => item.Score));
    }

    [Fact]
    public void MergeCandidatesInFirstSeenOrder_KeepsRetrievalOrderAndMaxScore()
    {
        var firstA = new ComposerCandidate("a", "first-a");
        var secondA = new ComposerCandidate("a", "second-a");
        var b = new ComposerCandidate("b", "b");
        var c = new ComposerCandidate("c", "c");
        var d = new ComposerCandidate("d", "d");

        var merged = SearchResultComposer.MergeCandidatesInFirstSeenOrder(
            new[] { (firstA, 0.1f), (b, 0.2f) },
            new[] { (c, 99f), (secondA, 100f) },
            new[] { (d, 98f) },
            candidate => candidate.Key);

        Assert.Equal(new[] { "a", "b", "c", "d" }, merged.Select(item => item.Item.Key));
        Assert.Equal("first-a", merged[0].Item.Label);
        Assert.Equal(new[] { 100f, 0.2f, 99f, 98f }, merged.Select(item => item.Score));
    }

    [Fact]
    public async Task CrossEncoderReranker_UsesIndexedRanksAndStableTieOrder()
    {
        var first = new ComposerCandidate("a", "first");
        var second = new ComposerCandidate("b", "second");
        var third = new ComposerCandidate("c", "third");
        var crossEncoder = new IndexedCrossEncoder(
        [
            new CrossEncoderRank(2, "third", 0.9f),
            new CrossEncoderRank(9, "missing", 10f),
            new CrossEncoderRank(-1, "negative", 10f),
            new CrossEncoderRank(1, "second", 0.5f),
            new CrossEncoderRank(1, "second duplicate", 0.8f),
            new CrossEncoderRank(0, "first", 0.5f)
        ]);

        var reranked = await SearchResultComposer.ApplyCrossEncoderRerankerAsync(
            crossEncoder,
            "query",
            new[] { (first, 0.1f), (second, 0.2f), (third, 0.3f) },
            candidate => candidate.Label,
            minScore: 0.5f,
            CancellationToken.None);

        Assert.Equal("query", crossEncoder.LastQuery);
        Assert.Equal(new[] { "first", "second", "third" }, crossEncoder.LastPassages);
        Assert.Equal(new[] { "c", "a", "b" }, reranked.Select(item => item.Item.Key));
        Assert.Equal(new[] { 0.9f, 0.5f, 0.5f }, reranked.Select(item => item.Score));
    }

    [Fact]
    public async Task CrossEncoderReranker_CollapsesDuplicatePassagesAndUsesLastCandidate()
    {
        var first = new ComposerCandidate("first", "same");
        var unique = new ComposerCandidate("unique", "unique");
        var last = new ComposerCandidate("last", "same");
        var crossEncoder = new IndexedCrossEncoder(
        [
            new CrossEncoderRank(1, "unique", 0.8f),
            new CrossEncoderRank(0, "same", 0.7f)
        ]);

        var reranked = await SearchResultComposer.ApplyCrossEncoderRerankerAsync(
            crossEncoder,
            "query",
            new[] { (first, 0.1f), (unique, 0.2f), (last, 0.3f) },
            candidate => candidate.Label,
            minScore: 0.5f,
            CancellationToken.None);

        Assert.Equal(new[] { "same", "unique" }, crossEncoder.LastPassages);
        Assert.Equal(new[] { "unique", "last" }, reranked.Select(item => item.Item.Key));
        Assert.Equal(new[] { 0.8f, 0.7f }, reranked.Select(item => item.Score));
    }

    [Fact]
    public void ToRankedList_PreservesHitOrderAndScores()
    {
        var first = new ComposerCandidate("a", "first");
        var second = new ComposerCandidate("b", "second");

        var ranked = SearchResultComposer.ToRankedList(
            new[]
            {
                new SearchHit<ComposerCandidate>(first, 0.25f),
                new SearchHit<ComposerCandidate>(second, 0.75f)
            });

        Assert.Equal(new[] { "a", "b" }, ranked.Select(item => item.Item.Key));
        Assert.Equal(new[] { 0.25f, 0.75f }, ranked.Select(item => item.Score));
    }

    [Fact]
    public void SplitRankedResults_PreservesOrderAndConvertsScores()
    {
        var first = new ComposerCandidate("a", "first");
        var second = new ComposerCandidate("b", "second");

        var (items, scores) = SearchResultComposer.SplitRankedResults(
            new[] { (first, 0.25f), (second, 0.75f) });

        Assert.Equal(new[] { "a", "b" }, items.Select(item => item.Key));
        Assert.Equal(new[] { 0.25d, 0.75d }, scores);
    }

    [Fact]
    public void LimitRanked_TruncatesWithoutReordering()
    {
        var first = new ComposerCandidate("a", "first");
        var second = new ComposerCandidate("b", "second");
        var third = new ComposerCandidate("c", "third");

        var ranked = new List<(ComposerCandidate Item, float Score)>
        {
            (first, 0.1f),
            (second, 0.2f),
            (third, 0.3f)
        };

        var limited = SearchResultComposer.LimitRanked(ranked, limit: 2);

        Assert.Same(ranked, limited);
        Assert.Equal(new[] { "a", "b" }, limited.Select(item => item.Item.Key));
        Assert.Equal(new[] { 0.1f, 0.2f }, limited.Select(item => item.Score));
        Assert.Empty(SearchResultComposer.LimitRanked(ranked, limit: 0));
    }

    [Fact]
    public void MmrReranker_UsesRankedItemsAndIgnoresInputScores()
    {
        var aligned = new VectorComposerCandidate("aligned", new[] { 1f, 0f });
        var mixed = new VectorComposerCandidate("mixed", new[] { 0.5f, 0.5f });
        var opposite = new VectorComposerCandidate("opposite", new[] { -1f, 0f });

        var reranked = SearchResultComposer.ApplyMmrReranker(
            new[] { (opposite, 100f), (mixed, 0.1f), (aligned, -100f) },
            new[] { 1f, 0f },
            candidate => candidate.Vector,
            limit: 2,
            lambda: 1,
            minScore: -2);

        Assert.Equal(new[] { "aligned", "mixed" }, reranked.Select(item => item.Item.Key));
        Assert.Equal(1f, reranked[0].Score, precision: 6);
        Assert.Equal(0.70710677f, reranked[1].Score, precision: 6);
    }

    [Fact]
    public void EpisodeMentionsSort_OrdersEdgesButKeepsScoresInPreSortOrder()
    {
        var firstTie = new EntityEdge { Uuid = "first", Episodes = { "a" } };
        var most = new EntityEdge { Uuid = "most", Episodes = { "a", "b" } };
        var secondTie = new EntityEdge { Uuid = "second", Episodes = { "a" } };

        var sorted = SearchResultComposer.SortByEpisodeMentions(
            new[] { (firstTie, 0.1f), (most, 0.2f), (secondTie, 0.3f) });

        Assert.Equal(new[] { "most", "first", "second" }, sorted.Select(item => item.Item.Uuid));
        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, sorted.Select(item => item.Score));
    }

    [Fact]
    public async Task EdgeNodeDistanceReranker_FansOutRanksToAllEdgesForSource()
    {
        var driver = new RankingSearchDriver
        {
            NodeDistanceRanks =
            [
                new SearchRank("missing", 9),
                new SearchRank("source-a", 2),
                new SearchRank("source-b", 1)
            ]
        };
        var ranked = new (EntityEdge Item, float Score)[]
        {
            (new EntityEdge { Uuid = "edge-a-1", SourceNodeUuid = "source-a" }, 0.9f),
            (new EntityEdge { Uuid = "edge-a-2", SourceNodeUuid = "source-a" }, 0.8f),
            (new EntityEdge { Uuid = "edge-b", SourceNodeUuid = "source-b" }, 0.7f)
        };

        var reranked = await SearchResultComposer.ApplyEdgeNodeDistanceRerankerAsync(
            driver,
            ranked,
            centerNodeUuid: "center",
            minScore: 0.5f,
            CancellationToken.None);

        Assert.Equal(new[] { "source-a", "source-b" }, driver.LastNodeDistanceRankUuids);
        Assert.Equal("center", driver.LastCenterNodeUuid);
        Assert.Equal(0.5f, driver.LastNodeDistanceMinScore);
        Assert.Equal(new[] { "edge-a-1", "edge-a-2", "edge-b" }, reranked.Select(item => item.Item.Uuid));
        Assert.Equal(new[] { 2f, 2f, 1f }, reranked.Select(item => item.Score));
    }

    [Fact]
    public async Task NodeDistanceReranker_PreservesDriverRankOrderAndIgnoresMissingRanks()
    {
        var driver = new RankingSearchDriver
        {
            NodeDistanceRanks =
            [
                new SearchRank("far", 3),
                new SearchRank("missing", 2),
                new SearchRank("near", 1)
            ]
        };
        var ranked = new (EntityNode Item, float Score)[]
        {
            (new EntityNode { Uuid = "near", Name = "Near" }, 0.9f),
            (new EntityNode { Uuid = "far", Name = "Far" }, 0.8f),
            (new EntityNode { Uuid = "near", Name = "Duplicate" }, 0.7f)
        };

        var reranked = await SearchResultComposer.ApplyNodeDistanceRerankerAsync(
            driver,
            ranked,
            centerNodeUuid: "center",
            minScore: 0.25f,
            CancellationToken.None);

        Assert.Equal(new[] { "near", "far" }, driver.LastNodeDistanceRankUuids);
        Assert.Equal(new[] { "far", "near" }, reranked.Select(item => item.Item.Uuid));
        Assert.Equal("Near", reranked[1].Item.Name);
        Assert.Equal(new[] { 3f, 1f }, reranked.Select(item => item.Score));
    }

    [Fact]
    public async Task EpisodeMentionsReranker_PreservesDriverRankOrderAndIgnoresMissingRanks()
    {
        var driver = new RankingSearchDriver
        {
            EpisodeMentionRanks =
            [
                new SearchRank("mentioned", 1),
                new SearchRank("missing", 2),
                new SearchRank("unmentioned", 3)
            ]
        };
        var ranked = new (EntityNode Item, float Score)[]
        {
            (new EntityNode { Uuid = "unmentioned", Name = "Unmentioned" }, 0.9f),
            (new EntityNode { Uuid = "mentioned", Name = "Mentioned" }, 0.8f)
        };

        var reranked = await SearchResultComposer.ApplyNodeEpisodeMentionsRerankerAsync(
            driver,
            ranked,
            minScore: 0.25f,
            CancellationToken.None);

        Assert.Equal(new[] { "unmentioned", "mentioned" }, driver.LastEpisodeMentionRankUuids);
        Assert.Equal(0.25f, driver.LastEpisodeMentionMinScore);
        Assert.Equal(new[] { "mentioned", "unmentioned" }, reranked.Select(item => item.Item.Uuid));
        Assert.Equal(new[] { 1f, 3f }, reranked.Select(item => item.Score));
    }

    private sealed record ComposerCandidate(string Key, string Label);

    private sealed record VectorComposerCandidate(string Key, IReadOnlyList<float> Vector);

    private sealed class IndexedCrossEncoder(IReadOnlyList<CrossEncoderRank> ranks) : CrossEncoderClient
    {
        public string? LastQuery { get; private set; }

        public IReadOnlyList<string>? LastPassages { get; private set; }

        public override Task<IReadOnlyList<(string Passage, float Score)>> RankAsync(
            string query,
            IReadOnlyList<string> passages,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override Task<IReadOnlyList<CrossEncoderRank>> RankIndexedAsync(
            string query,
            IReadOnlyList<string> passages,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            LastPassages = passages.ToList();
            return Task.FromResult(ranks);
        }
    }

    private sealed class RankingSearchDriver : ISearchGraphDriver
    {
        public IReadOnlyList<SearchRank> NodeDistanceRanks { get; init; } = [];

        public IReadOnlyList<SearchRank> EpisodeMentionRanks { get; init; } = [];

        public IReadOnlyList<string>? LastNodeDistanceRankUuids { get; private set; }

        public IReadOnlyList<string>? LastEpisodeMentionRankUuids { get; private set; }

        public string? LastCenterNodeUuid { get; private set; }

        public float LastNodeDistanceMinScore { get; private set; }

        public float LastEpisodeMentionMinScore { get; private set; }

        public Task<IReadOnlyList<SearchRank>> RankNodeDistanceAsync(
            IReadOnlyList<string> nodeUuids,
            string centerNodeUuid,
            float minScore = 0,
            CancellationToken cancellationToken = default)
        {
            LastNodeDistanceRankUuids = nodeUuids;
            LastCenterNodeUuid = centerNodeUuid;
            LastNodeDistanceMinScore = minScore;
            return Task.FromResult(NodeDistanceRanks);
        }

        public Task<IReadOnlyList<SearchRank>> RankNodeEpisodeMentionsAsync(
            IReadOnlyList<string> nodeUuids,
            float minScore = 0,
            CancellationToken cancellationToken = default)
        {
            LastEpisodeMentionRankUuids = nodeUuids;
            LastEpisodeMentionMinScore = minScore;
            return Task.FromResult(EpisodeMentionRanks);
        }

        public Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesFulltextAsync(
            string query,
            SearchFilters searchFilter,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesByEmbeddingAsync(
            IReadOnlyList<float> searchVector,
            SearchFilters searchFilter,
            IReadOnlyList<string>? groupIds,
            int limit,
            float minScore,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesFulltextAsync(
            string query,
            SearchFilters searchFilter,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesByEmbeddingAsync(
            IReadOnlyList<float> searchVector,
            SearchFilters searchFilter,
            IReadOnlyList<string>? groupIds,
            int limit,
            float minScore,
            string? sourceNodeUuid = null,
            string? targetNodeUuid = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SearchHit<EntityNode>>> SearchEntityNodesBfsAsync(
            IReadOnlyList<string>? originNodeUuids,
            SearchFilters searchFilter,
            int maxDepth,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SearchHit<EntityEdge>>> SearchEntityEdgesBfsAsync(
            IReadOnlyList<string>? originNodeUuids,
            SearchFilters searchFilter,
            int maxDepth,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SearchHit<EpisodicNode>>> SearchEpisodesFulltextAsync(
            string query,
            SearchFilters searchFilter,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesFulltextAsync(
            string query,
            IReadOnlyList<string>? groupIds,
            int limit,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SearchHit<CommunityNode>>> SearchCommunitiesByEmbeddingAsync(
            IReadOnlyList<float> searchVector,
            IReadOnlyList<string>? groupIds,
            int limit,
            float minScore,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
