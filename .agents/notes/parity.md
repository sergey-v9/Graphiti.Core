# Python Parity Matrix

This is the single source of truth for "what is actually ported". Update the affected row in the
same change that closes or reopens a gap. Do not claim parity from memory — verify against the
Python file before flipping a status.

**Python baseline:** `graphiti_core/` in the parent repo at commit `7514b44` (2026-05-20,
"Forward-port: attribute-hallucination guards, combined-extraction precision, saga episode-time
watermarks"). When the parent repo pulls newer upstream changes, diff `graphiti_core/` against this
baseline, add rows or reopen statuses for anything affected, then move the anchor.

**Statuses**
- `OK` — behavior and (for prompts) instruction text faithfully ported; divergences documented.
- `PARTIAL` — structure/data ported but meaningful behavior reduced; note says what is missing.
- `STUB` — placeholder stands in for real behavior. Counts as not ported.
- `MISSING` — no C# counterpart.
- `DIVERGENT` — deliberate, documented C# difference (see `decisions.md`); not a gap.
- `N/A` — intentionally out of scope for C#.

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
| Bulk ingestion (true batch dedup/resolve) | bulk_utils, graphiti.py:1230+ | Graphiti.Ingestion.cs:195+ | OK | Ported 2026-06-11; staged extraction, first-pass node resolution against live graph, directed node UUID maps, cross-batch node/edge dedupe, final node/edge resolution, pointer remapping, and per-episode provenance are covered |
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
| Graph drivers: InMemory (reference), LadybugDB (investment target), Neo4j (legacy reference) | OK | Runtime proof for Ladybug workflows, saga-scoped retrieval/content reads, paged group reads, directed endpoint-pair and incident edge reads, explicit and core file-backed paths, Kuzu `':memory:'` sentinel compatibility, package/native execution, and Ladybug-owned raw full-text query/label-filter construction; see kuzu-driver-port.md |
| LLM/embedder/reranker adapters via Microsoft.Extensions.AI | DIVERGENT | Documented decision; structured output + Polly retries in place. `MicrosoftExtensionsAICrossEncoderClient` uses structured boolean+confidence scoring because generic M.E.AI lacks OpenAI top-logprob controls |
| Retry-on-validation-failure with error feedback message | llm_client/client.py retry loop | OK | Ported 2026-06-11 in base `LlmClient`: `JsonException` parse/schema failures get two Python-style validation-feedback re-prompts, cache keys remain based on the original prepared messages, and only validated final responses are cached |
| GLiNER2 local extraction client | N/A | Specialized optional Python feature; out of scope unless requested |
| Real-provider end-to-end validation | PARTIAL | `samples/Graphiti.Sample.OpenAI` and `OpenAIProviderIntegrationTests` added 2026-06-11; sample compiles and integration tests skip cleanly without `OPENAI_API_KEY`, but no live provider run has passed yet. See plan 03 |
| eval harness (eval prompts, add-episode eval) | MISSING | Optional; Plan 03 proposal drafted in `.agents/notes/eval-harness-proposal.md`, awaiting user approval before implementation |
