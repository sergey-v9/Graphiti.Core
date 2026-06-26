# Graphiti Core C# port - agent guide

This folder is a managed C# port of Python `graphiti_core/`, not a native binding layer. Python is
the behavioral source of truth; C# should be idiomatic where behavior, JSON/wire shape, prompt/schema
identity, cache semantics, and performance/allocation discipline stay compatible.

**Working notes - read first, keep current.** The main living docs under `.agents/notes/` carry
context across agent sessions: `roadmap.md` (phased long-term plan), `parity.md` (row-by-row Python
parity ground truth — update it in the same change that closes or reopens a gap), `handoff.md`
(current state and verified context), `decisions.md` (standing port/parity/design decisions), and
`evolution.md` (milestone history for major divergences from Python). Start substantive work by
reading the relevant note and update it in the same change when a standing fact changes.
LadybugDB/Kuzu has its own focused handoff in `.agents/notes/kuzu-driver-port.md`. For C# submodule
commit rules, read `.agents/notes/commit-policy.md`.

**Current priority - how to pick work.** Concrete work orders live in `.agents/plans/`; the
roadmap orders them. Pick the lowest-numbered plan with unchecked actionable items, do exactly one
item as a slice (implement → verify → commit → check it off and update `parity.md` when parity state
changes), then stop or pick the next. Phases 1–3 are complete, plan-05 A–E are complete, and plan 06
has merged the LadybugDB driver back into `Graphiti.Core`. **The current actionable plan is plan 07 —
cross-platform (Linux x64) proof (roadmap goal G1)**; do its steps in order, with the documented
non-stall fallback to G3 hot-path profiling if the native binding fix stalls. Only release
versioning/publishing remains user-gated; remaining release-infrastructure work is decision-gated or
external-feed-gated (version cadence, publish path, metapackage shape, and future Ladybug package
publication/replacement). When the open plan items are blocked on those decisions,
choose work from `roadmap.md`/`handoff.md` that directly strengthens parity, packaging verification,
upstream sync, or documented current state. Performance work is allowed only when it is
benchmark-first and parity-safe.

**No Python coupling in the code.** The product is *behavioral / wire* parity with Python (enforced by
tests, tracked in `parity.md`) — but the C# `src/` and `tests/` must not be textually welded to Python.
Never put "Python" or a `.py` file/line into an identifier, method name, **test name**, or comment;
name and comment by what the C# code *does*, not its Python provenance ("ignores property filters",
not "...LikePython"). Python mapping/provenance lives in `parity.md`, not in code; commit messages
describe the change, not "like python". Full rule: `decisions.md` "Parity without Python coupling in
the code." The code was fully de-coupled on 2026-06-18 (commit `508abf3`) — **do not re-introduce** it.

The notes can change outside your session. Re-read relevant notes before finalizing work that touches
direction, architecture, providers, verification, or roadmap items; if current notes contradict your
plan, reconcile them before editing or ask. The coordination convention lives in
`.agents/notes/handoff.md`.

Optional local .NET skills:
- If `.agents/skills/*/SKILL.md` exists in your checkout, treat those files as task-specific C#/.NET
  operating guidance. Before doing substantive work in an area covered there, read the matching
  skill and any referenced files it says are required.
- Use `run-tests` before changing `dotnet test` commands or filters. Tests use xUnit v3; do not
  apply MSTest-specific guidance unless the test framework changes. Use `test-gap-analysis`,
  `test-anti-patterns`, `assertion-quality`, or
  `coverage-analysis` only when evaluating test quality or coverage; use
  `analyzing-dotnet-performance` and `microbenchmarking` when performance claims need a systematic
  scan or BenchmarkDotNet evidence; use the MCP and NuGet skills only for those specific packaging,
  publishing, or MCP tasks.
- Apply generic skill advice through Graphiti's port contract. If a skill recommends an AI agent
  framework, vector-store abstraction, MCP pattern, package layout, or testing migration that
  conflicts with this guide or `.agents/notes/decisions.md`, follow the Graphiti-specific decision
  and update the notes if the product direction truly changes.

Never:
- Do not paraphrase, summarize, or "improve" Python prompt instruction text when porting it; port
  near-verbatim per the prompt parity contract in `.agents/notes/decisions.md`. Prompt prose is
  product behavior, not documentation.
- Do not add fallbacks that fabricate graph content when an LLM call fails or returns nothing
  (heuristic entities, synthetic edges, and similar). Surface the empty/failed result; Python does.
- Do not start a performance/allocation slice while `.agents/plans/` has open parity items, except
  to fix a measured regression you introduced.
- Do not replace Graphiti's temporal graph behavior with Semantic Kernel memory, an agent framework,
  LangChain-style memory, an OGM, or a vector DB abstraction. Adapters are fine; replacement is not.
- Do not treat a generic .NET AI/ML skill as permission to add Microsoft Agent Framework, Semantic
  Kernel, vector database abstractions, or orchestration layers to core Graphiti. Use those skills to
  inform adapter decisions only when the Graphiti boundary already calls for it.
- Do not change Python wire values, enum serialization, prompt names, response-format names, schema
  fingerprints, or LLM cache-key inputs as incidental cleanup.
