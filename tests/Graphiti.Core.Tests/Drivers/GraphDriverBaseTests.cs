using Graphiti.Core;

namespace Graphiti.Core.Tests.Drivers;

public class GraphDriverBaseTests
{
    [Fact]
    public void IGraphDriver_PublicMethodsPlaceCancellationTokenLast()
    {
        var offenders = typeof(IGraphDriver)
            .GetMethods()
            .Select(method => new
            {
                Method = method,
                Parameters = method.GetParameters()
            })
            .Where(item =>
                item.Parameters
                    .Select((parameter, index) => (parameter, index))
                    .Any(parameter =>
                        parameter.parameter.ParameterType == typeof(CancellationToken)
                        && parameter.index != item.Parameters.Length - 1))
            .Select(item => item.Method.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public async Task GetByGroupIds_HonorsWithEmbeddingsProjection()
    {
        var driver = new InMemoryGraphDriver();
        var source = new EntityNode
        {
            Name = "Alice",
            GroupId = "group",
            NameEmbedding = new List<float> { 0.1f, 0.2f }
        };
        var target = new EntityNode
        {
            Name = "Bob",
            GroupId = "group",
            NameEmbedding = new List<float> { 0.3f, 0.4f }
        };
        var edge = new EntityEdge
        {
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "group",
            Name = "KNOWS",
            Fact = "Alice knows Bob",
            FactEmbedding = new List<float> { 0.5f, 0.6f }
        };

        await source.SaveAsync(driver);
        await target.SaveAsync(driver);
        await edge.SaveAsync(driver);

        var nodesWithoutEmbeddings = await driver.GetNodesByGroupIdsAsync<EntityNode>(new[] { "group" });
        var nodesWithEmbeddings = await driver.GetNodesByGroupIdsAsync<EntityNode>(
            new[] { "group" },
            withEmbeddings: true);
        var edgesWithoutEmbeddings = await driver.GetEdgesByGroupIdsAsync<EntityEdge>(new[] { "group" });
        var edgesWithEmbeddings = await driver.GetEdgesByGroupIdsAsync<EntityEdge>(
            new[] { "group" },
            withEmbeddings: true);

        Assert.All(nodesWithoutEmbeddings, node => Assert.Null(node.NameEmbedding));
        Assert.All(nodesWithEmbeddings, node => Assert.NotNull(node.NameEmbedding));
        Assert.Null(Assert.Single(edgesWithoutEmbeddings).FactEmbedding);
        Assert.Equal(new List<float> { 0.5f, 0.6f }, Assert.Single(edgesWithEmbeddings).FactEmbedding);
    }

    [Fact]
    public async Task SaveBulkAsync_PreCanceledTokenDoesNotEnumerateInputs()
    {
        var driver = new InMemoryGraphDriver();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var episodicNodes = new ThrowOnEnumerate<EpisodicNode>();
        var episodicEdges = new ThrowOnEnumerate<EpisodicEdge>();
        var entityNodes = new ThrowOnEnumerate<EntityNode>();
        var entityEdges = new ThrowOnEnumerate<EntityEdge>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.SaveBulkAsync(
            episodicNodes,
            episodicEdges,
            entityNodes,
            entityEdges,
            new HashEmbedder(2),
            cts.Token));

        Assert.False(episodicNodes.Enumerated);
        Assert.False(episodicEdges.Enumerated);
        Assert.False(entityNodes.Enumerated);
        Assert.False(entityEdges.Enumerated);
    }

    [Fact]
    public async Task SaveBulkAsync_CancelsBetweenPhasesWithoutEnumeratingLaterInputs()
    {
        using var cts = new CancellationTokenSource();
        var driver = new DelayedBulkSaveDriver(
            expectedEpisodicNodes: 1,
            expectedEntityNodes: 0,
            expectedEpisodicEdges: 0,
            cancelAfterEpisodicNodeSave: cts);
        var episode = new EpisodicNode { Name = "episode", GroupId = "group" };
        var episodicEdges = new ThrowOnEnumerate<EpisodicEdge>();
        var entityNodes = new ThrowOnEnumerate<EntityNode>();
        var entityEdges = new ThrowOnEnumerate<EntityEdge>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.SaveBulkAsync(
            new[] { episode },
            episodicEdges,
            entityNodes,
            entityEdges,
            new HashEmbedder(2),
            cts.Token));

        Assert.Equal(1, driver.SavedNodeCount);
        Assert.Equal(0, driver.SavedEdgeCount);
        Assert.False(entityNodes.Enumerated);
        Assert.False(episodicEdges.Enumerated);
        Assert.False(entityEdges.Enumerated);
    }

