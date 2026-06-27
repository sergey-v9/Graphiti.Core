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

**Latest upstream check:** 2026-06-26 `.\eng\Check-PythonUpstreamDelta.ps1 -Fetch -FailOnDelta`
found `origin/main` at `413b9b2e140e22f4a6d155b30ddc9779a3d47fe2`; `git log`, `git diff --stat`, and
`git diff --name-status` over `0ed90b7..origin/main -- graphiti_core` were empty. No new Python
library work needs porting.

**Statuses**
- `OK` — behavior and (for prompts) instruction text faithfully ported; divergences documented.
- `PARTIAL` — structure/data ported but meaningful behavior reduced; note says what is missing.
- `STUB` — placeholder stands in for real behavior. Counts as not ported.
- `MISSING` — no C# counterpart.
- `DIVERGENT` — deliberate, documented C# difference (see `decisions.md`); not a gap.
- `N/A` — intentionally out of scope for C#.

## 2026-06-13 parity-hardening pass + follow-ups (summary)

An adversarial Python-vs-C# review of the 2026-06-11 agent work (every flagged issue independently
re-checked) found that work largely faithful but surfaced real divergences the green unit suite could
not see — because the agent authored both the code and its golden tests. Those were fixed across
review branches and integrated, then extended by a long run of single-slice parity follow-ups
(2026-06-14 … 2026-06-19) covering prompts, edge resolution, ingestion/summary, search ranking and
filters, drivers, namespaces, and infrastructure. Each follow-up landed as its own verified slice; the
full suite stayed green throughout (current authoritative count in `handoff.md`).

The slice-by-slice narration that used to live here has been collapsed — git history holds it. The
durable outcomes are already captured where they belong: the per-area parity status lives in the
matrix tables below, and the list of deliberate/accepted divergences and the tracked-but-unfixed
items live in `decisions.md` ("Deliberate divergences accepted…", "Tracked-but-unfixed divergences",
and the per-area decision entries). For why a specific row diverges, read `decisions.md`; for what is
ported, read the tables below.

