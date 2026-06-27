# MMR Merge Benchmark - win-x64 - 2026-06-27

Benchmark suite: `Graphiti.Core.Benchmarks.SearchBenchmarks.Mmr_MergeCandidatesInFirstSeenOrder`

Before command:

```powershell
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*SearchBenchmarks.Mmr_MergeCandidatesInFirstSeenOrder*" --noOverwrite --artifacts artifacts\benchmarks\mmr-merge-before-2026-06-27
```

After command:

```powershell
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*SearchBenchmarks.Mmr_MergeCandidatesInFirstSeenOrder*" --noOverwrite --artifacts artifacts\benchmarks\mmr-merge-after-2026-06-27
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
| 200 | 29.28 us | 17.49 us | 28.7 KB | 20.56 KB |
| 500 | 85.71 us | 48.68 us | 72.03 KB | 51.66 KB |

Notes:

- The change keeps the MMR/cross-encoder candidate merge results in first-seen order while a
  dictionary maps candidate keys to result indexes for max-score updates. It removes the extra
  value-list allocation and sort previously used to restore first-seen order.
- This is a local Windows directional before/after run, not a release-grade performance claim.
