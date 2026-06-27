# Bulk Edge Word-Overlap Benchmark - win-x64 - 2026-06-27

Benchmark suite: `Graphiti.Core.Benchmarks.BulkEdgeDedupeBenchmarks`

Purpose: measure bulk edge dedupe after hoisting the left-fact word set out of the inner candidate
loop and scanning right-side fact words by span. The benchmark exercises a public bulk-ingestion
workflow with many extracted facts spread across endpoint pairs.

Commands:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*BulkEdgeDedupeBenchmarks*" --job Dry --noOverwrite --artifacts artifacts\benchmarks\bulk-edge-wordoverlap-dry-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*BulkEdgeDedupeBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\bulk-edge-wordoverlap-before-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*BulkEdgeDedupeBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\bulk-edge-wordoverlap-after-2026-06-27
```

Environment:

```text
BenchmarkDotNet v0.15.8
Windows 11 (10.0.26200.8737/25H2/2025Update/HudsonValley2)
Intel Core i5-14600K 3.50GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.301
Runtime: .NET 10.0.9, X64 RyuJIT x86-64-v3
Job: ShortRun, IterationCount=5, LaunchCount=1, WarmupCount=3
```

Results:

| Endpoint pairs | Before Mean | After Mean | Before Allocated | After Allocated |
| ---: | ---: | ---: | ---: | ---: |
| 8 | 4.172 ms | 5.338 ms | 4.95 MB | 4.52 MB |
| 16 | 11.427 ms | 17.634 ms | 11.18 MB | 10.39 MB |

Interpretation:

- Allocation drops by roughly 0.43 MB at 8 endpoint pairs and 0.79 MB at 16 endpoint pairs by avoiding
  repeated left-fact split/hash-set construction and right-side split arrays.
- Wall-clock timing is noisy in this full async ingestion benchmark; this slice is retained for the
  allocation reduction and unchanged focused bulk-ingestion behavior.
