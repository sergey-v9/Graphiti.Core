# InMemory Scale Benchmark Baseline - win-x64 - 2026-06-28

Benchmark suite: `Graphiti.Core.Benchmarks.InMemoryScaleBenchmarks`

Commands:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*InMemoryScaleBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\plan11-scale-2026-06-28
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*InMemoryScaleBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\plan11-scale-after-2026-06-28
```

Dry validation:

```powershell
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*InMemoryScaleBenchmarks*" --job Dry --noOverwrite --artifacts artifacts\benchmarks\plan11-scale-after-dry
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

Before:

| Method | NodeCount | EdgeCount | Mean | Error | StdDev | Gen0 | Gen1 | Gen2 | Allocated |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `SearchEdgeHybridRrf` | 10000 | 30000 | 75.275 ms | 8.1169 ms | 2.1079 ms | 1285.7143 | 1142.8571 | - | 22.96 MB |
| `SearchNodeVectorTopK` | 10000 | 30000 | 4.603 ms | 0.5351 ms | 0.1390 ms | 42.9688 | 11.7188 | 3.9063 | 1.01 MB |
| `SearchEdgeVectorTopK` | 10000 | 30000 | 16.334 ms | 1.0058 ms | 0.2612 ms | 62.5000 | 31.2500 | 31.2500 | 3.79 MB |
| `SearchEdgeHybridMmr_Limit100` | 10000 | 30000 | 79.466 ms | 11.1024 ms | 2.8833 ms | 1333.3333 | 833.3333 | - | 23.43 MB |
| `IngestBulkIntoLargeGraph` | 10000 | 30000 | 4,685.209 ms | 354.2777 ms | 54.8249 ms | 372000.0000 | 112000.0000 | 5000.0000 | 4717.65 MB |

After skipping endpoint-node lookup for edge filters that do not request node labels:

| Method | NodeCount | EdgeCount | Mean | Error | StdDev | Gen0 | Gen1 | Gen2 | Allocated |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `SearchEdgeHybridRrf` | 10000 | 30000 | 45.198 ms | 4.8541 ms | 0.7512 ms | 1333.3333 | 1250.0000 | - | 22.11 MB |
| `SearchNodeVectorTopK` | 10000 | 30000 | 1.687 ms | 0.0501 ms | 0.0130 ms | 42.9688 | 11.7188 | 3.9063 | 1.01 MB |
| `SearchEdgeVectorTopK` | 10000 | 30000 | 8.591 ms | 0.7187 ms | 0.1866 ms | 46.8750 | 15.6250 | 15.6250 | 3.37 MB |
| `SearchEdgeHybridMmr_Limit100` | 10000 | 30000 | 45.000 ms | 3.7521 ms | 0.9744 ms | 1363.6364 | 909.0909 | - | 22.59 MB |
| `IngestBulkIntoLargeGraph` | 10000 | 30000 | 3,407.165 ms | 68.8226 ms | 17.8730 ms | 408000.0000 | 120000.0000 | 6000.0000 | 5111.75 MB |

Notes:

- The retained implementation change is internal to InMemory/materialized edge search: when
  `CompiledSearchFilter.RequiresEndpointNodeLookup` is false, edge search now evaluates the edge
  predicate directly and skips building/loading endpoint node dictionaries. Node-label-constrained edge
  filters continue using the existing endpoint-node lookup path.
- The direct large-N edge-search wins are clear: edge hybrid RRF improved from 75.275 ms to 45.198 ms,
  edge vector from 16.334 ms to 8.591 ms, and edge hybrid MMR from 79.466 ms to 45.000 ms at
  10000 nodes / 30000 edges.
- Full-scan exact cosine did not dominate at this target size. The HNSW gate remains closed: exact
  node-vector search is within budget at 10000 nodes, and edge-vector search is not the dominant
  end-to-end cost after the endpoint-lookup fix.
- Treat the unmodified node-vector movement as contextual ShortRun noise, not as a claimed node-vector
  optimization. Future claims should compare against fresh same-machine before/after runs.
