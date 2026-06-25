using Graphiti.Core;
using Graphiti.Core.Drivers;
using Graphiti.Core.Models;
using Graphiti.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

const string GroupId = "observability";

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Graphiti.Sample.Observability"))
    .WithTracing(tracing => tracing.AddSource(GraphitiTelemetry.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(GraphitiTelemetry.MeterName))
    .UseOtlpExporter();

using var host = builder.Build();
await host.StartAsync();

await using var graphiti = new global::Graphiti.Core.Graphiti(
    graphDriver: new InMemoryGraphDriver("observability"));

await graphiti.BuildIndicesAndConstraintsAsync(deleteExisting: true);
await graphiti.AddEpisodeAsync(
    name: "Observed episode",
    episodeBody: "Maya Patel manages the Atlas migration project at Nimbus Health.",
    sourceDescription: "fixture transcript",
    referenceTime: new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc),
    source: EpisodeType.Message,
    groupId: GroupId);
await graphiti.SearchAsync(
    "Who manages Atlas?",
    groupIds: new[] { GroupId },
    numResults: 3);

Console.WriteLine("Graphiti traces and metrics were emitted through OpenTelemetry.");
Console.WriteLine("Set OTEL_EXPORTER_OTLP_ENDPOINT to send them to a collector.");

await host.StopAsync();
