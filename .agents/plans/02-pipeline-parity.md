# Plan 02 — Ingestion pipeline semantic parity

**Objective:** close the behavioral gaps between Python `add_episode`/`add_episode_bulk` and the
C# ingestion flow that remain even after prompts are ported (plan 01). Ground truth and row
statuses: `parity.md` ingestion table.

**Non-goals:** no performance work, no provider work, no public API changes without asking.

**Order matters less than in plan 01, but items 1–2 are the highest product-quality impact and
item 3 is the highest correctness impact. One item per slice; verify, commit, update `parity.md`
and check off here.**

- [x] 1. Entity summary generation. Python generates/updates entity summaries during ingestion
      (`node_operations.py:833-1000`: `_extract_entity_summaries_batch`, fact-appending for short
      summaries, `MAX_NODES`=30 batching, summary filter hook) using prompts
      `extract_nodes.extract_summaries_batch` / `extract_entity_summaries_from_episodes`. C# never
      writes `EntityNode.Summary` during ingestion, which starves search/rerankers and communities
      of signal. Port the batch flow + both prompts (extends plan 01 pattern). Watch
      `MAX_SUMMARY_CHARS` truncation parity (`utils/text_utils.py`).
      - Done 2026-06-11: `EntitySummaryService` appends short new edge facts, routes long/isolated
        summaries through 30-node LLM flights, sentence-truncates LLM summaries, supports the
        internal summary-filter / episode-prompt hooks, and is wired into single and bulk ingestion
        before graph save.
- [ ] 2. Remove invented LLM-failure fallbacks (parity + trust issue; rows in `parity.md`
      "Invented C# behaviors"):
      a. Delete `HeuristicEntityNames` fallback (EpisodeGraphExtractor.cs:104) — zero extracted
         entities is a valid result, not an error to paper over.
      b. Delete the fabricated `RELATES_TO` edge (EpisodeGraphExtractor.cs:205).
      c. Constrain `DeterministicCommunityText` fallback to the no-LLM/NotImplemented path only;
         real provider exceptions must propagate.
      Expect test fallout: tests that relied on fallbacks must switch to fake-LLM responses that
      exercise the real path. Do not keep the fallbacks "just gated" unless the user asks for an
      offline mode.
- [ ] 3. Broad edge-invalidation candidate search. Python searches beyond the immediate node pair
      for contradicted edges (`edge_operations.py:407-418` + resolve flow); C# only considers
      between-node and adjacent edges, so cross-graph contradictions are never expired. Port the
      candidate-gathering semantics (driver search reuse, limits) and add an InMemory-driver
      parity test that contradicts an edge attached to a *different* node pair.
- [ ] 4. Multi-episode attribution. Python tracks per-node/per-edge `episode_indices`
      (`node_operations.py:103-112,283-306`, `edge_operations.py` episode_attribution) and the
      prompts ask for them; C# hardcodes attribution to episode [0]. Port the attribution plumbing
      (extraction response → `node_episode_index_map` equivalent → episodic edges) — matters for
      `add_episode_bulk` correctness.
- [ ] 5. Combined extraction path. Python's newer single-call node+edge extraction
      (`utils/maintenance/combined_extraction.py`, prompt `extract_nodes_and_edges.extract_message`,
      batch timestamps via `extract_edges.extract_timestamps_batch`). Decide with the user whether
      C# adopts it as the default (as upstream is heading) or as an option; then port. Depends on
      items in plan 01 being done (prompts pattern) and benefits from item 4 (attribution).
- [ ] 6. Bulk ingestion true-batch semantics. Python `add_episode_bulk` dedupes/resolves across the
      whole batch (`bulk_utils.py`, `_extract_and_dedupe_nodes_bulk`, `dedupe_edges_bulk`); C#
      loops per-episode with an accumulated candidate set, which changes dedup outcomes and loses
      cross-episode merging. Align the flow; preserve saga watermark behavior
      (graphiti.py:1417 min-valid-at) which C# should already have — verify while there.
- [ ] 7. LLM validation-failure re-prompting. Python's retry loop appends the validation error as
      a user message and retries (`llm_client/client.py` / `openai_base_client.py:251-304`); C#
      Polly pipeline retries transport errors only, so a malformed structured response is
      terminal. Add the re-prompt-with-error retry inside `LlmClient`/`MicrosoftExtensionsAIChatClient`
      (bounded by MAX_RETRIES=2 like Python), without disturbing cache-key identity.
- [ ] 8. Decide the C#-only per-edge attribute pass (Graphiti.Ingestion.cs:103, marked DIVERGENT
      in `parity.md`): ask the user whether to keep (and document as a feature) or align with
      Python (drop from single-episode flow). Do not silently keep it.

## Done when

`parity.md` ingestion table has no MISSING rows and every PARTIAL is closed or explicitly
DIVERGENT with a decision recorded in `decisions.md`; `roadmap.md` Phase 2 marked complete.
