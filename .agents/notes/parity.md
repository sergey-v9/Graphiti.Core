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

**Latest upstream check:** 2026-06-17 `.\eng\Check-PythonUpstreamDelta.ps1 -Fetch -FailOnDelta`
found `origin/main` at `b82b80e4c0c962fc714a22b74caf8c20997e8d83`; `git log`, `git diff --stat`, and
`git diff --name-status` over `0ed90b7..origin/main -- graphiti_core` were empty. No new Python
library work needs porting.

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

**2026-06-14 edge-expiry follow-up:** closed the tracked resolution-clock divergence. C# now uses
`Graphiti.UtcNow` inside `EdgeResolutionService` for resolved-edge expiry, calls it per invalidated
candidate in `EdgeMergeHelpers.ResolveEdgeContradictions`, and leaves `expired_at` null for a
brand-new extracted edge with `invalid_at` when Python returns early because no candidates exist.
Edge candidate `created_at` still comes from the ingestion operation timestamp.

**2026-06-14 Ladybug ranker hygiene:** closed the tracked backend-only rank row issue. Ladybug
distance and episode-mention rank queries already constrain each per-UUID query to the requested
`node_uuid`; the executor now also ignores impossible rows whose returned `uuid` is outside the input
score map, so rank results stay restricted to requested candidates like Python's reranker output.

**2026-06-14 attribution reference-time follow-up:** closed the tracked edge `reference_time` drift
inside the latent multi-episode attribution helpers. Python maps valid `episode_indices` to episode
UUIDs by filtering valid entries, but chooses `reference_time` only from the first raw index when that
first raw index is valid; otherwise it falls back to the primary episode. C# now uses the same
first-raw-index rule in `EpisodeAttribution.ReferenceTimeForFirstIndex`, including the `[99, 1]`
case.

**2026-06-14 node attribution remap follow-up:** closed the remaining latent multi-episode node
attribution drift. C# no longer remaps extracted-node attribution to resolved canonical UUIDs before
building episodic edges; like Python, a resolved UUID that is absent from the extracted attribution map
falls back to all provided episodes.

**2026-06-14 public-surface audit:** the Python `Graphiti` public workflows are covered by C# async
counterparts (`AddEpisodeAsync`, `AddEpisodeBulkAsync`, `SearchAsync`/`SearchAdvancedAsync`,
`RetrieveEpisodesAsync`, `BuildCommunitiesAsync`, `SummarizeSagaAsync`,
`GetNodesAndEdgesByEpisodeAsync`, `AddTripletAsync`, `RemoveEpisodeAsync`, `CloseAsync`/dispose). The
audit found no missing core workflow. It did record three already-tested, accepted C# public-behavior
divergences in `decisions.md`: stronger episode removal/saga repair, bulk raw-content scrubbing when
`storeRawEpisodeContent` is false, and caller-owned explicit/DI driver lifecycle.

**2026-06-14 triplet duplicate follow-up:** closed the tracked triplet exact-duplicate drift. C# no
longer scans the raw full between-node edge set before duplicate-candidate search. Exact duplicate
reuse now lives in `ResolveEdgeWithLlmAsync` and scans only the reranked/truncated `relatedEdges`
candidate list, while `AddTripletAsync` mirrors Python by deriving `relatedEdges` through
`EDGE_HYBRID_SEARCH_RRF` before duplicate reuse or LLM resolution.

**2026-06-14 triplet UUID-collision proof:** added public workflow coverage for Python
`add_triplet` edge UUID collision behavior. If the caller submits an `EntityEdge` UUID that already
exists on a different source/target pair, C# keeps the original edge untouched and assigns a fresh UUID
to the new edge before saving, matching Python `graphiti.py` and `test_add_triplet.py`.

**2026-06-14 saga prompt truthiness follow-up:** closed a small prompt-rendering drift in
`summarize_sagas.summarize_saga`. Python renders `<EXISTING_KNOWLEDGE>` whenever
`existing_summary` is a non-empty string, including whitespace-only strings. C# now uses the same
non-empty truthiness instead of trimming whitespace before deciding whether to render the section.

