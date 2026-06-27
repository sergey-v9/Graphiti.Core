# Entity Dedupe MinHash Benchmark - win-x64 - 2026-06-27

Benchmark suite: `Graphiti.Core.Benchmarks.EntityDeduplicationBenchmarks`

Purpose: measure high-entropy entity-node fuzzy deduplication before and after encoding each shingle
to UTF-8 once per MinHash signature. The hash payload remains exactly `"{seed}:{shingleUtf8Bytes}"`;
only repeated per-seed UTF-8 shingle encoding was removed.

Commands:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*EntityDeduplicationBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\entity-dedupe-minhash-before-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*EntityDeduplicationBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\entity-dedupe-minhash-after2-2026-06-27
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

| Method | Node count | Before Mean | After Mean | Before Allocated | After Allocated |
| --- | ---: | ---: | ---: | ---: | ---: |
| `Resolve_FuzzyEntityNames` | 64 | 4.308 ms | 4.044 ms | 1.47 MB | 1.47 MB |
| `Resolve_FuzzyEntityNames` | 192 | 19.822 ms | 18.856 ms | 5.18 MB | 5.18 MB |

Interpretation:

- Time dropped about 5-6% with unchanged allocation in this fixture.
- A heap-cached byte-array variant was rejected during measurement because it increased allocations
  despite a small timing win. The committed version uses a per-shingle stack buffer scoped to a helper
  call and keeps the existing MinHash/LSH bucket values intact.
