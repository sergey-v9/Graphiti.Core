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
pipeline — do not port without a reason):

| Prompt | Python source | C# call site | Status | Notes |
|---|---|---|---|---|
| `extract_nodes.extract_message` | prompts/extract_nodes.py | EpisodeGraphExtractor → Prompts/ExtractNodesPrompts | OK | Ported 2026-06-11; golden-text tests pin content |
| `extract_nodes.extract_text` | prompts/extract_nodes.py | same | OK | same |
| `extract_nodes.extract_json` | prompts/extract_nodes.py | same | OK | same; C# previously never branched to a JSON-specific prompt |
| `extract_edges.edge` | prompts/extract_edges.py | EpisodeGraphExtractor → Prompts/ExtractEdgesPrompts | OK | Ported 2026-06-11; golden-text tests pin content |
| `extract_nodes.extract_attributes` | prompts/extract_nodes.py:383 | AttributeExtractionService → Prompts/ExtractNodesPrompts | OK | Ported 2026-06-11; golden-text tests pin HARD RULES anti-hallucination block |
| `extract_edges.extract_attributes` | prompts/extract_edges.py:181 | AttributeExtractionService → Prompts/ExtractEdgesPrompts | OK | Ported 2026-06-11; golden-text tests pin HARD RULES anti-hallucination block |
| `extract_edges.extract_timestamps` | prompts/extract_edges.py:242 | EdgeResolutionService → Prompts/ExtractEdgesPrompts | OK | Ported 2026-06-11; golden-text tests pin content |
| `extract_edges.extract_timestamps_batch` | prompts/extract_edges.py:274 | — | MISSING | Used by Python combined extraction |
| `dedupe_nodes.nodes` | prompts/dedupe_nodes.py:117 | NodeResolutionService → Prompts/DedupeNodesPrompts | OK | Ported 2026-06-11; golden-text tests pin content, including worked EXAMPLE block |
| `dedupe_edges.resolve_edge` | prompts/dedupe_edges.py:43 | EdgeResolutionService → Prompts/DedupeEdgesPrompts | OK | Ported 2026-06-11; golden-text tests pin duplicate/contradiction constraints |
| `extract_nodes_and_edges.extract_message` | prompts/extract_nodes_and_edges.py | — | MISSING | Combined single-call extraction; see pipeline row below |
| `extract_nodes.extract_summaries_batch` | prompts/extract_nodes.py:509 | — | MISSING | C# never generates entity summaries during ingestion |
| `extract_nodes.extract_entity_summaries_from_episodes` | prompts/extract_nodes.py:613 | — | MISSING | Same area |
| `summarize_nodes.summarize_pair` | prompts/summarize_nodes.py:54 | CommunityService → Prompts/SummarizeNodesPrompts | OK | Ported 2026-06-11; sends the two source summaries as JSON like Python, deterministic text remains only no-LLM fallback |
| `summarize_nodes.summary_description` | prompts/summarize_nodes.py:119 | CommunityService → Prompts/SummarizeNodesPrompts | OK | Ported 2026-06-11; golden-text tests pin one-sentence description prompt |
| `summarize_sagas.summarize_saga` | prompts/summarize_sagas.py | SagaService.cs:175 | OK | Near-verbatim port (verified word-for-word); the model for how the rest should look |

Unused-in-pipeline Python prompts (verify before porting; as of the baseline these have no live
call sites): `extract_nodes.classify_nodes`, `extract_nodes.extract_summary`,
`dedupe_nodes.node`, `dedupe_nodes.node_list`, `summarize_nodes.summarize_context`, `eval.*`
(eval harness; optional, see plan 03).

## Ingestion pipeline (add_episode / add_episode_bulk)

