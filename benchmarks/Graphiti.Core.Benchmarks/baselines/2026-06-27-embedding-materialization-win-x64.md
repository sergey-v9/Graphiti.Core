# Embedding Materialization Benchmark - win-x64 - 2026-06-27

Benchmark suite: `Graphiti.Core.Benchmarks.EmbeddingBenchmarks`

Purpose: measure embedding-vector copy/validation before and after filling pre-sized `List<float>`
backing storage via `CollectionsMarshal`, and measure the Microsoft.Extensions.AI adapter after
returning its validated ordered array directly instead of copying it into a second output list.

Commands:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*EmbeddingBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\embedding-materialization-before-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*EmbeddingBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\embedding-materialization-after-2026-06-27
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

| Method | Dimension | Batch count | Before Mean | After Mean | Before Allocated | After Allocated |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `MaterializeSingle_ReadOnlyMemory` | 256 | 32 | 265.7 ns | 186.9 ns | 1.05 KB | 1.05 KB |
| `MaterializeSingle_IReadOnlyList` | 256 | 32 | 342.4 ns | 235.7 ns | 1.05 KB | 1.05 KB |
| `MicrosoftExtensionsAI_CreateBatch` | 256 | 32 | 20,104.9 ns | 8,901.6 ns | 38.67 KB | 38.30 KB |
| `MaterializeSingle_ReadOnlyMemory` | 1024 | 32 | 1,001.3 ns | 633.9 ns | 4.05 KB | 4.05 KB |
| `MaterializeSingle_IReadOnlyList` | 1024 | 32 | 1,281.9 ns | 871.7 ns | 4.05 KB | 4.05 KB |
| `MicrosoftExtensionsAI_CreateBatch` | 1024 | 32 | 46,479.2 ns | 33,590.1 ns | 134.67 KB | 134.37 KB |

Interpretation:

- Vector materialization still allocates the required defensive `List<float>` copy, but filling the
  sized backing storage avoids repeated `Add` bookkeeping and drops copy/validation time by about
  29-38% in this fixture.
- The Microsoft.Extensions.AI batch path preserves provider-vector copy isolation, ordering, and
  missing-vector validation while avoiding the second output list. The small allocation drop is the
  removed list object/backing array; most allocation remains the required per-vector defensive copies.
