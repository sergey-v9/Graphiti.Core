# Provider Throughput Benchmark Baseline - win-x64 - 2026-06-28

Benchmark suites:

- `Graphiti.Core.Benchmarks.LlmProviderThroughputBenchmarks`
- `Graphiti.Core.Benchmarks.EmbeddingProviderThroughputBenchmarks`

Commands:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*ProviderThroughputBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\plan11-provider-metrics-2026-06-28
```

Dry validation:

```powershell
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*ProviderThroughputBenchmarks*" --job Dry --noOverwrite --artifacts artifacts\benchmarks\plan11-provider-metrics-dry
```

Environment:

```text
BenchmarkDotNet v0.15.8
Windows 11 (10.0.26200.8737/25H2/2025Update/HudsonValley2)
Intel Core i5-14600K 3.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.301
Host: .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
Job: ShortRun, InvocationCount=1, IterationCount=5, LaunchCount=1, UnrollFactor=1, WarmupCount=3
GC: Concurrent Workstation
```

LLM provider throughput:

| Method | ProviderLatencyMs | ProviderConcurrencyLimit | Mean | Error | StdDev | Allocated |
|---|---:|---:|---:|---:|---:|---:|
| `LlmCacheHits_SamePrompt` | 25 | 4 | 361.9 us | 64.69 us | 16.80 us | 97.54 KB |
| `LlmRateLimitedMisses_DistinctPrompts` | 25 | 4 | 121,559.6 us | 3,474.74 us | 902.38 us | 114.11 KB |

Embedding provider throughput:

| Method | ProviderLatencyMs | EmbeddingBatchSize | ProviderConcurrencyLimit | Mean | Error | StdDev | Allocated |
|---|---:|---:|---:|---:|---:|---:|---:|
| `EmbeddingBatch_LatencyBound` | 25 | 8 | 4 | 92.98 ms | 2.371 ms | 0.616 ms | 137.7 KB |
| `EmbeddingBatch_LatencyBound` | 25 | 32 | 4 | 30.52 ms | 2.786 ms | 0.724 ms | 124.96 KB |
| `EmbeddingBatch_LatencyBound` | 25 | 128 | 4 | 30.47 ms | 4.311 ms | 0.667 ms | 122.32 KB |

Notes:

- These benchmarks use latency-injecting fake Microsoft.Extensions.AI providers, not real network
  calls. They exercise the production `MicrosoftExtensionsAIChatClient`, `MemoryLlmResponseCache`,
  `MicrosoftExtensionsAIEmbedderClient`, and provider rate-limiter paths.
- The LLM benchmark attaches a `MeterListener` to `GraphitiTelemetry.MeterName`. The warmed-cache path
  asserts 16 `graphiti.llm.cache.lookups` hits, 0 misses, and 0 live provider calls after warmup. The
  distinct-miss path asserts the G4 token counter records 176 input tokens and 112 output tokens for 16
  live fake-provider calls.
- With 16 distinct prompts, 25 ms fake latency, and a concurrency limit of 4, the miss path completed
  in 121.6 ms, close to the expected four provider-latency waves plus adapter/schema overhead. No
  concurrency default change is justified by this run.
- For 96 embedding inputs with 25 ms fake latency and concurrency 4, batch size 8 requires roughly
  three waves (92.98 ms), while 32 and 128 both fit in one wave (30.52 ms and 30.47 ms). The current
  default batch size of 128 remains within budget for this latency-bound shape.
- No cache-key, TTL, schema identity, wire shape, or default setting changed.
