using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Graphiti.Core.Drivers;
using Graphiti.Core.LlmClients;
using Graphiti.Core.Models;
using Graphiti.Core.Models.Results;
using GraphitiCore = Graphiti.Core.Graphiti;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// Public ingestion workflows over the deterministic in-memory driver. These benchmarks cover the
/// orchestration cost of single-episode ingestion, true-batch bulk ingestion, sequential ingestion,
/// and a small ingest-then-search workflow without real provider calls.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Ingestion")]
public class IngestionBenchmarks
{
    private const string GroupId = "bench-ingestion";
    private readonly DateTime _start = new(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc);
    private EpisodeSpec[] _episodes = null!;
    private RawEpisode[] _rawEpisodes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _episodes =
        [
            new("Intro", "Maya Patel manages the Atlas migration project at Nimbus Health."),
            new("Owner", "Leo Chen owns the Atlas rollout checklist for Maya Patel."),
            new("Delay", "The Atlas rollout moved to March 29 because of an authentication issue."),
            new("QA", "QA cleared the Atlas authentication issue on March 22."),
            new("Checklist", "Maya Patel asked Leo Chen to prepare the Atlas launch checklist."),
            new("Memory", "Remember that Maya Patel manages Atlas at Nimbus Health.")
        ];
        _rawEpisodes = new RawEpisode[_episodes.Length];
        for (var i = 0; i < _episodes.Length; i++)
        {
            _rawEpisodes[i] = CreateRawEpisode(_episodes[i], i);
        }
    }

    [Benchmark]
    public async Task<int> AddEpisode_Single()
    {
        await using var graphiti = CreateGraphiti();
        var result = await AddEpisodeAsync(graphiti, _episodes[0], index: 0).ConfigureAwait(false);
        return Count(result);
    }

    [Benchmark]
    public async Task<int> AddEpisodesSequential_SixEpisodes()
    {
        await using var graphiti = CreateGraphiti();
        var total = 0;
        for (var i = 0; i < _episodes.Length; i++)
        {
            total += Count(await AddEpisodeAsync(graphiti, _episodes[i], i).ConfigureAwait(false));
        }

        return total;
    }

    [Benchmark]
    public async Task<int> AddEpisodeBulk_SixEpisodes()
    {
        await using var graphiti = CreateGraphiti();
        var result = await graphiti
            .AddEpisodeBulkAsync(_rawEpisodes, groupId: GroupId)
            .ConfigureAwait(false);
        return Count(result);
    }

    [Benchmark]
    public async Task<int> IngestBulkThenSearch_SixEpisodes()
    {
        await using var graphiti = CreateGraphiti();
        var result = await graphiti
            .AddEpisodeBulkAsync(_rawEpisodes, groupId: GroupId)
            .ConfigureAwait(false);
        var facts = await graphiti
            .SearchAsync("Who manages Atlas?", groupIds: new[] { GroupId }, numResults: 5)
            .ConfigureAwait(false);
        return Count(result) + facts.Count;
    }

    private Task<AddEpisodeResults> AddEpisodeAsync(
        GraphitiCore graphiti,
        EpisodeSpec episode,
        int index) =>
        graphiti.AddEpisodeAsync(
            episode.Name,
            episode.Content,
            sourceDescription: "fixture transcript",
            referenceTime: _start.AddMinutes(index * 5),
            EpisodeType.Message,
            groupId: GroupId);

    private RawEpisode CreateRawEpisode(EpisodeSpec episode, int index) => new()
    {
        Name = episode.Name,
        Content = episode.Content,
        SourceDescription = "fixture transcript",
        Source = EpisodeType.Message,
        ReferenceTime = _start.AddMinutes(index * 5)
    };

    private static GraphitiCore CreateGraphiti() =>
        new(
            llmClient: CreateLlmClient(),
            graphDriver: new InMemoryGraphDriver("bench-ingestion"),
            maxCoroutines: 2);

    private static StaticJsonLlmClient CreateLlmClient() =>
        new(_ => CreateExtractionResponse());

    private static JsonObject CreateExtractionResponse() => new()
    {
        ["extracted_entities"] = new JsonArray
        {
            new JsonObject { ["name"] = "Maya Patel", ["entity_type_id"] = 0, ["entity_type"] = "Person" },
            new JsonObject { ["name"] = "Atlas migration", ["entity_type_id"] = 0, ["entity_type"] = "Project" }
        },
        ["edges"] = new JsonArray
        {
            new JsonObject
            {
                ["source"] = "Maya Patel",
                ["target"] = "Atlas migration",
                ["source_entity_name"] = "Maya Patel",
                ["target_entity_name"] = "Atlas migration",
                ["relation_type"] = "MANAGES",
                ["fact"] = "Maya Patel manages the Atlas migration project.",
                ["valid_at"] = "2026-01-10T09:00:00Z"
            }
        },
        ["entity_resolutions"] = new JsonArray(),
        ["duplicate_facts"] = new JsonArray(),
        ["contradicted_facts"] = new JsonArray(),
        ["summaries"] = new JsonArray(),
        ["timestamps"] = new JsonArray(),
        ["summary"] = string.Empty,
        ["description"] = string.Empty
    };

    private static int Count(AddEpisodeResults result) =>
        result.Nodes.Count + result.Edges.Count + result.EpisodicEdges.Count;

    private static int Count(AddBulkEpisodeResults result) =>
        result.Episodes.Count + result.Nodes.Count + result.Edges.Count + result.EpisodicEdges.Count;

    private readonly record struct EpisodeSpec(string Name, string Content);
}