    [Fact]
    public async Task SaveBulkAsync_BatchesMissingNodeAndEdgeEmbeddings()
    {
        var driver = new InMemoryGraphDriver();
        var embedder = new RecordingEmbedder();
        var precomputedNodeEmbedding = new List<float> { 90, 91 };
        var precomputedEdgeEmbedding = new List<float> { 92, 93 };
        var nodes = new[]
        {
            new EntityNode { Name = "Alice\nA", GroupId = "group" },
            new EntityNode { Name = "Bob", GroupId = "group", NameEmbedding = precomputedNodeEmbedding },
            new EntityNode { Name = null!, GroupId = "group" },
            new EntityNode { Name = "Carol", GroupId = "group" }
        };
        var edges = new[]
        {
            new EntityEdge
            {
                SourceNodeUuid = nodes[0].Uuid,
                TargetNodeUuid = nodes[1].Uuid,
                GroupId = "group",
                Fact = "Alice\nknows Bob"
            },
            new EntityEdge
            {
                SourceNodeUuid = nodes[1].Uuid,
                TargetNodeUuid = nodes[2].Uuid,
                GroupId = "group",
                Fact = "Bob knows Carol",
                FactEmbedding = precomputedEdgeEmbedding
            },
            new EntityEdge
            {
                SourceNodeUuid = nodes[2].Uuid,
                TargetNodeUuid = nodes[3].Uuid,
                GroupId = "group",
                Fact = null!
            },
            new EntityEdge
            {
                SourceNodeUuid = nodes[3].Uuid,
                TargetNodeUuid = nodes[0].Uuid,
                GroupId = "group",
                Fact = "Carol knows Alice"
            }
        };

        await driver.SaveBulkAsync(
            Array.Empty<EpisodicNode>(),
            Array.Empty<EpisodicEdge>(),
            nodes,
            edges,
            embedder);

        Assert.Equal(2, embedder.BatchCalls.Count);
        Assert.Equal(0, embedder.SingleCallCount);
        Assert.Equal(new[] { "Alice A", string.Empty, "Carol" }, embedder.BatchCalls[0]);
        Assert.Equal(new[] { "Alice knows Bob", string.Empty, "Carol knows Alice" }, embedder.BatchCalls[1]);

        Assert.Equal(new List<float> { 1, 1 }, nodes[0].NameEmbedding);
        Assert.Same(precomputedNodeEmbedding, nodes[1].NameEmbedding);
        Assert.Equal(new List<float> { 1, 2 }, nodes[2].NameEmbedding);
        Assert.Equal(new List<float> { 1, 3 }, nodes[3].NameEmbedding);
        Assert.Equal(new List<float> { 2, 1 }, edges[0].FactEmbedding);
        Assert.Same(precomputedEdgeEmbedding, edges[1].FactEmbedding);
        Assert.Equal(new List<float> { 2, 2 }, edges[2].FactEmbedding);
        Assert.Equal(new List<float> { 2, 3 }, edges[3].FactEmbedding);
    }

    [Fact]
    public async Task SaveBulkAsync_SkipsEmbedderWhenNoEmbeddingsAreMissing()
    {
        var driver = new InMemoryGraphDriver();
        var nodeEmbedding = new List<float> { 1, 2 };
        var edgeEmbedding = new List<float> { 3, 4 };
        var node = new EntityNode
        {
            Name = "Alice",
            GroupId = "group",
            NameEmbedding = nodeEmbedding
        };
        var edge = new EntityEdge
        {
            SourceNodeUuid = node.Uuid,
            TargetNodeUuid = node.Uuid,
            GroupId = "group",
            Fact = "Alice knows Alice",
            FactEmbedding = edgeEmbedding
        };

        await driver.SaveBulkAsync(
            Array.Empty<EpisodicNode>(),
            Array.Empty<EpisodicEdge>(),
            new[] { node },
            new[] { edge },
            new ThrowingBatchEmbedder());

        Assert.Same(nodeEmbedding, node.NameEmbedding);
        Assert.Same(edgeEmbedding, edge.FactEmbedding);
    }

    [Fact]
    public async Task SaveBulkAsync_CancellationAfterEmbeddingBatchPreventsAssignment()
    {
        var driver = new InMemoryGraphDriver();
        using var cancellation = new CancellationTokenSource();
        var node = new EntityNode { Name = "Alice", GroupId = "group" };
        var edge = new EntityEdge
        {
            SourceNodeUuid = node.Uuid,
            TargetNodeUuid = node.Uuid,
            GroupId = "group",
            Fact = "Alice knows Alice"
        };
        var embedder = new CancelAfterBatchEmbedder(cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.SaveBulkAsync(
            Array.Empty<EpisodicNode>(),
            Array.Empty<EpisodicEdge>(),
            new[] { node },
            new[] { edge },
            embedder,
            cancellation.Token));

        Assert.Null(node.NameEmbedding);
        Assert.Null(edge.FactEmbedding);
    }