**2026-06-14 node extraction prompt golden hardening:** strengthened
`extract_nodes.extract_message` coverage for the dynamic entity-types + previous-messages +
custom-instructions case from fragment/suffix assertions to full-string equality. A read-only
Python-vs-C# audit confirmed custom instructions remain appended immediately after the final
`</EXAMPLE>` block with no wrapper; compact JSON rendering stays the documented mechanical C#
prompt divergence.

**2026-06-14 prompt edge-case golden cleanup:** converted the remaining fragment-based prompt
assertions under `tests/Graphiti.Core.Tests/Prompts` to full-string equality. Edge cases now pinned
end-to-end include `dedupe_nodes.nodes` first-non-`Entity` type-description fallback,
`extract_entity_summaries_from_episodes` rendering an empty-string entity type description, and
`summarize_sagas.summarize_saga` rendering whitespace-only existing knowledge. A follow-up scan found
no remaining `Assert.Contains`/`StartsWith`/`EndsWith` prompt assertions.

**2026-06-14 episode-concatenation golden hardening:** strengthened
`TextUtilities.ConcatenateEpisodes` tests from fragment assertions to full-string equality for
multi-episode headers, blank-line separators, Python-style `datetime.isoformat()` timestamp shapes,
local offsets, subsecond precision, and the `unknown` timestamp fallback. Python source remains
`graphiti_core/utils/text_utils.py::concatenate_episodes`; no production behavior changed.

**2026-06-14 search concurrency test hardening:** stabilized the driver-backed search concurrency
proof without changing production behavior. `SearchEngineDriverBackedTests` now parks fake driver
calls at an observable barrier before releasing them, so `SearchAsync` scope fan-out and per-type
BM25/vector fan-out are proven directly instead of relying on a watchdog cancellation token inside
the production search path. This keeps the C# proof aligned with Python's `semaphore_gather` search
fan-out.

**2026-06-14 separate edge-extraction self-edge proof:** added a public workflow parity test for
Python `edge_operations.extract_edges` self-edge filtering. `AddEpisodeAsync` now has provider-free
coverage showing an LLM-returned `Alice -> Alice` fact is dropped after names resolve to the same
node UUID, while the same extraction's `Alice -> Bob` fact is returned and persisted. No production
behavior changed; the existing C# guard lives in `EdgeResolutionService.BuildExtractedEdgeCandidates`.

**2026-06-14 separate edge-extraction exact-name alignment:** closed a reachable endpoint-name
validation drift. Python `extract_edges` builds a plain `name_to_node` dict and requires exact
`source_entity_name` / `target_entity_name` membership before UUID resolution; C# now enumerates
`nodesByExtractedName` keys with `StringComparison.Ordinal` inside
`EdgeResolutionService.BuildExtractedEdgeCandidates`, so case-mismatched LLM edge endpoints are
skipped even though node-resolution maps remain case-insensitive for their other dedupe duties.

**2026-06-16 edge-type signature workflow proof:** strengthened public `AddEpisodeAsync` metadata
coverage for Python's `edge_type_signatures_map` invariant. The existing prompt-builder test already
proved `ExtractEdgesPrompts` emits every `fact_type_signatures` entry for a fact type; the public
workflow test now also passes two `WORKS_AT` signatures through ingestion and asserts both reach the
edge extraction prompt.

**2026-06-16 public search driver-override proof:** added provider-free public workflow coverage for
Python `Graphiti.search` / `Graphiti.search_` `driver` override forwarding. C# now has a regression
test showing both basic `SearchAsync` and advanced `SearchAdvancedAsync` read from the supplied
override driver rather than the `Graphiti` instance's root driver.

**2026-06-16 public read/community driver-override proof:** added provider-free public workflow
coverage for Python `Graphiti.retrieve_episodes` and `Graphiti.build_communities` optional `driver`
forwarding. C# now proves episode retrieval reads from the override driver, and community rebuild
removes stale communities plus saves replacement communities on the override driver while leaving the
instance root driver's communities untouched.

