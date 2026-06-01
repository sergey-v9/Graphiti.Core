using System.Text.Json;
using Graphiti.Core.Drivers.Ladybug;

namespace Graphiti.Core.Tests.Drivers.Ladybug;

public class LadybugGraphDriverTests
{
    [Fact]
    public async Task SaveBulkAsync_ExecutesIndividualStatementsAfterEmbeddingBackfill()
    {
        var executor = new RecordingLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var episode = new EpisodicNode { Uuid = "episode-1", Name = "episode", Content = "episode content" };
        var mention = new EpisodicEdge
        {
            Uuid = "mention-1",
            SourceNodeUuid = "episode-1",
            TargetNodeUuid = "entity-1"
        };
        var entity = new EntityNode { Uuid = "entity-1", Name = "Alice" };
        var fact = new EntityEdge
        {
            Uuid = "edge-1",
            SourceNodeUuid = "entity-1",
            TargetNodeUuid = "entity-2",
            Fact = "Alice knows Bob"
        };

        await driver.SaveBulkAsync(
            [episode],
            [mention],
            [entity],
            [fact],
            new HashEmbedder(4));

        Assert.Equal(4, executor.Executed.Count);
        Assert.All(executor.Executed, statement => Assert.DoesNotContain("UNWIND", statement.Query, StringComparison.Ordinal));
        Assert.Contains("MERGE (n:Episodic {uuid: $uuid})", executor.Executed[0].Query, StringComparison.Ordinal);
        Assert.Contains("MERGE (n:Entity {uuid: $uuid})", executor.Executed[1].Query, StringComparison.Ordinal);
        Assert.Contains("MERGE (episode)-[e:MENTIONS {uuid: $uuid}]->(node)", executor.Executed[2].Query, StringComparison.Ordinal);
        Assert.Contains("MERGE (source)-[:RELATES_TO]->(e:RelatesToNode_ {uuid: $uuid})", executor.Executed[3].Query, StringComparison.Ordinal);
        Assert.NotNull(executor.Executed[1].Parameters["name_embedding"]);
        Assert.NotNull(executor.Executed[3].Parameters["fact_embedding"]);
    }

    [Fact]
    public async Task GetNodeByUuidAsync_MapsRecordsAndThrowsWhenMissing()
    {
        var executor = new RecordingLadybugExecutor();
        executor.EnqueueQuery(EntityRecord("entity-1"));
        var driver = new LadybugGraphDriver(executor);

        var node = await driver.GetNodeByUuidAsync<EntityNode>("entity-1");

        Assert.Single(executor.Queried);
        Assert.Contains("MATCH (n:Entity {uuid: $uuid})", executor.Queried[0].Query, StringComparison.Ordinal);
        Assert.Equal("entity-1", executor.Queried[0].Parameters["uuid"]);
        Assert.Equal("Alice", node.Name);
        Assert.Equal("tenant", node.GroupId);
        Assert.Equal(42, Assert.IsType<JsonElement>(node.Attributes["age"]).GetInt32());

        var missing = new LadybugGraphDriver(new RecordingLadybugExecutor());
        await Assert.ThrowsAsync<NodeNotFoundException>(() => missing.GetNodeByUuidAsync<EntityNode>("missing"));
    }

