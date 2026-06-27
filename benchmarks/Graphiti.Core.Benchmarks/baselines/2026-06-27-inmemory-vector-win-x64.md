# InMemory Vector Search Benchmark Baseline - win-x64 - 2026-06-27

Benchmark suite: `Graphiti.Core.Benchmarks.InMemoryVectorSearchBenchmarks`

Command:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*InMemoryVectorSearchBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\inmemory-vector-2026-06-27
```

Dry validation:

```powershell
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*InMemoryVectorSearchBenchmarks*" --job Dry --noOverwrite --artifacts artifacts\benchmarks\inmemory-vector-dry-2026-06-27
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

| Method | CandidateCount | Mean | Error | StdDev | Gen0 | Gen1 | Allocated |
|---|---:|---:|---:|---:|---:|---:|---:|
| `SearchEntityNodesByEmbedding_TopK` | 500 | 104.5 us | 1.05 us | 0.16 us | 7.1411 | 0.6714 | 87.82 KB |
| `SearchEntityNodesByEmbedding_TopK` | 2000 | 387.4 us | 7.39 us | 1.14 us | 19.5313 | 3.6621 | 240.48 KB |

Notes:

- This benchmark covers the deterministic InMemory reference driver's full-scan node-vector search,
  including filter checks, top-k selection, and final-hit cloning.
- No implementation change was made in this slice. Use fresh same-machine before/after runs before
  claiming a vector-search optimization.
