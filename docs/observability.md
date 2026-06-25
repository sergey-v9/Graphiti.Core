# Observability

Graphiti Core emits OpenTelemetry-compatible traces and metrics through BCL diagnostics types. The
library does not reference exporter SDKs; applications decide how to collect and export signals.

## Instrumentation names

Use the constants in `Graphiti.Core.Telemetry.GraphitiTelemetry`:

| Signal | Constant | Value |
|---|---|---|
| Traces | `ActivitySourceName` | `Graphiti.Core` |
| Metrics | `MeterName` | `Graphiti.Core` |

## Traces

Subscribe to `GraphitiTelemetry.ActivitySourceName`. Spans cover the public write/search operations,
graph writes, extraction, resolution, provider calls, embedding calls, search retrieval, reranking,
community builds, and saga summarization. Span tags use the `graphiti.*` prefix for Graphiti-specific
state and `gen_ai.*` for provider call fields.

## Metrics

Subscribe to `GraphitiTelemetry.MeterName`.

| Instrument | Unit | Meaning |
|---|---|---|
| `graphiti.episodes.ingested` | `{episode}` | Successful `AddEpisodeAsync` / `AddEpisodeBulkAsync` episode count. |
| `graphiti.episode.ingestion.duration` | `s` | Ingestion duration, tagged with operation and status. |
| `graphiti.episode.ingestion.results` | `{result}` | Graph elements produced by ingestion, tagged by result kind. |
| `graphiti.search.duration` | `s` | Search duration, tagged with operation and status. |
| `graphiti.search.results` | `{result}` | Search result counts, tagged by result kind (`edge`, `node`, `episode`, `community`). |
| `graphiti.llm.tokens` | `{token}` | Validated provider token usage, tagged as `input` or `output`. |
| `graphiti.llm.cache.lookups` | `{lookup}` | LLM response cache lookups, tagged as `hit` or `miss`. |

Compute cache hit rate from `graphiti.llm.cache.lookups`:

```text
hit_rate = hit / (hit + miss)
```

## OTLP wiring

Add OpenTelemetry packages to the application or sample host, not to `Graphiti.Core`:

```xml
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.16.0" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.16.0" />
```

Register Graphiti traces and metrics with the host:

```csharp
using Graphiti.Core.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("my-graphiti-app"))
    .WithTracing(tracing => tracing.AddSource(GraphitiTelemetry.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(GraphitiTelemetry.MeterName))
    .UseOtlpExporter();
```

Configure the endpoint with standard OpenTelemetry environment variables:

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
dotnet run --project samples\Graphiti.Sample.Observability
```

The runnable sample in `samples/Graphiti.Sample.Observability` wires OTLP export, runs a tiny
in-memory ingestion/search flow, and emits both traces and metrics.
