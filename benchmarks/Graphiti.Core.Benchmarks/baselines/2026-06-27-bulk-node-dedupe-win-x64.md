# Bulk Node Dedupe Benchmark - win-x64 - 2026-06-27

Benchmark suite: `Graphiti.Core.Benchmarks.BulkEdgeDedupeBenchmarks`

Purpose: measure bulk ingestion before and after indexing canonical nodes by normalized name during
final bulk node dedupe. The change replaces repeated scans of `canonicalNodes.Values` and repeated
`CopyDictionaryValues(canonicalNodes)` fallback snapshots with a first-admission normalized-name
lookup plus a canonical node list in insertion order.

Commands:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*BulkEdgeDedupeBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\bulk-node-dedupe-before-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*BulkEdgeDedupeBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\bulk-node-dedupe-after-2026-06-27
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
| `AddEpisodeBulk_ManyEndpointPairs` | 8 | 5.499 ms | 5.535 ms | 4.52 MB | 4.46 MB |
| `AddEpisodeBulk_ManyEndpointPairs` | 16 | 7.684 ms | 9.012 ms | 10.31 MB | 10.05 MB |

Interpretation:

- Allocation dropped by about 60 KB at 8 endpoint pairs and 260 KB at 16 endpoint pairs in this
  fixture. The remaining bulk edge work dominates wall-clock time, and ShortRun timing is noisy.
- The normalized-name index is first-admission only (`TryAdd`) so first input order remains the
  canonical winner when later extracted names normalize to the same key.
