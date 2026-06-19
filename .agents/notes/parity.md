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

**Latest upstream check:** 2026-06-19 `.\eng\Check-PythonUpstreamDelta.ps1 -Fetch -FailOnDelta`
found `origin/main` at `b9a74644fb641910a03d325ec2b8f669d3db75dc`; `git log`, `git diff --stat`, and
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
  summaries use the raw entity summary, not name-prefixed text; node resolution no longer
  over-widens null override candidate pools.
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
2026-06-18 follow-up: C# now also sorts `invalidation_candidates` by `valid_at` ascending with nulls
last before resolved-edge expiry and contradiction handling, matching Python's stable
`(c.valid_at is None, ensure_utc(c.valid_at))` sort.

**2026-06-14 Ladybug ranker hygiene:** closed the tracked backend-only rank row issue. Ladybug
distance and episode-mention rank queries already constrain each per-UUID query to the requested
`node_uuid`; the executor now also ignores impossible rows whose returned `uuid` is outside the input
score map, so rank results stay restricted to requested candidates like Python's reranker output.

**2026-06-18 LLM token-usage follow-up:** closed a usage-accounting drift in the M.E.AI chat adapter.
Provider usage is now held pending until the base LLM pipeline accepts a parsed and schema-validated
response. Content-filter refusals, malformed/empty JSON attempts, and schema-invalid retry attempts
no longer increment `TokenTracker`; only the validated live response attempt is counted.
2026-06-18 parser-tolerance follow-up: the M.E.AI chat adapter no longer scans arbitrary prose for
the first embedded JSON object/array. It parses the whole trimmed response, or the whole payload after
stripping a wrapping markdown code fence, and otherwise raises `JsonException` for the retry-feedback
loop.

**2026-06-17 Ladybug empty-cursor follow-up:** closed a provider query-builder drift. Python node and
edge group reads add `uuid_cursor` predicates only when the cursor string is truthy, so `uuid_cursor=""`
behaves like no cursor. Ladybug node and edge group-read statements now use the same truthiness:
empty cursors do not emit `uuid < $uuid` and do not bind a `uuid` parameter, while non-empty cursors
still page normally.

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
2026-06-19 follow-up: `AddEpisodeAsync(updateCommunities: true)` deliberately keeps C#'s flattened
community-update result shape. The source workflow mis-destructures per-node update results and can
throw or produce invalid result payloads for one, two, or three-plus nodes; C# keeps the usable
`Communities`/`CommunityEdges` lists and now pins the one-node public path.

**2026-06-19 public model-default audit:** constructor/default behavior remains a decision-gated
public-surface divergence, not an internal parity slice. Python source models use uuid4 generation,
require public fields such as node name/group, edge endpoints, edge name/fact, and edge/episode
timestamps at model construction, and default node creation time to the current clock. C# currently
pins UUIDv7 generation, empty-string defaults for those public string fields, and
`GraphitiHelpers.DefaultTimestamp` for missing/uninitialized timestamps in `GraphitiHelperTests` and
`ModernInfrastructureTests`. Changing this would alter public constructor/deserialization behavior
and should happen only with explicit public API direction plus snapshot/test updates.

**2026-06-14 triplet duplicate follow-up:** closed the tracked triplet exact-duplicate drift. C# no
longer scans the raw full between-node edge set before duplicate-candidate search. Exact duplicate
reuse now lives in `ResolveEdgeWithLlmAsync` and scans only the reranked/truncated `relatedEdges`
candidate list, while `AddTripletAsync` mirrors Python by deriving `relatedEdges` through
`EDGE_HYBRID_SEARCH_RRF` before duplicate reuse or LLM resolution.

**2026-06-14 triplet UUID-collision proof:** added public workflow coverage for Python
`add_triplet` edge UUID collision behavior. If the caller submits an `EntityEdge` UUID that already
exists on a different source/target pair, C# keeps the original edge untouched and assigns a fresh UUID
to the new edge before saving, matching Python `graphiti.py` and `test_add_triplet.py`.