## 2026-06-14 upstream sync (anchor `34f56e6` → `origin/main` `0ed90b7`)

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
| `extract_nodes.extract_message` | prompts/extract_nodes.py | EpisodeGraphExtractor → Prompts/ExtractNodesPrompts | OK | Ported 2026-06-11; golden-text tests pin content, including custom entity types, previous messages, and post-example custom instructions |
| `extract_nodes.extract_text` | prompts/extract_nodes.py | same | OK | same |
| `extract_nodes.extract_json` | prompts/extract_nodes.py | same | OK | same; C# previously never branched to a JSON-specific prompt |
| `extract_edges.edge` | prompts/extract_edges.py | EpisodeGraphExtractor → Prompts/ExtractEdgesPrompts | OK | Ported 2026-06-11; golden-text tests pin content |
| `extract_nodes.extract_attributes` | prompts/extract_nodes.py:383 | AttributeExtractionService → Prompts/ExtractNodesPrompts | OK | Ported 2026-06-11; golden-text tests pin HARD RULES anti-hallucination block |
| `extract_edges.extract_attributes` | prompts/extract_edges.py:181 | AttributeExtractionService → Prompts/ExtractEdgesPrompts | OK | Ported 2026-06-11; golden-text tests pin HARD RULES anti-hallucination block |
| `extract_edges.extract_timestamps` | prompts/extract_edges.py:242 | EdgeResolutionService → Prompts/ExtractEdgesPrompts | OK | Ported 2026-06-11; golden-text tests pin content |
| `extract_edges.extract_timestamps_batch` | prompts/extract_edges.py:274 | EpisodeGraphExtractor combined path → Prompts/ExtractEdgesPrompts | OK | Ported 2026-06-11 for internal combined extraction; public ingestion remains on separate extraction because Python's public `Graphiti` surface does not expose the combined helper flag |
| `dedupe_nodes.nodes` | prompts/dedupe_nodes.py:117 | NodeResolutionService → Prompts/DedupeNodesPrompts | OK | Ported 2026-06-11; golden-text tests pin content, including worked EXAMPLE block and first-non-`Entity` type-description fallback |
| `dedupe_edges.resolve_edge` | prompts/dedupe_edges.py:43 | EdgeResolutionService → Prompts/DedupeEdgesPrompts | OK | Ported 2026-06-11; golden-text tests pin duplicate/contradiction constraints |
| `extract_nodes_and_edges.extract_message` | prompts/extract_nodes_and_edges.py | EpisodeGraphExtractor combined path → Prompts/ExtractNodesAndEdgesPrompts | OK | Ported 2026-06-11; internal combined path only, matching Python's public default |
| `extract_nodes.extract_summaries_batch` | prompts/extract_nodes.py:509 | EntitySummaryService → Prompts/ExtractNodesPrompts | OK | Ported 2026-06-11; normal ingestion LLM-summary path, golden tests pin key sections |
| `extract_nodes.extract_entity_summaries_from_episodes` | prompts/extract_nodes.py:613 | EntitySummaryService → Prompts/ExtractNodesPrompts | OK | Ported 2026-06-11; internal `skip_fact_appending`/episode-summary path supported; full-string tests pin optional entity-type-description section placement, including empty-string descriptions |
| `summarize_nodes.summarize_pair` | prompts/summarize_nodes.py:54 | CommunityService → Prompts/SummarizeNodesPrompts | OK | Ported 2026-06-11; sends the two source summaries as JSON like Python, deterministic text remains only no-LLM fallback |
| `summarize_nodes.summary_description` | prompts/summarize_nodes.py:119 | CommunityService → Prompts/SummarizeNodesPrompts | OK | Ported 2026-06-11; golden-text tests pin one-sentence description prompt |
| `summarize_sagas.summarize_saga` | prompts/summarize_sagas.py | SagaService → Prompts/SummarizeSagasPrompts | OK | Ported to prompt builder 2026-06-11; golden-text tests pin content, including worked examples. 2026-06-14 follow-up aligns and full-string-pins existing-summary section truthiness for whitespace-only summaries. 2026-06-16 follow-up pins empty typed summaries as persisted empty strings, matching Python |

Unused-in-pipeline Python prompts (verify before porting; as of the baseline these have no live
call sites): `extract_nodes.classify_nodes`, `extract_nodes.extract_summary`,
`dedupe_nodes.node`, `dedupe_nodes.node_list`, `summarize_nodes.summarize_context`, `eval.*`
(eval harness; optional, see plan 03).

## Ingestion pipeline (add_episode / add_episode_bulk)

