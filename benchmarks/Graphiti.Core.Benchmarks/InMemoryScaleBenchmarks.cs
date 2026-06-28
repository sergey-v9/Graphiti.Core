using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Graphiti.Core.Drivers;
using Graphiti.Core.Embedding;
using Graphiti.Core.LlmClients;
using Graphiti.Core.Models;
using Graphiti.Core.Models.Edges;
using Graphiti.Core.Models.Nodes;
using Graphiti.Core.Models.Results;
using Graphiti.Core.Search;
using GraphitiCore = Graphiti.Core.Graphiti;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// Large-N in-memory graph workflows. Setup preloads a deterministic graph so benchmark timings
/// isolate search and ingestion maintenance over an existing graph rather than fixture construction.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Scale", "InMemory")]
public class InMemoryScaleBenchmarks : IAsyncDisposable
{
    private const string GroupId = "bench-scale";
    private const int VectorDimension = 256;
    private const int IngestEpisodeCount = 8;
    private const int IngestEntityCount = 24;
    private const int IngestEdgeCount = 36;
    private readonly DateTime _start = new(2026, 3, 3, 12, 0, 0, DateTimeKind.Utc);
    private LargeGraphFixture _fixture = null!;
    private InMemoryGraphDriver _searchDriver = null!;
    private GraphitiCore _searchGraphiti = null!;
    private RawEpisode[] _ingestEpisodes = null!;
    private string[] _groupIds = null!;
    private SearchFilters _filters = null!;
    private float[] _queryVector = null!;
    private StaticJsonLlmClient _ingestLlm = null!;
    private HashEmbedder _embedder = null!;

    [Params(10_000)]
    public int NodeCount { get; set; }

    [Params(30_000)]
    public int EdgeCount { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _fixture = LargeGraphFixture.Create(NodeCount, EdgeCount, VectorDimension, GroupId);
        _embedder = new HashEmbedder(VectorDimension);
        _groupIds = [GroupId];
        _filters = new SearchFilters { NodeLabels = new List<string> { "Entity" } };
        _queryVector = BenchmarkData.CreateUnitVector(VectorDimension, seed: 91_001);
        _ingestEpisodes = CreateRawEpisodes();
        _ingestLlm = new StaticJsonLlmClient(_ => CreateIngestionResponse());

        _searchDriver = new InMemoryGraphDriver("bench-scale-search");
        await _fixture.LoadAsync(_searchDriver, _embedder).ConfigureAwait(false);
        _searchGraphiti = CreateGraphiti(_searchDriver, _ingestLlm);
    }

    [IterationSetup(Target = nameof(IngestBulkIntoLargeGraph))]
    public void SetupIngestionIteration()
    {
        _searchGraphiti?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _searchDriver = new InMemoryGraphDriver("bench-scale-ingest");
        _fixture.LoadAsync(_searchDriver, _embedder).GetAwaiter().GetResult();
        _searchGraphiti = CreateGraphiti(_searchDriver, _ingestLlm);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_searchGraphiti is not null)
        {
            await _searchGraphiti.DisposeAsync().ConfigureAwait(false);
        }

        _ingestLlm?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Cleanup().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    public async Task<int> SearchEdgeHybridRrf()
    {
        var edges = await _searchGraphiti
            .SearchAsync(
                "temporal product launch relationship search",
                groupIds: _groupIds,
                numResults: 20)
            .ConfigureAwait(false);
        return edges.Count;
    }

    [Benchmark]
    public async Task<int> SearchNodeVectorTopK()
    {
        var hits = await _searchDriver
            .SearchEntityNodesByEmbeddingAsync(
                _queryVector,
                _filters,
                _groupIds,
                limit: 25,
                minScore: -2.0f)
            .ConfigureAwait(false);
        return hits.Count;
    }

    [Benchmark]
    public async Task<int> SearchEdgeVectorTopK()
    {
        var hits = await _searchDriver
            .SearchEntityEdgesByEmbeddingAsync(
                _queryVector,
                new SearchFilters(),
                _groupIds,
                limit: 25,
                minScore: -2.0f)
            .ConfigureAwait(false);
        return hits.Count;
    }

    [Benchmark]
    public async Task<int> SearchEdgeHybridMmr_Limit100()
    {
        var config = SearchConfigRecipes.EdgeHybridSearchMmr;
        config.Limit = 100;
        var results = await _searchGraphiti
            .SearchAsync(
                "temporal product launch relationship search",
                config,
                groupIds: _groupIds)
            .ConfigureAwait(false);
        return results.Edges.Count;
    }

    [Benchmark]
    public async Task<int> IngestBulkIntoLargeGraph()
    {
        var result = await _searchGraphiti
            .AddEpisodeBulkAsync(_ingestEpisodes, groupId: GroupId)
            .ConfigureAwait(false);
        return Count(result);
    }

    private static GraphitiCore CreateGraphiti(InMemoryGraphDriver driver, StaticJsonLlmClient llmClient) =>
        new(
            llmClient: llmClient,
            embedder: new HashEmbedder(VectorDimension),
            graphDriver: driver,
            maxCoroutines: 4);