**2026-06-17 in-memory typed UUID-boundary follow-up:** closed a reference-driver storage drift.
Python stores nodes by concrete label and relationships by concrete relationship type, so different
node/edge kinds can coexist with the same `uuid`. `InMemoryGraphDriver` now keys primary storage and
group/incident indexes by concrete type plus UUID. Entity, episodic, community, and saga nodes can
share a UUID without overwriting each other; entity, episodic, community, has-episode, and
next-episode edges can do the same. `AddTripletAsync` now preserves a submitted EntityEdge UUID when
only a non-entity edge has that UUID, matching Python's same-type collision check.

**2026-06-18 edge equality boundary follow-up:** closed a model equality drift from the edge/node
base classes. Edges still hash by UUID, but typed edge-to-edge equality now returns false while
object equality only matches a node with the same UUID. Node equality remains node-only, preserving
the asymmetric edge-to-node boundary exposed by the source model methods.

**2026-06-18 in-memory endpoint-gated edge-save follow-up:** closed a reference-driver persistence
drift. Python edge `save` queries `MATCH` the typed source and target nodes before `MERGE`, so an
edge save with missing or wrong-typed endpoints creates no relationship and does not replace an
existing relationship. `InMemoryGraphDriver.SaveEdgeAsync` now applies the same typed endpoint gate
for `EntityEdge`, `EpisodicEdge`, `CommunityEdge` (entity or community target), `HasEpisodeEdge`,
and `NextEpisodeEdge`; delete cleanup also recognizes community-to-community membership targets.

**2026-06-18 saga-name association follow-up:** closed a saga hydration drift. Python
`_get_or_create_saga` returns only `uuid`, `name`, `group_id`, and `created_at` for an existing saga,
then later saves that partial `SagaNode`; C# name-based saga association now projects existing driver
results to that same minimal shape. Existing summaries, first/last pointers, and summarization
watermarks are therefore overwritten from the partial model when callers pass a saga name, matching
Python. Passing an explicit `SagaNode` remains the caller-supplied object path.

**2026-06-18 driver index-maintenance surface follow-up:** closed a driver public-surface gap.
Python exposes `delete_all_indexes` on `GraphDriver`; C# now exposes `DeleteAllIndexesAsync` on
`IGraphDriver` with a base no-op for providers without runtime index deletion. `deleteExisting`
setup documentation now states the provider-dependent behavior instead of promising unconditional
drop-and-recreate semantics. LadybugDB/Kuzu and InMemory remain no-op for index deletion.

