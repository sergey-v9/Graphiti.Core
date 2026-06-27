# LLM PrepareMessages Benchmark - win-x64 - 2026-06-27

Benchmark suite: `Graphiti.Core.Benchmarks.LlmClientBenchmarks.PrepareMessages_CleanNoSchema`

Purpose: measure the clean prompt-message preparation path before and after removing the eager
per-message `Message` clone. `Message` is an immutable sealed record, and `PrepareMessages` still
returns a new prepared list; unchanged message records can be shared until a `with` replacement is
needed for language/schema/preamble/cleaning changes.

Commands:

```powershell
dotnet build benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -c Release --verbosity minimal
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*LlmClientBenchmarks.PrepareMessages_CleanNoSchema" --job Dry --noOverwrite --artifacts artifacts\benchmarks\llm-preparemessages-dry-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*LlmClientBenchmarks.PrepareMessages_CleanNoSchema" --noOverwrite --artifacts artifacts\benchmarks\llm-preparemessages-before-2026-06-27
dotnet run -c Release --no-build --project benchmarks\Graphiti.Core.Benchmarks\Graphiti.Core.Benchmarks.csproj -- --filter "*LlmClientBenchmarks.PrepareMessages_CleanNoSchema" --noOverwrite --artifacts artifacts\benchmarks\llm-preparemessages-after-2026-06-27
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
| `PrepareMessages_CleanNoSchema` | 628.8 ns | 498.6 ns | 800 B | 672 B |

Interpretation:

- Removing the eager record clone saves one `Message` allocation for each unchanged prompt message.
- The prepared list is still newly allocated, and messages that receive language/schema/preamble or
  cleanup changes are still replaced with new immutable records.
