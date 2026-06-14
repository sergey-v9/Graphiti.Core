# Python Parity Matrix

This is the single source of truth for "what is actually ported". Update the affected row in the
same change that closes or reopens a gap. Do not claim parity from memory — verify against the
Python file before flipping a status.

**Python baseline:** `graphiti_core/` synced to upstream `origin/main` HEAD `0ed90b7` (we track HEAD,
not tagged releases). The local parent-repo mirror was advanced to match in parent commit `e36c387`
(`git checkout origin/main -- graphiti_core`), so the local `graphiti_core/` tree now equals
`origin/main`. The library tree at `0ed90b7` is identical to `ff7e29c` — every commit after `ff7e29c`
in the range touches only `mcp_server/`/`server/`/CI/deps, not the library. The five library commits
since the previous anchor (`34f56e6`) were reviewed + adversarially verified in the 2026-06-14
upstream sync below (none touched prompts/search/pipeline). **To pull the next batch, follow
`upstream-sync-procedure.md`**: diff `graphiti_core/` against this anchor, disposition each change,
verify, then move the anchor to the new `origin/main` HEAD.

**Statuses**
- `OK` — behavior and (for prompts) instruction text faithfully ported; divergences documented.
- `PARTIAL` — structure/data ported but meaningful behavior reduced; note says what is missing.
- `STUB` — placeholder stands in for real behavior. Counts as not ported.
- `MISSING` — no C# counterpart.
- `DIVERGENT` — deliberate, documented C# difference (see `decisions.md`); not a gap.
- `N/A` — intentionally out of scope for C#.

## 2026-06-13 parity-hardening pass (supervisor review of the 2026-06-11 agent work)

An adversarial Python-vs-C# review (every flagged issue independently re-checked) found the
2026-06-11 work largely faithful but surfaced real divergences the green unit suite could not see —
because the agent authored both the code and its golden tests. Fixed on four review branches and
integrated; full verification green (948 passed, 2 skipped, format/build/pack clean). What changed:

- **Prompts:** restored Unicode glyphs folded to ASCII (`°`, `≤`, `→`) and trailing newlines/blank
  lines that C# raw strings had dropped (notably the saga `EXISTING_KNOWLEDGE` blank line, reachable
  on every incremental update); fixed `dedupe_nodes` `entity_type_description` to use the first
  non-Entity label only; converted the substring-only combined-prompt golden test to full-string
  equality (it then exposed and fixed four more latent glyph/newline defects).
- **Edge resolution:** the duplicate-candidate facts shown to the resolve-edge LLM are now relevance-
  reranked and limited via `EDGE_HYBRID_SEARCH_RRF` (was raw/unlimited); timestamp prompt uses
  `episode.valid_at`; override edges no longer leak into invalidation candidates; duplicate hits
  attach only the resolution episode UUID; the serialized edge loop was restored to concurrent
  resolution (`ThrottledWork.SelectAsync`) and the test that had been loosened to hide the
  serialization was reverted to `Equal(2)`.
- **Ingestion/summary:** bulk summaries no longer append edge facts (Python `edges=None`); community
  summaries use the raw entity summary, not name-prefixed text; bulk first-pass node dedup no longer
  over-widens the candidate pool.
- **Infra:** Ladybug full-text is now verbatim-or-empty (was collapse+truncate+search); cross-encoder
  uses the primary model not the small model; empty LLM responses re-prompt instead of being swallowed;
  content-filter refusals surface as a non-retryable `LlmRefusalException`.

Deliberate divergences and tracked-but-unfixed items from this pass are recorded in `decisions.md`
("Deliberate divergences accepted…" and "Tracked-but-unfixed divergences"). The bulk row below is
flagged accordingly.

**2026-06-14 follow-up pass:** real-provider validation passed (Phase 3 gate met — see the
real-provider row); the eval harness was built to the proposal's graph-building design and run live;
and plan-04 follow-ups landed (full-string golden tests for the remaining prompts; missing-endpoint
DB fetch; dropped the fabricated `RELATES_TO` default; cross-encoder golden + smoke; log/test hygiene).
A second adversarial review of that work caught and fixed eval-prompt interior trailing spaces, F3
over-scoping by group_id, and the eval measuring retrieval-QA instead of graph-building. All
integrated; verification green (962 tests). Plans 03 and 04 are closed.