**2026-06-17 build-communities group-discovery follow-up:** closed the public workflow group-selection
drift. Python discovers entity groups only when `group_ids is None`; an explicit `[]` clears existing
communities and builds none. C# now preserves that null-vs-empty distinction. InMemory and LadybugDB
group discovery also now include the default empty-string group like Python's `WHERE n.group_id IS
NOT NULL` discovery query, so omitted `BuildCommunitiesAsync()` rebuilds default-group graphs.
2026-06-17 follow-up: community clustering now selects candidate relationship edges by endpoint
entity membership in the selected group(s), not by the edge's own `GroupId`. This matches Python's
`MATCH (n:Entity {group_id})-[e]-(m:Entity {group_id})` shape and keeps a same-group entity pair
connected even when the stored edge has a different group id.
2026-06-17 timestamp follow-up: community rebuild now captures `UtcNow()` inside each
`BuildCommunityAsync` call after summary/name generation and reuses that value for the community
node plus its membership edges, matching Python's `build_community` timestamp scope.

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

**2026-06-18 in-memory saga membership follow-up:** closed a reference-driver saga retrieval/content
drift. InMemory saga-scoped episode retrieval now enumerates the selected saga's `HAS_EPISODE`
relationship rows before applying source/reference-time filters, so linked episodes are returned even
when their own `group_id` differs from the saga lookup group. InMemory saga episode-content reads now
preserve duplicate membership-row multiplicity while retaining the existing initial/since ordering and
limit-before-empty-content behavior.

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
including the default empty `CommunitySearchConfig.SearchMethods` + RRF shape, so BM25/RRF
community configs still execute vector retrieval without calling the embedder.

**2026-06-16 community blank-summary follow-up:** closed a reachable `build_community` drift. Python
seeds community summary reduction from `[entity.summary for entity in community_cluster]`, preserving
empty strings through `summarize_nodes.summarize_pair`. C# now preserves blank entity summaries in the
pairwise reducer instead of filtering them before prompting, with regression coverage at the prompt
payload boundary.
2026-06-17 follow-up: C# now also mirrors Python's same-level `semaphore_gather` fan-out inside that
pairwise reducer. Multiple summary pairs in a single community reduction layer are launched
concurrently and collected in input order before the next reduction layer starts.

**2026-06-18 community group-order follow-up:** closed a reachable `build_communities` ordering
drift. C# now builds community clusters per resolved group id in caller/discovery order before
launching community generation, matching the Python community-cluster group loop and preserving the
returned community and membership-edge order for explicit group lists.
2026-06-18 intra-group follow-up: C# community clustering now preserves projection/read order inside
each group when emitting clusters and members, matching Python label propagation's insertion-order
cluster map. It no longer sorts cluster members or clusters alphabetically before community
generation.

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

**2026-06-19 multi-label filter follow-up:** closed a reference-backend matcher drift for non-empty
multi-label filters. Python's Kuzu/Ladybug lineage uses `list_has_all(n.labels, $labels)` for node
filters and the same all-label predicate on both endpoints for edge filters. C#'s Ladybug query
builder already emitted that provider predicate, but `CompiledSearchFilter` used by InMemory and
materialized search accepted any requested label. The matcher now requires every requested label on
each matched node while preserving the documented empty-label no-op hardening.

**2026-06-19 edge-embedding endpoint-scope follow-up:** closed a lower-level embedding-search filter
drift. Python's generic and Kuzu edge-similarity search append `source_uuid` / `target_uuid`
predicates only inside the non-null `group_ids` block. C# Ladybug, InMemory, and materialized
embedding search now ignore endpoint filters when `groupIds` is null, while preserving endpoint
filtering for non-null group scopes.

**2026-06-19 fallback ranker scope follow-up:** closed a materialized-search fallback drift. Python's
`node_distance_reranker` and `episode_mentions_reranker` do not accept or apply `group_ids`, so ranker
evidence can come from outside the candidate-retrieval group scope. `SearchEngine` no longer passes
the current search `groupIds` into `MaterializingSearchGraphDriver`'s ranker evidence loader; regular
BM25/vector/BFS retrieval remains group-scoped through each search method's own `groupIds` parameter.

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
query for blank/whitespace sanitized Lucene input. Keep this as an intentional divergence, pinned by
`SearchUtilitiesTests`. (The direct full-text path then in the temporary Neo4j driver, removed
2026-06-17, also skipped the backend call.)

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
episodic-edge case through the static model helper, while preserving mixed-hit behavior and
list-returning misses for entity/community/has/next edge helpers.

**2026-06-17 edge namespace all-miss follow-up:** closed the public namespace/static-helper split for
entity and episodic edge plural reads. Python public edge namespaces delegate to driver operations
and return empty lists for all-missing UUID lists or group reads, even where static model helpers
raise on empty all-miss results. C# `graphiti.Edges.Entity` and `graphiti.Edges.Episode` now call the
driver plural APIs directly for `GetByUuidsAsync`/`GetByGroupIdsAsync`, preserving static
`EntityEdge`/`EpisodicEdge` helper exceptions separately.

**2026-06-17 content-chunking follow-up:** closed the zero-overlap half of the chunking candidate and
documented the JSON-spacing half. Python's public chunk helpers use `overlap_tokens = overlap_tokens
or CHUNK_OVERLAP_TOKENS`, so an explicit `overlap_tokens=0` falls back to the configured/default
overlap rather than disabling overlap. C# static `ContentChunking.Chunk*Content(..., overlapTokens:
0)` now follows that falsy-default behavior. `DefaultContentChunker` still allows a configured
`ChunkOverlapTokens = 0`, which mirrors setting Python's `CHUNK_OVERLAP_TOKENS` environment-derived
constant to zero. JSON chunk output remains compact System.Text.Json formatting rather than Python
`json.dumps`'s default separator spaces; this is whitespace-only and pinned as an intentional C#
serialization divergence.
2026-06-19 follow-up: `HeuristicTokenCounter` now also provides character-window token boundaries and
budget checks, so callers that opt into the chars-per-token heuristic get source-style fixed-size
text chunks rather than under-splitting on floor-estimated token counts. The remaining large
`GenerateCoveringChunks` random-sampling difference is documented in `decisions.md` as deterministic
C# hardening; the pair-coverage contract remains pinned.

**2026-06-17 saga episode-retrieval follow-up:** closed the `saga` + null/empty `groupIds`
provider drift. Python's public fallback always takes the saga branch when `saga is not None`, binds
`group_id = None` when no group list is supplied, and therefore does not fall through to generic
episode retrieval or pick a saga from another group. C# now matches that public outcome across the
reachable providers: InMemory returns no rows without a supplied group, and Ladybug emits the grouped
saga query with `group_id = null`. (The temporary Neo4j driver, removed 2026-06-17, was also corrected
to drop its name-only saga match while present.)

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
Separate ask-user API observation: C# entity attribute definitions currently do not expose Python's
per-field `max_length` and required-field metadata. Adding that shape would be a public API expansion;
until decided, the current global attribute cap remains the documented C# behavior.

**2026-06-17 typed node-delete follow-up:** closed a Saga boundary drift. Python base
`Node.delete`, `Node.delete_by_group_id`, and `Node.delete_by_uuids` reach only `Entity`,
`Episodic`, and `Community`, while `SagaNode` delete helpers are Saga-only. C# direct model
`DeleteAsync`, static base `Node.DeleteByGroupIdAsync` / `Node.DeleteByUuidsAsync`, and typed node
namespaces now route through typed deletion for the same Python-scoped node types. InMemory and
Ladybug implement the internal seam directly (the temporary Neo4j compatibility path did too while
present, before its 2026-06-17 removal), so deleting through the wrong Entity/Saga node type or
inherited base helper no longer removes saga nodes across that boundary. Third-party drivers that do
not implement the internal seam still use a typed read before falling back to the existing broad
delete primitive.

**2026-06-18 node-delete batch-size follow-up:** closed a delete-helper argument drift. Python
node namespace deletion passes `batch_size` through to the provider operation, and the current Kuzu
operation accepts the value without validating or using it. C# typed/static namespace deletion,
InMemory node delete loops, and Ladybug node delete loops now accept non-positive batch sizes instead
of rejecting them. Providers that batch UUID deletes normalize non-positive values to one all-items
batch, preserving null UUID-collection validation.

**2026-06-19 namespace save-bulk batch-size follow-up:** closed the matching public namespace save
drift. Python namespace `save_bulk` methods pass `batch_size` through, and current save operations
ignore it on the save path. C# namespace `SaveBulkAsync` now accepts zero/negative `batchSize` for
node and edge save namespaces, treating those values as one materialized all-items batch while
leaving delete-batch normalization separate.

**2026-06-17 entity UUID group-filter follow-up:** closed a model helper drift. Python
`EntityNode.get_by_uuids(..., group_id=...)` accepts `group_id`, but the normal fallback query filters
only by UUID and the Neo4j operation implementation also has no group parameter. C#
`EntityNode.GetByUuidsAsync(..., groupId: ...)` now preserves the optional parameter for public
signature compatibility but ignores it before calling the driver, so model-helper reads return the
requested entity UUIDs regardless of group like Python. Lower-level `IGraphDriver.GetNodesByUuidsAsync`
still keeps its optional group filter for callers that use the driver contract directly.

**2026-06-17 in-memory episodic metadata follow-up:** closed a reference-driver drift. Python's
`EpisodicNode` model has `episode_metadata`, but episodic node save, bulk-save, return projection, and
record parsing do not persist or hydrate it. C# Ladybug already followed that storage shape (as did
the temporary Neo4j driver before its 2026-06-17 removal);
the in-memory reference driver now does too by dropping `EpisodeMetadata` when cloning episodic nodes
into storage.

**2026-06-17 entity/episodic-edge group miss follow-up:** closed the group-miss model-helper drift.
Python `EntityEdge.get_by_group_ids` and `EpisodicEdge.get_by_group_ids` raise
`GroupsEdgesNotFoundError(group_ids)` when the group query returns no edges, while relationship edge
helpers return empty lists. C# `EntityEdge.GetByGroupIdsAsync` and
`EpisodicEdge.GetByGroupIdsAsync` now throw `GroupsEdgesNotFoundException` on empty results through
the static model helpers and public namespaces.

**2026-06-17 in-memory UUID embedding projection follow-up:** closed the reference-driver UUID-read
drift. Python entity-node and entity-edge UUID helpers use return projections that omit
`name_embedding` / `fact_embedding` by default; callers load those vectors through explicit
embedding loaders. `InMemoryGraphDriver` now applies the same default projection for entity/fact
`GetByUuidAsync` and `GetByUuidsAsync`, while its internal embedding-load path returns cloned stored
vectors for `LoadNameEmbeddingAsync`, `LoadFactEmbeddingAsync`, and namespace bulk loaders.

**2026-06-17 semaphore default follow-up:** closed the helper default-concurrency drift. Python
`semaphore_gather(..., max_coroutines=None)` uses `SEMAPHORE_LIMIT` (`20` by default), and
`max_coroutines=0` also falls back to that default via Python's `or` semantics. C#
`GraphitiHelpers.SemaphoreGatherAsync` now uses a 20-operation cap when `maxConcurrency` is omitted
or zero, preserves explicit positive caps, and rejects negative direct helper input instead of
running unbounded. Follow-up: Graphiti workflow-level throttling now uses the same null/zero
resolution, so the constructor and DI options accept zero like Python and feed `ThrottledWork` a
positive cap of 20. Positive bulk-scoping follow-up: C# bulk extraction, first-pass bulk node
resolution, and final bulk node resolution now use the default cap of 20 even when the Graphiti
instance has a positive `maxCoroutines`, matching Python bulk helpers that call bare
`semaphore_gather`; Graphiti-level fan-outs still use the instance cap.

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

**2026-06-18 extraction response-shape follow-up:** closed the remaining live structured extraction
schema drift. Separate node extraction now requires the top-level `extracted_entities` array plus each
entity's canonical `name` and `entity_type_id`; separate edge extraction requires the top-level
`edges` array plus canonical `source_entity_name`, `target_entity_name`, `relation_type`, and `fact`;
combined extraction requires both arrays and the same canonical item fields. C# still keeps legacy
alias handling in the direct `JsonObject` parser for compatibility tests, but those aliases are no
longer part of the live response model/schema sent to or validated for LLM providers.

**2026-06-18 remaining structured response-schema follow-up:** closed the same required-field drift
for the other live prompt response models. Saga/community summaries, community descriptions, batch
entity summaries, node resolutions, edge duplicate/contradiction resolutions, and batch edge
timestamps now require the top-level fields and nested item fields that the source Pydantic response
models mark required. `EdgeTimestampResponse.valid_at` and `invalid_at` stay optional/null because
the source timestamp model defaults those temporal bounds to `None`.

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

**2026-06-17 search-config validation follow-up:** closed an eager-validation drift. Python search
configs are plain Pydantic fields: `sim_min_score`, `mmr_lambda`, and `bfs_max_depth` are only read
when the corresponding retrieval/reranker branch runs, and `limit=0` naturally yields empty result
slices. C# `SearchConfigValidator` now validates only structural shape (null configs/method lists and
unknown enum values) plus rejects negative limits; zero limits return empty results, and inactive
numeric knobs no longer block BM25/RRF searches.

**2026-06-17 text truncation negative-limit follow-up:** closed a public helper edge-case drift.
Python `truncate_at_sentence(text, max_chars)` applies normal slice semantics for negative
`max_chars`, while C# previously threw. `TextUtilities.TruncateAtSentence` now computes the same
effective slice length, including preserving sentence-boundary preference inside that shortened
prefix.

**2026-06-17 entity/fact prompt type-key follow-up:** closed the prompt/rendering half of the
aliased custom-type identity drift. Python builds node `entity_type_name` and edge
`fact_type_name` from the `entity_types` / `edge_types` dictionary keys, and summary type-description
maps use the same keys. C# prompt builders and `EntitySummaryService` now use those keys rather than
`EntityTypeDefinition.Name`, so aliased type definitions render the same identifiers the parser
already treats as canonical.

**2026-06-18 combined extraction fact-type-map follow-up:** closed the combined-prompt variant of
custom fact-type rendering. Python's combined extraction helper renders `<FACT_TYPES>` only when both
`edge_types` and `edge_type_map` are supplied; C# now omits that section when a caller supplies fact
types without a type map. The standalone edge-extraction prompt keeps its existing fallback signature
behavior because it is a separate prompt path.

**2026-06-17 MMR retrieval-order follow-up:** closed the MMR candidate-order drift for node, edge,
and community search. Python builds UUID maps from retrieval results in method/result order and
passes `list(..._uuid_map.values())` into `maximal_marginal_relevance`, so tied MMR scores keep
first-seen retrieval order rather than preliminary BM25/vector scores. C# now feeds MMR through the
first-seen merge path for all three scopes while still preserving max preliminary scores for the
non-MMR merge path.

**2026-06-17 edge episode-mentions score follow-up:** closed the score-list ordering drift. Python's
edge `episode_mentions` reranker first computes RRF UUIDs/scores, then sorts only the returned edge
objects by episode count and returns the original RRF score slice. C# now mirrors that shape: edges
move by episode-count order, while `EdgeRerankerScores` remain in the pre-sort RRF order.

**2026-06-17 edge-resolution duplicate-result follow-up:** closed the final concrete candidate from
the current read-only audit. Python appends every resolved edge and every invalidated-edge chunk from
`resolve_extracted_edges` in input order, even when the same edge UUID appears more than once. C#
now preserves those duplicate appearances during the resolution collection pass; callers that build
episode edge UUID lists from the single-episode path therefore see the same duplicate sequence shape
Python would return.

**2026-06-19 edge-resolution block-order follow-up:** closed the separate resolved-vs-invalidated
ordering drift. Python collects all `resolved_edges` and all `invalidated_edges` separately, then
single ingestion uses `resolved_edges + invalidated_edges`. C# no longer interleaves each resolved
edge with that outcome's invalidated chunk; `ResolveEntityEdgesAsync` now returns every resolved edge
first, followed by invalidated chunks in extraction order.

**2026-06-18 add-triplet invalidation-candidate follow-up:** closed a triplet-only edge-resolution
drift. Python `add_triplet` passes the reranked between-node `related_edges` and the broad
`existing_edges` search results directly into `resolve_extracted_edge`, so the same edge UUID may
appear in both prompt lists with continuous indexes. C# normal ingestion still excludes duplicate
candidates from broad invalidation search, but `AddTripletAsync` now keeps related UUIDs in the broad
candidate list and can invalidate a related edge through its broad-list index.

**2026-06-18 pre-expired edge candidate-order follow-up:** closed the remaining edge-resolution audit
candidate. Python sets `expired_at` on a resolved edge that already has `invalid_at`, then skips the
candidate sort because `expired_at is None` is false; contradiction handling therefore keeps the LLM
selected/index order. C# now sorts invalidation candidates only inside the unexpired resolved-edge
branch, preserving candidate order for already-expired/invalidated resolved edges while keeping the
date-order behavior for normal unexpired edges.

**2026-06-17 bulk type-validation follow-up:** closed an `add_episode_bulk` entrypoint drift. Python
validates `entity_types` / `excluded_entity_types` for single `add_episode`, but its bulk path does
not call `validate_entity_types` or `validate_excluded_entity_types`; it passes the values directly to
bulk extraction. C# `AddEpisodeBulkAsync` now mirrors that asymmetry by skipping the extra upfront
validation while `AddEpisodeAsync` keeps the Python single-episode validation behavior.

**2026-06-18 node-resolution candidate follow-up:** closed the remaining candidate-widening drift
from the latest bulk audit. `ResolveExtractedNodesAsync` now collects ordered candidates per
extracted node from semantic node search, merges only an explicit existing-node override, and runs
deterministic plus LLM dedupe against those candidate lists. A null override no longer triggers a
whole-group read or lexical fallback, so an extracted node with no search hits remains new and skips
the node-dedupe prompt. Exact duplicate extracted nodes are still collapsed before resolution, with
episode attribution merged at the extraction boundary.

**2026-06-18 entity-attribute key follow-up:** closed a custom attribute validation/merge drift.
Python custom entity field names are exact Pydantic field names: `validate_entity_types` rejects only
exact `EntityNode.model_fields` names, and structured attribute responses address exact field names.
C# `EntityTypeDefinition.Attributes`, protected-name validation, dynamic attribute schemas, and
attribute response merging now use ordinal exact keys, so case variants and C# property-style names no
longer collide with snake_case framework fields or with each other. The separate response-envelope /
schema-metadata and per-field `max_length` / required-metadata API expansion remains
decision-gated.

**2026-06-18 node-attribute no-schema follow-up:** closed a reachable attribute-clearing drift.
Python runs node attribute extraction for every resolved node and assigns the returned dictionary
back; when no matching entity type exists or the matched type declares no fields, that returned
dictionary is `{}`. C# now clears resolved node attributes for omitted/empty entity-type maps and
for nodes whose matched type has no declared attributes, while still skipping the attribute prompt in
those no-schema cases.

**2026-06-19 ontology matching follow-up:** closed a custom type-selection drift. Custom entity and
edge attribute schemas now resolve only from exact ontology dictionary keys plus exact edge-type-map
endpoint labels and relation names. Case variants and `EntityTypeDefinition.Name` aliases no longer
select a custom schema or trigger the attribute prompt.

**2026-06-17 Ladybug clear-data empty-list follow-up:** closed a provider clear-flow drift. Python
`clear_data` treats only `group_ids is None` as clear-all; a non-null empty list runs scoped deletion
and matches nothing. The Ladybug driver now distinguishes null from an empty group list, preserving
all nodes/edges for `ClearDataAsync(Array.Empty<string>())` while retaining null clear-all behavior.

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
| Entity attribute extraction | node_operations.extract_attributes_from_nodes | AttributeExtractionService | OK | Overlay merge and anti-hallucination prompt ported 2026-06-11. Nodes without an applicable declared-attribute schema are cleared to an empty attribute map without prompting |
| Entity summary generation (batch, fact-appending) | node_operations.py:833-1000 | EntitySummaryService | OK | Ported 2026-06-11; appends short new edge facts, batches 30-node LLM flights, supports internal filter/episode-prompt hooks, truncates LLM summaries |
| Edge extraction (LLM) | edge_operations.extract_edges | EpisodeGraphExtractor + EdgeResolutionService | OK | Prompts ported 2026-06-11; public `AddEpisodeAsync` coverage pins Python's separate-extraction self-edge drop after source/target names resolve to the same node UUID and exact endpoint-name validation before UUID resolution |
| Edge resolution: dedup fast-path, timestamps, contradictions | edge_operations.resolve_extracted_edge | EdgeResolutionService | OK | Prompt text ported 2026-06-11; broad candidate search remains tracked separately below |
| Broad invalidation-candidate search | edge_operations.py:407-418 | EdgeResolutionService | OK | Verified 2026-06-11; unfiltered edge hybrid search supplies invalidation candidates beyond the node pair, with regression coverage for invalidating an edge on a different target node |
| Combined node+edge extraction path | utils/maintenance/combined_extraction.py | EpisodeGraphExtractor.ExtractCombinedEpisodeGraphAsync | OK | Internal path ported 2026-06-11: single LLM call, orphan dropping, node attribution from facts, self-fact preservation, and batch timestamps. 2026-06-18 follow-up: combined prompt fact types render only when both fact types and a type map are supplied. Public `Graphiti` ingestion remains on separate extraction because Python exposes `use_combined_extraction` only as an internal bulk helper flag defaulting to `False`; tests pin that `add_episode` and `add_episode_bulk` do not call the combined prompt by default |
| Edge attribute extraction during add_episode | edge_operations.resolve_extracted_edge | EdgeResolutionService | OK | Aligned 2026-06-11: structured edge attributes are extracted during edge resolution only. There is no post-resolution ingestion-stage edge attribute pass; exact duplicate reuse skips the prompt and preserves existing attributes, while non-fast-path resolution replaces/clears attributes like Python |
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
| Search config recipes, reranker enums, wire values | OK | Verified equivalent, parity-tested. Numeric knobs follow Python's lazy-use shape: inactive `sim_min_score`/`mmr_lambda`/`bfs_max_depth` values are not rejected up front, and `limit=0` returns empty results |
| Hybrid search flow (semantic + BM25 + BFS), RRF/MMR/cross-encoder/node-distance/episode-mentions | OK | Deterministic parts well tested; edge/episode cross-encoder candidate windows now match Python's pre-rerank `limit` slices, including first-seen retrieval-order edge windowing, while node/community remain intentionally unwindowed like Python and pass full first-seen retrieval-order pools. Duplicate cross-encoder passages now match Python's first-seen passage / last-duplicate-candidate mapping. Community search now mirrors Python's unconditional vector retrieval when a query vector is available. Empty `EdgeTypes`/`EdgeUuids` filters are active match-none predicates like Python. `SearchFilters.PropertyFilters` remains on the DTO for Python wire-shape parity but is ignored by backend query construction and in-memory/materialized matching, matching Python's current filter constructors. Public search-result context helpers are exposed via `SearchHelpers` |
| Community label propagation | OK | Algorithmically equivalent |
| Graph drivers: LadybugDB (first-class investment target), InMemory (reference/test); FalkorDB/Neptune (enum/wire-compat only) | OK | Runtime proof for Ladybug workflows, direct package binding of list/array/empty-list/null parameters, Python-compatible scoped clear-data handling (null clears all, empty lists preserve records, group-scoped clear leaves Saga nodes while deleting Entity/Episodic/Community nodes and incident edges), driver-level `DeleteAllIndexesAsync` surface with provider no-op behavior for LadybugDB/Kuzu and InMemory, direct driver bulk-save embedding/relationship persistence, namespace/model embedding reloads by UUID, public namespace community/saga reads and typed deletes, saga-scoped retrieval/content reads, paged/default-empty group reads, directed endpoint-pair and incident entity-edge reads, InMemory concrete node/edge type UUID boundaries and Python-style typed endpoint-gated edge saves, explicit and core file-backed paths, Kuzu `':memory:'` sentinel compatibility, package/native execution, and Ladybug-owned raw full-text query/label-filter construction; Neo4j was removed 2026-06-17 and is no longer a provider; see kuzu-driver-port.md |
| LLM/embedder/reranker adapters via Microsoft.Extensions.AI | DIVERGENT | Documented decision; structured output + Polly retries in place. `HashEmbedder` and `IdentityCrossEncoderClient` are the provider-free constructor/DI defaults instead of Python's implicit OpenAI-backed defaults. Provider-backed C# embedders reject output-count mismatches, dimension mismatches, and non-finite vectors at the adapter boundary instead of slicing/forwarding malformed vectors. `MicrosoftExtensionsAICrossEncoderClient` uses structured boolean+confidence scoring because generic M.E.AI lacks OpenAI top-logprob controls. The M.E.AI chat parser strips only wrapping markdown fences and does not extract JSON embedded in prose |
| Retry-on-validation-failure with error feedback message | llm_client/client.py retry loop | OK | Ported 2026-06-11 in base `LlmClient`: `JsonException` parse/schema failures get two Python-style validation-feedback re-prompts, cache keys remain based on the original prepared messages, `promptName` stays telemetry/usage metadata rather than cache identity, and only validated final responses are cached. 2026-06-18 follow-up: token usage is recorded only for the validated live response, not refused/malformed/schema-invalid attempts |
| GLiNER2 local extraction client | N/A | Specialized optional Python feature; out of scope unless requested |
| Real-provider end-to-end validation | OK (PASSED 2026-06-13) | First live OpenAI run passed: both `OpenAIProviderIntegrationTests` green (all 11 structured schemas + dynamic attribute schema accepted by the real provider; real 2-episode resolved temporal graph), and the 6-episode `Graphiti.Sample.OpenAI` produced a sane graph — rich entity summaries, correct bi-temporal invalidation (blocked-fact `invalid_at` = QA-clearance date), and relevant reranked search. `gpt-4.1-mini`/`gpt-4.1-nano`/`text-embedding-3-small@1536`. Re-run via `eng/Run-OpenAIProviderValidation.ps1` (auto-loads gitignored `.env`). One extraction observation (March-15 rollout not invalidated by the reschedule) is LLM-variance, tracked for the future eval. See plan 03 |
| eval harness (eval prompts, add-episode eval) | OK (BUILT + RUN 2026-06-14) | The four `eval.py` prompts ported with full-string golden tests (`Prompts/EvalPrompts.cs`); `samples/Graphiti.Eval` implements the proposal's graph-building regression eval (mirrors Python `tests/evals/eval_e2e_graph_building.py`: candidate per-episode `AddEpisodeResults` judged vs a persisted baseline artifact via `eval_add_episode_results`) plus a fixed `--qa` retrieval mode (top-1 fact + distractor). Live: graph-building **6/6 not-worse** on identical code (judge genuinely diffs extractions); QA **3/7 honest**, distractor correctly fails. An adversarial review caught and fixed that the first cut measured retrieval-QA instead of graph-building. See plan 03 item 4 |