| Step | Python | C# | Status | Notes |
|---|---|---|---|---|
| Episode bookkeeping, previous-episode window | graphiti.py | Graphiti.Ingestion.cs | OK | Public workflow coverage pins both explicit existing-episode UUID reuse (stored episode content/source drive extraction, replacement call fields ignored like Python) and explicit `previousEpisodeUuids` overriding the automatic recent-context window |
| Node extraction (LLM) | node_operations.extract_nodes | EpisodeGraphExtractor | OK | Prompts ported 2026-06-11 |
| Multi-episode node/fact attribution | node_operations.py:103-112, 283-306; edge_operations.py:170-180, 290-313 | EpisodeGraphExtractor → EpisodeAttribution → MaintenanceUtilities.BuildEpisodicEdges / EdgeResolutionService | OK | C# parses `episode_indices` for extracted nodes and facts, maps fact attribution to edge `Episodes`/`ReferenceTime` using Python's first-raw-index `reference_time` rule, and now keeps node attribution keyed to extracted-node UUIDs like Python; resolved-node UUID mismatches therefore fall back to all provided episodes |
| Node resolution: deterministic + embedding + LLM dedup | node_operations.resolve_extracted_nodes | NodeResolutionService | OK | Prompt ported 2026-06-11; deterministic, embedding, and LLM dedupe stages covered. 2026-06-17: label promotion now mirrors Python `_promote_resolved_node`, adding extracted specific labels only when the matched canonical node is still generic `Entity`. 2026-06-26: repeated extracted-node slots that resolve to the same canonical UUID stay repeated in result nodes and episode mentions |
| Entity attribute extraction | node_operations.extract_attributes_from_nodes | AttributeExtractionService | OK | Overlay merge and anti-hallucination prompt ported 2026-06-11. Nodes without an applicable declared-attribute schema are cleared to an empty attribute map without prompting. Per-field `MaxLength` and required-field over-cap retention are exposed through `EntityAttributeDefinition` and honored by the shared attribute merger |
| Entity summary generation (batch, fact-appending) | node_operations.py:833-1000 | EntitySummaryService | OK | Ported 2026-06-11; appends short new edge facts, batches 30-node LLM flights, supports internal filter/episode-prompt hooks, truncates LLM summaries |
| Edge extraction (LLM) | edge_operations.extract_edges | EpisodeGraphExtractor + EdgeResolutionService | OK | Prompts ported 2026-06-11; public `AddEpisodeAsync` coverage pins Python's separate-extraction self-edge drop after source/target names resolve to the same node UUID and exact endpoint-name validation before UUID resolution |
| Edge resolution: dedup fast-path, timestamps, contradictions | edge_operations.resolve_extracted_edge | EdgeResolutionService | OK | Prompt text ported 2026-06-11; broad candidate search remains tracked separately below |
| Broad invalidation-candidate search | edge_operations.py:407-418 | EdgeResolutionService | OK | Verified 2026-06-11; unfiltered edge hybrid search supplies invalidation candidates beyond the node pair, with regression coverage for invalidating an edge on a different target node |
| Combined node+edge extraction path | utils/maintenance/combined_extraction.py | EpisodeGraphExtractor.ExtractCombinedEpisodeGraphAsync | OK | Internal path ported 2026-06-11: single LLM call, orphan dropping, node attribution from facts, self-fact preservation, and batch timestamps. 2026-06-18 follow-up: combined prompt fact types render only when both fact types and a type map are supplied. Public `Graphiti` ingestion remains on separate extraction because Python exposes `use_combined_extraction` only as an internal bulk helper flag defaulting to `False`; tests pin that `add_episode` and `add_episode_bulk` do not call the combined prompt by default |
| Edge attribute extraction during add_episode | edge_operations.resolve_extracted_edge | EdgeResolutionService | OK | Aligned 2026-06-11: structured edge attributes are extracted during edge resolution only. There is no post-resolution ingestion-stage edge attribute pass; exact duplicate reuse skips the prompt and preserves existing attributes, while non-fast-path resolution replaces/clears attributes like Python. Per-field `MaxLength` and required-field over-cap retention use the same `EntityAttributeDefinition` metadata and shared merger as entity attributes |
| Episodic edge building | edge_operations.build_episodic_edges | MaintenanceUtilities | OK | |
| Bulk ingestion (true batch dedup/resolve) | bulk_utils, graphiti.py:1230+ | Graphiti.Ingestion.cs:195+ | OK + DIVERGENT | Staged extraction, cross-batch node/edge dedupe, final resolution, pointer remapping, per-episode provenance. 2026-06-13 fixes: bulk summaries no longer append edge facts (Python `edges=None`). 2026-06-18 fix: first-pass and final node resolution no longer widen null override candidate pools beyond semantic hits. Behaviors KEPT as documented DIVERGENT (see `decisions.md`): cross-episode edge invalidation is more aggressive than Python, bulk episodes own `episode.EntityEdges` where Python's bulk leaves it empty, and `storeRawEpisodeContent: false` also scrubs stored bulk episode content after extraction |
| Saga association + episode-time watermarks | graphiti.py | SagaService | OK | Watermarks present. Name-based existing saga association uses Python's minimal `_get_or_create_saga` projection before save |
| Community update on ingest | graphiti.py | CommunityService | OK | Flow parity; community summary/name prompts ported 2026-06-11; blank entity summaries are preserved when summarized into communities |

