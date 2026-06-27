# C# Port Roadmap

This roadmap is the long-term plan. It is phased, and the phases are ordered: do not start work
from a later phase while an earlier phase has open items, unless the user says otherwise. Concrete
work orders for the active phases live in `.agents/plans/`; the current parity ground truth lives
in `parity.md`. Keep completed history out of this file (`evolution.md` owns milestones).

## Resolved scope decisions (Sergey, 2026-06-17)

These were taken by the agent ahead of sign-off; Sergey has now ruled on them:

1. **CI — KEEP AS-IS, DO NOT EXPAND.** Plan 06 retired the old core-only lane, leaving the full
   authenticated verifier lane. Plan 07 added a separately gated linux-x64 LadybugDB extension smoke;
   keep it behind `GRAPHITI_ENABLE_LINUX_LADYBUG_SMOKE=1` unless Sergey asks to make it unconditional.
2. **LadybugDB feed — GITHUB PACKAGES ONLY.** Keep `NuGet.config` pointed at the
   `sergey-v9/ladybug-dotnet` GitHub Packages feed; do NOT re-add a local offline fallback. A
   `read:packages` credential for source `github_ladybug` is required for any full (Ladybug-inclusive)
   restore, locally and in CI. This is intentional.
3. **Neo4j — DONE, removed 2026-06-17.** The `Neo4jGraphDriver` and its helpers, the
   `GraphProvider.Neo4j` enum member, the `uri`/`user`/`password` constructor parameters,
   `GraphitiOptions.Uri`/`User`/`Password`, the `Neo4j.Driver` package reference, and all Neo4j tests
   are gone; the public-API baseline was regenerated and the parity matrix and docs were updated.
4. **Merge Ladybug into Core — DONE 2026-06-26.** LadybugDB is first-class and now lives inside
   `Graphiti.Core` under `src/Graphiti.Core/Drivers/Ladybug/`. Accepted consequence: Core depends on
   the LadybugDB packages + `github_ladybug` feed and cannot publish to nuget.org until LadybugDB is
   public there (fine for the private-fork workflow). Details in plan 06 + `kuzu-driver-port.md`.
5. **Self-service bindings (standing).** `sergey-v9/ladybug-dotnet` is our fork: a capability the
   LadybugDB engine has but the C# bindings lack can be implemented in `tools/csharp_api`, pushed to the
   fork (builds a new dev package), and consumed by bumping the pin. Supersedes "do not push remotely."

Still user-gated (do not self-authorize): **release publishing / versioning** of the Graphiti packages
themselves (2.0.0 line, alpha→beta cadence, metapackage shape).

## Where the port actually is (2026-06-11 reassessment)

The infrastructure is real and green: builds clean, deterministic tests pass, packaging works,
LadybugDB has runtime proof as the provider target, InMemory is proven as the reference/test backend,
and the search/ranking/community algorithms have genuine parity coverage. The live Python prompt
instruction text has been ported into C#.
Phase 2 has closed the ingestion semantic gaps: entity summaries, invented fallback removal, broad
invalidation candidates, multi-episode attribution, true-batch bulk ingestion, combined extraction
internals with public separate-extraction parity, validation-failure re-prompting, and edge
attribute extraction aligned into edge resolution. Phase 3 now has a runnable OpenAI sample host,
env-gated OpenAI tests, and an opt-in M.E.AI-backed cross-encoder reranker wired into the sample,
but no end-to-end run against a real provider has passed yet. `parity.md` has the row-by-row truth.

A supervisor-driven adversarial parity review on 2026-06-13 (the 2026-06-11 work was self-tested, so
golden tests could not catch transcription/logic drift) found and fixed real divergences in edge
resolution (duplicate-candidate reranking, concurrency restoration), bulk ingestion (summary edge
facts, candidate-pool widening), community summaries, the cross-encoder model, LLM empty/refusal
handling, several prompts, and the Ladybug full-text query. Integrated green (948 tests). Two bulk
behaviors were kept as documented DIVERGENT and a set of low/latent items recorded as tracked — see
`decisions.md`. This hardens Phases 1–2.

**Phase 3 acceptance gate MET 2026-06-13.** The first live OpenAI run passed: both env-gated
integration tests (all structured schemas accepted by the real provider; a real resolved temporal
graph) and the 6-episode sample (rich entity summaries, correct bi-temporal invalidation, relevant
reranked search). Re-runnable via `eng/Run-OpenAIProviderValidation.ps1` (auto-loads a gitignored
`.env`). See plan 03 "Live validation result" and `evolution.md` M3.

