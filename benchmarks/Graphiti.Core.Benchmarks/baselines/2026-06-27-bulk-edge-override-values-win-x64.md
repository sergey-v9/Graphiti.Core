# Bulk Edge Override Values Benchmark - win-x64 - 2026-06-27

Benchmark suite: `Graphiti.Core.Benchmarks.BulkEdgeDedupeBenchmarks`

Purpose: measure bulk edge resolution before and after passing `allEdgesByUuid.Values` directly as
the per-episode existing-edge override collection instead of copying dictionary values on each
episode. The override collection is consumed while the bulk episode loop is still sequential; the
canonical edge dictionary is updated only after `ResolveEntityEdgesAsync` returns.

Commands:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*BulkEdgeDedupeBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\bulk-edge-override-before-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*BulkEdgeDedupeBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\bulk-edge-override-after-2026-06-27
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

| Method | Endpoint pairs | Before Mean | After Mean | Before Allocated | After Allocated |
| --- | ---: | ---: | ---: | ---: | ---: |
| `AddEpisodeBulk_ManyEndpointPairs` | 8 | 5.918 ms | 6.321 ms | 4.52 MB | 4.52 MB |
| `AddEpisodeBulk_ManyEndpointPairs` | 16 | 19.921 ms | 16.770 ms | 10.41 MB | 10.38 MB |

Interpretation:

- Removing the per-episode dictionary-value copy trims the larger case by about 30 KB in this
  fixture; the 8-pair case rounds to the same 4.52 MB allocation total.
- ShortRun timing is noisy for this async ingestion fixture. The useful signal is the allocation
  reduction plus unchanged focused bulk and edge-merge behavior.
- If the per-episode final edge-resolution loop is parallelized later, reassess this live
  `Dictionary.ValueCollection` handoff or restore an explicit snapshot/reusable buffer.
