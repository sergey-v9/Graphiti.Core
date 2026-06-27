using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Graphiti.Core.Drivers;
using Graphiti.Core.LlmClients;
using Graphiti.Core.Models;
using Graphiti.Core.Models.Results;
using GraphitiCore = Graphiti.Core.Graphiti;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// Bulk ingestion fixture with many extracted facts spread across endpoint pairs. It exercises the
/// edge-dedupe candidate scan that runs after batch extraction and node resolution.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Ingestion")]
public class BulkEdgeDedupeBenchmarks
{
    private const string GroupId = "bench-bulk-edge-dedupe";
    private const int EpisodeCount = 4;
    private const int EdgesPerPair = 2;
    private readonly DateTime _start = new(2026, 2, 4, 10, 0, 0, DateTimeKind.Utc);
    private RawEpisode[] _rawEpisodes = null!;

    [Params(8, 16)]
    public int EndpointPairCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _rawEpisodes = new RawEpisode[EpisodeCount];
        for (var i = 0; i < _rawEpisodes.Length; i++)
        {
            _rawEpisodes[i] = new RawEpisode
            {
                Name = $"Bulk edge dedupe {i:D2}",
                Content = $"Synthetic bulk edge dedupe fixture {i:D2}.",
                SourceDescription = "fixture transcript",
                Source = EpisodeType.Message,
                ReferenceTime = _start.AddMinutes(i * 5)
            };
        }
    }

    [Benchmark]
    public async Task<int> AddEpisodeBulk_ManyEndpointPairs()
    {
        await using var graphiti = new GraphitiCore(
            llmClient: new StaticJsonLlmClient(_ => CreateExtractionResponse(EndpointPairCount)),
            graphDriver: new InMemoryGraphDriver("bench-bulk-edge-dedupe"),
            maxCoroutines: 1);
        var result = await graphiti
            .AddEpisodeBulkAsync(_rawEpisodes, groupId: GroupId)
            .ConfigureAwait(false);
        return Count(result);
    }

    private static JsonObject CreateExtractionResponse(int endpointPairCount)
    {
        var entities = new JsonArray();
        var edges = new JsonArray();
        for (var pair = 0; pair < endpointPairCount; pair++)
        {
            var sourceName = $"Source {pair:D2}";
            var targetName = $"Target {pair:D2}";
            entities.Add(new JsonObject
            {
                ["name"] = sourceName,
                ["entity_type_id"] = 0,
                ["entity_type"] = "Entity"
            });
            entities.Add(new JsonObject
            {
                ["name"] = targetName,
                ["entity_type_id"] = 0,
                ["entity_type"] = "Entity"
            });

            for (var edge = 0; edge < EdgesPerPair; edge++)
            {
                edges.Add(new JsonObject
                {
                    ["source"] = sourceName,
                    ["target"] = targetName,
                    ["source_entity_name"] = sourceName,
                    ["target_entity_name"] = targetName,
                    ["relation_type"] = "COORDINATES",
                    ["fact"] = $"{sourceName} coordinates milestone {edge:D2} with {targetName}.",
                    ["valid_at"] = "2026-02-04T10:00:00Z"
                });
            }
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
}