**2026-06-16 saga empty-summary follow-up:** closed a reachable `summarize_saga` drift. Python reads
`llm_response.get('summary', '')`, hard-truncates only if needed, and persists the value directly; it
does not synthesize a deterministic fallback when the typed LLM response contains `""`. C# now
preserves an empty typed summary while still advancing the wall-clock and episode-time watermarks.

**2026-06-16 episode contribution fan-out follow-up:** closed a reachable
`get_nodes_and_edges_by_episode` drift. Python loads each episode's attributed entity edges through
`semaphore_gather(..., max_coroutines=self.max_coroutines)` and flattens the gathered batches in
episode order. C# now uses bounded `SelectThrottledAsync` for those per-episode edge loads while
preserving the existing ordered flattening and per-episode multiplicity.

**2026-06-16 cross-encoder candidate-window follow-up:** closed a search-reranker drift. Python
limits edge cross-encoder passages to the first `limit` deduped edge candidates and episode
cross-encoder passages to the first `limit` RRF-seeded episode candidates, while node and community
cross-encoder rerankers intentionally see the full preliminary candidate set. C# now applies the
same pre-cross-encoder window only for edge and episode search, with regression coverage proving a
third low-preliminary-rank candidate cannot be rescued by cross-encoder scoring in those two scopes.
2026-06-17 follow-up: the edge window and the node/community full cross-encoder inputs now also use
Python's first-seen retrieval-result order for deduped candidates instead of sorting by preliminary
BM25/vector/BFS score before the cross-encoder call.

**2026-06-16 community-search method follow-up:** closed a reachable community search drift. Python
`community_search` always executes both community full-text and vector retrieval; the
`search_methods` list is effectively ignored in that scope. C# now runs community vector retrieval
whenever a query vector is available, so a custom `BM25 + MMR` community config can rerank
text-only and vector-only candidates like Python. The public search-filter docs were also corrected
to state Python's temporal grouping shape: OR of AND-groups.
2026-06-17 follow-up: top-level `SearchAsync` now also mirrors Python's fallback
`[0.0] * EMBEDDING_DIM` query vector for community searches that do not otherwise require embedding,
so BM25/RRF community configs still execute vector retrieval without calling the embedder.

**2026-06-16 community blank-summary follow-up:** closed a reachable `build_community` drift. Python
seeds community summary reduction from `[entity.summary for entity in community_cluster]`, preserving
empty strings through `summarize_nodes.summarize_pair`. C# now preserves blank entity summaries in the
pairwise reducer instead of filtering them before prompting, with regression coverage at the prompt
payload boundary.

**2026-06-16 empty edge-filter follow-up:** closed the reachable edge half of the empty-filter-list
drift. Python `edge_search_filter_query_constructor` emits `e.name in $edge_types` and
`e.uuid in $edge_uuids` whenever those lists are non-null, so explicitly empty lists are active
match-none predicates. C# now preserves the same null-vs-empty distinction for `EdgeTypes` and
`EdgeUuids` in backend query fragments and in-memory/materialized matching.

**2026-06-17 empty node/temporal filter closure:** formally kept the remaining empty-filter shapes as
intentional C# hardening divergences. Python's filter compiler treats `node_labels=[]` as non-null and
can emit malformed/backend-dependent label fragments (`n:`, `n: AND m:`, or Kuzu
`list_has_all(..., [])`). It also emits invalid temporal fragments for empty date groups
(`(`, `()`, `( OR ...)`, or `(... OR )`). C# keeps those shapes as no-op filters instead of
reproducing invalid backend queries, pinned by the existing
`CompiledSearchFilter_EmptyNodeLabelsNoOpButEmptyEdgeListsMatchNone`,
`CompiledSearchFilter_TreatsEmptyDateBranchAsNoOp`,
`EdgeSearchFilterQueryConstructor_SkipsEmptyDateFilterList`, and
`EdgeSearchFilterQueryConstructor_EmptyDateOrBranchDoesNotEmitInvalidCypher` tests.