    private RawEpisode[] CreateRawEpisodes()
    {
        var episodes = new RawEpisode[IngestEpisodeCount];
        for (var i = 0; i < episodes.Length; i++)
        {
            episodes[i] = new RawEpisode
            {
                Name = $"Scale ingest {i:D2}",
                Content = $"Synthetic scale ingestion episode {i:D2} links product entities.",
                SourceDescription = "benchmark fixture",
                Source = EpisodeType.Message,
                ReferenceTime = _start.AddMinutes(i * 3)
            };
        }

        return episodes;
    }

    private static JsonObject CreateIngestionResponse()
    {
        var entities = new JsonArray();
        for (var i = 0; i < IngestEntityCount; i++)
        {
            entities.Add(new JsonObject
            {
                ["name"] = $"Scale entity {i:D3}",
                ["entity_type_id"] = 0,
                ["entity_type"] = "Entity"
            });
        }

        var edges = new JsonArray();
        for (var i = 0; i < IngestEdgeCount; i++)
        {
            var source = $"Scale entity {i % IngestEntityCount:D3}";
            var target = $"Scale entity {(i * 7 + 3) % IngestEntityCount:D3}";
            if (source == target)
            {
                target = $"Scale entity {(i + 1) % IngestEntityCount:D3}";
            }

            edges.Add(new JsonObject
            {
                ["source"] = source,
                ["target"] = target,
                ["source_entity_name"] = source,
                ["target_entity_name"] = target,
                ["relation_type"] = "COORDINATES",
                ["fact"] = $"{source} coordinates benchmark milestone {i:D3} with {target}.",
                ["valid_at"] = "2026-03-03T12:00:00Z"
            });
        }

        return new JsonObject
        {
            ["extracted_entities"] = entities,
            ["edges"] = edges,
            ["entity_resolutions"] = new JsonArray(),
            ["duplicate_facts"] = new JsonArray(),
            ["contradicted_facts"] = new JsonArray(),
            ["summaries"] = new JsonArray(),
            ["timestamps"] = new JsonArray(),
            ["summary"] = string.Empty,
            ["description"] = string.Empty
        };
    }

    private static int Count(AddBulkEpisodeResults result) =>
        result.Episodes.Count + result.Nodes.Count + result.Edges.Count + result.EpisodicEdges.Count;

    private sealed class LargeGraphFixture
    {
        private readonly List<EntityNode> _nodes;
        private readonly List<EntityEdge> _edges;

        private LargeGraphFixture(List<EntityNode> nodes, List<EntityEdge> edges)
        {
            _nodes = nodes;
            _edges = edges;
        }

        public static LargeGraphFixture Create(
            int nodeCount,
            int edgeCount,
            int vectorDimension,
            string groupId)
        {
            var nodes = new List<EntityNode>(nodeCount);
            var createdAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var i = 0; i < nodeCount; i++)
            {
                nodes.Add(new EntityNode
                {
                    Uuid = $"scale-node-{i:D6}",
                    Name = $"Scale entity {i:D6}",
                    Summary = BenchmarkData.CreateDocument(approximateWords: 18, seed: 100_000 + i),
                    GroupId = groupId,
                    CreatedAt = createdAt.AddSeconds(i),
                    Labels = { "Entity" },
                    NameEmbedding = [.. BenchmarkData.CreateUnitVector(vectorDimension, seed: 110_000 + i)]
                });
            }

            var edges = new List<EntityEdge>(edgeCount);
            for (var i = 0; i < edgeCount; i++)
            {
                var sourceIndex = i % nodeCount;
                var targetIndex = (i * 37 + 11) % nodeCount;
                if (sourceIndex == targetIndex)
                {
                    targetIndex = (targetIndex + 1) % nodeCount;
                }

                edges.Add(new EntityEdge
                {
                    Uuid = $"scale-edge-{i:D6}",
                    GroupId = groupId,
                    SourceNodeUuid = nodes[sourceIndex].Uuid,
                    TargetNodeUuid = nodes[targetIndex].Uuid,
                    CreatedAt = createdAt.AddSeconds(nodeCount + i),
                    Name = "RELATES_TO",
                    Fact = $"Scale entity {sourceIndex:D6} coordinates product milestone {i:D6} with scale entity {targetIndex:D6}.",
                    FactEmbedding = [.. BenchmarkData.CreateUnitVector(vectorDimension, seed: 120_000 + i)],
                    Episodes = { $"scale-episode-{i % 200:D4}" },
                    ValidAt = createdAt.AddMinutes(i % 1440),
                    ReferenceTime = createdAt.AddMinutes(i % 1440)
                });
            }

            return new LargeGraphFixture(nodes, edges);
        }

        public Task LoadAsync(InMemoryGraphDriver driver, IEmbedderClient embedder) =>
            driver.SaveBulkAsync(
                Array.Empty<EpisodicNode>(),
                Array.Empty<EpisodicEdge>(),
                _nodes,
                _edges,
                embedder);
    }
}
