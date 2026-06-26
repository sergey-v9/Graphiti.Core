# Serialization Benchmark Baseline - win-x64 - 2026-06-26

Benchmark suite: `Graphiti.Core.Benchmarks.SerializationBenchmarks`

Command:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*SerializationBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\serialization-2026-06-26
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

| Method | Mean | Error | StdDev | Gen0 | Gen1 | Allocated |
|---|---:|---:|---:|---:|---:|---:|
| `CacheKey_SerializeHashHex` | 3,127.3 ns | 97.24 ns | 25.25 ns | 0.2937 | 0.0038 | 3.63 KB |
| `ResponsePayload_Serialize` | 1,099.1 ns | 69.13 ns | 17.95 ns | 0.1535 | - | 1.89 KB |
| `ResponsePayload_Parse` | 1,271.8 ns | 50.80 ns | 13.19 ns | 0.1383 | - | 1.7 KB |
| `ResponsePayload_DeepClone` | 874.0 ns | 72.65 ns | 18.87 ns | 0.2394 | 0.0019 | 2.94 KB |

Notes:

- This is a local Windows directional baseline for the prompt/cache-key and JSON payload
  serialization paths, not a release-grade performance claim.
- No implementation change was made in this slice; compare future serialization changes against a
  fresh same-machine before/after run.
