using System.Collections.Concurrent;
using Graphiti.Core;

namespace Graphiti.Core.Tests;

public class GraphitiDriverRoutingTests
{
    [Fact]
    public async Task AddEpisode_UsesGroupCloneWithoutMutatingRootDriver()
    {
        var driver = new RecordingCloneGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver, embedder: new HashEmbedder(8));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice likes Bob",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "tenant");

        Assert.Same(driver, graphiti.Driver);
        Assert.Same(driver, graphiti.Clients.Driver);
        Assert.Equal(string.Empty, graphiti.Driver.Database);
        Assert.Contains("tenant", driver.CloneDatabases);
        Assert.Contains("tenant", driver.SaveBulkDatabases);
        Assert.Contains("tenant", driver.DisposedDatabases);
        Assert.Equal("tenant", result.Episode.GroupId);

        var storedEpisodes = await driver.GetNodesByGroupIdsAsync<EpisodicNode>(new[] { "tenant" });
        Assert.Single(storedEpisodes);
    }

    [Fact]
    public async Task AddEpisode_UsesRootDriverWhenGroupMatchesRootDatabase()
    {
        var driver = new RecordingCloneGraphDriver(database: "tenant");
        var graphiti = new Graphiti(graphDriver: driver, embedder: new HashEmbedder(8));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice likes Bob",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "tenant");

        Assert.Same(driver, graphiti.Driver);
        Assert.Empty(driver.CloneDatabases);
        Assert.Empty(driver.DisposedDatabases);
        Assert.Contains("tenant", driver.SaveBulkDatabases);
        Assert.Equal("tenant", result.Episode.GroupId);
    }

    private sealed class RecordingCloneGraphDriver : GraphDriverBase
    {
        private readonly InMemoryGraphDriver _inner;

        public RecordingCloneGraphDriver(string database = "")
            : this(
                new InMemoryGraphDriver(),
                database,
                new ConcurrentBag<string>(),
                new ConcurrentBag<string>(),
                new ConcurrentBag<string>())
        {
        }

        private RecordingCloneGraphDriver(
            InMemoryGraphDriver inner,
            string database,
            ConcurrentBag<string> cloneDatabases,
            ConcurrentBag<string> disposedDatabases,
            ConcurrentBag<string> saveBulkDatabases)
            : base(GraphProvider.InMemory, database)
        {
            _inner = inner;
            CloneDatabases = cloneDatabases;
            DisposedDatabases = disposedDatabases;
            SaveBulkDatabases = saveBulkDatabases;
        }

        public ConcurrentBag<string> CloneDatabases { get; }
        public ConcurrentBag<string> DisposedDatabases { get; }
        public ConcurrentBag<string> SaveBulkDatabases { get; }

        public override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            _inner.BuildIndicesAndConstraintsAsync(deleteExisting, cancellationToken);

        public override Task CloseAsync(CancellationToken cancellationToken = default) =>
            _inner.CloseAsync(cancellationToken);

        public override IGraphDriver Clone(string database)
        {
            CloneDatabases.Add(database);
            return new RecordingCloneGraphDriver(
                _inner,
                database,
                CloneDatabases,
                DisposedDatabases,
                SaveBulkDatabases);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!string.IsNullOrEmpty(Database))
            {
                DisposedDatabases.Add(Database);
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }

        public override Task SaveNodeAsync(Node node, CancellationToken cancellationToken = default) =>
            _inner.SaveNodeAsync(node, cancellationToken);

        public override Task SaveEdgeAsync(Edge edge, CancellationToken cancellationToken = default) =>
            _inner.SaveEdgeAsync(edge, cancellationToken);

        public override Task SaveBulkAsync(
            IEnumerable<EpisodicNode> episodicNodes,
            IEnumerable<EpisodicEdge> episodicEdges,
            IEnumerable<EntityNode> entityNodes,
            IEnumerable<EntityEdge> entityEdges,
            IEmbedderClient embedder,
            CancellationToken cancellationToken = default)
        {
            SaveBulkDatabases.Add(Database);
            return _inner.SaveBulkAsync(
                episodicNodes,
                episodicEdges,
                entityNodes,
                entityEdges,
                embedder,
                cancellationToken);
        }

        public override Task DeleteNodeAsync(string uuid, CancellationToken cancellationToken = default) =>
            _inner.DeleteNodeAsync(uuid, cancellationToken);

        public override Task DeleteNodesByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default) =>
            _inner.DeleteNodesByGroupIdAsync(groupId, batchSize, cancellationToken);

        public override Task DeleteNodesByUuidsAsync(IEnumerable<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default) =>
            _inner.DeleteNodesByUuidsAsync(uuids, batchSize, cancellationToken);

        public override Task DeleteEdgeAsync(string uuid, CancellationToken cancellationToken = default) =>
            _inner.DeleteEdgeAsync(uuid, cancellationToken);

        public override Task DeleteEdgesByUuidsAsync(IEnumerable<string> uuids, CancellationToken cancellationToken = default) =>
            _inner.DeleteEdgesByUuidsAsync(uuids, cancellationToken);

        public override Task ClearDataAsync(IReadOnlyList<string>? groupIds = null, CancellationToken cancellationToken = default) =>
            _inner.ClearDataAsync(groupIds, cancellationToken);

        public override Task<TNode> GetNodeByUuidAsync<TNode>(string uuid, CancellationToken cancellationToken = default) =>
            _inner.GetNodeByUuidAsync<TNode>(uuid, cancellationToken);

        public override Task<IReadOnlyList<TNode>> GetNodesByUuidsAsync<TNode>(
            IEnumerable<string> uuids,
            string? groupId = null,
            CancellationToken cancellationToken = default) =>
            _inner.GetNodesByUuidsAsync<TNode>(uuids, groupId, cancellationToken);

        public override Task<IReadOnlyList<TNode>> GetNodesByGroupIdsAsync<TNode>(
            IEnumerable<string> groupIds,
            int? limit = null,
            string? uuidCursor = null,
            bool withEmbeddings = false,
            CancellationToken cancellationToken = default) =>
            _inner.GetNodesByGroupIdsAsync<TNode>(groupIds, limit, uuidCursor, withEmbeddings, cancellationToken);

        public override Task<T> GetEdgeByUuidAsync<T>(string uuid, CancellationToken cancellationToken = default) =>
            _inner.GetEdgeByUuidAsync<T>(uuid, cancellationToken);

        public override Task<IReadOnlyList<T>> GetEdgesByUuidsAsync<T>(IEnumerable<string> uuids, CancellationToken cancellationToken = default) =>
            _inner.GetEdgesByUuidsAsync<T>(uuids, cancellationToken);

        public override Task<IReadOnlyList<T>> GetEdgesByGroupIdsAsync<T>(
            IEnumerable<string> groupIds,
            int? limit = null,
            string? uuidCursor = null,
            bool withEmbeddings = false,
            CancellationToken cancellationToken = default) =>
            _inner.GetEdgesByGroupIdsAsync<T>(groupIds, limit, uuidCursor, withEmbeddings, cancellationToken);

        public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesBetweenNodesAsync(
            string sourceNodeUuid,
            string targetNodeUuid,
            CancellationToken cancellationToken = default) =>
            _inner.GetEntityEdgesBetweenNodesAsync(sourceNodeUuid, targetNodeUuid, cancellationToken);

        public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesByNodeUuidAsync(
            string nodeUuid,
            CancellationToken cancellationToken = default) =>
            _inner.GetEntityEdgesByNodeUuidAsync(nodeUuid, cancellationToken);

        public override Task<IReadOnlyList<EpisodicNode>> GetEpisodesByEntityNodeUuidAsync(
            string entityNodeUuid,
            CancellationToken cancellationToken = default) =>
            _inner.GetEpisodesByEntityNodeUuidAsync(entityNodeUuid, cancellationToken);

        public override Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(
            DateTime referenceTime,
            int lastN,
            IReadOnlyList<string>? groupIds = null,
            EpisodeType? source = null,
            string? saga = null,
            CancellationToken cancellationToken = default) =>
            _inner.RetrieveEpisodesAsync(referenceTime, lastN, groupIds, source, saga, cancellationToken);

        public override Task<IReadOnlyList<EntityNode>> GetMentionedNodesAsync(
            IReadOnlyList<EpisodicNode> episodes,
            CancellationToken cancellationToken = default) =>
            _inner.GetMentionedNodesAsync(episodes, cancellationToken);

        public override Task<IReadOnlyList<CommunityNode>> GetCommunitiesByNodesAsync(
            IReadOnlyList<EntityNode> nodes,
            CancellationToken cancellationToken = default) =>
            _inner.GetCommunitiesByNodesAsync(nodes, cancellationToken);

        public override Task<SagaNode?> FindSagaByNameAsync(string name, string groupId, CancellationToken cancellationToken = default) =>
            _inner.FindSagaByNameAsync(name, groupId, cancellationToken);

        public override Task<string?> GetSagaPreviousEpisodeUuidAsync(
            string sagaUuid,
            string currentEpisodeUuid,
            CancellationToken cancellationToken = default) =>
            _inner.GetSagaPreviousEpisodeUuidAsync(sagaUuid, currentEpisodeUuid, cancellationToken);

        public override Task<IReadOnlyList<SagaEpisodeContent>> GetSagaEpisodeContentsAsync(
            string sagaUuid,
            DateTime? since = null,
            int limit = 200,
            CancellationToken cancellationToken = default) =>
            _inner.GetSagaEpisodeContentsAsync(sagaUuid, since, limit, cancellationToken);
    }
}
