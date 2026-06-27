# Bulk Edge Dedupe Benchmark Baseline - win-x64 - 2026-06-27

Benchmark suite: `Graphiti.Core.Benchmarks.BulkEdgeDedupeBenchmarks`

Command:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*BulkEdgeDedupeBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\bulk-edge-dedupe-before-2026-06-27
```

Dry validation:

```powershell
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*BulkEdgeDedupeBenchmarks*" --job Dry --noOverwrite --artifacts artifacts\benchmarks\bulk-edge-dedupe-dry-2026-06-27
```

Environment:

```text
BenchmarkDotNet v0.15.8
Windows 11 (10.0.26200.8737/25H2/2025Update/HudsonValley2)
Intel Core i5-14600K 3.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.301
Host: .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
Job: ShortRun, IterationCount=5, LaunchCount=1, WarmupCount=3
GC: Concurrent Workstation
```

Results:

| Method | EndpointPairCount | Mean | Error | StdDev | Gen0 | Gen1 | Allocated |
|---|---:|---:|---:|---:|---:|---:|---:|
| `AddEpisodeBulk_ManyEndpointPairs` | 8 | 6.111 ms | 3.5605 ms | 0.9247 ms | 453.1250 | 171.8750 | 4.95 MB |
| `AddEpisodeBulk_ManyEndpointPairs` | 16 | 10.016 ms | 0.7615 ms | 0.1978 ms | 1000.0000 | 531.2500 | 11.18 MB |

Notes:

- This benchmark covers a public bulk-ingestion workflow with four episodes and many extracted facts
  spread across multiple endpoint pairs, exercising the bulk edge-dedupe candidate scan after node
  resolution.
- An endpoint-pair bucketing trial was measured and not kept: the stable 16-pair case moved only from
  10.016 ms to 9.972 ms, and allocation stayed effectively unchanged. Future changes here should use
  narrower profiling or a stronger same-workflow before/after signal.
