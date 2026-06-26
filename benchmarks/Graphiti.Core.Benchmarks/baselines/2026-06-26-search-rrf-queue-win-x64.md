# RRF Queue Capacity Benchmark - win-x64 - 2026-06-26

Benchmark suite: `Graphiti.Core.Benchmarks.SearchBenchmarks.Rrf_*`

Before command:

```powershell
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*SearchBenchmarks.Rrf_*" --noOverwrite --artifacts artifacts\benchmarks\topbyscore-queue-before-2026-06-26
```

After command:

```powershell
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*SearchBenchmarks.Rrf_*" --noOverwrite --artifacts artifacts\benchmarks\topbyscore-queue-after-2026-06-26
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

| Method | CandidateCount | Before Mean | After Mean | Before Allocated | After Allocated |
|---|---:|---:|---:|---:|---:|
| Rrf_TwoLists | 200 | 37.64 us | 36.99 us | 44.13 KB | 34.36 KB |
| Rrf_ThreeLists | 200 | 43.40 us | 41.37 us | 51.16 KB | 41.39 KB |
| Rrf_EnumerableLists | 200 | 53.94 us | 51.40 us | 56.77 KB | 47.01 KB |
| Rrf_TwoLists | 500 | 100.14 us | 101.22 us | 102.52 KB | 86.11 KB |
| Rrf_ThreeLists | 500 | 120.38 us | 103.69 us | 119.89 KB | 103.48 KB |
| Rrf_EnumerableLists | 500 | 163.56 us | 161.13 us | 123.91 KB | 107.49 KB |

Notes:

- The change pre-sizes the bounded top-k priority queue when candidate count is known.
- Allocations dropped by about 9.8 KB at 200 candidates and 16.4 KB at 500 candidates across the RRF cases.
- ShortRun timing columns are directional; the 500-candidate three-list after run was noisy.