**Phase 3 fully complete 2026-06-14.** The eval harness (plan 03 item 4) was built to the proposal's
graph-building regression design (mirrors Python `eval_e2e_graph_building.py`) and run live: 6/6
no-regression on identical code via the `eval_add_episode_results` judge, plus a fixed retrieval-QA
mode (3/7 honest, distractor correctly fails). Plan 04 follow-ups also landed. With Phases 1–3 done,
the **performance moratorium is lifted** — a first benchmark-first pass already landed two measured,
parity-safe wins (RRF pre-sizing, span-token scoring); future perf work stays evidence-driven
(BenchmarkDotNet before/after) per Phase 5.

## Performance/allocation moratorium

Performance and allocation work is **paused** until Phases 1–3 are complete. Exception: fixing a
measured regression introduced by your own change. The previous "treat allocations as first-class"
guidance produced an unbounded stream of micro-optimization slices on a library whose semantic
layer was still hollow; that ordering was wrong. Modernization/polish resumes, with measurement
discipline, in Phase 5.

## Phase 1 — Prompt parity for existing C# call sites (COMPLETE 2026-06-11)

Port the instruction text of every live Python prompt into `src/Graphiti.Core/Prompts/`, with
golden-text tests pinning the rendered output.

Complete for every existing C# prompt call site: prompt builders live in `Prompts/`, services no
longer inline prompt text, and golden tests exist for each builder. Remaining `MISSING` prompt rows
in `parity.md` are tied to absent pipeline features (entity summary generation and combined
extraction) and are owned by Phase 2.

## Phase 2 — Pipeline semantic parity (COMPLETE 2026-06-11)

Closed 2026-06-11. Entity summary generation,
removal/constraining of invented LLM-failure fallbacks, broad invalidation-candidate search,
multi-episode attribution, combined extraction internals, true-batch bulk ingestion,
validation-failure re-prompting, and edge attribute extraction alignment are complete. Public
ingestion intentionally stays on separate extraction because Python's public `Graphiti` API does
not expose the internal default-false combined helper flag.

## Phase 3 — Real-provider validation (COMPLETE 2026-06-14; acceptance gate met 2026-06-13, eval harness run live)

Prove the library end-to-end with a real LLM + embedder. A sample OpenAI host, env-gated OpenAI
integration tests, and an opt-in M.E.AI cross-encoder exist, and the port has been run successfully
against a real LLM/embedding/reranking provider. This phase is the acceptance test for Phases 1–2. The
eval harness was approved, built (`samples/Graphiti.Eval`), and run live 2026-06-14. (Caveat: that live
run is a single local Windows pass, not a continuous CI signal — see Goal G2 below.)

Done when: an env-gated integration test (or sample app run) ingests episodes through a real
provider, produces a graph whose entities/edges/summaries are sane on manual inspection, and hybrid
search returns relevant results; findings are recorded in `parity.md`/`handoff.md`.

## Phase 4 — LadybugDB productization

Existing direction, unchanged: LadybugDB is the provider investment target. Remaining work lives in
`kuzu-driver-port.md` (conditional native/CI smoke tests and release packaging). The final provider
naming decision is complete: `GraphProvider.LadybugDb` is driver-facing, and `GraphProvider.Kuzu` is
only an `[Obsolete]` compatibility alias. Active Ladybug full-text and label-filter behavior now lives
inside `Drivers/Ladybug/`; direct package parameter binding is covered through the local repaired
LadybugDB package family; shared Kuzu branches were retired from the generic search helpers. Neo4j was
removed 2026-06-17 and is no longer a provider.

**DONE 2026-06-26:** plan 06 reversed the plan-05 E split. `Graphiti.Core` now owns the LadybugDB
driver, package refs, `AddLadybugDbGraphDriver`, `LadybugDbOptions`, and factory. The two-assembly API
snapshot, core-only verifier, and core-only CI lane were retired. Accepted consequence: every restore
needs the private `github_ladybug` feed credential until LadybugDB is public on nuget.org. Provider
context remains in `kuzu-driver-port.md`.

## Phase 5 — Release readiness (IN PROGRESS)

Landed 2026-06-14: XML docs across the consumer-facing public surface, with the shippable packages now
generating IntelliSense XML documentation; a public-API **snapshot test**
(`tests/Graphiti.Core.Tests/Api/`, via `PublicApiGenerator`) that fails CI on accidental API drift;
and a consumer `README.md` + `docs/search.md`. A first benchmark-first perf pass already landed
(two measured parity-safe wins).

