# Uncached LLM Cache-Key Benchmark - win-x64 - 2026-06-26

Benchmark suite: `Graphiti.Core.Benchmarks.IngestionBenchmarks`

Before command:

```powershell
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*IngestionBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\cachekey-before-2026-06-26
```

After command:

```powershell
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*IngestionBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\cachekey-after-2026-06-26
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
| `AddEpisode_Single` | 211.92 KB | 195.11 KB |
| `AddEpisodesSequential_SixEpisodes` | 1588.04 KB | 1482.66 KB |
| `AddEpisodeBulk_SixEpisodes` | 1129.56 KB | 1019.67 KB |
| `IngestBulkThenSearch_SixEpisodes` | 1155.95 KB | 1042.15 KB |

Notes:

- The change skips deterministic cache-key JSON serialization and SHA-256 hashing when no LLM
  response cache is configured.
- The timing columns were noisy in this ShortRun and should not be treated as a stable time claim.
