using Graphiti.Core.Drivers.Ladybug;

namespace Graphiti.Core.Tests.Drivers.Ladybug;

public class LadybugSearchExecutorTests
{
    [Fact]
    public async Task FulltextSearch_SkipsEmptyKuzuQueryAndMapsScoredNodes()
    {
        var recorder = new RecordingLadybugExecutor();
        var search = new LadybugSearchExecutor(recorder);

        var empty = await search.SearchEntityNodesFulltextAsync(
            "   ",
            new SearchFilters(),
            groupIds: null,
            limit: 5);

        Assert.Empty(empty);
        Assert.Empty(recorder.Queried);

        recorder.EnqueueQuery(EntityRecord("entity-1", "Alice", score: 0.91f));

        var hits = await search.SearchEntityNodesFulltextAsync(
            "Alice",
            new SearchFilters { NodeLabels = new List<string> { "Person" } },
            new[] { "tenant" },
            limit: 5);

        var hit = Assert.Single(hits);
        Assert.Equal("entity-1", hit.Item.Uuid);
        Assert.Equal("Alice", hit.Item.Name);
        Assert.Equal(0.91f, hit.Score);
        Assert.Single(recorder.Queried);
        Assert.Contains("QUERY_FTS_INDEX('Entity'", recorder.Queried[0].Query, StringComparison.Ordinal);
        Assert.Equal("Alice", recorder.Queried[0].Parameters["query"]);
        Assert.Equal(new[] { "tenant" }, Assert.IsType<List<string>>(recorder.Queried[0].Parameters["group_ids"]));
    }

    [Fact]
    public async Task FulltextSearch_UsesKuzuVerbatimOrEmptyQuerySemantics()
    {
        // Mirrors graphiti_core/search/search_utils.py:88-92 (KUZU branch of fulltext_query):
        // the query is returned VERBATIM (no whitespace normalization, no per-term truncation), and
        // when the single-space word count exceeds MAX_QUERY_LENGTH the search is skipped entirely
        // (empty string => LadybugSearchExecutor returns no results without querying the index).
        var recorder = new RecordingLadybugExecutor();
        var search = new LadybugSearchExecutor(recorder);

        // 129 single-space-separated words => 129 > 128 => over the limit => no search issued.
        var overLimitQuery = string.Join(" ", Enumerable.Repeat("term", SearchUtilities.MaxQueryLength + 1));

        // A query that contains internal tabs/newlines but only enough spaces to stay within the
        // single-space word limit must pass through unchanged, preserving every character.
        const string verbatimQuery = "  Alice+(Bob)\tgroup_id:tenant/one\r\nCarol  ";

        await search.SearchEntityNodesFulltextAsync(
            verbatimQuery,
            new SearchFilters(),
            new[] { "tenant" },
            limit: 5);
        var overLimit = await search.SearchEntityEdgesFulltextAsync(
            overLimitQuery,
            new SearchFilters(),
            groupIds: null,
            limit: 5);
        await search.SearchEpisodesFulltextAsync(
            "  Episode\tQuery  ",
            new SearchFilters(),
            new[] { "tenant" },
            limit: 5);
        await search.SearchCommunitiesFulltextAsync(
            "  Community+(Team)\r\nLaunch  ",
            new[] { "tenant" },
            limit: 5);

        // The over-limit edge search performs no query, so only three statements are recorded.
        Assert.Empty(overLimit);
        Assert.Equal(3, recorder.Queried.Count);
        Assert.Equal(verbatimQuery, recorder.Queried[0].Parameters["query"]);
        Assert.Equal("  Episode\tQuery  ", recorder.Queried[1].Parameters["query"]);
        Assert.Equal("  Community+(Team)\r\nLaunch  ", recorder.Queried[2].Parameters["query"]);
        Assert.Equal(new[] { "tenant" }, Assert.IsType<List<string>>(recorder.Queried[0].Parameters["group_ids"]));
    }