## 2026-06-14 upstream sync (anchor `34f56e6` → `origin/main` `ff7e29c`)

Reviewed the 5 `graphiti_core` commits upstream added since our anchor. **None touched
`prompts/`, `search/`, `nodes.py`, `edges.py`, or the ingestion/utils pipeline** — the
parity-critical layers are unchanged upstream, so no prompt/pipeline rows move. Per-commit
disposition:

- **`ff7e29c` fix(falkordb) default group_id `\_`→`_` (#1549) — ADOPTED.** C#
  `GraphitiHelpers.GetDefaultGroupId(FalkorDb)` returned `@"\_"`, which fails C#'s own
  `ValidateGroupId` (backslashes rejected) — the same latent bug upstream fixed. Changed to `"_"`
  (`Text/Helpers.cs`, test `GraphitiHelperTests.cs`). The RediSearch fulltext-escaping half is N/A
  (C# has no FalkorDB driver). FalkorDB stays enum/wire-compat only.
- **`f723545` feat(llm) default model `gpt-5.5` + model-tied reasoning effort (#1551) — DIVERGENT
  (deliberate).** C# keeps `LlmConfig.Model = "gpt-4.1-mini"`. Reasoning-effort/temperature-omission
  are not modeled in C# — they belong to the consumer's `Microsoft.Extensions.AI` chat client, not
  Graphiti. Copying the `gpt-5.5` default verbatim would be *harmful*: gpt-5.5 is a reasoning model,
  and without C# sending `reasoning_effort:'none'` the M.E.AI/OpenAI path would apply the API's
  *medium* default reasoning — the expensive/slow behavior Python specifically engineered around.
  `DEFAULT_SMALL_MODEL` (`gpt-4.1-nano`) is unchanged upstream and already matches
  (`CrossEncoder` DefaultModel). Recorded in `decisions.md`.
- **`c537ed4` fix(llm) generic client json_schema/json_object + EmptyResponseError retryable (#1537)
  — N/A + already-aligned.** No C# `OpenAIGenericClient` (M.E.AI is the single adapter for all
  providers); json_schema-vs-json_object/response_format/markdown-fence handling are M.E.AI concerns.
  The portable bit — treat an empty response as a retryable failure — C# already does:
  `MicrosoftExtensionsAIChatClient.ParseJsonResponse` throws on empty/whitespace and routes through
  `LlmClient.GenerateValidatedResponseWithRetryAsync`.
- **`57778eb` deprecate(kuzu) (#1548) — REJECTED (deliberate).** Upstream deprecates Kuzu because the
  *upstream Kuzu project* is unmaintained. The C# port's primary provider is **LadybugDB**, a
  maintained Kuzu-lineage engine we build/repair locally — so the deprecation rationale does not
  apply to us. No `DeprecationWarning`, no `[Obsolete]` on the Ladybug driver. (`GraphProvider.Kuzu`
  remains an `[Obsolete]` alias of `LadybugDb` for a *different*, naming reason — see plan 05 B.)
- **`ecb521d` fix(falkor) strip nul bytes from parameters (#1531) — N/A.** No FalkorDB driver in C#.

Net code change from this sync: the single one-character FalkorDB group_id fix. The parity-critical
layers needed nothing.

## Prompts (LLM instruction text)

This is the highest-risk area. The prompt prose in `graphiti_core/prompts/` is core Graphiti IP:
extraction quality with a real LLM depends on it. "The structured-output schema and JSON data
context exist" does NOT make a prompt ported — the instruction text must be ported near-verbatim
(see `decisions.md` "Prompt parity contract"). The deterministic unit suite cannot detect
prompt-quality gaps because it uses fake LLM clients.

Live Python pipeline call sites (everything else in `prompts/` is currently unused by the Python
pipeline — do not port without a reason). Entity summary prompts were closed with Plan 02 item 1;
combined-extraction prompt rows were closed with the internal combined extractor port on
2026-06-11. That path remains inactive in public ingestion because the Python baseline exposes only
an internal default-false helper flag; C# pins the same public default.

| Prompt | Python source | C# call site | Status | Notes |
|---|---|---|---|---|
| `extract_nodes.extract_message` | prompts/extract_nodes.py | EpisodeGraphExtractor → Prompts/ExtractNodesPrompts | OK | Ported 2026-06-11; golden-text tests pin content |
| `extract_nodes.extract_text` | prompts/extract_nodes.py | same | OK | same |
| `extract_nodes.extract_json` | prompts/extract_nodes.py | same | OK | same; C# previously never branched to a JSON-specific prompt |
| `extract_edges.edge` | prompts/extract_edges.py | EpisodeGraphExtractor → Prompts/ExtractEdgesPrompts | OK | Ported 2026-06-11; golden-text tests pin content |
| `extract_nodes.extract_attributes` | prompts/extract_nodes.py:383 | AttributeExtractionService → Prompts/ExtractNodesPrompts | OK | Ported 2026-06-11; golden-text tests pin HARD RULES anti-hallucination block |
| `extract_edges.extract_attributes` | prompts/extract_edges.py:181 | AttributeExtractionService → Prompts/ExtractEdgesPrompts | OK | Ported 2026-06-11; golden-text tests pin HARD RULES anti-hallucination block |
| `extract_edges.extract_timestamps` | prompts/extract_edges.py:242 | EdgeResolutionService → Prompts/ExtractEdgesPrompts | OK | Ported 2026-06-11; golden-text tests pin content |
| `extract_edges.extract_timestamps_batch` | prompts/extract_edges.py:274 | EpisodeGraphExtractor combined path → Prompts/ExtractEdgesPrompts | OK | Ported 2026-06-11 for internal combined extraction; public ingestion remains on separate extraction because Python's public `Graphiti` surface does not expose the combined helper flag |
| `dedupe_nodes.nodes` | prompts/dedupe_nodes.py:117 | NodeResolutionService → Prompts/DedupeNodesPrompts | OK | Ported 2026-06-11; golden-text tests pin content, including worked EXAMPLE block |
| `dedupe_edges.resolve_edge` | prompts/dedupe_edges.py:43 | EdgeResolutionService → Prompts/DedupeEdgesPrompts | OK | Ported 2026-06-11; golden-text tests pin duplicate/contradiction constraints |
| `extract_nodes_and_edges.extract_message` | prompts/extract_nodes_and_edges.py | EpisodeGraphExtractor combined path → Prompts/ExtractNodesAndEdgesPrompts | OK | Ported 2026-06-11; internal combined path only, matching Python's public default |
| `extract_nodes.extract_summaries_batch` | prompts/extract_nodes.py:509 | EntitySummaryService → Prompts/ExtractNodesPrompts | OK | Ported 2026-06-11; normal ingestion LLM-summary path, golden tests pin key sections |
| `extract_nodes.extract_entity_summaries_from_episodes` | prompts/extract_nodes.py:613 | EntitySummaryService → Prompts/ExtractNodesPrompts | OK | Ported 2026-06-11; internal `skip_fact_appending`/episode-summary path supported |
| `summarize_nodes.summarize_pair` | prompts/summarize_nodes.py:54 | CommunityService → Prompts/SummarizeNodesPrompts | OK | Ported 2026-06-11; sends the two source summaries as JSON like Python, deterministic text remains only no-LLM fallback |
| `summarize_nodes.summary_description` | prompts/summarize_nodes.py:119 | CommunityService → Prompts/SummarizeNodesPrompts | OK | Ported 2026-06-11; golden-text tests pin one-sentence description prompt |
| `summarize_sagas.summarize_saga` | prompts/summarize_sagas.py | SagaService → Prompts/SummarizeSagasPrompts | OK | Ported to prompt builder 2026-06-11; golden-text tests pin content, including worked examples |

Unused-in-pipeline Python prompts (verify before porting; as of the baseline these have no live
call sites): `extract_nodes.classify_nodes`, `extract_nodes.extract_summary`,
`dedupe_nodes.node`, `dedupe_nodes.node_list`, `summarize_nodes.summarize_context`, `eval.*`
(eval harness; optional, see plan 03).

## Ingestion pipeline (add_episode / add_episode_bulk)

| Step | Python | C# | Status | Notes |
|---|---|---|---|---|
| Episode bookkeeping, previous-episode window | graphiti.py | Graphiti.Ingestion.cs | OK | |
| Node extraction (LLM) | node_operations.extract_nodes | EpisodeGraphExtractor | OK | Prompts ported 2026-06-11 |
| Multi-episode node/fact attribution | node_operations.py:103-112, 283-306; edge_operations.py:170-180, 290-313 | EpisodeGraphExtractor → EpisodeAttribution → MaintenanceUtilities.BuildEpisodicEdges / EdgeResolutionService | OK | C# parses `episode_indices` for extracted nodes and facts, remaps node attribution through resolution before building episodic edges, and maps fact attribution to edge `Episodes`/`ReferenceTime`; true bulk dedupe/resolve semantics remain tracked in the bulk ingestion row |
| Node resolution: deterministic + embedding + LLM dedup | node_operations.resolve_extracted_nodes | NodeResolutionService | OK | Prompt ported 2026-06-11; deterministic, embedding, and LLM dedupe stages covered |
| Entity attribute extraction | node_operations.extract_attributes_from_nodes | AttributeExtractionService | OK | Overlay merge and anti-hallucination prompt ported 2026-06-11 |
| Entity summary generation (batch, fact-appending) | node_operations.py:833-1000 | EntitySummaryService | OK | Ported 2026-06-11; appends short new edge facts, batches 30-node LLM flights, supports internal filter/episode-prompt hooks, truncates LLM summaries |
| Edge extraction (LLM) | edge_operations.extract_edges | EpisodeGraphExtractor | OK | Prompts ported 2026-06-11 |
| Edge resolution: dedup fast-path, timestamps, contradictions | edge_operations.resolve_extracted_edge | EdgeResolutionService | OK | Prompt text ported 2026-06-11; broad candidate search remains tracked separately below |
| Broad invalidation-candidate search | edge_operations.py:407-418 | EdgeResolutionService | OK | Verified 2026-06-11; unfiltered edge hybrid search supplies invalidation candidates beyond the node pair, with regression coverage for invalidating an edge on a different target node |
| Combined node+edge extraction path | utils/maintenance/combined_extraction.py | EpisodeGraphExtractor.ExtractCombinedEpisodeGraphAsync | OK | Internal path ported 2026-06-11: single LLM call, orphan dropping, node attribution from facts, self-fact preservation, and batch timestamps. Public `Graphiti` ingestion remains on separate extraction because Python exposes `use_combined_extraction` only as an internal bulk helper flag defaulting to `False`; tests pin that `add_episode` and `add_episode_bulk` do not call the combined prompt by default |
| Edge attribute extraction during add_episode | edge_operations.resolve_extracted_edge | EdgeResolutionService | OK | Aligned 2026-06-11: structured edge attributes are extracted during edge resolution only. There is no post-resolution ingestion-stage edge attribute pass; exact duplicate reuse skips the prompt and preserves existing attributes, while non-fast-path resolution replaces/clears attributes like Python |
| Episodic edge building | edge_operations.build_episodic_edges | MaintenanceUtilities | OK | |
| Bulk ingestion (true batch dedup/resolve) | bulk_utils, graphiti.py:1230+ | Graphiti.Ingestion.cs:195+ | OK + 2 DIVERGENT | Staged extraction, cross-batch node/edge dedupe, final resolution, pointer remapping, per-episode provenance. 2026-06-13 fixes: bulk summaries no longer append edge facts (Python `edges=None`); first-pass node dedup no longer over-widens the candidate pool. Two behaviors KEPT as documented DIVERGENT (see `decisions.md`): cross-episode edge invalidation is more aggressive than Python, and bulk episodes own `episode.EntityEdges` where Python's bulk leaves it empty |
| Saga association + episode-time watermarks | graphiti.py | SagaService | OK | Watermarks present |
| Community update on ingest | graphiti.py | CommunityService | OK | Flow parity; community summary/name prompts ported 2026-06-11 |

## Invented C# behaviors (not in Python)

| Behavior | Location | Status |
|---|---|---|
| Heuristic entity names when LLM returns zero entities | EpisodeGraphExtractor.cs | REMOVED 2026-06-11; empty structured node extraction now yields zero nodes |
| Fabricated `RELATES_TO` edge between first two nodes when LLM returns zero edges | EpisodeGraphExtractor.cs | REMOVED 2026-06-11; empty structured edge extraction now yields zero edges |
| Deterministic community summary/name fallback on LLM failure | CommunityService.cs, DeterministicCommunityText | CONSTRAINED 2026-06-11; used only for `NoOpLlmClient` empty output or `NotImplementedException`, while real-client empty/malformed structured responses fail |
| Lexical-overlap `IdentityCrossEncoderClient` as default reranker | CrossEncoder/ | DIVERGENT 2026-06-11; retained as the provider-free C# constructor/DI default and documented in `decisions.md`. Real-provider hosts can opt into `MicrosoftExtensionsAICrossEncoderClient`; the OpenAI sample does |

## Search, drivers, infrastructure

| Area | Status | Notes |
|---|---|---|
| Search config recipes, reranker enums, wire values | OK | Verified equivalent, parity-tested |
| Hybrid search flow (semantic + BM25 + BFS), RRF/MMR/cross-encoder/node-distance/episode-mentions | OK | Deterministic parts well tested |
| Community label propagation | OK | Algorithmically equivalent |
| Graph drivers: InMemory (reference), LadybugDB (investment target), Neo4j (legacy reference) | OK | Runtime proof for Ladybug workflows, direct package binding of list/array/empty-list/null parameters, direct driver bulk-save embedding/relationship persistence, namespace/model embedding reloads by UUID, public namespace community/saga reads and typed deletes, saga-scoped retrieval/content reads, paged group reads, directed endpoint-pair and incident edge reads, explicit and core file-backed paths, Kuzu `':memory:'` sentinel compatibility, package/native execution, and Ladybug-owned raw full-text query/label-filter construction; see kuzu-driver-port.md |
| LLM/embedder/reranker adapters via Microsoft.Extensions.AI | DIVERGENT | Documented decision; structured output + Polly retries in place. `MicrosoftExtensionsAICrossEncoderClient` uses structured boolean+confidence scoring because generic M.E.AI lacks OpenAI top-logprob controls |
| Retry-on-validation-failure with error feedback message | llm_client/client.py retry loop | OK | Ported 2026-06-11 in base `LlmClient`: `JsonException` parse/schema failures get two Python-style validation-feedback re-prompts, cache keys remain based on the original prepared messages, and only validated final responses are cached |
| GLiNER2 local extraction client | N/A | Specialized optional Python feature; out of scope unless requested |
| Real-provider end-to-end validation | OK (PASSED 2026-06-13) | First live OpenAI run passed: both `OpenAIProviderIntegrationTests` green (all 11 structured schemas + dynamic attribute schema accepted by the real provider; real 2-episode resolved temporal graph), and the 6-episode `Graphiti.Sample.OpenAI` produced a sane graph — rich entity summaries, correct bi-temporal invalidation (blocked-fact `invalid_at` = QA-clearance date), and relevant reranked search. `gpt-4.1-mini`/`gpt-4.1-nano`/`text-embedding-3-small@1536`. Re-run via `eng/Run-OpenAIProviderValidation.ps1` (auto-loads gitignored `.env`). One extraction observation (March-15 rollout not invalidated by the reschedule) is LLM-variance, tracked for the future eval. See plan 03 |
| eval harness (eval prompts, add-episode eval) | OK (BUILT + RUN 2026-06-14) | The four `eval.py` prompts ported with full-string golden tests (`Prompts/EvalPrompts.cs`); `samples/Graphiti.Eval` implements the proposal's graph-building regression eval (mirrors Python `tests/evals/eval_e2e_graph_building.py`: candidate per-episode `AddEpisodeResults` judged vs a persisted baseline artifact via `eval_add_episode_results`) plus a fixed `--qa` retrieval mode (top-1 fact + distractor). Live: graph-building **6/6 not-worse** on identical code (judge genuinely diffs extractions); QA **3/7 honest**, distractor correctly fails. An adversarial review caught and fixed that the first cut measured retrieval-QA instead of graph-building. See plan 03 item 4 |
