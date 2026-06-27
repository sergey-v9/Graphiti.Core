# LLM Clean-Input Benchmark - win-x64 - 2026-06-27

Benchmark suite: `Graphiti.Core.Benchmarks.LlmClientBenchmarks`

Purpose: measure `LlmClient.CleanInput` for prepared prompt-message content before and after replacing
the clean-path per-rune scan with a `SearchValues<char>` removable-character scan plus surrogate
validation. Dirty input still uses the existing rune-preserving cleanup path.

Commands:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*LlmClientBenchmarks*" --job Dry --noOverwrite --artifacts artifacts\benchmarks\llm-cleaninput-dry-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*LlmClientBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\llm-cleaninput-before-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*LlmClientBenchmarks*" --noOverwrite --artifacts artifacts\benchmarks\llm-cleaninput-after-2026-06-27
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
| `CleanInput_CleanAscii` | 3.967 us | 228.5 ns | 0 B | 0 B |
| `CleanInput_CleanUnicode` | 3.414 us | 309.9 ns | 0 B | 0 B |
| `CleanInput_DirtyControls` | 6.803 us | 6.941 us | 5288 B | 5288 B |

Interpretation:

- Clean prompt-message content is the expected hot path; the fast path is roughly an order of
  magnitude faster and remains allocation-free.
- Dirty cleanup still allocates the cleaned string and preserves the existing rune-based behavior.