    [Fact]
    public async Task SaveBulkAsync_RejectsEmbeddingCountMismatch()
    {
        var driver = new InMemoryGraphDriver();
        var node = new EntityNode { Name = "Alice", GroupId = "group" };
        var embedder = new InvalidBatchEmbedder(
            embeddingDimension: 2,
            new[]
            {
                (IReadOnlyList<float>)new List<float> { 1, 2 },
                new List<float> { 3, 4 }
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => driver.SaveBulkAsync(
            Array.Empty<EpisodicNode>(),
            Array.Empty<EpisodicEdge>(),
            new[] { node },
            Array.Empty<EntityEdge>(),
            embedder));

        Assert.Contains("entity node name embeddings", exception.Message, StringComparison.Ordinal);
        Assert.Contains("expected 1", exception.Message, StringComparison.Ordinal);
        Assert.Null(node.NameEmbedding);
    }

    [Fact]
    public async Task SaveBulkAsync_RejectsEmbeddingDimensionMismatch()
    {
        var driver = new InMemoryGraphDriver();
        var edge = new EntityEdge { Fact = "Alice knows Bob", GroupId = "group" };
        var embedder = new InvalidBatchEmbedder(
            embeddingDimension: 3,
            new[] { (IReadOnlyList<float>)new List<float> { 1, 2 } });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => driver.SaveBulkAsync(
            Array.Empty<EpisodicNode>(),
            Array.Empty<EpisodicEdge>(),
            Array.Empty<EntityNode>(),
            new[] { edge },
            embedder));

        Assert.Contains("entity edge", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dimension 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("expected 3", exception.Message, StringComparison.Ordinal);
        Assert.Null(edge.FactEmbedding);
    }

    [Fact]
    public async Task SaveBulkAsync_SavesWithinEachPhaseConcurrently()
    {
        var episodicNodes = Enumerable.Range(0, 6)
            .Select(index => new EpisodicNode { Name = $"episode-{index}", GroupId = "group" })
            .ToList();
        var entityNodes = Enumerable.Range(0, 6)
            .Select(index => new EntityNode
            {
                Name = $"entity-{index}",
                GroupId = "group",
                NameEmbedding = new List<float> { index }
            })
            .ToList();
        var episodicEdges = Enumerable.Range(0, 6)
            .Select(index => new EpisodicEdge
            {
                SourceNodeUuid = episodicNodes[0].Uuid,
                TargetNodeUuid = entityNodes[index].Uuid,
                GroupId = "group"
            })
            .ToList();
        var entityEdges = Enumerable.Range(0, 6)
            .Select(index => new EntityEdge
            {
                SourceNodeUuid = entityNodes[index].Uuid,
                TargetNodeUuid = entityNodes[(index + 1) % entityNodes.Count].Uuid,
                GroupId = "group",
                Fact = $"fact-{index}",
                FactEmbedding = new List<float> { index }
            })
            .ToList();
        var driver = new DelayedBulkSaveDriver(
            expectedEpisodicNodes: episodicNodes.Count,
            expectedEntityNodes: entityNodes.Count,
            expectedEpisodicEdges: episodicEdges.Count);

        await driver.SaveBulkAsync(
            episodicNodes,
            episodicEdges,
            entityNodes,
            entityEdges,
            new HashEmbedder(4));

        Assert.True(driver.MaxConcurrentSaves > 1);
        Assert.InRange(driver.MaxConcurrentSaves, 2, driver.BulkConcurrency);
        Assert.False(driver.PhaseViolation);
        Assert.Equal(episodicNodes.Count + entityNodes.Count, driver.SavedNodeCount);
        Assert.Equal(episodicEdges.Count + entityEdges.Count, driver.SavedEdgeCount);
    }

    private sealed class ThrowOnEnumerate<T> : IEnumerable<T>
    {
        private int _enumerated;

        public bool Enumerated => Volatile.Read(ref _enumerated) != 0;

        public IEnumerator<T> GetEnumerator()
        {
            Interlocked.Exchange(ref _enumerated, 1);
            throw new InvalidOperationException("Input should not have been enumerated.");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class RecordingEmbedder : EmbedderClient
    {
        public RecordingEmbedder()
            : base(new EmbedderConfig(embeddingDimension: 2))
        {
        }

        public int SingleCallCount { get; private set; }
        public List<IReadOnlyList<string>> BatchCalls { get; } = new();

        public override Task<IReadOnlyList<float>> CreateAsync(
            string input,
            CancellationToken cancellationToken = default)
        {
            SingleCallCount++;
            throw new InvalidOperationException("SaveBulkAsync should use batch embedding.");
        }

        public override Task<IReadOnlyList<IReadOnlyList<float>>> CreateBatchAsync(
            IReadOnlyList<string> input,
            CancellationToken cancellationToken = default)
        {
            BatchCalls.Add(input.ToList());
            var batchNumber = BatchCalls.Count;
            IReadOnlyList<IReadOnlyList<float>> embeddings = input
                .Select((_, index) => (IReadOnlyList<float>)new List<float> { batchNumber, index + 1 })
                .ToList();
            return Task.FromResult(embeddings);
        }
    }

    private sealed class ThrowingBatchEmbedder : EmbedderClient
    {
        public ThrowingBatchEmbedder()
            : base(new EmbedderConfig(embeddingDimension: 2))
        {
        }

        public override Task<IReadOnlyList<float>> CreateAsync(
            string input,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No embedding calls should be made.");

        public override Task<IReadOnlyList<IReadOnlyList<float>>> CreateBatchAsync(
            IReadOnlyList<string> input,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No embedding calls should be made.");
    }

    private sealed class CancelAfterBatchEmbedder : EmbedderClient
    {
        private readonly CancellationTokenSource _cancellation;

        public CancelAfterBatchEmbedder(CancellationTokenSource cancellation)
            : base(new EmbedderConfig(embeddingDimension: 2))
        {
            _cancellation = cancellation;
        }

        public override Task<IReadOnlyList<float>> CreateAsync(
            string input,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SaveBulkAsync should use batch embedding.");

        public override Task<IReadOnlyList<IReadOnlyList<float>>> CreateBatchAsync(
            IReadOnlyList<string> input,
            CancellationToken cancellationToken = default)
        {
            _cancellation.Cancel();
            IReadOnlyList<IReadOnlyList<float>> embeddings = new[]
            {
                (IReadOnlyList<float>)new List<float> { 1, 2 }
            };
            return Task.FromResult(embeddings);
        }
    }

    private sealed class InvalidBatchEmbedder : EmbedderClient
    {
        private readonly IReadOnlyList<IReadOnlyList<float>> _embeddings;

        public InvalidBatchEmbedder(int embeddingDimension, IReadOnlyList<IReadOnlyList<float>> embeddings)
            : base(new EmbedderConfig(embeddingDimension))
        {
            _embeddings = embeddings;
        }

        public override Task<IReadOnlyList<float>> CreateAsync(
            string input,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SaveBulkAsync should use batch embedding.");

        public override Task<IReadOnlyList<IReadOnlyList<float>>> CreateBatchAsync(
            IReadOnlyList<string> input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_embeddings);
    }

    private sealed class DelayedBulkSaveDriver : GraphDriverBase
    {
        private readonly int _expectedEpisodicNodes;
        private readonly int _expectedEntityNodes;
        private readonly int _expectedEpisodicEdges;
        private int _activeSaves;
        private int _activeEpisodicNodes;
        private int _activeEntityNodes;
        private int _activeEpisodicEdges;
        private int _episodicNodeStarts;
        private int _entityNodeStarts;
        private int _episodicEdgeStarts;
        private int _maxConcurrentSaves;
        private int _phaseViolation;
        private int _savedNodeCount;
        private int _savedEdgeCount;
        private readonly CancellationTokenSource? _cancelAfterEpisodicNodeSave;

        public DelayedBulkSaveDriver(
            int expectedEpisodicNodes,
            int expectedEntityNodes,
            int expectedEpisodicEdges,
            CancellationTokenSource? cancelAfterEpisodicNodeSave = null)
            : base(GraphProvider.InMemory)
        {
            _expectedEpisodicNodes = expectedEpisodicNodes;
            _expectedEntityNodes = expectedEntityNodes;
            _expectedEpisodicEdges = expectedEpisodicEdges;
            _cancelAfterEpisodicNodeSave = cancelAfterEpisodicNodeSave;
        }

        public int BulkConcurrency => BulkSaveConcurrency;
        public int MaxConcurrentSaves => Volatile.Read(ref _maxConcurrentSaves);
        public bool PhaseViolation => Volatile.Read(ref _phaseViolation) != 0;
        public int SavedNodeCount => Volatile.Read(ref _savedNodeCount);
        public int SavedEdgeCount => Volatile.Read(ref _savedEdgeCount);

        public override async Task SaveNodeAsync(Node node, CancellationToken cancellationToken = default)
        {
            switch (node)
            {
                case EpisodicNode:
                    Interlocked.Increment(ref _episodicNodeStarts);
                    Interlocked.Increment(ref _activeEpisodicNodes);
                    await DelaySaveAsync(cancellationToken).ConfigureAwait(false);
                    Interlocked.Decrement(ref _activeEpisodicNodes);
                    _cancelAfterEpisodicNodeSave?.Cancel();
                    break;
                case EntityNode:
                    if (Volatile.Read(ref _episodicNodeStarts) < _expectedEpisodicNodes
                        || Volatile.Read(ref _activeEpisodicNodes) != 0)
                    {
                        Interlocked.Exchange(ref _phaseViolation, 1);
                    }

                    Interlocked.Increment(ref _entityNodeStarts);
                    Interlocked.Increment(ref _activeEntityNodes);
                    await DelaySaveAsync(cancellationToken).ConfigureAwait(false);
                    Interlocked.Decrement(ref _activeEntityNodes);
                    break;
            }

            Interlocked.Increment(ref _savedNodeCount);
        }

        public override async Task SaveEdgeAsync(Edge edge, CancellationToken cancellationToken = default)
        {
            switch (edge)
            {
                case EpisodicEdge:
                    if (Volatile.Read(ref _entityNodeStarts) < _expectedEntityNodes
                        || Volatile.Read(ref _activeEntityNodes) != 0)
                    {
                        Interlocked.Exchange(ref _phaseViolation, 1);
                    }

                    Interlocked.Increment(ref _episodicEdgeStarts);
                    Interlocked.Increment(ref _activeEpisodicEdges);
                    await DelaySaveAsync(cancellationToken).ConfigureAwait(false);
                    Interlocked.Decrement(ref _activeEpisodicEdges);
                    break;
                case EntityEdge:
                    if (Volatile.Read(ref _episodicEdgeStarts) < _expectedEpisodicEdges
                        || Volatile.Read(ref _activeEpisodicEdges) != 0)
                    {
                        Interlocked.Exchange(ref _phaseViolation, 1);
                    }

                    await DelaySaveAsync(cancellationToken).ConfigureAwait(false);
                    break;
            }

            Interlocked.Increment(ref _savedEdgeCount);
        }

        private async Task DelaySaveAsync(CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _activeSaves);
            UpdateMax(ref _maxConcurrentSaves, active);
            try
            {
                await Task.Delay(40, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _activeSaves);
            }
        }

        private static void UpdateMax(ref int target, int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref target);
                if (value <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref target, value, current) == current)
                {
                    return;
                }
            }
        }

        public override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override IGraphDriver Clone(string database) => throw new NotSupportedException();
        public override Task DeleteNodeAsync(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteNodesByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteNodesByUuidsAsync(IEnumerable<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteEdgeAsync(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteEdgesByUuidsAsync(IEnumerable<string> uuids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task ClearDataAsync(IReadOnlyList<string>? groupIds = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<TNode> GetNodeByUuidAsync<TNode>(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<TNode>> GetNodesByUuidsAsync<TNode>(IEnumerable<string> uuids, string? groupId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<TNode>> GetNodesByGroupIdsAsync<TNode>(IEnumerable<string> groupIds, int? limit = null, string? uuidCursor = null, bool withEmbeddings = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<T> GetEdgeByUuidAsync<T>(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<T>> GetEdgesByUuidsAsync<T>(IEnumerable<string> uuids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<T>> GetEdgesByGroupIdsAsync<T>(IEnumerable<string> groupIds, int? limit = null, string? uuidCursor = null, bool withEmbeddings = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesBetweenNodesAsync(string sourceNodeUuid, string targetNodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesByNodeUuidAsync(string nodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EpisodicNode>> GetEpisodesByEntityNodeUuidAsync(string entityNodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(DateTime referenceTime, int lastN, IReadOnlyList<string>? groupIds = null, EpisodeType? source = null, string? saga = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EntityNode>> GetMentionedNodesAsync(IReadOnlyList<EpisodicNode> episodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<CommunityNode>> GetCommunitiesByNodesAsync(IReadOnlyList<EntityNode> nodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<SagaNode?> FindSagaByNameAsync(string name, string groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<string?> GetSagaPreviousEpisodeUuidAsync(string sagaUuid, string currentEpisodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<SagaEpisodeContent>> GetSagaEpisodeContentsAsync(string sagaUuid, DateTime? since = null, int limit = 200, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
