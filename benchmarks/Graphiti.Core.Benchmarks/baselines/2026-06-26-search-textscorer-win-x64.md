# TextScorer Search Benchmark - win-x64 - 2026-06-26

Benchmark suite: `Graphiti.Core.Benchmarks.SearchBenchmarks.TextScorer_ScoreAll`

Before command:

```powershell
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*SearchBenchmarks.TextScorer_ScoreAll*" --noOverwrite --artifacts artifacts\benchmarks\textscorer-before-2026-06-26
```

After command:

```powershell
dotnet run -c Release --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*SearchBenchmarks.TextScorer_ScoreAll*" --noOverwrite --artifacts artifacts\benchmarks\textscorer-after-2026-06-26
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

| CandidateCount | Before Mean | After Mean | Before Allocated | After Allocated |
|---:|---:|---:|---:|---:|
| 200 | 239.7 us | 216.7 us | 55.84 KB | 0 B |
| 500 | 617.9 us | 555.7 us | 139.29 KB | 0 B |

Notes:

- The change tracks distinct query-term matches with a 64-bit mask for normal short queries and keeps
  the previous hash-set fallback for very large query term sets.
- This is a local Windows directional before/after run, not a release-grade performance claim.