- Do not move nested `Graphiti.*Response` DTOs out of their stable identity unless the cache/schema
  impact is deliberate and tested.
- Do not add native binaries, RID packaging, or provider-specific SDK dependencies beyond the current
  core graph-provider dependencies without an explicit packaging/provider decision.
- Do not reintroduce a Neo4j provider (removed 2026-06-17). Do not add FalkorDB or Neptune provider
  support unless a separate provider decision changes the C# scope.

Always:
- Treat Python `graphiti_core/` in this repo as the behavioral source of truth; keep C# idiomatic
  where behavior and wire compatibility stay intact.
- Write new code allocation-aware by default (simple loops, pre-sized collections, non-throwing
  parse paths). Existing-code performance work is allowed in Phase 5 only when benchmark-first,
  measured before/after, and demonstrably parity-safe.
- If an iteration keeps repeating the same build/test/format commands, create a small helper script
  and run that instead of manually stepping through the loop. Commit the script when it becomes useful
  workflow, and keep one-off throwaway scripts out of durable source.
- Run the full C# verification command before handing off substantive changes, unless you report why
  it could not run.
- Preserve active-driver scoping through `UseGroupDriver` / `AsyncLocal<IGraphDriver?>`.
- Keep `InMemoryGraphDriver` deterministic; it is the reference backend for tests.
- Update `.agents/notes/*` when you change a standing decision, handoff fact, roadmap item, milestone
  status, or Kuzu status.

Ask first:
- Public API shape changes, namespace moves, target framework changes, package version strategy, or
  changes to `TreatWarningsAsErrors` / analyzer enforcement.
- Changing provider status for LadybugDB/Kuzu or Neptune. LadybugDB is the planned primary provider
  and Kuzu parity lineage; see
  `.agents/notes/kuzu-driver-port.md`.
- Replacing cache, retry, telemetry, tokenizer, vector math, or graph driver infrastructure beyond
  the existing boundaries.

Primary build/test command from this folder:

```powershell
.\eng\Verify-GraphitiCore.ps1
```

Keep the latest verification checkpoint in `.agents/notes/handoff.md`; do not duplicate volatile test
counts here.

Port contract:
- Preserve Python-compatible JSON shape: snake_case properties, relaxed escaping, and wire-value enum
  converters are part of the public contract.
- Keep Graphiti ranking/search semantics custom and parity-tested: RRF, MMR, cross-encoder ordering,
  node-distance, episode-mentions, search config constants, and filter behavior are not commodity
  infrastructure.
- `Microsoft.Extensions.AI` is the adapter boundary for chat and embeddings; keep `ILlmClient`,
  `IEmbedderClient`, and `ICrossEncoderClient` as Graphiti-facing contracts.
- `HybridCache`, Polly `ResiliencePipeline<T>`, `ActivitySource`, `Meter`, `Microsoft.ML.Tokenizers`,
  and tensor primitives are infrastructure choices, not permission to change Graphiti behavior or hide
  avoidable allocations behind fashionable abstractions.
- Neo4j was removed 2026-06-17 and is no longer a C# provider.
  FalkorDB and Neptune are compatibility/helper surfaces, not supported configured C# providers today.
  LadybugDB is the core provider investment target, and the
  in-memory driver is a real deterministic test/reference backend rather than a product provider.

Generated vs hand-written:
- There is no ClangSharp/native binding generator and no checked-in generated interop layer.
- Source-generated regexes, logging, and System.Text.Json metadata are compiler-generated from
  hand-written C# source; edit the source declarations, not generated build output.

Provider/package facts:
- This is currently a managed `net10.0` library with central package versions in
  `Directory.Packages.props`.
- `Graphiti.Core` carries the graph-driver contract, deterministic InMemory reference/test driver, and
  first-class LadybugDB driver. It owns the `LadybugDB`/`LadybugDB.Native` package references,
  `LadybugDbOptions`, `AddLadybugDbGraphDriver`, and `LadybugDbGraphDriverFactory`.
- `Verify-GraphitiCore.ps1` packs the single shippable `Graphiti.Core` package and runs a fresh
  package-consumer smoke that exercises both InMemory and LadybugDB from that package. LadybugDB
  package refs restore from the `sergey-v9/ladybug-dotnet` GitHub Packages feed through source
  `github_ladybug`; restore/test runs require credentials with `read:packages`. Do not bump the
  pinned LadybugDB version without explicit user approval.
- Durable provider policy lives in `.agents/notes/decisions.md`; detailed LadybugDB state lives in
  `.agents/notes/kuzu-driver-port.md`.

Pointers:
- `.agents/notes/decisions.md` has standing port decisions and parity calls.
- `.agents/notes/evolution.md` has milestone history for major divergences from Python.
- `.agents/notes/handoff.md` has current context and known audited areas.
- `.agents/notes/roadmap.md` has follow-up work.
- `.agents/notes/kuzu-driver-port.md` is the focused LadybugDB/Kuzu handoff.
- Use repo analyzers/.editorconfig for C# style; do not restate generic .NET rules here.