    [Fact]
    public async Task FulltextSearch_AllowsExactlyMaxQueryLengthWords()
    {
        // Boundary check: 128 single-space-separated words equals MAX_QUERY_LENGTH and is NOT over
        // the limit (Python uses a strict `>` comparison at search_utils.py:90), so it still searches.
        var recorder = new RecordingLadybugExecutor();
        var search = new LadybugSearchExecutor(recorder);
        var atLimitQuery = string.Join(" ", Enumerable.Repeat("term", SearchUtilities.MaxQueryLength));

        await search.SearchEntityEdgesFulltextAsync(
            atLimitQuery,
            new SearchFilters(),
            groupIds: null,
            limit: 5);

        Assert.Single(recorder.Queried);
        Assert.Equal(atLimitQuery, recorder.Queried[0].Parameters["query"]);
    }

    [Fact]
    public async Task FulltextSearch_ValidatesGroupIdsBeforeQuerying()
    {
        var recorder = new RecordingLadybugExecutor();
        var search = new LadybugSearchExecutor(recorder);

        await Assert.ThrowsAsync<GroupIdValidationException>(
            () => search.SearchEntityNodesFulltextAsync(
                "   ",
                new SearchFilters(),
                new[] { "tenant:bad" },
                limit: 5));

        Assert.Empty(recorder.Queried);
    }