**2026-06-16 search-helper follow-up:** closed the public helper gap for
`graphiti_core.search.search_helpers`. C# now exposes `SearchHelpers.FormatEdgeDateRange` and
`SearchHelpers.SearchResultsToContextString`, preserving Python's sectioned context shape, date
fallback labels (`date unknown`, `present`, `None`, `Present`), and field names. JSON stays compact
per the established C# prompt-serializer decision.

**2026-06-16 excluded-entity validation follow-up:** closed a validation drift in ingestion
options. Python `validate_excluded_entity_types` accepts only `Entity` plus the keys of the supplied
`entity_types` dictionary; it does not also accept the Pydantic model/display class names. C#
`ValidateExcludedEntityTypes` now uses the same key-only availability set and formats invalid/available
type names like Python's sorted string-list representation. Later extraction/type resolution can still
map labels/display names where that behavior is separately established.

**2026-06-16 blank Lucene full-text audit:** disposed the open blank-query candidate as documented
hardening. Python top-level search skips blank input, but lower-level Lucene full-text helpers can
still build `()` / grouped-empty query strings. C# `SearchUtilities.FulltextQuery` returns an empty
query for blank/whitespace sanitized Lucene input and direct Neo4j full-text methods skip the backend
call. Keep this as an intentional divergence, pinned by `SearchUtilitiesTests`.

**2026-06-17 ParseDbDate follow-up:** closed the database date parsing drift. Python
`parse_db_date` delegates string values to `datetime.fromisoformat`, so blank strings, padded strings,
and locale-style slash dates raise instead of becoming `None` or being parsed through culture
fallbacks. C# `GraphitiHelpers.ParseDbDate` now uses a strict Python-compatible ISO parser for string
inputs, preserving date-only, basic date, ISO week date, shortened time, comma/fraction, and offset
forms while rejecting Python-invalid blanks/padding/non-ISO strings. Parsed values still normalize to
UTC because the C# model surface stores `DateTime` rather than Python's naive/aware datetime union.

**2026-06-17 episodic-edge plural-read follow-up:** closed the narrow namespace/model edge miss
drift. Python `EpisodicEdge.get_by_uuids` raises `EdgeNotFoundError(uuids[0])` when a non-empty UUID
list returns no rows, while the other plural edge helpers return empty lists. C#
`EpisodicEdge.GetByUuidsAsync` now raises `EdgeNotFoundException` for that all-missing non-empty
episodic-edge case through both the static model helper and `graphiti.Edges.Episode`, while preserving
mixed-hit behavior and list-returning misses for entity/community/has/next edge helpers.