    [Fact]
    public async Task CollectionReads_FilterAndDeduplicateWithoutReordering()
    {
        var executor = new RecordingLadybugExecutor();
        executor.EnqueueQuery(
            EntityRecord("entity-other", "other"),
            EntityRecord("entity-2"),
            EntityRecord("entity-1"));
        executor.EnqueueQuery(
            new Dictionary<string, object?> { ["group_id"] = "tenant" },
            new Dictionary<string, object?> { ["group_id"] = "" },
            new Dictionary<string, object?> { ["group_id"] = "other" },
            new Dictionary<string, object?> { ["group_id"] = "tenant" },
            new Dictionary<string, object?> { ["group_id"] = null });
        var driver = new LadybugGraphDriver(executor);

        var nodes = await driver.GetNodesByUuidsAsync<EntityNode>(
            ["entity-1", "entity-2", "entity-other"],
            groupId: "tenant");
        var groupIds = await driver.GetEntityGroupIdsAsync();

        Assert.Equal(new[] { "entity-2", "entity-1" }, nodes.Select(node => node.Uuid));
        Assert.Equal(new[] { "tenant", "other" }, groupIds);
        Assert.Equal(2, executor.Queried.Count);
        Assert.Contains("WHERE n.uuid IN $uuids", executor.Queried[0].Query, StringComparison.Ordinal);
        Assert.Contains("RETURN DISTINCT n.group_id AS group_id", executor.Queried[1].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteNodeAsync_CleansEntityIntermediatesBeforeAllNodeTables()
    {
        var executor = new RecordingLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);

        await driver.DeleteNodeAsync("node-1");

        Assert.Equal(5, executor.Executed.Count);
        Assert.Contains(
            "MATCH (n:Entity {uuid: $uuid})-[:RELATES_TO]->(r:RelatesToNode_)",
            executor.Executed[0].Query,
            StringComparison.Ordinal);
        Assert.Contains("DETACH DELETE r", executor.Executed[0].Query, StringComparison.Ordinal);
        Assert.Contains("MATCH (n:Entity {uuid: $uuid})", executor.Executed[1].Query, StringComparison.Ordinal);
        Assert.Contains("MATCH (n:Episodic {uuid: $uuid})", executor.Executed[2].Query, StringComparison.Ordinal);
        Assert.Contains("MATCH (n:Community {uuid: $uuid})", executor.Executed[3].Query, StringComparison.Ordinal);
        Assert.Contains("MATCH (n:Saga {uuid: $uuid})", executor.Executed[4].Query, StringComparison.Ordinal);
        Assert.All(executor.Executed, statement => Assert.Equal("node-1", statement.Parameters["uuid"]));
    }

    [Fact]
    public async Task NonSearchReadSurface_UsesLadybugStatementsAndMapsRecords()
    {
        var executor = new RecordingLadybugExecutor();
        var createdAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var validAt = createdAt.AddHours(1);
        executor.EnqueueQuery(EpisodeRecord("episode-1", createdAt, validAt));
        executor.EnqueueQuery(EntityRecord("entity-1"));
        executor.EnqueueQuery(CommunityRecord("community-1"));
        executor.EnqueueQuery(SagaRecord("saga-1"));
        executor.EnqueueQuery(new Dictionary<string, object?> { ["uuid"] = "episode-previous" });
        executor.EnqueueQuery(
            new Dictionary<string, object?> { ["content"] = "newer", ["valid_at"] = validAt },
            new Dictionary<string, object?> { ["content"] = "older", ["valid_at"] = validAt.AddMinutes(-1) });
        var driver = new LadybugGraphDriver(executor);

        var episodes = await driver.GetEpisodesByEntityNodeUuidAsync("entity-1");
        var mentioned = await driver.GetMentionedNodesAsync([new EpisodicNode { Uuid = "episode-1" }]);
        var communities = await driver.GetCommunitiesByNodesAsync([new EntityNode { Uuid = "entity-1" }]);
        var saga = await driver.FindSagaByNameAsync("checkout", "tenant");
        var previous = await driver.GetSagaPreviousEpisodeUuidAsync("saga-1", "episode-2");
        var contents = await driver.GetSagaEpisodeContentsAsync("saga-1");

        Assert.Equal("episode-1", Assert.Single(episodes).Uuid);
        Assert.Equal("entity-1", Assert.Single(mentioned).Uuid);
        Assert.Equal("community-1", Assert.Single(communities).Uuid);
        Assert.Equal("saga-1", saga?.Uuid);
        Assert.Equal("saga summary", saga?.Summary);
        Assert.Equal("episode-first", saga?.FirstEpisodeUuid);
        Assert.Equal("episode-previous", previous);
        Assert.Equal(new[] { "older", "newer" }, contents.Select(content => content.Content));
        Assert.Contains("MATCH (e:Episodic)-[r:MENTIONS]->(n:Entity {uuid: $entity_node_uuid})", executor.Queried[0].Query, StringComparison.Ordinal);
        Assert.Contains("WHERE episode.uuid IN $uuids", executor.Queried[1].Query, StringComparison.Ordinal);
        Assert.Contains("MATCH (c:Community)-[:HAS_MEMBER]->(n:Entity)", executor.Queried[2].Query, StringComparison.Ordinal);
        Assert.Contains("MATCH (s:Saga {name: $name, group_id: $group_id})", executor.Queried[3].Query, StringComparison.Ordinal);
        Assert.Contains("WHERE e.uuid <> $current_episode_uuid", executor.Queried[4].Query, StringComparison.Ordinal);
        Assert.Contains("ORDER BY e.valid_at DESC, e.created_at DESC", executor.Queried[5].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildIndicesAndClose_UseSchemaAndDisposeExecutor()
    {
        var executor = new RecordingLadybugExecutor();
        var driver = new LadybugGraphDriver(executor, database: "graphiti");

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.CloseAsync();
        await driver.CloseAsync();

        Assert.Equal(GraphProvider.Kuzu, driver.Provider);
        Assert.Equal("graphiti", driver.Database);
        Assert.Single(executor.Executed);
        Assert.Equal(LadybugSchema.SchemaQueries, executor.Executed[0].Query);
        Assert.True(executor.Disposed);
        Assert.Throws<NotSupportedException>(() => driver.Clone("other"));
    }

    private static Dictionary<string, object?> EntityRecord(string uuid, string groupId = "tenant") =>
        new(StringComparer.Ordinal)
        {
            ["uuid"] = uuid,
            ["name"] = "Alice",
            ["group_id"] = groupId,
            ["labels"] = new[] { "Entity", "Person" },
            ["created_at"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ["summary"] = "summary",
            ["attributes"] = """{"age":42}"""
        };

    private static Dictionary<string, object?> EpisodeRecord(
        string uuid,
        DateTime createdAt,
        DateTime validAt) =>
        new(StringComparer.Ordinal)
        {
            ["uuid"] = uuid,
            ["name"] = "episode",
            ["group_id"] = "tenant",
            ["created_at"] = createdAt,
            ["source"] = "message",
            ["source_description"] = "chat",
            ["content"] = "content",
            ["valid_at"] = validAt,
            ["entity_edges"] = new[] { "edge-1" }
        };

    private static Dictionary<string, object?> CommunityRecord(string uuid) =>
        new(StringComparer.Ordinal)
        {
            ["uuid"] = uuid,
            ["name"] = "community",
            ["group_id"] = "tenant",
            ["created_at"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ["summary"] = "community summary",
            ["name_embedding"] = new[] { 0.1f, 0.2f }
        };

    private static Dictionary<string, object?> SagaRecord(string uuid) =>
        new(StringComparer.Ordinal)
        {
            ["uuid"] = uuid,
            ["name"] = "checkout",
            ["group_id"] = "tenant",
            ["created_at"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ["summary"] = "saga summary",
            ["first_episode_uuid"] = "episode-first",
            ["last_episode_uuid"] = "episode-last",
            ["last_summarized_at"] = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            ["last_summarized_episode_valid_at"] = new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc)
        };

    private sealed class RecordingLadybugExecutor : ILadybugQueryExecutor
    {
        private readonly Queue<IReadOnlyList<IReadOnlyDictionary<string, object?>>> _queryResults = new();

        internal List<LadybugStatement> Executed { get; } = new();

        internal List<LadybugStatement> Queried { get; } = new();

        internal bool Disposed { get; private set; }

        internal void EnqueueQuery(params IReadOnlyDictionary<string, object?>[] records) =>
            _queryResults.Enqueue(records);

        public Task ExecuteAsync(LadybugStatement statement, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Executed.Add(statement);
            return Task.CompletedTask;
        }

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

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
