# Plan 09 — Robustness & resilience hardening of the LLM / parse boundary

**DONE 2026-06-28.** Step 0: the HNSW gate was closed (exact cosine stays the default at current scale),
and G5 landed as `eng/Invoke-UpstreamDeltaReminder.ps1` (a committed, non-blocking upstream-delta
reminder). Then: an LLM-boundary risk map (`.agents/notes/llm-boundary-risk-map.md`), `LlmBoundaryFuzzTests`
(malformed-LLM-output fuzz coverage of the coercion layer), and `ProviderResilienceWorkflowTests`
(transient/429/empty/schema-fail-past-two-repairs/partial-batch/embedding-dimension/cross-encoder failure
modes). Step D fixed one real defect: `Graphiti` now materializes and validates missing entity node/edge
embeddings **before** driver bulk save, so a malformed provider embedding can't leave an episode
persisted with dangling entity-edge UUIDs.

Durable record: the HNSW decision in `decisions.md`, `llm-boundary-risk-map.md`, git history.
(Stub per `doc-hygiene.md`, 2026-06-28.)
