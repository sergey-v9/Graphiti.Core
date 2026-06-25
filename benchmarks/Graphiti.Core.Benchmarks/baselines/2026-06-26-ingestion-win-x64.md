# Ingestion Benchmark Baseline - win-x64 - 2026-06-26

Benchmark suite: `Graphiti.Core.Benchmarks.IngestionBenchmarks`

Command:

```powershell
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*IngestionBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\ingestion-2026-06-26
```

Validation: `--job Dry` completed before this run.

Environment:

```text
BenchmarkDotNet v0.15.8
Windows 11 (10.0.26200.8655/25H2/2025Update/HudsonValley2)
Intel Core i5-14600K 3.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.301
Host: .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
Job: ShortRun, IterationCount=5, LaunchCount=1, WarmupCount=3
GC: Concurrent Workstation
```

Results:

| Method | Mean | Error | StdDev | Gen0 | Gen1 | Allocated |
|---|---:|---:|---:|---:|---:|---:|
| `AddEpisode_Single` | 227.5 us | 99.99 us | 15.47 us | 17.5781 | 1.9531 | 211.53 KB |
| `AddEpisodesSequential_SixEpisodes` | 2,929.7 us | 5,102.64 us | 1,325.14 us | 125.0000 | 23.4375 | 1585.66 KB |
| `AddEpisodeBulk_SixEpisodes` | 1,624.8 us | 198.92 us | 51.66 us | 105.4688 | 31.2500 | 1129.32 KB |
| `IngestBulkThenSearch_SixEpisodes` | 1,065.4 us | 1,450.17 us | 376.60 us | 105.4688 | 29.2969 | 1150.53 KB |

Notes:

- This is a local Windows directional baseline, not a release-grade performance claim.
- Compare future changes against a fresh same-machine run before drawing conclusions.
- The benchmark uses deterministic in-memory graph storage and a static JSON LLM fixture; no real
  provider calls are included.
