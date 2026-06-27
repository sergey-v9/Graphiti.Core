# Edge Resolution Endpoint Lookup Benchmark - win-x64 - 2026-06-27

Benchmark suite: `Graphiti.Core.Benchmarks.EdgeResolutionBenchmarks`

Purpose: measure extracted-edge candidate preparation before and after replacing per-edge exact
endpoint-name scans over the case-insensitive node map with one `StringComparer.Ordinal` lookup built
from the map's enumerated keys and current values.

Commands:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*EdgeResolutionBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\edge-resolution-before-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*EdgeResolutionBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\edge-resolution-after-2026-06-27
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

| Method | Node count | Edge count | Before Mean | After Mean | Before Allocated | After Allocated |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `BuildExtractedEdgeCandidates_ManyExactEndpoints` | 64 | 512 | 584.1 us | 187.7 us | 520.32 KB | 466.45 KB |
| `BuildExtractedEdgeCandidates_ManyExactEndpoints` | 256 | 512 | 1,640.8 us | 188.9 us | 520.32 KB | 472.52 KB |

Interpretation:

- Endpoint resolution no longer scales with `edges * nodes`; the 256-node case drops from about
  1.64 ms to 0.19 ms for the same 512 extracted edges.
- Allocation falls by about 48-54 KB in this fixture. The new one-time lookup allocation is smaller
  than the old repeated dictionary enumerator overhead across source and target endpoint checks.
- The lookup is built from enumerated map keys with `TryAdd`, preserving the exact case-sensitive
  endpoint contract and the existing `OrdinalIgnoreCase` dictionary behavior where later equal-key
  assignments update the value without replacing the originally enumerated key.
