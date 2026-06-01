# Graphiti Core C# port - agent guide

This folder is a managed C# port of Python `graphiti_core/`, not a native binding layer. Python is
the behavioral source of truth; C# should be idiomatic where behavior, JSON/wire shape, prompt/schema
identity, cache semantics, and performance/allocation discipline stay compatible.

**Working notes - read first, keep current.** The main living docs under `.agents/notes/` carry
context across agent sessions: `roadmap.md` (phase status / what's left), `handoff.md` (current state
and verified context), and `decisions.md` (standing port/parity/design decisions). Start substantive
work by reading the relevant note and update it in the same change when a standing fact changes.
LadybugDB/Kuzu has its own focused handoff in `.agents/notes/kuzu-driver-port.md`. If the C# port
tree is still broadly uncommitted, read `.agents/notes/commit-recovery.md` before continuing.

The notes can change outside your session. Re-read relevant notes before finalizing work that touches
direction, architecture, providers, verification, or roadmap items; if current notes contradict your
plan, reconcile them before editing or ask. The coordination convention lives in
`.agents/notes/handoff.md`.

Optional local .NET skills:
- If `.agents/skills/*/SKILL.md` exists in your checkout, treat those files as task-specific C#/.NET
  operating guidance. Before doing substantive work in an area covered there, read the matching
  skill and any referenced files it says are required.
- Use `run-tests` before changing `dotnet test` commands or filters; use `writing-mstest-tests` for
  MSTest authoring; use `test-gap-analysis`, `test-anti-patterns`, `assertion-quality`, or
  `coverage-analysis` only when evaluating test quality or coverage; use
  `analyzing-dotnet-performance` and `microbenchmarking` when performance claims need a systematic
  scan or BenchmarkDotNet evidence; use the MCP and NuGet skills only for those specific packaging,
  publishing, or MCP tasks.
- Apply generic skill advice through Graphiti's port contract. If a skill recommends an AI agent
  framework, vector-store abstraction, MCP pattern, package layout, or testing migration that
  conflicts with this guide or `.agents/notes/decisions.md`, follow the Graphiti-specific decision
  and update the notes if the product direction truly changes.

Never:
- Do not replace Graphiti's temporal graph behavior with Semantic Kernel memory, an agent framework,
  LangChain-style memory, an OGM, or a vector DB abstraction. Adapters are fine; replacement is not.
- Do not treat a generic .NET AI/ML skill as permission to add Microsoft Agent Framework, Semantic
  Kernel, vector database abstractions, or orchestration layers to core Graphiti. Use those skills to
  inform adapter decisions only when the Graphiti boundary already calls for it.
- Do not change Python wire values, enum serialization, prompt names, response-format names, schema
  fingerprints, or LLM cache-key inputs as incidental cleanup.
- Do not move nested `Graphiti.*Response` DTOs out of their stable identity unless the cache/schema
  impact is deliberate and tested.
- Do not add native binaries, RID packaging, or provider-specific SDK dependencies to core without an
  explicit packaging/provider decision.
- Do not spend more effort improving Neo4j or FalkorDB unless it is needed to preserve existing
  behavior, validate shared abstractions, or unblock LadybugDB work.

Always:
- Treat Python `graphiti_core/` in this repo as the behavioral source of truth; keep C# idiomatic
  where behavior and wire compatibility stay intact.
- Treat allocation behavior as part of idiomatic C# design. Prefer simple loops, pre-sized
  collections, spans, source generation, and non-throwing parse paths in hot/shared code when they
  preserve clarity; be skeptical of LINQ chains, closure captures, regex/split arrays, boxing, and
  extra materialization on frequently executed paths.
- Run the full C# verification command before handing off substantive changes, unless you report why
  it could not run.
- Preserve active-driver scoping through `UseGroupDriver` / `AsyncLocal<IGraphDriver?>`.
- Keep `InMemoryGraphDriver` deterministic; it is the reference backend for tests.
- Update `.agents/notes/*` when you change a standing decision, handoff fact, roadmap item, or Kuzu
  status.

Ask first:
- Public API shape changes, namespace moves, target framework changes, package version strategy, or
  changes to `TreatWarningsAsErrors` / analyzer enforcement.
- Changing provider status for LadybugDB/Kuzu or Neptune. LadybugDB is the planned primary provider
  and Kuzu parity lineage; see
  `.agents/notes/kuzu-driver-port.md`.
- Replacing cache, retry, telemetry, tokenizer, vector math, or graph driver infrastructure beyond
  the existing boundaries.

Verified build/test command from this folder:

```powershell
dotnet test "Graphiti.Core.CSharp.slnx" --verbosity minimal
```

Verified on 2026-06-01 after proving the runtime-backed Ladybug direct triplet workflow:
restore/format/build succeeded with 0 warnings, 826 tests passed, and
`Graphiti.Core` packed as `2.0.0-alpha.1`.

Port contract:
- Preserve Python-compatible JSON shape: snake_case properties, relaxed escaping, and wire-value enum
  converters are part of the public contract.
- Keep Graphiti ranking/search semantics custom and parity-tested: RRF, MMR, cross-encoder ordering,
  node-distance, episode-mentions, search config constants, and filter behavior are not commodity
  infrastructure.
- `Microsoft.Extensions.AI` is the adapter boundary for chat and embeddings; keep `ILlmClient`,
  `IEmbedderClient`, and `ICrossEncoderClient` as Graphiti-facing contracts.
- `HybridCache`, Polly `ResiliencePipeline<T>`, `ActivitySource`, `Microsoft.ML.Tokenizers`, and
  tensor primitives are infrastructure choices, not permission to change Graphiti behavior or hide
  avoidable allocations behind fashionable abstractions.
- Existing Neo4j/FalkorDB behavior may stay as reference coverage, but new provider investment should
  focus on LadybugDB first.

Generated vs hand-written:
- There is no ClangSharp/native binding generator and no checked-in generated interop layer.
- Source-generated regexes, logging, and System.Text.Json metadata are compiler-generated from
  hand-written C# source; edit the source declarations, not generated build output.

Provider/package facts:
- This is currently a managed `net10.0` library with central package versions in
  `Directory.Packages.props`.
- The NuGet package currently packs the C# README and managed assemblies; no `runtimes/{rid}/native`
  asset layout is defined.
- Current durable provider priority: LadybugDB is the main/default target, implemented via the
  LadybugDB NuGet package while preserving Kuzu parity; InMemory and Neo4j can remain as reference
  backends; FalkorDB is not an improvement target; Neptune is enum/wire compatibility only.

Pointers:
- `.agents/notes/decisions.md` has standing port decisions and parity calls.
- `.agents/notes/handoff.md` has current context and known audited areas.
- `.agents/notes/roadmap.md` has follow-up work.
- `.agents/notes/kuzu-driver-port.md` is the focused LadybugDB/Kuzu handoff.
- Use repo analyzers/.editorconfig for C# style; do not restate generic .NET rules here.
