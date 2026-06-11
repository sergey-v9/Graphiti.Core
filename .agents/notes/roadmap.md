# C# Port Roadmap

This roadmap is the long-term plan. It is phased, and the phases are ordered: do not start work
from a later phase while an earlier phase has open items, unless the user says otherwise. Concrete
work orders for the active phases live in `.agents/plans/`; the current parity ground truth lives
in `parity.md`. Keep completed history out of this file (`evolution.md` owns milestones).

## Where the port actually is (2026-06-11 reassessment)

The infrastructure is real and green: builds clean, deterministic tests pass, packaging works,
drivers (InMemory, LadybugDB, Neo4j-legacy) have runtime proof, search/ranking/community algorithms
have genuine parity coverage, and the live Python prompt instruction text has been ported into C#.
Phase 2 has closed the ingestion semantic gaps: entity summaries, invented fallback removal, broad
invalidation candidates, multi-episode attribution, true-batch bulk ingestion, combined extraction
internals with public separate-extraction parity, validation-failure re-prompting, and edge
attribute extraction aligned into edge resolution. Phase 3 now has a runnable OpenAI sample host,
env-gated OpenAI tests, and an opt-in M.E.AI-backed cross-encoder reranker wired into the sample,
but no end-to-end run against a real provider has passed yet. `parity.md` has the row-by-row truth.

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

## Phase 2 — Pipeline semantic parity (COMPLETE 2026-06-11)

Closed 2026-06-11. Work order: `.agents/plans/02-pipeline-parity.md`. Entity summary generation,
removal/constraining of invented LLM-failure fallbacks, broad invalidation-candidate search,
multi-episode attribution, combined extraction internals, true-batch bulk ingestion,
validation-failure re-prompting, and edge attribute extraction alignment are complete. Public
ingestion intentionally stays on separate extraction because Python's public `Graphiti` API does
not expose the internal default-false combined helper flag.

## Phase 3 — Real-provider validation (ACTIVE; provider run pending)

Prove the library end-to-end with a real LLM + embedder. Work order:
`.agents/plans/03-provider-validation.md`. A sample OpenAI host, env-gated OpenAI integration
tests, and an opt-in M.E.AI cross-encoder now exist, but the port still has not been run
successfully against a real LLM/embedding/reranking provider. A port of an LLM-driven library that
has never talked to an LLM is unverified by definition; this phase is the acceptance test for
Phases 1–2. The optional eval harness has a proposal in
`.agents/notes/eval-harness-proposal.md`, but implementation requires explicit user approval.

Done when: an env-gated integration test (or sample app run) ingests episodes through a real
provider, produces a graph whose entities/edges/summaries are sane on manual inspection, and hybrid
search returns relevant results; findings are recorded in `parity.md`/`handoff.md`.

## Phase 4 — LadybugDB productization

Existing direction, unchanged: LadybugDB is the provider investment target. Remaining work lives in
`kuzu-driver-port.md` (final naming decision, conditional native/CI smoke tests, remaining
Kuzu→LadybugDB terminology transition). Active Ladybug full-text and label-filter behavior now lives
inside `Drivers/Ladybug/`; shared Kuzu branches remain for compatibility callers only. Neo4j removal
is a user decision; do not remove without asking.

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
