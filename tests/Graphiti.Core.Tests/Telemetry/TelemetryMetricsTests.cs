using System.Diagnostics.Metrics;
using System.Text.Json.Nodes;

namespace Graphiti.Core.Tests.Telemetry;

public class TelemetryMetricsTests
{
    [Fact]
    public void GraphitiTelemetry_ExposesMeterWithActivitySourceName()
    {
        Assert.Equal("Graphiti.Core", GraphitiTelemetry.ActivitySourceName);
        Assert.Equal(GraphitiTelemetry.ActivitySourceName, GraphitiTelemetry.MeterName);
        Assert.Equal(GraphitiTelemetry.MeterName, GraphitiTelemetry.Meter.Name);
    }

    [Fact]
    public async Task LlmClient_RecordsTokenUsageMetricsAfterValidation()
    {
        var client = new UsageReportingLlmClient(inputTokens: 7, outputTokens: 11);

        var metrics = await CaptureMetricsAsync(() => client.GenerateResponseAsync(
            new[] { new Message("user", "extract") },
            promptName: "metrics_prompt")).ConfigureAwait(true);

        var input = Assert.Single(metrics, measurement =>
            measurement.InstrumentName == "graphiti.llm.tokens"
            && HasTag(measurement, "graphiti.token.type", "input")
            && HasTag(measurement, "graphiti.prompt_name", "metrics_prompt"));
        var output = Assert.Single(metrics, measurement =>
            measurement.InstrumentName == "graphiti.llm.tokens"
            && HasTag(measurement, "graphiti.token.type", "output")
            && HasTag(measurement, "graphiti.prompt_name", "metrics_prompt"));

        Assert.Equal(7d, input.Value);
        Assert.Equal(11d, output.Value);
    }

    [Fact]
    public async Task MemoryLlmResponseCache_RecordsHitAndMissMetrics()
    {
        using var cache = new MemoryLlmResponseCache();
        var factoryCalls = 0;

        var metrics = await CaptureMetricsAsync(async () =>
        {
            await cache.GetOrCreateAsync(
                "cache-key",
                _ =>
                {
                    factoryCalls++;
                    return Task.FromResult(new JsonObject { ["ok"] = true });
                }).ConfigureAwait(false);
            await cache.GetOrCreateAsync(
                "cache-key",
                _ =>
                {
                    factoryCalls++;
                    return Task.FromResult(new JsonObject { ["ok"] = true });
                }).ConfigureAwait(false);
        }).ConfigureAwait(true);

        Assert.Equal(1, factoryCalls);
        Assert.Contains(metrics, measurement =>
            measurement.InstrumentName == "graphiti.llm.cache.lookups"
            && HasTag(measurement, "graphiti.cache.kind", nameof(MemoryLlmResponseCache))
            && HasTag(measurement, "graphiti.cache.outcome", "miss"));
        Assert.Contains(metrics, measurement =>
            measurement.InstrumentName == "graphiti.llm.cache.lookups"
            && HasTag(measurement, "graphiti.cache.kind", nameof(MemoryLlmResponseCache))
            && HasTag(measurement, "graphiti.cache.outcome", "hit"));
    }

    [Fact]
    public async Task Graphiti_RecordsIngestionAndSearchMetrics()
    {
        const string groupId = "metrics-group";
        var graphiti = new Graphiti(
            graphDriver: new InMemoryGraphDriver(),
            llmClient: new StaticJsonLlmClient(messages =>
            {
                var prompt = messages.Count == 0 ? string.Empty : messages[^1].Content;
                if (prompt.Contains("<ENTITIES>", StringComparison.Ordinal))
                {
                    return new JsonObject
                    {
                        ["edges"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["source_entity_name"] = "Alice",
                                ["target_entity_name"] = "Bob",
                                ["relation_type"] = "LIKES",
                                ["fact"] = "Alice likes Bob",
                                ["valid_at"] = "2026-01-01T00:00:00Z"
                            }
                        }
                    };
                }

                if (prompt.Contains("<CURRENT MESSAGE>", StringComparison.Ordinal))
                {
                    return new JsonObject
                    {
                        ["extracted_entities"] = new JsonArray
                        {
                            new JsonObject { ["name"] = "Alice", ["entity_type_id"] = 0 },
                            new JsonObject { ["name"] = "Bob", ["entity_type_id"] = 0 }
                        }
                    };
                }

                return new JsonObject();
            }));

