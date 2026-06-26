# Bulk Throttling List-Copy Benchmark - win-x64 - 2026-06-26

Benchmark suite: `Graphiti.Core.Benchmarks.IngestionBenchmarks`

Before command:

```powershell
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*IngestionBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\cachekey-after-2026-06-26
```

After command:

```powershell
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*IngestionBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\bulk-list-after-2026-06-26
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

| Method | Before Allocated | After Allocated |
|---|---:|---:|
| `AddEpisodeBulk_SixEpisodes` | 1019.67 KB | 1013.87 KB |
| `IngestBulkThenSearch_SixEpisodes` | 1042.15 KB | 1041.72 KB |

Notes:

- The change lets private throttling helpers accept `IReadOnlyList<T>` and removes two bulk-ingestion
  `.ToList()` copies.
- This is a small allocation cleanup; ShortRun timing columns were noisy.
