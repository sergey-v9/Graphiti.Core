# Search MMR Dimension Check Benchmark - win-x64 - 2026-06-27

Benchmark suite: `Graphiti.Core.Benchmarks.SearchBenchmarks.Mmr_Rerank`

Purpose: measure MMR reranking before and after hoisting vector dimension validation out of the
pairwise O(n^2) similarity loop. Candidate/query dimensions are validated once up front; empty vectors
remain zero-similarity inputs, and non-empty mismatches still throw.

Commands:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*SearchBenchmarks.Mmr_Rerank" --noOverwrite --artifacts artifacts\benchmarks\mmr-dimension-before-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*SearchBenchmarks.Mmr_Rerank" --noOverwrite --artifacts artifacts\benchmarks\mmr-dimension-after-2026-06-27
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

| Method | Candidate count | Before Mean | After Mean | Before Allocated | After Allocated |
| --- | ---: | ---: | ---: | ---: | ---: |
| `Mmr_Rerank` | 200 | 386.6 us | 379.0 us | 218.38 KB | 218.38 KB |
| `Mmr_Rerank` | 500 | 2,282.1 us | 2,222.8 us | 538.30 KB | 538.30 KB |

Interpretation:

- Allocation is unchanged because the slice removes redundant validation branches, not storage.
- ShortRun timing dropped about 2-3% in this fixture. The larger benefit is keeping the inner
  pairwise loop to zero/known-same-dimension dot products after a single validation pass.