    [Fact]
    public async Task EdgeEmbeddingSearch_ForwardsEndpointFiltersAndMapsScoredEdges()
    {
        var recorder = new RecordingLadybugExecutor();
        recorder.EnqueueQuery(EntityEdgeRecord("edge-1", score: 0.73f));
        var search = new LadybugSearchExecutor(recorder);

        var hits = await search.SearchEntityEdgesByEmbeddingAsync(
            new[] { 0.1f, 0.2f },
            new SearchFilters { EdgeTypes = new List<string> { "KNOWS" } },
            new[] { "tenant" },
            limit: 3,
            minScore: 0.6f,
            sourceNodeUuid: "source-1",
            targetNodeUuid: "target-1");

        var hit = Assert.Single(hits);
        Assert.Equal("edge-1", hit.Item.Uuid);
        Assert.Equal("source-1", hit.Item.SourceNodeUuid);
        Assert.Equal("target-1", hit.Item.TargetNodeUuid);
        Assert.Equal(new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc), hit.Item.ReferenceTime);
        Assert.Equal(0.73f, hit.Score);
        Assert.Contains("array_cosine_similarity(e.fact_embedding", recorder.Queried[0].Query, StringComparison.Ordinal);
        Assert.Equal(new[] { 0.1f, 0.2f }, Assert.IsType<List<float>>(recorder.Queried[0].Parameters["search_vector"]));
        Assert.Equal("source-1", recorder.Queried[0].Parameters["source_uuid"]);
        Assert.Equal("target-1", recorder.Queried[0].Parameters["target_uuid"]);
    }

    [Fact]
    public async Task BfsSearch_ExecutesStatementPlanDeduplicatesAndLimitsInPythonOrder()
    {
        var recorder = new RecordingLadybugExecutor();
        recorder.EnqueueQuery(EntityRecord("node-a", "A"));
        recorder.EnqueueQuery(
            EntityRecord("node-a", "duplicate", score: 0.5f),
            EntityRecord("node-b", "B", score: 0.4f));
        recorder.EnqueueQuery(EntityRecord("node-c", "C", score: 0.3f));
        var search = new LadybugSearchExecutor(recorder);

        var hits = await search.SearchEntityNodesBfsAsync(
            new[] { "origin-1" },
            new SearchFilters(),
            maxDepth: 2,
            groupIds: null,
            limit: 2);

        Assert.Equal(new[] { "node-a", "node-b" }, hits.Select(hit => hit.Item.Uuid));
        Assert.Equal(1f, hits[0].Score);
        Assert.Equal(0.4f, hits[1].Score);
        Assert.Equal(2, recorder.Queried.Count);
        Assert.All(recorder.Queried, statement => Assert.DoesNotContain("UNWIND", statement.Query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EpisodeAndCommunitySearch_MapScoresThroughSharedExecutor()
    {
        var recorder = new RecordingLadybugExecutor();
        recorder.EnqueueQuery(EpisodeRecord("episode-1", score: 0.8f));
        recorder.EnqueueQuery(CommunityRecord("community-1", score: 0.7f));
        recorder.EnqueueQuery(CommunityRecord("community-2", score: 0.6f));
        var search = new LadybugSearchExecutor(recorder);

        var episodes = await search.SearchEpisodesFulltextAsync(
            "episode",
            new SearchFilters(),
            new[] { "tenant" },
            limit: 5);
        var communities = await search.SearchCommunitiesFulltextAsync(
            "community",
            new[] { "tenant" },
            limit: 5);
        var vectorCommunities = await search.SearchCommunitiesByEmbeddingAsync(
            new[] { 0.1f, 0.2f },
            new[] { "tenant" },
            limit: 5,
            minScore: 0.5f);

        Assert.Equal("episode-1", Assert.Single(episodes).Item.Uuid);
        Assert.Equal(0.8f, episodes[0].Score);
        Assert.Equal("community-1", Assert.Single(communities).Item.Uuid);
        Assert.Equal(0.7f, communities[0].Score);
        Assert.Equal("community-2", Assert.Single(vectorCommunities).Item.Uuid);
        Assert.Equal(0.6f, vectorCommunities[0].Score);
        Assert.Contains("QUERY_FTS_INDEX('Episodic'", recorder.Queried[0].Query, StringComparison.Ordinal);
        Assert.Contains("QUERY_FTS_INDEX('Community'", recorder.Queried[1].Query, StringComparison.Ordinal);
        Assert.Contains("array_cosine_similarity(c.name_embedding", recorder.Queried[2].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rankers_UsePerUuidStatementsAndCSharpSearchRankOrdering()
    {
        var recorder = new RecordingLadybugExecutor();
        recorder.EnqueueQuery(new Dictionary<string, object?> { ["uuid"] = "near", ["score"] = 1 });
        recorder.EnqueueQuery();
        recorder.EnqueueQuery(new Dictionary<string, object?> { ["uuid"] = "node-a", ["score"] = 2 });
        recorder.EnqueueQuery();
        var search = new LadybugSearchExecutor(recorder);

        var distance = await search.RankNodeDistanceAsync(
            new[] { "far", "center", "near" },
            "center",
            minScore: 0);
        var mentions = await search.RankNodeEpisodeMentionsAsync(
            new[] { "node-a", "node-b" },
            minScore: 0);

        Assert.Equal(new[] { "center", "near", "far" }, distance.Select(rank => rank.Uuid));
        Assert.Equal(10f, distance[0].Score);
        Assert.Equal(1f, distance[1].Score);
        Assert.Equal(0f, distance[2].Score);
        Assert.Equal(new[] { "node-a", "node-b" }, mentions.Select(rank => rank.Uuid));
        Assert.Equal(2f, mentions[0].Score);
        Assert.True(float.IsPositiveInfinity(mentions[1].Score));
        Assert.Equal(4, recorder.Queried.Count);
        Assert.All(recorder.Queried, statement => Assert.DoesNotContain("UNWIND", statement.Query, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rankers_DedupeInputsBreakScoreTiesByFirstInputAndKeepDefaults()
    {
        // The per-uuid rank Cypher constrains `n.uuid = $node_uuid` and returns `n.uuid AS uuid`
        // (LadybugSearchStatementBuilder.BuildNodeDistanceRankStatements / *EpisodeMentions*), so each
        // statement can only ever return rows whose uuid equals the requested input uuid - never a
        // foreign uuid, and (the aggregate `count(*)` / hardcoded `1 AS score`) at most one score per
        // uuid. This test therefore feeds only rows the real package can actually produce, and pins the
        // behaviors that survive: duplicate input UUIDs are deduplicated (the center is also skipped),
        // a score tie is broken by first input position, unconnected/unmentioned inputs keep their
        // default (0 for distance, +inf for mentions), and the center node is forced to 10.
        var recorder = new RecordingLadybugExecutor();
        // Distance statements run in deduped input order: first, dup, second (center is skipped).
        recorder.EnqueueQuery(new Dictionary<string, object?> { ["uuid"] = "first", ["score"] = 1 });
        recorder.EnqueueQuery(new Dictionary<string, object?> { ["uuid"] = "dup", ["score"] = 1 });
        recorder.EnqueueQuery(); // `second` is not connected to center -> keeps default 0.
        // Mentions statements run in deduped input order: node-b, node-a, node-c.
        recorder.EnqueueQuery(new Dictionary<string, object?> { ["uuid"] = "node-b", ["score"] = 5 });
        recorder.EnqueueQuery(new Dictionary<string, object?> { ["uuid"] = "node-a", ["score"] = 3 });
        recorder.EnqueueQuery(); // `node-c` has no mentions -> keeps default +inf.
        var search = new LadybugSearchExecutor(recorder);

        var distance = await search.RankNodeDistanceAsync(
            new[] { "first", "dup", "center", "dup", "second" },
            "center",
            minScore: 0);
        var mentions = await search.RankNodeEpisodeMentionsAsync(
            new[] { "node-b", "node-a", "node-b", "node-c" },
            minScore: 0);

        // center=10 (forced); first and dup both score 1, tie broken by first input index
        // (first at index 0 precedes dup at index 1); second keeps default 0.
        Assert.Equal(new[] { "center", "first", "dup", "second" }, distance.Select(rank => rank.Uuid));
        Assert.Equal(new[] { 10f, 1f, 1f, 0f }, distance.Select(rank => rank.Score));

        // Ascending mention score: node-a (3) before node-b (5); node-c keeps default +inf last.
        Assert.Equal(new[] { "node-a", "node-b", "node-c" }, mentions.Select(rank => rank.Uuid));
        Assert.Equal(3f, mentions[0].Score);
        Assert.Equal(5f, mentions[1].Score);
        Assert.True(float.IsPositiveInfinity(mentions[2].Score));

        // The center node is never queried per-uuid (it is excluded from the distance statements).
        Assert.DoesNotContain(
            recorder.Queried,
            statement => statement.Parameters.TryGetValue("node_uuid", out var uuid)
                && uuid is string text
                && string.Equals(text, "center", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rankers_IgnoreBackendRowsOutsideRequestedInputs()
    {
        var recorder = new RecordingLadybugExecutor();
        recorder.EnqueueQuery(
            new Dictionary<string, object?> { ["uuid"] = "near", ["score"] = 1 },
            new Dictionary<string, object?> { ["uuid"] = "backend-only-distance", ["score"] = 99 });
        recorder.EnqueueQuery(
            new Dictionary<string, object?> { ["uuid"] = "node-a", ["score"] = 2 },
            new Dictionary<string, object?> { ["uuid"] = "backend-only-mentions", ["score"] = 1 });
        var search = new LadybugSearchExecutor(recorder);

        var distance = await search.RankNodeDistanceAsync(
            new[] { "near" },
            "center",
            minScore: 0);
        var mentions = await search.RankNodeEpisodeMentionsAsync(
            new[] { "node-a" },
            minScore: 0);

        Assert.Equal(new[] { "near" }, distance.Select(rank => rank.Uuid));
        Assert.Equal(1f, Assert.Single(distance).Score);
        Assert.Equal(new[] { "node-a" }, mentions.Select(rank => rank.Uuid));
        Assert.Equal(2f, Assert.Single(mentions).Score);
    }

    [Fact]
    public async Task Rankers_ApplyInclusiveMinimumScoreToDefaults()
    {
        var recorder = new RecordingLadybugExecutor();
        recorder.EnqueueQuery();
        recorder.EnqueueQuery();
        recorder.EnqueueQuery();
        var search = new LadybugSearchExecutor(recorder);

        var distanceAtDefault = await search.RankNodeDistanceAsync(
            new[] { "far" },
            "center",
            minScore: 0);
        var distanceAboveDefault = await search.RankNodeDistanceAsync(
            new[] { "far" },
            "center",
            minScore: 0.1f);
        var mentions = await search.RankNodeEpisodeMentionsAsync(
            new[] { "unmentioned" },
            minScore: 100);

        Assert.Equal("far", Assert.Single(distanceAtDefault).Uuid);
        Assert.Empty(distanceAboveDefault);
        Assert.Equal("unmentioned", Assert.Single(mentions).Uuid);
        Assert.True(float.IsPositiveInfinity(mentions[0].Score));
    }

    [Fact]
    public async Task PreCanceledSearch_DoesNotQueryExecutor()
    {
        var recorder = new RecordingLadybugExecutor();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var search = new LadybugSearchExecutor(recorder);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => search.SearchEntityNodesByEmbeddingAsync(
                new[] { 0.1f },
                new SearchFilters(),
                groupIds: null,
                limit: 1,
                minScore: 0,
                cancellationToken: cts.Token));
        Assert.Empty(recorder.Queried);
    }

    private static Dictionary<string, object?> EntityRecord(
        string uuid,
        string name,
        float? score = null)
    {
        var record = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["uuid"] = uuid,
            ["name"] = name,
            ["group_id"] = "tenant",
            ["labels"] = new[] { "Entity", "Person" },
            ["created_at"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ["summary"] = "summary",
            ["attributes"] = "{}"
        };
        if (score is not null)
        {
            record["score"] = score.Value;
        }

        return record;
    }

    private static Dictionary<string, object?> EntityEdgeRecord(string uuid, float? score = null)
    {
        var record = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["uuid"] = uuid,
            ["source_node_uuid"] = "source-1",
            ["target_node_uuid"] = "target-1",
            ["group_id"] = "tenant",
            ["created_at"] = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            ["name"] = "KNOWS",
            ["fact"] = "Alice knows Bob",
            ["episodes"] = new[] { "episode-1" },
            ["valid_at"] = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            ["reference_time"] = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc),
            ["attributes"] = "{}"
        };
        if (score is not null)
        {
            record["score"] = score.Value;
        }

        return record;
    }

    private static Dictionary<string, object?> EpisodeRecord(string uuid, float? score = null)
    {
        var record = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["uuid"] = uuid,
            ["name"] = "episode",
            ["group_id"] = "tenant",
            ["created_at"] = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            ["source"] = "message",
            ["source_description"] = "chat",
            ["content"] = "episode content",
            ["valid_at"] = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            ["entity_edges"] = new[] { "edge-1" }
        };
        if (score is not null)
        {
            record["score"] = score.Value;
        }

        return record;
    }

    private static Dictionary<string, object?> CommunityRecord(string uuid, float? score = null)
    {
        var record = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["uuid"] = uuid,
            ["name"] = "community",
            ["group_id"] = "tenant",
            ["created_at"] = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc),
            ["summary"] = "community summary",
            ["name_embedding"] = new[] { 0.1f, 0.2f }
        };
        if (score is not null)
        {
            record["score"] = score.Value;
        }

        return record;
    }

    private sealed class RecordingLadybugExecutor : ILadybugQueryExecutor
    {
        private readonly Queue<IReadOnlyList<IReadOnlyDictionary<string, object?>>> _queryResults = new();

        internal List<LadybugStatement> Queried { get; } = new();

        internal void EnqueueQuery(params IReadOnlyDictionary<string, object?>[] records) =>
            _queryResults.Enqueue(records);

        public Task ExecuteAsync(LadybugStatement statement, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
            LadybugStatement statement,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Queried.Add(statement);
            return Task.FromResult(
                _queryResults.Count == 0
                    ? Array.Empty<IReadOnlyDictionary<string, object?>>()
                    : _queryResults.Dequeue());
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