Plan `.agents/plans/05-release-readiness.md` steps **A–E are COMPLETE (2026-06-14)**, integrated and
green. Plan 06 later merged the LadybugDB driver back into Core, so the verifier now packs the single
`Graphiti.Core` package and the fresh temp package consumer exercises both InMemory and LadybugDB:
surface
hardening (A), `GraphProvider.LadybugDb`/`AddGraphiti` with obsolete aliases (B+C), InMemory-default
constructor + `AddEpisodeOptions` (D), and the historical LadybugDB package split (E.1+E.3), which
plan 06 has now reversed. The public-API snapshot and package-readiness tests guard the merged
single-assembly shape.

Remaining (release infra): Step F's plan-folder sweep is recorded in plan 05 and stays ahead of both
the approved plan-06 Ladybug merge and the user-gated release decisions. Anything newly found in `.agents/plans/` or
directly linked notes should be split into its own parity/provider/perf/docs slice before versioning or
publishing work. E.2 is complete: Graphiti points at the `sergey-v9/ladybug-dotnet` GitHub Packages
feed and pins the fork-published `0.17.1-dev.2.1.g53e5ab5` LadybugDB package family; full local
verification requires a NuGet credential for source `github_ladybug` with `read:packages`.
**Versioning** (2.0.0 line / alpha→beta cadence), publish path, and metapackage shape remain
decision-gated. CI has the full Ladybug-inclusive Windows lane running `eng\Verify-GraphitiCore.ps1`
with authenticated GitHub Packages restore; the former core-only lane was retired by plan 06. NuGet
metadata, README packing, XML docs, symbol package generation, and package-consumption smoke checks
are present for the merged package. The "Stable public API release"
candidate milestone in `evolution.md` is the target. A WS-1 audit on 2026-06-14 found local LadybugDB
`0.17.1` artifacts with the needed binding and Unix-loader repairs; the 2026-06-17 bump now uses the
fork workflow's published dev package version rather than those local artifacts. The public-API
snapshot stays a drift guard (not a freeze); surface changes regenerate the baseline.

NOTE for future parallel batches: do NOT run multiple worktree agents' `dotnet test` concurrently —
the LadybugDB native package serializes poorly across worktrees and deadlocks. Stagger the test step
or have agents build-only and run the consolidated test centrally.

## Long-term goals — active development (set 2026-06-19 from the full-project review)

Phases 1–3 (parity) are done and the deterministic suite is green; the port is faithful and mature.
The forward agenda is **productionization and confidence**, not more parity micro-slices. Ordered by
value. Each is a stream, not a one-slice; verify centrally, keep docs lean, don't drift into the
user-gated items.

- **G1 — Cross-platform proof (HIGH): DONE 2026-06-26.** The linux-x64 failure was reproduced as an
  FTS extension undefined-symbol error under `~/.lbdb/extension`, classified as a `ladybug-dotnet`
  package runtime-asset loader gap, fixed in `W:\code\ladybug\tools\csharp_api` commit `53e5ab5`, and
  consumed through fork package `0.17.1-dev.2.1.g53e5ab5`. Graphiti now has a gated linux-x64
  `fts`+`vector` CREATE/QUERY smoke in `.github/workflows/full.yml`; win-x64 remains the unconditional
  full verifier lane.
- **G2 — Continuous quality, not one-shot (HIGH): DONE 2026-06-26.** A dedicated
  `.github/workflows/live-provider.yml` workflow now runs on `workflow_dispatch` and a weekly schedule,
  requires `OPENAI_API_KEY`, runs `eng/Run-OpenAIProviderValidation.ps1`, and then runs
  `samples/Graphiti.Eval` in fail-loud mode against the committed
  `samples/Graphiti.Eval/baselines/baseline_graph_results.json` graph-building baseline plus the
  retrieval-QA mode. Normal PR CI remains unchanged: the full verifier still lets OpenAI provider tests
  skip cleanly when no key is present.
