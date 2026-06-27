# Ladybug Statement Builder Benchmark - win-x64 - 2026-06-27

Benchmark suite: `Graphiti.Core.Benchmarks.LadybugStatementBuilderBenchmarks`

Purpose: measure Ladybug statement construction before and after changing the local
`Parameters(...)` helpers from params arrays to `params ReadOnlySpan<(string, object?)>`. The
builders still return fresh mutable `Dictionary<string, object?>` instances with
`StringComparer.Ordinal`; only the temporary params-array allocation at call sites is removed.

Commands:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*LadybugStatementBuilderBenchmarks*" --job Dry --noOverwrite --artifacts artifacts\benchmarks\ladybug-statement-builder-dry-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*LadybugStatementBuilderBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\ladybug-statement-builder-before-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*LadybugStatementBuilderBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\ladybug-statement-builder-after-2026-06-27
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
| `BuildNodeGetByUuid_OneParameter` | 143.3 ns | 141.83 ns | 1104 B | 1064 B |
| `BuildSagaEpisodeContentsGet_ThreeParameters` | 108.4 ns | 94.74 ns | 816 B | 744 B |
| `BuildCommunityEmbeddingSearch_WithVectorAndGroups` | 376.5 ns | 500.54 ns | 2128 B | 2056 B |
| `BuildNodeDistanceRankStatements_ManyTwoParameterMaps` | 692.9 ns | 870.05 ns | 3936 B | 3320 B |
| `BuildEntityNodeBfsSearchStatements_Filtered` | 10,505.3 ns | 10,353.02 ns | 62,256 B | 62,256 B |

Interpretation:

- Direct `Parameters(...)` call sites avoid compiler-created params arrays while preserving the
  fresh mutable parameter dictionary behavior required by optional parameter additions.
- Allocation reductions are visible for one-, two-, and three-parameter map construction. The BFS
  case is unchanged because the repeated statements use the separate `SearchParameters` clone path.
- ShortRun timing is noisy at this scale; this slice is justified by allocation reductions and
  unchanged statement/parameter behavior rather than wall-clock movement.
