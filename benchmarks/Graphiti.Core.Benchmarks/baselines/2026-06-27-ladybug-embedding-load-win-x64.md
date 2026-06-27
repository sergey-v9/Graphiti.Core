# Ladybug Embedding Load Benchmark - win-x64 - 2026-06-27

Benchmark suite: `Graphiti.Core.Benchmarks.LadybugEmbeddingLoadBenchmarks`

Purpose: measure Ladybug embedding-load record mapping before and after reading the projected embedding
column directly instead of mapping a full `EntityNode` / `EntityEdge`. The Ladybug load queries already
project only `uuid` plus the embedding column, so this isolates mapper overhead avoided by the driver.

Commands:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*LadybugEmbeddingLoadBenchmarks*" --job Dry --noOverwrite --artifacts artifacts\benchmarks\ladybug-embedding-load-dry-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*LadybugEmbeddingLoadBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\ladybug-embedding-load-before-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*LadybugEmbeddingLoadBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\ladybug-embedding-load-after-2026-06-27
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

| Method | Before Mean | After Mean | Before Allocated | After Allocated |
| --- | ---: | ---: | ---: | ---: |
| `LoadEntityNodeEmbeddingFromRecord` | 795.4 ns | 108.9 ns | 1.66 KB | 312 B |
| `LoadEntityEdgeEmbeddingFromRecord` | 730.5 ns | 109.0 ns | 1.66 KB | 312 B |

Interpretation:

- Direct embedding-column reads avoid allocating and populating full model objects, labels/episode
  lists, attribute dictionaries, and date/default fields when namespace loads only need vectors.
- Query shape and vector conversion semantics stay unchanged; the driver still returns copied
  `List<float>` instances through the existing mapper list conversion.
