# C# Port Roadmap

This roadmap is the long-term plan. It is phased, and the phases are ordered: do not start work
from a later phase while an earlier phase has open items, unless the user says otherwise. Concrete
work orders for the active phases live in `.agents/plans/`; the current parity ground truth lives
in `parity.md`. Keep completed history out of this file (`evolution.md` owns milestones).

## Where the port actually is (2026-06-11 reassessment)

The infrastructure is real and green: builds clean, deterministic tests pass, packaging works,
drivers (InMemory, LadybugDB, Neo4j-legacy) have runtime proof, search/ranking/community algorithms
have genuine parity coverage. What is NOT done is the LLM-facing semantic layer: most prompt
instruction text was never ported (one-line system messages + raw JSON context stand in for
Python's engineered prompts), entity summaries are never generated, combined extraction is absent,
and no end-to-end run against a real LLM provider has ever happened. A library in this state
produces a structurally valid but semantically poor graph with a real LLM. Closing that gap is the
whole near-term roadmap. `parity.md` has the row-by-row truth.

## Performance/allocation moratorium

Performance and allocation work is **paused** until Phases 1–3 are complete. Exception: fixing a
measured regression introduced by your own change. The previous "treat allocations as first-class"
guidance produced an unbounded stream of micro-optimization slices on a library whose semantic
layer was still hollow; that ordering was wrong. Modernization/polish resumes, with measurement
discipline, in Phase 5.

## Phase 1 — Prompt parity for existing C# call sites (COMPLETE 2026-06-11)

Port the instruction text of every live Python prompt into `src/Graphiti.Core/Prompts/`, with
golden-text tests pinning the rendered output. Work order: `.agents/plans/01-prompt-parity.md`.

Complete for every existing C# prompt call site: prompt builders live in `Prompts/`, services no
longer inline prompt text, and golden tests exist for each builder. Remaining `MISSING` prompt rows
in `parity.md` are tied to absent pipeline features (entity summary generation and combined
extraction) and are owned by Phase 2.

## Phase 2 — Pipeline semantic parity (ACTIVE)

Close the behavioral gaps in ingestion that survive even with good prompts. Work order:
`.agents/plans/02-pipeline-parity.md`. Entity summary generation, removal/constraining of invented
LLM-failure fallbacks, broad invalidation-candidate search, and multi-episode attribution are
closed as of 2026-06-11. Combined extraction internals are ported but not activated. Remaining
headline items: combined extraction activation decision/wiring, bulk true-batch semantics,
validation-failure re-prompting in the LLM client.

Done when: the ingestion-pipeline table in `parity.md` has no `MISSING` rows and every `PARTIAL`
is either closed or converted to a documented `DIVERGENT` decision.

## Phase 3 — Real-provider validation (BLOCKED on 1, can start once 1 lands)

Prove the library end-to-end with a real LLM + embedder. Work order:
`.agents/plans/03-provider-validation.md`. A port of an LLM-driven library that has never talked
to an LLM is unverified by definition; this phase is the acceptance test for Phases 1–2.

Done when: an env-gated integration test (or sample app run) ingests episodes through a real
provider, produces a graph whose entities/edges/summaries are sane on manual inspection, and hybrid
search returns relevant results; findings are recorded in `parity.md`/`handoff.md`.

## Phase 4 — LadybugDB productization

Existing direction, unchanged: LadybugDB is the provider investment target. Remaining work lives in
`kuzu-driver-port.md` (naming decision, native-gated smoke tests, moving provider-specific query
behavior into the driver, Kuzu→LadybugDB terminology transition). Neo4j removal is a user
decision; do not remove without asking.

## Phase 5 — Release readiness

API surface review and freeze, XML docs for the public surface, README/samples for external
consumers, packaging/versioning decision (2.0.0 line), measured performance pass (benchmarks
first, then optimize hot paths that benchmarks justify), CI story.

## Standing direction (unchanged)

- LadybugDB is the main provider target; InMemory is the deterministic reference/test driver;
  Neo4j is legacy reference only; FalkorDB/Neptune are enum/wire compatibility surfaces.
- Search stays custom and parity-tested: RRF, MMR, cross-encoder ordering, node-distance,
  episode-mentions, filters, BFS, result merge.
- `Microsoft.Extensions.AI` remains the chat/embedding adapter boundary; `ILlmClient`,
  `IEmbedderClient`, `ICrossEncoderClient` remain the Graphiti-facing contracts.
- Keep custom: episode ingestion workflow, temporal invalidation, graph driver contract, search
  merge semantics, prompt/result DTO wire compatibility with Python.
- Do not add Lucene.NET as a default core dependency; do not replace Graphiti with agent-framework
  memory abstractions (see `decisions.md` Replacement Policy).

## Future decisions to revisit

- Final LadybugDB provider naming beyond the `GraphProvider.Kuzu` compatibility value.
- Whether to expose external adapters (OpenAI, Azure OpenAI, Qdrant, Semantic Kernel) as separate
  packages.
- Whether to add a compatibility option defaulting chunking to Python's chars-per-token heuristic.
- Whether Neo4j removal becomes its own milestone.