| Step | Python | C# | Status | Notes |
|---|---|---|---|---|
| Episode bookkeeping, previous-episode window | graphiti.py | Graphiti.Ingestion.cs | OK | |
| Node extraction (LLM) | node_operations.extract_nodes | EpisodeGraphExtractor | OK | Prompts ported 2026-06-11 |
| Multi-episode node attribution | node_operations.py:103-112, 283-306 | — | PARTIAL | C# always attributes nodes to episode [0]; bulk loses per-episode granularity |
| Node resolution: deterministic + embedding + LLM dedup | node_operations.resolve_extracted_nodes | NodeResolutionService | PARTIAL | Stage structure OK; LLM prompt is a stub (see prompts table) |
| Entity attribute extraction | node_operations.extract_attributes_from_nodes | AttributeExtractionService | PARTIAL | Overlay merge OK; prompt stub; hallucination guards missing |
| Entity summary generation (batch, fact-appending) | node_operations.py:833-1000 | — | MISSING | C# entities never get summaries during ingestion; summaries drive search quality |
| Edge extraction (LLM) | edge_operations.extract_edges | EpisodeGraphExtractor | OK | Prompts ported 2026-06-11 |
| Edge resolution: dedup fast-path, timestamps, contradictions | edge_operations.resolve_extracted_edge | EdgeResolutionService | PARTIAL | Flow OK; prompts are stubs |
| Broad invalidation-candidate search | edge_operations.py:407-418 | EdgeResolutionService | PARTIAL | C# only searches edges between/around the node pair; Python searches wider for contradicted edges |
| Combined node+edge extraction path | utils/maintenance/combined_extraction.py | — | MISSING | Newer Python default-quality path; single LLM call, fewer orphan nodes, batch timestamps |
| Edge attribute extraction during add_episode | not done in Python single-episode flow | Graphiti.Ingestion.cs:103 | DIVERGENT | C# added a per-edge attribute pass; decide keep-or-align in plan 02 |
| Episodic edge building | edge_operations.build_episodic_edges | MaintenanceUtilities | OK | |
| Bulk ingestion (true batch dedup/resolve) | bulk_utils, graphiti.py:1230+ | Graphiti.Ingestion.cs:195+ | PARTIAL | C# loops per-episode with accumulated candidates instead of Python's batch dedup/resolve |
| Saga association + episode-time watermarks | graphiti.py | SagaService | OK | Watermarks present |
| Community update on ingest | graphiti.py | CommunityService | OK | Flow parity; summary prompts are stubs |

## Invented C# behaviors (not in Python — masks LLM failures; remove or gate, see plan 02)

| Behavior | Location | Risk |
|---|---|---|
| Heuristic entity names when LLM returns zero entities | EpisodeGraphExtractor.cs:104 | Silently fills graph with junk instead of surfacing extraction failure |
| Fabricated `RELATES_TO` edge between first two nodes when LLM returns zero edges | EpisodeGraphExtractor.cs:205 | Invents relationships that were never stated |
| Deterministic community summary/name fallback on LLM failure | CommunityService.cs, DeterministicCommunityText | Acceptable only for NotImplemented/no-LLM test scenarios; must not swallow real provider errors |
| Lexical-overlap `IdentityCrossEncoderClient` as default reranker | CrossEncoder/ | Fine as explicit test/offline choice; wrong as silent default if Python parity expects model reranking |

## Search, drivers, infrastructure

| Area | Status | Notes |
|---|---|---|
| Search config recipes, reranker enums, wire values | OK | Verified equivalent, parity-tested |
| Hybrid search flow (semantic + BM25 + BFS), RRF/MMR/cross-encoder/node-distance/episode-mentions | OK | Deterministic parts well tested |
| Community label propagation | OK | Algorithmically equivalent |
| Graph drivers: InMemory (reference), LadybugDB (investment target), Neo4j (legacy reference) | OK | Runtime proof for Ladybug workflows; see kuzu-driver-port.md |
| LLM/embedder adapters via Microsoft.Extensions.AI | DIVERGENT | Documented decision; structured output + Polly retries in place |
| Retry-on-validation-failure with error feedback message | llm_client/client.py retry loop | PARTIAL | C# Polly retries transport errors but does not re-prompt with validation-error context the way Python does |
| GLiNER2 local extraction client | N/A | Specialized optional Python feature; out of scope unless requested |
| Real-provider end-to-end validation | MISSING | Never run; no sample app, no env-gated integration test. See plan 03 |
| eval harness (eval prompts, add-episode eval) | MISSING | Optional; see plan 03 |