- **G3 — Performance & allocation program (HIGH, evidence-driven; IN PROGRESS).** Moratorium lifted.
  The first baseline slice landed 2026-06-26: `IngestionBenchmarks` covers single ingestion, sequential
  six-episode ingestion, true-bulk six-episode ingestion, and bulk-ingest-then-search with
  `[MemoryDiagnoser]`, plus a committed win-x64 ShortRun baseline. A measured BM25 allocation win also
  landed 2026-06-26 using `SearchBenchmarks.Bm25_Rank` before/after, followed by a measured uncached LLM
  cache-key allocation win, a small bulk throttling list-copy cleanup using `IngestionBenchmarks`
  before/after, an RRF bounded-queue allocation win using `SearchBenchmarks.Rrf_*`, and a TextScorer
  short-query allocation win using `SearchBenchmarks.TextScorer_ScoreAll`. A serialization baseline
  for cache-key/payload JSON paths was also committed. An InMemory full-scan node-vector-search
  baseline landed 2026-06-27, covering the reference driver's O(n) cosine path with
  `[MemoryDiagnoser]`. A bulk edge-dedupe public-workflow baseline also landed 2026-06-27; endpoint
  bucketing was measured there but not kept because the same workflow showed no material win.
  Remaining work:
  use the vector baseline to decide whether full-scan cosine needs optimization at target graph size,
  and land only measured, parity-safe wins (BenchmarkDotNet before/after).
  This program also
  *gates* the deferred
  opt-in HNSW vector tier (G-future) — only pursue HNSW if the bench shows full-scan cosine is the
  bottleneck at the target graph size, and keep exact cosine the default.
- **G4 — Observability + consumer DX (DONE 2026-06-26).** `GraphitiTelemetry` exposes a public `Meter`
  next to the existing `ActivitySource`; metrics cover episodes ingested, ingestion/search duration,
  ingestion and search result counts, LLM tokens, and LLM response-cache hit/miss lookups. `docs/observability.md`
  documents OTLP wiring and cache hit-rate calculation, `samples/Graphiti.Sample.Observability` wires
  OTLP export in a host-owned sample, `samples/Graphiti.Sample.Quickstart` is the no-key hello graph,
  and `samples/Graphiti.Sample.GenericProvider` proves the `Microsoft.Extensions.AI` boundary without
  OpenAI. Core remains free of exporter/provider SDK dependencies.
- **G5 — Sustained upstream parity (MED, cheap insurance).** Keep tracking `getzep/graphiti` via
  `upstream-sync-procedure.md`; add a low-cost recurring reminder that runs `Check-PythonUpstreamDelta`
  and surfaces a *non-blocking* notification on delta, so new library commits get triaged promptly
  without expanding CI.
- **G6 — Release readiness (publish is USER-GATED).** The **non-gated** surface work is complete
  (2026-06-27): `.agents/plans/08-release-surface-finalization.md` settled the remaining alpha
  public-surface decisions (kept `CommunityEdgeNamespace.SaveBulkAsync`; added attribute
  `MaxLength`/`Required` metadata), froze the public API snapshot, audited package/RID truth, and ran a
  release-readiness pack + fresh-consumer dry run. Versioning/publish remain gated: do that **only when
  Sergey initiates** — no `<Version>` stamp or package push has been performed.

Standing principle: continue **bounded** adversarial parity hardening (only real, reachable divergences
verified against the Python source — not speculative churn), and keep the docs lean (the matrix +
decisions are the durable record; the changelog lives in git).

## Standing direction (unchanged)

- **Tracking upstream Python:** we follow `getzep/graphiti` `origin/main` HEAD (not tagged releases),
  mirroring `graphiti_core/` only. To pull a new batch, follow the repeatable
  `.agents/notes/upstream-sync-procedure.md` (delta → classify → incorporate → other-provider
  adaptation check → verify centrally → adversarial audit → record → advance the local pointer). The
  current sync point is in `parity.md`'s anchor.
- LadybugDB is the main provider target; InMemory is the deterministic reference/test driver;
  Neo4j was removed 2026-06-17 and is no longer a provider; FalkorDB/Neptune are enum/wire
  compatibility surfaces.
- Search stays custom and parity-tested: RRF, MMR, cross-encoder ordering, node-distance,
  episode-mentions, filters, BFS, result merge.
- `Microsoft.Extensions.AI` remains the chat/embedding adapter boundary; `ILlmClient`,
  `IEmbedderClient`, `ICrossEncoderClient` remain the Graphiti-facing contracts.
- Keep custom: episode ingestion workflow, temporal invalidation, graph driver contract, search
  merge semantics, prompt/result DTO wire compatibility with Python.
- Do not add Lucene.NET as a default core dependency; do not replace Graphiti with agent-framework
  memory abstractions (see `decisions.md` Replacement Policy).

## Future decisions to revisit

- Whether to expose external adapters (OpenAI, Azure OpenAI, Qdrant, Semantic Kernel) as separate
  packages.
- Whether to add a compatibility option defaulting chunking to Python's chars-per-token heuristic.