**2026-06-17 content-chunking follow-up:** closed the zero-overlap half of the chunking candidate and
documented the JSON-spacing half. Python's public chunk helpers use `overlap_tokens = overlap_tokens
or CHUNK_OVERLAP_TOKENS`, so an explicit `overlap_tokens=0` falls back to the configured/default
overlap rather than disabling overlap. C# static `ContentChunking.Chunk*Content(..., overlapTokens:
0)` now follows that falsy-default behavior. `DefaultContentChunker` still allows a configured
`ChunkOverlapTokens = 0`, which mirrors setting Python's `CHUNK_OVERLAP_TOKENS` environment-derived
constant to zero. JSON chunk output remains compact System.Text.Json formatting rather than Python
`json.dumps`'s default separator spaces; this is whitespace-only and pinned as an intentional C#
serialization divergence.

**2026-06-17 saga episode-retrieval follow-up:** closed the `saga` + null/empty `groupIds`
provider drift. Python's public fallback always takes the saga branch when `saga is not None`, binds
`group_id = None` when no group list is supplied, and therefore does not fall through to generic
episode retrieval or pick a saga from another group. C# now matches that public outcome across the
reachable providers: InMemory returns no rows without a supplied group, Ladybug emits the grouped saga
query with `group_id = null`, and Neo4j no longer uses a name-only saga match.

**2026-06-17 namespace/triplet audit follow-up:** closed public namespace embedding and
`add_triplet` collision drifts found by the current Python-vs-C# audit. Python namespace single saves
always regenerate entity/community node and entity-edge embeddings, while namespace bulk saves delegate
the supplied models as-is. C# namespace `SaveAsync` now regenerates prefilled embeddings, and namespace
`SaveBulkAsync` preserves null/precomputed embeddings without calling the embedder; driver-level bulk
ingestion still generates missing embeddings. `AddTripletAsync` also now preserves existing non-entity
edges in the default in-memory backend when a submitted entity-edge UUID collides with an episodic,
community, has-episode, or next-episode edge by assigning the entity edge a fresh UUID before saving.
Remaining audit observation not changed in this pass: `CommunityEdgeNamespace.SaveBulkAsync` is an
additive C# public helper not present in Python. Treat it as an ask-user public API decision before
removing it; keeping it would need an explicit documented additive-API call.

**2026-06-17 typed node-delete follow-up:** closed a Saga boundary drift. Python base
`Node.delete`, `Node.delete_by_group_id`, and `Node.delete_by_uuids` reach only `Entity`,
`Episodic`, and `Community`, while `SagaNode` delete helpers are Saga-only. C# direct model
`DeleteAsync`, static base `Node.DeleteByGroupIdAsync` / `Node.DeleteByUuidsAsync`, and typed node
namespaces now route through typed deletion for the same Python-scoped node types. InMemory and
Ladybug implement the internal seam directly, and the temporary Neo4j compatibility path does too
while present, so deleting through the wrong Entity/Saga node type or inherited base helper no longer
removes saga nodes across that boundary. Third-party drivers that do not implement the internal seam
still use a typed read before falling back to the existing broad delete primitive.

**2026-06-17 entity UUID group-filter follow-up:** closed a model helper drift. Python
`EntityNode.get_by_uuids(..., group_id=...)` accepts `group_id`, but the normal fallback query filters
only by UUID and the Neo4j operation implementation also has no group parameter. C#
`EntityNode.GetByUuidsAsync(..., groupId: ...)` now preserves the optional parameter for public
signature compatibility but ignores it before calling the driver, so model-helper reads return the
requested entity UUIDs regardless of group like Python. Lower-level `IGraphDriver.GetNodesByUuidsAsync`
still keeps its optional group filter for callers that use the driver contract directly.

**2026-06-17 in-memory episodic metadata follow-up:** closed a reference-driver drift. Python's
`EpisodicNode` model has `episode_metadata`, but episodic node save, bulk-save, return projection, and
record parsing do not persist or hydrate it. C# Neo4j and Ladybug already followed that storage shape;
the in-memory reference driver now does too by dropping `EpisodeMetadata` when cloning episodic nodes
into storage.

**2026-06-17 entity/episodic-edge group miss follow-up:** closed the group-miss model-helper drift.
Python `EntityEdge.get_by_group_ids` and `EpisodicEdge.get_by_group_ids` raise
`GroupsEdgesNotFoundError(group_ids)` when the group query returns no edges, while relationship edge
helpers return empty lists. C# `EntityEdge.GetByGroupIdsAsync` and
`EpisodicEdge.GetByGroupIdsAsync` now throw `GroupsEdgesNotFoundException` on empty results through
the static model helpers and public namespaces.

**2026-06-17 semaphore default follow-up:** closed the helper default-concurrency drift. Python
`semaphore_gather(..., max_coroutines=None)` uses `SEMAPHORE_LIMIT` (`20` by default), and
`max_coroutines=0` also falls back to that default via Python's `or` semantics. C#
`GraphitiHelpers.SemaphoreGatherAsync` now uses a 20-operation cap when `maxConcurrency` is omitted
or zero, preserves explicit positive caps, and rejects negative direct helper input instead of
running unbounded.

**2026-06-17 normalize_l2 follow-up:** closed the remaining vector-normalization helper drift.
Python `normalize_l2` preserves only zero-norm embeddings; non-finite norms divide through NumPy and
propagate `NaN`/zero results. C# `GraphitiHelpers.NormalizeL2` now uses the same zero-only guard
instead of leaving non-finite inputs unchanged. Provider embedding validation still rejects
non-finite generated embeddings before graph persistence.

**2026-06-17 entity-type-id key follow-up:** closed an extraction parsing drift. Python builds the
LLM `entity_type_id` context from `entity_types.items()` and resolves extracted IDs back to the
dictionary key (`type_name`), not the model/display class name. C#
`Graphiti.ExtractEntityNames` / `ExtractEntities` now resolve IDs to the declared
`entityTypes` key, so aliased types such as `person_alias -> Person` flow into labels and exclusion
matching like Python.

**2026-06-17 entity-type-id schema follow-up:** closed the response-boundary half of the same
extraction contract. Python Pydantic requires `entity_type_id: int` for both separate
`ExtractedEntity` and combined `CombinedEntity` responses before nodes are materialized. C# response
DTOs now require `entity_type_id`, reject missing or nonnumeric values during structured-response
validation/direct parsing, preserve Pydantic-style numeric-string coercion, and still let valid
out-of-range integers fall back to `Entity` like Python node creation.

**2026-06-17 cross-encoder duplicate follow-up:** closed the duplicate-passage search drift. Python's
search rerankers collapse duplicate cross-encoder passage strings through dict-comprehension behavior:
passages are sent once in first-seen passage order, while the last duplicate candidate wins the
passage-to-result mapping. C# `SearchResultComposer` now ranks unique passages with the same first-seen
ordering and maps each ranked passage back to the last candidate using that passage. Driver-backed
tests pin the behavior for duplicate edge facts, node names, episode content, and community names.

**2026-06-17 cross-encoder retrieval-order follow-up:** closed a remaining search-reranker drift in
multi-method candidate pools. Python builds node, edge, and community cross-encoder maps from
retrieval result-list order (`search_results` flattening), so a high-scoring vector/BFS candidate does
not jump ahead of earlier BM25 candidates before the cross-encoder input/window. C# now uses a
first-seen candidate merge for cross-encoder inputs while preserving max preliminary scores for
non-cross-encoder merge paths. Edge cross-encoder windowing now takes the first `limit` deduped edge
candidates in retrieval order, and node/community cross-encoder inputs remain full-pool but in the
same retrieval order.

**2026-06-17 base edge-delete follow-up:** closed the inherited base edge helper drift. Python base
`Edge.delete` / `Edge.delete_by_uuids` delete only `MENTIONS`, `RELATES_TO`, and `HAS_MEMBER`;
`HAS_EPISODE` and `NEXT_EPISODE` have concrete delete paths. C# base `Edge.DeleteByUuidsAsync` now
uses the same inherited-base scope, while direct concrete `DeleteAsync` and typed edge namespaces use
an internal typed-delete driver seam for all five edge types. The stronger C# episode-removal saga
repair still deletes saga membership/order edges, but now does so through concrete
`HasEpisodeEdge`/`NextEpisodeEdge` typed delete calls instead of the Python-scoped base helper.

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
| Node resolution: deterministic + embedding + LLM dedup | node_operations.resolve_extracted_nodes | NodeResolutionService | OK | Prompt ported 2026-06-11; deterministic, embedding, and LLM dedupe stages covered. 2026-06-17: label promotion now mirrors Python `_promote_resolved_node`, adding extracted specific labels only when the matched canonical node is still generic `Entity` |
| Entity attribute extraction | node_operations.extract_attributes_from_nodes | AttributeExtractionService | OK | Overlay merge and anti-hallucination prompt ported 2026-06-11 |
| Entity summary generation (batch, fact-appending) | node_operations.py:833-1000 | EntitySummaryService | OK | Ported 2026-06-11; appends short new edge facts, batches 30-node LLM flights, supports internal filter/episode-prompt hooks, truncates LLM summaries |
| Edge extraction (LLM) | edge_operations.extract_edges | EpisodeGraphExtractor + EdgeResolutionService | OK | Prompts ported 2026-06-11; public `AddEpisodeAsync` coverage pins Python's separate-extraction self-edge drop after source/target names resolve to the same node UUID and exact endpoint-name validation before UUID resolution |
| Edge resolution: dedup fast-path, timestamps, contradictions | edge_operations.resolve_extracted_edge | EdgeResolutionService | OK | Prompt text ported 2026-06-11; broad candidate search remains tracked separately below |
| Broad invalidation-candidate search | edge_operations.py:407-418 | EdgeResolutionService | OK | Verified 2026-06-11; unfiltered edge hybrid search supplies invalidation candidates beyond the node pair, with regression coverage for invalidating an edge on a different target node |
| Combined node+edge extraction path | utils/maintenance/combined_extraction.py | EpisodeGraphExtractor.ExtractCombinedEpisodeGraphAsync | OK | Internal path ported 2026-06-11: single LLM call, orphan dropping, node attribution from facts, self-fact preservation, and batch timestamps. Public `Graphiti` ingestion remains on separate extraction because Python exposes `use_combined_extraction` only as an internal bulk helper flag defaulting to `False`; tests pin that `add_episode` and `add_episode_bulk` do not call the combined prompt by default |
| Edge attribute extraction during add_episode | edge_operations.resolve_extracted_edge | EdgeResolutionService | OK | Aligned 2026-06-11: structured edge attributes are extracted during edge resolution only. There is no post-resolution ingestion-stage edge attribute pass; exact duplicate reuse skips the prompt and preserves existing attributes, while non-fast-path resolution replaces/clears attributes like Python |
| Episodic edge building | edge_operations.build_episodic_edges | MaintenanceUtilities | OK | |
| Bulk ingestion (true batch dedup/resolve) | bulk_utils, graphiti.py:1230+ | Graphiti.Ingestion.cs:195+ | OK + DIVERGENT | Staged extraction, cross-batch node/edge dedupe, final resolution, pointer remapping, per-episode provenance. 2026-06-13 fixes: bulk summaries no longer append edge facts (Python `edges=None`); first-pass node dedup no longer over-widens the candidate pool. Behaviors KEPT as documented DIVERGENT (see `decisions.md`): cross-episode edge invalidation is more aggressive than Python, bulk episodes own `episode.EntityEdges` where Python's bulk leaves it empty, and `storeRawEpisodeContent: false` also scrubs stored bulk episode content after extraction |
| Saga association + episode-time watermarks | graphiti.py | SagaService | OK | Watermarks present |
| Community update on ingest | graphiti.py | CommunityService | OK | Flow parity; community summary/name prompts ported 2026-06-11; blank entity summaries are preserved when summarized into communities |

## Public Graphiti workflows

| Workflow | Python | C# | Status | Notes |
|---|---|---|---|---|
| Lifecycle | `close` | `CloseAsync` / `DisposeAsync` | DIVERGENT | C# closes only owned drivers; explicit/DI drivers are caller/container-owned |
| Episode retrieval | `retrieve_episodes` | `RetrieveEpisodesAsync` | OK | |
| Communities | `build_communities` | `BuildCommunitiesAsync` | OK | Community summary reduction preserves raw entity summaries, including blank strings, like Python |
| Basic fact search | `search` | `SearchAsync(query, ...)` | OK | |
| Advanced graph search | `search_` | `SearchAdvancedAsync` / `SearchAsync(query, SearchConfig, ...)` | OK | Idiomatic C# names; Python-style aliases intentionally not added |
| Episode contribution lookup | `get_nodes_and_edges_by_episode` | `GetNodesAndEdgesByEpisodeAsync` | OK + DIVERGENT | Per-episode entity-edge loads use bounded fan-out like Python `semaphore_gather`, then flatten in episode order. Bulk episodes own entity-edge UUIDs in C#, so bulk episode contribution lookup is more complete than Python |
| Triplet ingest | `add_triplet` | `AddTripletAsync` | OK | Exact duplicate reuse scans the reranked/limited related-edge set after `EDGE_HYBRID_SEARCH_RRF`, matching Python; edge UUID collisions on different endpoint pairs generate a fresh UUID and preserve the original edge; in-memory cross-type UUID collisions also generate a fresh entity-edge UUID so non-entity edges are not overwritten |
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
| Search config recipes, reranker enums, wire values | OK | Verified equivalent, parity-tested |
| Hybrid search flow (semantic + BM25 + BFS), RRF/MMR/cross-encoder/node-distance/episode-mentions | OK | Deterministic parts well tested; edge/episode cross-encoder candidate windows now match Python's pre-rerank `limit` slices, including first-seen retrieval-order edge windowing, while node/community remain intentionally unwindowed like Python and pass full first-seen retrieval-order pools. Duplicate cross-encoder passages now match Python's first-seen passage / last-duplicate-candidate mapping. Community search now mirrors Python's unconditional vector retrieval when a query vector is available. Empty `EdgeTypes`/`EdgeUuids` filters are active match-none predicates like Python. Public search-result context helpers are exposed via `SearchHelpers` |
| Community label propagation | OK | Algorithmically equivalent |
| Graph drivers: LadybugDB (first-class investment target), InMemory (reference/test), Neo4j (temporary legacy compatibility) | OK | Runtime proof for Ladybug workflows, direct package binding of list/array/empty-list/null parameters, direct driver bulk-save embedding/relationship persistence, namespace/model embedding reloads by UUID, public namespace community/saga reads and typed deletes, saga-scoped retrieval/content reads, paged group reads, directed endpoint-pair and incident edge reads, explicit and core file-backed paths, Kuzu `':memory:'` sentinel compatibility, package/native execution, and Ladybug-owned raw full-text query/label-filter construction; Neo4j is kept only to avoid regressions while present and is expected to be removed; see kuzu-driver-port.md |
| LLM/embedder/reranker adapters via Microsoft.Extensions.AI | DIVERGENT | Documented decision; structured output + Polly retries in place. `MicrosoftExtensionsAICrossEncoderClient` uses structured boolean+confidence scoring because generic M.E.AI lacks OpenAI top-logprob controls |
| Retry-on-validation-failure with error feedback message | llm_client/client.py retry loop | OK | Ported 2026-06-11 in base `LlmClient`: `JsonException` parse/schema failures get two Python-style validation-feedback re-prompts, cache keys remain based on the original prepared messages, and only validated final responses are cached |
| GLiNER2 local extraction client | N/A | Specialized optional Python feature; out of scope unless requested |
| Real-provider end-to-end validation | OK (PASSED 2026-06-13) | First live OpenAI run passed: both `OpenAIProviderIntegrationTests` green (all 11 structured schemas + dynamic attribute schema accepted by the real provider; real 2-episode resolved temporal graph), and the 6-episode `Graphiti.Sample.OpenAI` produced a sane graph — rich entity summaries, correct bi-temporal invalidation (blocked-fact `invalid_at` = QA-clearance date), and relevant reranked search. `gpt-4.1-mini`/`gpt-4.1-nano`/`text-embedding-3-small@1536`. Re-run via `eng/Run-OpenAIProviderValidation.ps1` (auto-loads gitignored `.env`). One extraction observation (March-15 rollout not invalidated by the reschedule) is LLM-variance, tracked for the future eval. See plan 03 |
| eval harness (eval prompts, add-episode eval) | OK (BUILT + RUN 2026-06-14) | The four `eval.py` prompts ported with full-string golden tests (`Prompts/EvalPrompts.cs`); `samples/Graphiti.Eval` implements the proposal's graph-building regression eval (mirrors Python `tests/evals/eval_e2e_graph_building.py`: candidate per-episode `AddEpisodeResults` judged vs a persisted baseline artifact via `eval_add_episode_results`) plus a fixed `--qa` retrieval mode (top-1 fact + distractor). Live: graph-building **6/6 not-worse** on identical code (judge genuinely diffs extractions); QA **3/7 honest**, distractor correctly fails. An adversarial review caught and fixed that the first cut measured retrieval-QA instead of graph-building. See plan 03 item 4 |