        var metrics = await CaptureMetricsAsync(async () =>
        {
            await graphiti.AddEpisodeAsync(
                "conversation",
                "Alice likes Bob",
                "message",
                new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
                groupId: groupId).ConfigureAwait(false);
            await graphiti.SearchAsync("Alice Bob", groupIds: new[] { groupId }).ConfigureAwait(false);
        }).ConfigureAwait(true);

        var episodes = Assert.Single(
            metrics,
            measurement => measurement.InstrumentName == "graphiti.episodes.ingested"
                           && HasTag(measurement, "graphiti.group_id", groupId));
        Assert.Equal(1d, episodes.Value);
        Assert.Equal("add_episode", episodes.Tags["graphiti.operation"]);
        Assert.Equal("success", episodes.Tags["graphiti.status"]);
        Assert.Equal(groupId, episodes.Tags["graphiti.group_id"]);
        Assert.Equal("Message", episodes.Tags["graphiti.episode.source"]);

        Assert.Contains(metrics, measurement =>
            measurement.InstrumentName == "graphiti.episode.ingestion.duration"
            && HasTag(measurement, "graphiti.operation", "add_episode")
            && HasTag(measurement, "graphiti.status", "success"));
        Assert.Contains(metrics, measurement =>
            measurement.InstrumentName == "graphiti.episode.ingestion.results"
            && HasTag(measurement, "graphiti.group_id", groupId)
            && HasTag(measurement, "graphiti.result.kind", "node")
            && measurement.Value == 2d);
        Assert.Contains(metrics, measurement =>
            measurement.InstrumentName == "graphiti.episode.ingestion.results"
            && HasTag(measurement, "graphiti.group_id", groupId)
            && HasTag(measurement, "graphiti.result.kind", "edge")
            && measurement.Value == 1d);
        Assert.Contains(metrics, measurement =>
            measurement.InstrumentName == "graphiti.search.duration"
            && HasTag(measurement, "graphiti.operation", "search_edges")
            && HasTag(measurement, "graphiti.status", "success"));

        Assert.Contains(metrics, measurement =>
            measurement.InstrumentName == "graphiti.search.results"
            && HasTag(measurement, "graphiti.operation", "search_edges")
            && HasTag(measurement, "graphiti.result.kind", "edge")
            && measurement.Value == 1d);
    }

    private static async Task<IReadOnlyList<MetricMeasurement>> CaptureMetricsAsync(Func<Task> action)
    {
        var measurements = new List<MetricMeasurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == GraphitiTelemetry.MeterName)
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            measurements.Add(new MetricMeasurement(instrument.Name, measurement, CopyTags(tags))));
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
            measurements.Add(new MetricMeasurement(instrument.Name, measurement, CopyTags(tags))));
        listener.Start();

        await action().ConfigureAwait(false);
        listener.RecordObservableInstruments();
        return measurements;
    }

    private static bool HasTag(MetricMeasurement measurement, string key, object value) =>
        measurement.Tags.TryGetValue(key, out var actual) && Equals(value, actual);

    private static Dictionary<string, object?> CopyTags(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            result[tag.Key] = tag.Value;
        }

        return result;
    }

    private sealed record MetricMeasurement(
        string InstrumentName,
        double Value,
        Dictionary<string, object?> Tags);

    private sealed class UsageReportingLlmClient : LlmClient
    {
        private readonly long _inputTokens;
        private readonly long _outputTokens;

        public UsageReportingLlmClient(long inputTokens, long outputTokens)
            : base(config: null, cache: false)
        {
            _inputTokens = inputTokens;
            _outputTokens = outputTokens;
        }

        protected override Task<JsonObject> GenerateResponseCoreAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel,
            StructuredResponseSchema? responseSchema,
            int maxTokens,
            ModelSize modelSize,
            string? promptName,
            CancellationToken cancellationToken)
        {
            _ = messages;
            _ = responseModel;
            _ = responseSchema;
            _ = maxTokens;
            _ = modelSize;
            cancellationToken.ThrowIfCancellationRequested();
            SetPendingTokenUsage(promptName, _inputTokens, _outputTokens);
            return Task.FromResult(new JsonObject { ["ok"] = true });
        }
    }
}