## Public Graphiti workflows

| Workflow | Python | C# | Status | Notes |
|---|---|---|---|---|
| Lifecycle | `close` | `CloseAsync` / `DisposeAsync` | DIVERGENT | C# closes only owned drivers; explicit/DI drivers are caller/container-owned |
| Episode retrieval | `retrieve_episodes` | `RetrieveEpisodesAsync` | OK | Saga-scoped InMemory retrieval follows membership rows directly, including linked episodes from other groups and duplicate membership rows |
| Communities | `build_communities` | `BuildCommunitiesAsync` | OK | Community summary reduction preserves raw entity summaries, including blank strings, like Python. Omitted group IDs discover all entity groups, including the default empty-string group; explicit `[]` clears existing communities and builds none like Python. Explicit group order and intra-group read order are preserved for returned communities and membership edges |
| Basic fact search | `search` | `SearchAsync(query, ...)` | OK | |
| Advanced graph search | `search_` | `SearchAdvancedAsync` / `SearchAsync(query, SearchConfig, ...)` | OK | Idiomatic C# names; Python-style aliases intentionally not added |
| Episode contribution lookup | `get_nodes_and_edges_by_episode` | `GetNodesAndEdgesByEpisodeAsync` | OK + DIVERGENT | Per-episode entity-edge loads use bounded fan-out like Python `semaphore_gather`, then flatten in episode order. Bulk episodes own entity-edge UUIDs in C#, so bulk episode contribution lookup is more complete than Python |
| Triplet ingest | `add_triplet` | `AddTripletAsync` | OK | Exact duplicate reuse scans the reranked/limited related-edge set after `EDGE_HYBRID_SEARCH_RRF`, matching Python; same-type entity-edge UUID collisions on different endpoint pairs generate a fresh UUID and preserve the original edge; cross-type in-memory edge UUID collisions preserve the submitted EntityEdge UUID like Python labels/relationship types |
| Episode removal | `remove_episode` | `RemoveEpisodeAsync` | DIVERGENT | C# prunes shared edge support and repairs saga membership/adjacency; Python only deletes first-supporting edges and the episode |

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
| Search config recipes, reranker enums, wire values | OK + DIVERGENT | Verified equivalent, parity-tested. Numeric knobs follow Python's lazy-use shape: inactive `sim_min_score`/`mmr_lambda`/`bfs_max_depth` values are not rejected up front, and `limit=0` returns empty results. C# recipe properties return fresh configs instead of Python's mutable module-level singleton recipes; see `decisions.md` |
| Hybrid search flow (semantic + BM25 + BFS), RRF/MMR/cross-encoder/node-distance/episode-mentions | OK + DIVERGENT | Deterministic parts well tested; edge/episode cross-encoder candidate windows now match Python's pre-rerank `limit` slices, including first-seen retrieval-order edge windowing, while node/community remain intentionally unwindowed like Python and pass full first-seen retrieval-order pools. Duplicate cross-encoder passages now match Python's first-seen passage / last-duplicate-candidate mapping. Community search now mirrors Python's unconditional vector retrieval when a query vector is available. Empty `EdgeTypes`/`EdgeUuids` filters are active match-none predicates like Python. `SearchFilters.PropertyFilters` remains on the DTO for Python wire-shape parity but is ignored by backend query construction and in-memory/materialized matching, matching Python's current filter constructors. Public search-result context helpers are exposed via `SearchHelpers`. C# skips custom driver BFS calls for null/empty origins or nonpositive depth; built-in results remain aligned |
| Community label propagation | OK | Algorithmically equivalent |
| Graph drivers: LadybugDB (first-class investment target), InMemory (reference/test); FalkorDB/Neptune (enum/wire-compat only) | OK | Runtime proof for Ladybug workflows, direct package binding of list/array/empty-list/null parameters, Python-compatible scoped clear-data handling (null clears all, empty lists preserve records, group-scoped clear leaves Saga nodes while deleting Entity/Episodic/Community nodes and incident edges), driver-level `DeleteAllIndexesAsync` surface with provider no-op behavior for LadybugDB/Kuzu and InMemory, direct driver bulk-save embedding/relationship persistence, namespace/model embedding reloads by UUID, public namespace community/saga reads and typed deletes, saga-scoped retrieval/content reads, paged/default-empty group reads, directed endpoint-pair and incident entity-edge reads, InMemory concrete node/edge type UUID boundaries and Python-style typed endpoint-gated edge saves, explicit and core file-backed paths, Kuzu `':memory:'` sentinel compatibility, package/native execution, and Ladybug-owned raw full-text query/label-filter construction; Neo4j was removed 2026-06-17 and is no longer a provider; see kuzu-driver-port.md |
| LLM/embedder/reranker adapters via Microsoft.Extensions.AI | DIVERGENT | Documented decision; structured output + Polly retries in place. `HashEmbedder` and `IdentityCrossEncoderClient` are the provider-free constructor/DI defaults instead of Python's implicit OpenAI-backed defaults. Provider-backed C# embedders reject output-count mismatches, dimension mismatches, and non-finite vectors at the adapter boundary instead of slicing/forwarding malformed vectors. `MicrosoftExtensionsAICrossEncoderClient` uses structured boolean+confidence scoring because generic M.E.AI lacks OpenAI top-logprob controls. The M.E.AI chat parser strips only wrapping markdown fences and does not extract JSON embedded in prose |
| Retry-on-validation-failure with error feedback message | llm_client/client.py retry loop | OK | Ported 2026-06-11 in base `LlmClient`: `JsonException` parse/schema failures get two Python-style validation-feedback re-prompts, cache keys remain based on the original prepared messages, `promptName` stays telemetry/usage metadata rather than cache identity, and only validated final responses are cached. 2026-06-18 follow-up: token usage is recorded only for the validated live response, not refused/malformed/schema-invalid attempts |
| GLiNER2 local extraction client | N/A | Specialized optional Python feature; out of scope unless requested |
| Real-provider end-to-end validation | OK (PASSED 2026-06-13) | First live OpenAI run passed: both `OpenAIProviderIntegrationTests` green (all 11 structured schemas + dynamic attribute schema accepted by the real provider; real 2-episode resolved temporal graph), and the 6-episode `Graphiti.Sample.OpenAI` produced a sane graph — rich entity summaries, correct bi-temporal invalidation (blocked-fact `invalid_at` = QA-clearance date), and relevant reranked search. `gpt-4.1-mini`/`gpt-4.1-nano`/`text-embedding-3-small@1536`. Re-run via `eng/Run-OpenAIProviderValidation.ps1` (auto-loads gitignored `.env`). One extraction observation (March-15 rollout not invalidated by the reschedule) is LLM-variance, tracked for the future eval. See plan 03 |
| eval harness (eval prompts, add-episode eval) | OK (BUILT + RUN 2026-06-14) | The four `eval.py` prompts ported with full-string golden tests (`Prompts/EvalPrompts.cs`); `samples/Graphiti.Eval` implements the proposal's graph-building regression eval (mirrors Python `tests/evals/eval_e2e_graph_building.py`: candidate per-episode `AddEpisodeResults` judged vs a persisted baseline artifact via `eval_add_episode_results`) plus a fixed `--qa` retrieval mode (top-1 fact + distractor). Live: graph-building **6/6 not-worse** on identical code (judge genuinely diffs extractions); QA **3/7 honest**, distractor correctly fails. An adversarial review caught and fixed that the first cut measured retrieval-QA instead of graph-building. See plan 03 item 4 |
