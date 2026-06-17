# C# Port Handoff

This is the working handoff for agents continuing the C# Graphiti Core port. Keep it current-state
focused; do not turn it back into a per-commit changelog.

## Current Goal

`csharp/src/Graphiti.Core` is a managed C# port of Python `graphiti_core/`: temporal context graphs
for AI agents with episode ingestion, entity/fact extraction, deduplication, invalidation,
communities, sagas, and hybrid search.

Python remains the behavioral source of truth. The C# port should be idiomatic .NET where that is
compatible with Graphiti semantics, wire values, cache/schema identity, and performance/allocation
discipline.

Provider work is focused on LadybugDB. InMemory is the deterministic reference/test backend. Neo4j was
removed 2026-06-17 and is no longer a provider. The focused provider state
lives in `kuzu-driver-port.md`; do not duplicate its proof matrix here.

> ⚠ **Supervisor review 2026-06-17:** CI lanes and publishing the LadybugDB binding to the
> `sergey-v9/ladybug-dotnet` GitHub Packages feed (dropping the local offline feed) were done by
> following the roadmap into user-gated territory. They are
> PENDING Sergey's confirmation. See `roadmap.md` → "User-gated". Neo4j removal: DONE (2026-06-17).
> The library work itself
> (parity sweep, search/pipeline correctness) is solid and green (1048 passed / 3 skipped).

## Current Layout

- `Graphiti.cs` and `Graphiti.*.cs`: public orchestrator, lifecycle, ingestion, search, removal,
  saga, community, infrastructure, and extraction parsing partials.
- `Models/`: node, edge, result DTO, entity type, entity attribute, and episode type models.
- `Drivers/` (in `Graphiti.Core`): only the driver contract/base (`IGraphDriver`, base driver),
  the deterministic in-memory reference/test driver, the provider enum, and saga episode
  content.
- `Graphiti.Core.Drivers.Ladybug` (separate project): owns the LadybugDB driver/factory/executor,
  statement builders, search statement/filter, record mapper, schema, and `LadybugDbOptions` +
  `AddLadybugDbGraphDriver`.
- `Namespaces/`: node and edge namespace facades over drivers.
- `Search/`: search configs/results, hybrid search engine, rerankers, filter builders/matchers,
  fallback graph materialization, search-result composition, and search-driver retrieval adapter.
- `Maintenance/`: entity deduplication and community clustering.
- `Text/`: chunking, token counting, text helpers, and Graphiti helper functions.
- `LlmClients/`, `Embedding/`, `CrossEncoder/`: provider abstractions, Microsoft.Extensions.AI
  adapters, deterministic/test implementations, cache/usage helpers, and rerankers.
- `Configuration/`: options, validators, DI registration, cache/resilience settings.
- `Telemetry/`: `ActivitySource` spans and source-generated logging.
- `Serialization/`: System.Text.Json serializer and source-generated context.
- `Internal/`: helper/services for extraction context, attribute merging, edge merging, type
  resolution, deterministic text, throttling, rate limiting, saga/community/attribute/edge/node
  services, and episode graph extraction.

## Current State

Reassessed 2026-06-11 against Python baseline `0ed90b7` (see `parity.md` for the full matrix):

- **Solid and verified:** project/infrastructure shape (net10.0, analyzers, packaging), drivers
  (InMemory reference/test, LadybugDB runtime proof), search ranking/fusion/reranking,
  community label propagation, text utilities, serialization/cache identity, DI/options. The
  deterministic suite is green in the latest verification checkpoint below.
- **Phase 2 complete:** the LLM-facing semantic layer has moved from scaffold prompts to ported
  Python prompt text for live call sites. Node/edge extraction prompts and edge timestamp
  extraction prompts, node dedupe prompts, edge dedupe prompts, node/edge attribute extraction
  prompts, community summary/name prompts, and saga summary prompts were ported 2026-06-11
  (`Prompts/`). Entity summary generation was ported 2026-06-11:
  `EntitySummaryService` appends short new edge facts, batches 30-node LLM summary flights, supports
  the internal filter/episode-prompt hooks, and is wired into single and bulk ingestion before save.
  Invented extractor fallbacks were removed/constrained 2026-06-11: empty structured extraction no
  longer fabricates nodes or `RELATES_TO` edges, and community deterministic fallback is limited to
  no-op/NotImplemented paths. Broad edge-invalidation candidate search is now regression-tested for
  cross-node-pair contradictions. Multi-episode attribution was ported 2026-06-11: structured node
  and edge `episode_indices` now flow into episodic edge creation and fact episode/reference-time
  metadata. Combined extraction internals were ported 2026-06-11: the
  `extract_nodes_and_edges.extract_message` prompt, `extract_edges.extract_timestamps_batch`,
  orphan dropping, node attribution from facts, and self-fact preservation are covered behind
  `EpisodeGraphExtractor.ExtractCombinedEpisodeGraphAsync`. Public ingestion intentionally does not
  call it because the current Python baseline exposes `use_combined_extraction` only as an internal
  bulk helper flag defaulting to `False`, not on the `Graphiti` public surface; tests pin separate
  extraction as the default for both `add_episode` and `add_episode_bulk`. Bulk
  ingestion true-batch semantics were ported 2026-06-11: C# now stages extraction, first-pass node
  resolution, cross-batch node/edge dedupe, pointer remapping, final node/edge resolution, and
  per-episode provenance rather than running each episode through the whole maintenance chain.
  Validation-failure re-prompting was ported 2026-06-11 in base `LlmClient`: malformed JSON or
  schema-validation `JsonException`s get Python's two repair attempts with a validation-error user
  message, while retry feedback stays out of the cache key and only final validated responses are
  cached. Edge attribute extraction was aligned 2026-06-11: C# no longer runs a separate
  ingestion-stage edge attribute pass, and custom edge attributes are extracted inside edge
  resolution. Exact duplicate edge reuse skips the edge-attribute prompt and preserves existing
  structured attributes like Python.
- **Phase 3 real-provider validation: PASSED (2026-06-13).** Real LLM/embedding/reranker providers
  have been exercised end to end. With `OPENAI_API_KEY` supplied locally via gitignored `.env`, both
  `OpenAIProviderIntegrationTests` passed against the real OpenAI API (all structured schemas
  accepted; real resolved temporal graph) and the 6-episode `Graphiti.Sample.OpenAI` produced a sane
  graph (rich summaries, correct bi-temporal invalidation, relevant reranked search).
  `samples/Graphiti.Sample.OpenAI` is the runnable OpenAI host; `OpenAIProviderIntegrationTests` are
  the env-gated provider tests (skip cleanly without the key); the sample uses
  `MicrosoftExtensionsAICrossEncoderClient` for real reranking. Re-run with
  `.\eng\Run-OpenAIProviderValidation.ps1` (auto-loads `.env`). See the Verification checkpoints below.
- **Eval harness: BUILT and run live (2026-06-14).** The harness from
  `.agents/notes/eval-harness-proposal.md` was implemented as `samples/Graphiti.Eval` to the
  proposal's graph-building regression design and run live (6/6 no-regression on identical code; QA
  mode 3/7 honest, distractor correctly fails).
- **Phases 1–3 are DONE.** The performance/allocation moratorium is LIFTED; further performance work
  is evidence-driven (benchmark-first) only (`roadmap.md`).
- Work selection rule: follow `.agents/plans/` in order (see AGENTS.md "Current priority"). Phases
  1–3 are complete; the active plan-05 surface now has an explicit Step F plan-folder backlog triage
  gate before release infrastructure. E.2 now consumes the fork-published LadybugDB dev package family,
  and CI has both the core-only lane
  (`.github/workflows/core-only.yml`) and full Ladybug-inclusive lane (`.github/workflows/full.yml`)
  wired. Workflow YAML parsing and `.\eng\Verify-GraphitiCoreOnly.ps1` are green locally; the full
  verifier is also green with GitHub Packages credentials. Versioning and publish-path decisions remain
  decision-gated. Performance work is benchmark-first and no longer on moratorium (`roadmap.md`).
- Decomposition context: `Graphiti` is the public orchestrator; behavior lives in partials plus
  internal services and helpers. Search boundaries: `SearchEngine` orchestrates,
  `SearchRetrievalRunner` retrieves, `SearchResultComposer` shapes results. Prompt builders live
  in `Prompts/` (one static class per Python prompt module).
- Optional local `.agents/skills` files are specialist references only. Use them for matching tasks,
  but do not let generic AI/ML/framework advice override `decisions.md`.

## LadybugDB / Kuzu

LadybugDB is the main provider target while Kuzu remains the Python parity lineage and compatibility
vocabulary. `GraphProvider.LadybugDb` is the driver-facing provider value; `GraphProvider.Kuzu` is an
`[Obsolete]` core options/DI alias that resolves to the same LadybugDB-backed driver when
`AddLadybugDbGraphDriver` is registered. `AddLadybugDbGraphDriver` remains the explicit host-facing
configuration helper for `DatabasePath`. The LadybugDB factory accepts both the native empty-string
in-memory path and Python Kuzu's `':memory:'` sentinel, normalizing the sentinel at the Graphiti
boundary. Active Ladybug full-text search builds Python Kuzu-style raw whitespace queries inside
`Drivers/Ladybug/LadybugFulltextQuery`, and active Ladybug node-label search filters are built by
`Drivers/Ladybug/LadybugSearchFilter`; the generic `SearchUtilities` and `CompiledSearchFilter` no
longer carry separate `GraphProvider.Kuzu` compatibility branches.
Graphiti now consumes the fork-published LadybugDB package family
`0.17.1-dev.1.1.g6f3dbed` from the `sergey-v9/ladybug-dotnet` GitHub Packages feed via
`NuGet.config`; that binding supports Graphiti's list/array/empty-list/null parameters directly, so
the former `LadybugStatementNormalizer` workaround has been removed. Restores that include the
Ladybug driver require a NuGet credential for source `github_ladybug` with `read:packages`.

For provider status, package facts, package bug recovery, runtime proof, and remaining work, read
`kuzu-driver-port.md`. If implementation uncovers a likely LadybugDB package/binding issue, mark it
separately from Graphiti port gaps. The current user-approved recovery path is to patch and commit
the fix in `W:\code\ladybug`, push the fork's `dev` branch when a fresh package is needed, let the
`sergey-v9/ladybug-dotnet` workflow publish a new GitHub Packages dev version, and bump Graphiti to
that published version.

## Verification

Rerun verification before claiming the tree is green; historical test counts drift as coverage is
added.

Latest verification checkpoint, 2026-06-17: plan 05 now has an explicit Step F plan-folder backlog
triage gate before release infrastructure, search cross-encoder candidate pools preserve Python's
first-seen retrieval-result order across BM25/vector/BFS inputs, and `NormalizeL2` preserves only
zero-norm embeddings while propagating non-finite norms like Python. `EntityEdge.GetByGroupIdsAsync`
and `EpisodicEdge.GetByGroupIdsAsync` now both throw on empty group results like Python. Extracted
nodes in separate and combined extraction now require `entity_type_id` like Python Pydantic, preserve
numeric-string coercion, and still fall back to `Entity` for valid out-of-range IDs. InMemory storage
now preserves Python's concrete node-label and relationship-type UUID boundaries, so cross-type
nodes/edges can share a UUID and `AddTripletAsync` preserves an EntityEdge UUID when only a
non-entity edge has that UUID. `BuildCommunitiesAsync` now treats explicit empty `groupIds` as build
none, while omitted group IDs discover all entity groups including the default empty-string group for
InMemory and LadybugDB. Public entity/episodic edge namespaces now return empty lists for all-missing
plural UUID/group reads like Python namespaces, while static model helper exceptions remain pinned.
Edge cross-encoder windowing applies the `limit` after
retrieval-order dedupe; node/community cross-encoder inputs remain full-pool but use the same
retrieval order. `TextUtilities.TruncateAtSentence` mirrors Python negative-slice behavior for
negative `max_chars`, and node/edge extraction prompt contexts plus entity-summary type-description
maps use dictionary keys for custom type names like Python. MMR reranking now receives node, edge,
and community candidates in Python's first-seen retrieval order, and edge episode-mentions scores
stay in pre-sort RRF order like Python.
`.\eng\Verify-GraphitiCore.ps1` is green with the active GitHub Packages credential (`1048` passed,
`3` skipped; both shippable packages packed and both fresh package-consumer smoke builds succeeded).

Package-feed checkpoint, 2026-06-17: Graphiti now points at the `sergey-v9/ladybug-dotnet` GitHub
Packages feed for LadybugDB packages (`0.17.1-dev.1.1.g6f3dbed`) and `NuGet.config` includes package
source mapping for central package management. GitHub Packages currently reports only that published
version for both `LadybugDB` and `LadybugDB.Native` (created 2026-06-16). With the active GitHub token
supplied through `NuGetPackageSourceCredentials_github_ladybug`, full restore from source
`github_ladybug` succeeds and `.\eng\Verify-GraphitiCore.ps1` is green (`1021` passed, `3` skipped;
both shippable packages packed and both fresh package-consumer smoke builds succeeded).

Current core-only checkpoint, 2026-06-17: typed node deletes now preserve Python's Saga boundary for
direct model `DeleteAsync`, base `Node.DeleteByGroupIdAsync` / `Node.DeleteByUuidsAsync`, and typed
node namespaces; `EntityNode.GetByUuidsAsync` now accepts but ignores `groupId` like Python's fallback
query; InMemory episodic-node persistence now drops `EpisodeMetadata` like Python's queries and
record parser; `EntityEdge.GetByGroupIdsAsync` and `EpisodicEdge.GetByGroupIdsAsync` now raise on
empty group results like Python; and cross-encoder search now collapses duplicate passage strings like
Python while mapping each passage back to the last duplicate candidate. Base
`Edge.DeleteByUuidsAsync` now matches Python's inherited base helper scope by excluding
`HAS_EPISODE`/`NEXT_EPISODE`, while concrete saga edge deletes and episode-removal saga repair still
delete those relationship types through typed paths. `.\eng\Verify-GraphitiCoreOnly.ps1` is green
(`933` passed, `0` skipped; core pack succeeded). The full Ladybug-inclusive verifier is also green
with the active GitHub Packages credential (`1021` passed, `3` skipped; package-consumer smoke builds
succeeded).

Latest checkpoint, 2026-06-17:

`.\eng\Verify-GraphitiCoreOnly.ps1` is green after wiring the core-only GitHub Actions lane
(`.github/workflows/core-only.yml`): strict nuget.org-only restore with a temp package cache, core
format/build/pack, and the core-only test slice with `GraphitiCoreOnlyTests=true` (`905` passed,
`0` skipped). `.\eng\Verify-GraphitiCore.ps1` is also green after the normal Ladybug-inclusive path,
public driver-override proofs, saga empty-summary parity proof, and episode contribution fan-out
proof, edge/episode cross-encoder candidate-window parity, community vector-search parity, temporal-filter
grouping documentation, community blank-summary reduction parity, empty edge-filter-list parity, and
search-helper context formatting parity, excluded-entity-type validation parity, blank Lucene
full-text hardening documentation, database date parsing parity, and episodic-edge plural-read
parity, content chunking zero-overlap/default semantics, and saga-scoped null-group retrieval parity:
restore, format, warning-clean build including `Graphiti.Sample.OpenAI`, full test suite (`1009`
passed, `3` skipped, `1012` total), `dotnet pack` for
both shippable packages
(`Graphiti.Core.2.0.0-alpha.1.nupkg` + `.snupkg` and
`Graphiti.Core.Drivers.Ladybug.2.0.0-alpha.1.nupkg` + `.snupkg`), and fresh temp consumer
restore/build/setup/run checks for both packages. The verifier now packs both projects, then creates isolated
`net10.0` consumers with strict `NuGet.config` files (`<clear />`), temp `NUGET_PACKAGES`, and
`--no-cache`: the core consumer restores from the packed core output + nuget.org only, while the
Ladybug consumer restores from both packed Graphiti outputs + the fork GitHub Packages feed +
nuget.org and embeds the packed LadybugDB driver in `Graphiti`. Both consumers call
`BuildIndicesAndConstraintsAsync()`, add a triplet through the public API, search the inserted fact
back, then assert the provider and hit UUID (`InMemory:smoke-edge`, `LadybugDb:smoke-edge`). Both csprojs set
`IncludeSymbols=true`/`SymbolPackageFormat=snupkg`, and `PackageReadinessTests` guards shared NuGet
metadata, README packing, symbol settings, SemVer-like same-version alignment, the two-project pack
loop, the package-consumer smoke path, and the core-only verifier's nuget.org-only/non-Ladybug shape.
`GraphitiWorkflowTests` now pins Python
`extract_edges` self-edge behavior through `AddEpisodeAsync`: an LLM-returned `Alice -> Alice` edge is
dropped while the same extraction's `Alice -> Bob` edge is returned and persisted. It also pins
Python's exact endpoint-name validation: a case-mismatched LLM edge endpoint is skipped even though
C# node-resolution maps remain case-insensitive for other dedupe paths. `GraphitiWorkflowTests` also
pins Python `add_triplet` behavior for edge UUID collisions: if a submitted edge UUID already exists
on a different endpoint pair, C# generates a fresh edge UUID and preserves the original edge.
`AddEpisode_PassesTypeMetadataAndCustomInstructionsToExtractionPrompt` now proves the public
ingestion path forwards all `fact_type_signatures` for an edge type instead of only one signature.
`AddEpisode_ReusesExistingEpisodeWhenUuidIsProvided` pins Python's `uuid is not None` branch: stored
episode content/source drive extraction and replacement call fields are ignored.
`AddEpisode_ExplicitPreviousEpisodeUuidsOverrideAutomaticPreviousContext` pins Python's
`previous_episode_uuids` branch by proving an explicit UUID list replaces, rather than merges with,
the automatic recent-episode context window.
`GraphitiSearchAsync_UsesProvidedDriverOverrideForBasicAndAdvanced` pins Python `search`/`search_`
`driver` override forwarding by proving basic and advanced public searches read from the supplied
override driver rather than the instance root driver.
`RetrieveEpisodes_UsesProvidedDriverOverride` and `BuildCommunities_UsesProvidedDriverOverride` pin
Python `retrieve_episodes` / `build_communities` `driver` override forwarding: retrieval reads from
the supplied driver, and community rebuild removes stale communities plus saves replacements on that
driver while leaving the instance root driver's communities untouched.
`SummarizeSaga_PreservesEmptyTypedLlmSummaryLikePython` pins Python `summarize_saga` summary
assignment: an explicit empty typed LLM `summary` is persisted as `""` rather than replaced by
deterministic episode-content fallback, while the wall-clock and episode-time watermarks still
advance.
`GetNodesAndEdgesByEpisode_FetchesEpisodeEdgesWithBoundedConcurrency` pins Python
`get_nodes_and_edges_by_episode` edge-loading behavior: attributed edge batches are fetched with
bounded fan-out under `maxCoroutines` and flattened in episode order, preserving duplicate edge
appearances across episodes.
`EdgeSearch_CrossEncoderRanksOnlyLimitedPreliminaryCandidates` and
`EpisodeSearch_CrossEncoderRanksOnlyLimitedRrfCandidates` pin Python search windowing: edge
cross-encoder passages are limited to the first `limit` preliminary edge candidates in first-seen
retrieval-result order, episode cross-encoder passages are limited to the first `limit` RRF-seeded
episode candidates, and node/community cross-encoder paths remain intentionally unwindowed like
Python while preserving first-seen retrieval-result input order across BM25/vector/BFS pools.
`CommunitySearch_Bm25MmrStillRunsVectorSearchLikePython` pins Python community-search behavior:
community vector retrieval still runs when a query vector is available even if the custom
`CommunitySearchConfig.SearchMethods` list contains only `Bm25`, so MMR can see text-only and
vector-only candidates. `CompiledSearchFilter_DateFiltersUsePythonOrOfAndGroups` pins the in-memory
temporal-filter grouping shape, and the public docs/XML comments now state the same OR-of-AND-groups
semantics that Python's query builder implements.
`BuildCommunities_PreservesBlankEntitySummariesInPairReduction` pins Python `build_community`
behavior: blank entity summaries are preserved in the pairwise `summarize_nodes.summarize_pair`
payload instead of being filtered before reduction.
`CompiledSearchFilter_EmptyNodeLabelsNoOpButEmptyEdgeListsMatchNone`,
`BuildEdgeQuery_PreservesEmptyEdgeListsBeforeLadybugLabelFilter`, and
`PackageRuntime_SearchExecutorRunsNonEmptySearchFilters` pin Python's null-vs-empty distinction for
`EdgeTypes` and `EdgeUuids`: an explicitly empty edge type/UUID list emits an active predicate and
matches no edges, including through the Ladybug package runtime, while empty node labels remain a
documented pending bug-compatibility decision.
`FormatEdgeDateRange_UsesPythonFallbackLabels`,
`FormatEdgeDateRange_RendersInvariantPythonLikeDateTimes`, and
`SearchResultsToContextString_RendersPythonSearchHelperSections` pin the public C# counterpart to
Python `graphiti_core.search.search_helpers`: `SearchHelpers` now exposes edge date-range formatting
and a sectioned `SearchResults` context formatter for direct LLM prompt use.
`ValidateExcludedEntityTypes_AllowsBuiltInAndDeclaredTypeKeys` and
`ValidateExcludedEntityTypes_RejectsDeclaredDisplayNamesLikePython` pin Python's key-only
`excluded_entity_types` validation: exclusions may name `Entity` or custom `entity_types` dictionary
keys, but not separate display/model names.
`FulltextQuery_SkipsBlankLuceneQueriesAsIntentionalHardening` documents the deliberate direct Lucene
full-text divergence: lower-level C# full-text helpers skip blank/whitespace Lucene queries instead
of issuing Python's backend-dependent `()` / grouped-empty query strings.
`ParseDbDate_UsesInvariantUtcParsing`, `ParseDbDate_AcceptsPythonIsoformatVariants`,
`ParseDbDate_RejectsPythonInvalidStringsWithFormatException`, and
`TryParseDbDate_ReturnsFalseForInvalidValuesWithoutThrowing` pin Python `parse_db_date` string
semantics: ISO strings parse, blank/padded/non-ISO strings are rejected instead of being trimmed or
parsed by culture fallback, and C# normalizes accepted values to UTC because its model surface stores
`DateTime`.
`EpisodeEdgeNamespace_ThrowsForAllMissingPluralUuidsLikePython` pins the narrow Python
`EpisodicEdge.get_by_uuids` miss behavior: a non-empty all-missing UUID list raises
`EdgeNotFoundException` for the first requested UUID through both the static model helper and
`graphiti.Edges.Episode`, while mixed hits, empty input, and entity-edge plural misses stay
list-returning.
`ChunkJsonContent_ExplicitZeroOverlapUsesPythonDefaultOverlap` pins Python's direct helper
zero-overlap semantics: `overlap_tokens=0` is falsy and falls back to the default overlap rather than
disabling overlap. `DefaultContentChunker_HonorsConfiguredZeroOverlap` continues to pin the
environment/configuration analogue where the configured default overlap itself is zero.
`ChunkJsonContent_UsesCompactJsonSerializationAsIntentionalDivergence` documents the remaining
JSON-spacing difference as compact System.Text.Json output with the same parsed structure/chunking
contract.
`RetrieveEpisodes_WithSagaAndNoGroupIdsDoesNotMatchAcrossGroups`,
`BuildRetrieveEpisodes_WithSagaAndNoGroupsBindsNullGroupId`, and
`BuildRetrieveEpisodesStatement_BindsNullSagaGroupWithoutGroupIds` close the saga-scoped retrieval
`groupIds: null` provider drift: InMemory no longer picks a cross-group saga, and Ladybug no longer
falls through to generic episode retrieval. (Neo4j, removed 2026-06-17, was also corrected to drop its
name-only saga match while present.)
The remaining empty-filter-list candidates are now deliberately disposed as hardening divergences:
Python emits malformed/backend-dependent empty `NodeLabels` fragments (`n:`, `n: AND m:`, or Kuzu
`list_has_all(..., [])`) and empty temporal fragments (`(`, `()`, or dangling `OR` groups), while C#
keeps those shapes as no-op filters via existing `CompiledSearchFilter`/query-builder coverage.
Current audit follow-up closed namespace embedding drift, an in-memory triplet collision drift, the
typed node-delete Saga boundary, the duplicate-passage cross-encoder drift, the base node-delete
scope drift, the entity UUID group-filter drift, the in-memory episodic-metadata drift, the
entity/episodic-edge group-miss drift, the semaphore default-concurrency drift, the entity-type-id key
mapping/schema drift, the base edge-delete scope drift, the eager search-config numeric validation
drift, the negative-limit text truncation drift, the entity/fact prompt type-key drift, and the MMR
candidate-order drift, the edge episode-mentions score-order drift, and the edge-resolution
duplicate-result drift:
namespace `SaveAsync` regenerates entity/community node and entity-edge embeddings even when prefilled,
namespace `SaveBulkAsync` now preserves supplied null/precomputed embeddings without calling the
embedder, and `AddTripletAsync` creates a fresh entity-edge UUID when the default in-memory backend
already has a non-entity edge with the requested UUID. Direct model `DeleteAsync`, base
`Node.DeleteByGroupIdAsync` / `Node.DeleteByUuidsAsync`, and typed node namespaces now use typed
deletion for the Python-scoped node types, so deleting through the wrong Entity/Saga node type or
inherited base helper no longer removes saga nodes across that boundary. `EntityNode.GetByUuidsAsync`
keeps the Python-compatible optional `groupId` parameter but no longer applies it, because Python's
normal entity UUID query filters only by UUID. InMemory no longer persists `EpisodicNode.EpisodeMetadata`,
matching Python's episodic save/projection/record-parser behavior and the Ladybug persistence
shape. `EntityEdge.GetByGroupIdsAsync` and `EpisodicEdge.GetByGroupIdsAsync` now throw
`GroupsEdgesNotFoundException` on empty group results, matching Python's `GroupsEdgesNotFoundError`
branches for those group reads.
`GraphitiHelpers.SemaphoreGatherAsync` now uses Python's default semaphore limit of 20 when callers
omit the cap or pass zero, while rejecting negative direct helper input instead of treating every
non-positive cap as unbounded.
Extracted `entity_type_id` values now map back to the declared `entityTypes` dictionary keys instead
of `EntityTypeDefinition.Name`, so custom type aliases flow into labels and exclusions like Python's
`entity_types` context. Extracted-node response DTOs now also require `entity_type_id` for separate
and combined extraction like Python Pydantic, preserve numeric-string coercion, and still fall back
to `Entity` for valid out-of-range IDs.
Excluded-entity validation error messages now format invalid and available type names like Python's
sorted string-list representation.
Node dedupe label promotion now matches Python's `_promote_resolved_node`: an extracted specific
label upgrades a matched canonical node only when that canonical node is still generic `Entity`;
already-typed existing nodes keep their labels.
Cross-encoder search composition now
deduplicates passage strings in first-seen order and maps ranked passages back to the last duplicate
candidate, matching Python's dict-comprehension behavior. Base `Edge.DeleteByUuidsAsync` now excludes
`HAS_EPISODE`/`NEXT_EPISODE` like Python's inherited base helper, with C# saga repair using concrete
typed deletes instead. Search concurrency proof was also tightened so the fake driver waits for
monotonic search-call arrivals instead of asserting a transient active count.
Search config validation now mirrors Python's lazy numeric use: zero limits return empty results, and
inactive `sim_min_score`/`mmr_lambda`/`bfs_max_depth` values no longer fail BM25/RRF searches before
dispatch.
`TextUtilities.TruncateAtSentence` now mirrors Python's negative `max_chars` slice semantics instead
of throwing before truncation.
Node/edge extraction prompt contexts and entity-summary type-description maps now render custom type
dictionary keys instead of `EntityTypeDefinition.Name`, matching Python's aliased type identity.
MMR node, edge, and community search now feed tied reranker candidates in first-seen retrieval order
instead of preliminary-score order, matching Python's UUID-map input order.
Edge episode-mentions reranking now sorts only returned edges by episode count while leaving the
returned score list in pre-sort RRF order, matching Python's returned tuple behavior.
Edge resolution now appends resolved and invalidated edge appearances directly in input order instead
of deduping by UUID, matching Python's result-list shape.
Top-level community search now supplies Python's zero query vector fallback when no real embedding
path is configured, so BM25/RRF community searches still execute vector retrieval like Python.
Verified with `.\eng\Verify-GraphitiCore.ps1`: restore, format, warning-clean build, full tests
(`1048` passed, `3` skipped, `1051` total), both shippable package packs, and both package-consumer
smoke builds. `OPENAI_API_KEY` was unset; the three skipped tests were the env-gated
`OpenAIProviderIntegrationTests`.

Open WS-2 audit candidate from this mini-pass: decide whether to keep or remove the additive
`CommunityEdgeNamespace.SaveBulkAsync` public helper. A read-only side audit confirmed Python has no
community-edge `save_bulk`, while C# exposes `SaveBulkAsync` publicly and pins it in the API snapshot
plus Ladybug package runtime tests. Treat this as an ask-user public API decision before changing the
surface. A separate read-only maintenance audit found that C# entity attribute definitions do not
currently expose Python's per-field `max_length` and required-field metadata; adding that shape would
also be a public API decision. The concrete non-decision candidates found in this audit mini-pass
have been split into separate slices and closed; continue the broader full-pipeline parity audit
against current Python, and add new candidates here as they are confirmed.

Latest checkpoint, 2026-06-13:

Succeeded after integrating the supervisor-driven parity-hardening pass (review branches `ws/a`
prompts, `ws/b` edge resolution, `ws/c` ingestion/summary, `ws/d` infra, merged to `main`):
`.\eng\Verify-GraphitiCore.ps1` — restore, format verification, warning-clean build including
`Graphiti.Sample.OpenAI`, full test suite (`948` passed, `2` skipped, `950` total), and `dotnet pack`
for `Graphiti.Core.2.0.0-alpha.1.nupkg`. `OPENAI_API_KEY` unset; the 2 skips are the env-gated
`OpenAIProviderIntegrationTests`. See `parity.md` "2026-06-13 parity-hardening pass" and `decisions.md`
for what changed and the accepted divergences.

Real-provider validation, 2026-06-13: the Phase 3 acceptance gate is MET. With the key supplied
locally via `.env` (gitignored), both `OpenAIProviderIntegrationTests` passed against the real OpenAI
API (all structured schemas accepted; real resolved temporal graph) and the 6-episode
`Graphiti.Sample.OpenAI` produced a sane graph (rich summaries, correct bi-temporal invalidation,
relevant reranked search). Re-run with `.\eng\Run-OpenAIProviderValidation.ps1` (auto-loads `.env`).

Plan-05 release-readiness checkpoint, 2026-06-14 (latest direct verification green: build with
`dotnet build -c Release -clp:ErrorsOnly`; test with `dotnet test -c Release --no-build` green on
rerun with 968 passed, 3 skipped, 971 total; format with
`dotnet format --verify-no-changes --no-restore`). The former
`SearchEngineDriverBackedTests.SearchAsync_ExecutesConfiguredScopesConcurrently` timing flake is now
hardened by a fake-driver barrier that proves concurrent scope startup before releasing calls. Steps A–E complete plus the
B2/B3 follow-through: the shippable `Graphiti.Core` and `Graphiti.Core.Drivers.Ladybug` packages now
generate IntelliSense XML docs; the two-assembly public-API snapshot guard remains in
`tests/Graphiti.Core.Tests/Api/` (`PublicApiGenerator` — `Graphiti.Core.approved.txt` and
`Graphiti.Core.Drivers.Ladybug.approved.txt`; regenerate the relevant baseline deliberately on an
intended API change); a consumer `README.md`/`docs/search.md`; surface hardening; the
`GraphProvider.LadybugDb`/`AddGraphiti` names (with `Kuzu`/`AddGraphitiCore` `[Obsolete]` aliases;
`GRPH0001` and `GRPH0002` are locally suppressed only at deliberate compatibility-alias sites); the
InMemory-default constructor + `AddEpisodeOptions`; the
LadybugDB package split; and the retired shared Kuzu branches in generic search helpers. Remaining:
E.2 follow-through on the fork GitHub Packages feed, versioning, full CI. The
`Graphiti.Core`-only GitHub Actions lane is wired via `.github/workflows/core-only.yml`, which runs
`eng\Verify-GraphitiCoreOnly.ps1`: it creates a strict nuget.org-only temp feed,
restores/builds/tests the core-only test slice with `GraphitiCoreOnlyTests=true`, filters out the
OpenAI provider tests, formats/packs `Graphiti.Core`, and excludes the Ladybug test folder and
Ladybug public-API snapshot half without changing the normal full-suite path. See `plans/05`
and `decisions.md`. GOTCHA (still applies): do NOT run multiple worktree agents' `dotnet test`
concurrently — the LadybugDB native package deadlocks across worktrees (1.5h hang on 06-14; recovered
by killing orphaned testhost processes). Have worktree agents build/format-only and run the
consolidated test centrally.

WS-1 pre-bump audit checkpoint, 2026-06-14: direct local verification was green before any pin change
(`dotnet build -c Release -clp:ErrorsOnly`, `dotnet test -c Release --no-build` with 968 passed,
3 skipped, 971 total, and `dotnet format --verify-no-changes --no-restore`). Read-only subagent audits
confirmed the nearby `W:\code\ladybug\tools\csharp_api` `0.17.1` artifacts include the Graphiti
list/array/empty-list/null parameter-binding repair, FTS/vector regression coverage, and Unix
`RTLD_GLOBAL` native-loader work. On 2026-06-17 Graphiti moved to the fork-published
`0.17.1-dev.1.1.g6f3dbed` package family. Do not adopt `LadybugDB.Extensions` without a concrete
Graphiti Core need. Follow-up hardening made the known search concurrency proof deterministic and made
Ladybug file-backed setup idempotent across reopen by ignoring duplicate errors for the four exact
Graphiti FTS indexes; runtime coverage now proves build-write-close-reopen-build-search.
Rechecked 2026-06-17: the nested binding repo is clean on
`feature/parity-extensions-2026-06` at `0e709a0`; the actual local NuGet artifacts/nuspecs are
`0.17.1` despite binding-side `version.txt`/README text saying `0.17.1.0`.

Upstream Python check, 2026-06-17: `.\eng\Check-PythonUpstreamDelta.ps1 -Fetch -FailOnDelta`
confirmed `origin/main` is now `b82b80e4c0c962fc714a22b74caf8c20997e8d83`, and the
log/stat/name-status checks over `0ed90b7..origin/main -- graphiti_core` are empty. There is no
`graphiti_core/` delta to port. The helper implements the Step 1 upstream-sync log/stat/name-status
check and supports `-FailOnDelta`.

Edge-expiry parity follow-up, 2026-06-14: the tracked single-resolution-clock divergence was closed.
`EdgeResolutionService` now uses `Graphiti.UtcNow` for non-fast-path resolved-edge expiry, and
`EdgeMergeHelpers.ResolveEdgeContradictions` calls a clock callback per invalidated candidate. New
edges that only carry extracted/LLM `invalid_at` and have no candidates keep `expired_at = null`,
matching Python's early return.

Ladybug ranker hygiene follow-up, 2026-06-14: the tracked impossible-row ranker issue was closed.
`LadybugSearchExecutor` now ignores distance/episode-mention rank rows whose `uuid` was not in the
requested input set; real per-UUID Cypher already constrains these rows, and the guard keeps mocked or
backend-anomalous rows from surfacing backend-only UUIDs.

Attribution reference-time follow-up, 2026-06-14: the latent edge `reference_time` helper now matches
Python's first-raw-episode-index rule. Valid episode UUIDs are still filtered from all valid
`episode_indices`, but `reference_time` comes only from the first raw index when that first index is
valid; otherwise it falls back to the primary episode.

Node attribution remap follow-up, 2026-06-14: the remaining latent multi-episode node-attribution
difference was closed. C# now keeps node attribution keyed to extracted-node UUIDs through episodic
edge construction, so a resolved canonical UUID mismatch falls back to all provided episodes like
Python's `build_episodic_edges`.

Public-surface audit follow-up, 2026-06-14: a read-only Python-vs-C# audit found no missing core
`Graphiti` public workflow. It did identify tested but under-documented C# public-workflow
divergences; these are now recorded in `decisions.md`/`parity.md`: stronger episode removal with
saga repair, bulk raw-content scrubbing under `storeRawEpisodeContent: false`, explicit/DI drivers
remaining caller-owned on `CloseAsync`/dispose.
The shipped XML docs for `AddEpisodeBulkAsync` were corrected so they no longer state that C# bulk
cross-episode invalidation is less aggressive.

Triplet duplicate parity follow-up, 2026-06-14: the tracked exact-duplicate fast-path drift was
closed. `AddTripletAsync` now reranks raw between-node edges through `EDGE_HYBRID_SEARCH_RRF` before
resolution, and `ResolveEdgeWithLlmAsync` owns the exact duplicate fast path over only the reranked
`relatedEdges` set, matching Python `add_triplet`/`resolve_extracted_edge`.

Follow-up checkpoint, 2026-06-14 (`.\eng\Verify-GraphitiCore.ps1` green: 959 passed, 3 skipped, 962
total; format/build/pack clean). Landed since 06-13: the eval harness (`samples/Graphiti.Eval`) built
to the proposal's graph-building regression design and run live (6/6 no-regression on identical code;
QA mode 3/7 honest, distractor correctly fails); plan-04 follow-ups (full-string golden tests,
missing-endpoint DB fetch, dropped `RELATES_TO` fabrication, cross-encoder golden + smoke, log/test
hygiene); and a benchmark-first performance pass (two measured parity-safe wins). A second adversarial
review of all this work caught and fixed eval-prompt interior trailing spaces, F3 group-scoping, and
the eval's measurement target. Plans 03 and 04 are closed; the two bulk divergences are finalized as
documented (`decisions.md`). Phases 1–3 done; performance moratorium lifted (evidence-driven only).

Previous checkpoint, 2026-06-11:

Succeeded after repairing LadybugDB .NET list/null binding locally, wiring Graphiti to the local
`0.17.0-alpha.2-graphiti.1` package family, removing Graphiti's Ladybug statement normalizer, and
adding package-backed public namespace community/saga read/delete coverage:

```powershell
.\eng\Verify-GraphitiCore.ps1
dotnet test Graphiti.Core.CSharp.slnx --no-restore --no-build --filter "LadybugPackageRuntimeTests" --verbosity minimal
dotnet test Graphiti.Core.CSharp.slnx --no-restore --no-build --filter "LadybugFoundationTests" --verbosity minimal
```

Full verification passed: restore, format, warning-clean build including `Graphiti.Sample.OpenAI`,
full test suite (`941` passed, `2` skipped, `943` total), and `dotnet pack` for
`Graphiti.Core.2.0.0-alpha.1.nupkg`. `OPENAI_API_KEY` was unset; the two skipped tests were
`OpenAIProviderIntegrationTests.StructuredResponseSchemas_WithOpenAIProvider_AreAccepted` and
`OpenAIProviderIntegrationTests.AddEpisodeAsync_WithOpenAIProvider_IngestsResolvedTemporalGraph`.
Focused `LadybugPackageRuntimeTests` also passed with `18` tests, including direct package binding
of Graphiti list/array/empty-list/null parameter shapes and public namespace community/saga group
reads plus typed-delete isolation. `LadybugFoundationTests` passed with `16` tests.

Previous full-suite checkpoint, 2026-06-11:

Succeeded after fixing LadybugDB namespace/model embedding reloads by UUID:

```powershell
.\eng\Verify-GraphitiCore.ps1
```

Restore, format verification, solution build including `Graphiti.Sample.OpenAI`, full test suite
(`942` passed, `2` skipped, `944` total), and `dotnet pack` for
`Graphiti.Core.2.0.0-alpha.1.nupkg`. `OPENAI_API_KEY` was unset; the two skipped tests were
`OpenAIProviderIntegrationTests.StructuredResponseSchemas_WithOpenAIProvider_AreAccepted` and
`OpenAIProviderIntegrationTests.AddEpisodeAsync_WithOpenAIProvider_IngestsResolvedTemporalGraph`.
No live provider run has passed yet. Focused Ladybug statement, shared search-filter, search
executor, and runtime tests also passed:

```powershell
dotnet test Graphiti.Core.CSharp.slnx --filter "FullyQualifiedName~LadybugSearchStatementTests|FullyQualifiedName~SearchFilterTests" --verbosity minimal
```

with `35` Ladybug statement/shared search-filter tests passed.

```powershell
dotnet test Graphiti.Core.CSharp.slnx --filter "FullyQualifiedName~LadybugSearchExecutorTests" --verbosity minimal
```

with `10` Ladybug search executor tests passed.

```powershell
dotnet test Graphiti.Core.CSharp.slnx --filter "FullyQualifiedName~LadybugRuntimeDriverTests" --verbosity minimal
```

with `16` Ladybug runtime tests passed.

```powershell
dotnet test Graphiti.Core.CSharp.slnx --filter "FullyQualifiedName~LadybugPackageRuntimeTests" --verbosity minimal
```

with `17` Ladybug package-runtime tests passed, including actual package/native proof for direct
driver bulk-save embedding/relationship persistence, namespace/model embedding reloads by UUID,
saga-scoped retrieval, saga content filtering/order/limit behavior, paged node/edge group reads,
directed endpoint-pair edge reads, incident entity-edge reads, and group-id enumeration. Focused
edge-attribute and telemetry tests also passed earlier:

```powershell
dotnet test Graphiti.Core.CSharp.slnx --no-build --filter "FullyQualifiedName~GraphitiWorkflowTests.AddEpisode_HydratesDeclaredEdgeAttributes|FullyQualifiedName~GraphitiWorkflowTests.AddEpisode_NonFastDuplicateEdgeAttributeHydrationDropsOverlongStringsAndReplacesOmittedFields|FullyQualifiedName~GraphitiWorkflowTests.AddEpisode_ExactDuplicatePreservesExistingEdgeAttributesAndSkipsEdgeAttributePrompt|FullyQualifiedName~GraphitiWorkflowTests.AddEpisode_ReusesEdgeAttributeSchemaForSameTypeBatch|FullyQualifiedName~GraphitiWorkflowTests.AddEpisode_EdgeAttributeExtractionRunsDuringResolution|FullyQualifiedName~GraphitiWorkflowTests.AddEpisode_SkipsEdgeAttributePromptWhenTypeMapDoesNotMatchEndpoints" --verbosity minimal
dotnet test Graphiti.Core.CSharp.slnx --no-build --filter "FullyQualifiedName~TelemetryTests.Graphiti_EmitsActivitiesForIngestionAndSearch" --verbosity minimal
```

with `6` workflow tests and `1` telemetry test passed.

Live OpenAI validation helper:

```powershell
.\eng\Run-OpenAIProviderValidation.ps1
```

It requires `OPENAI_API_KEY`, runs restore/build, the focused OpenAI integration tests, and the
OpenAI sample. No-key behavior exits `2`; no live provider run has passed yet.

Sample no-key path was also verified:

```powershell
dotnet run --project samples\Graphiti.Sample.OpenAI\Graphiti.Sample.OpenAI.csproj --no-restore
```

with `OPENAI_API_KEY` unset. It prints setup guidance and exits `2` as expected. No real-provider
run has ever been executed (plan 03).

Primary full verification command from the C# repo root:

```powershell
.\eng\Verify-GraphitiCore.ps1
```

Use `-FocusedFilter "FullyQualifiedName~..."` to run a VSTest-style focused filter before the full
restore/format/build/test/pack pass.

Equivalent manual commands:

```powershell
dotnet restore Graphiti.Core.CSharp.slnx --locked-mode
dotnet format Graphiti.Core.CSharp.slnx --verify-no-changes --verbosity minimal
dotnet build Graphiti.Core.CSharp.slnx --no-restore --no-incremental --verbosity minimal
dotnet test Graphiti.Core.CSharp.slnx --no-build --verbosity minimal
dotnet pack src\Graphiti.Core\Graphiti.Core.csproj --configuration Release --no-restore --verbosity minimal
dotnet pack src\Graphiti.Core.Drivers.Ladybug\Graphiti.Core.Drivers.Ladybug.csproj --configuration Release --no-restore --verbosity minimal
```

The package-consumption smoke is part of normal `.\eng\Verify-GraphitiCore.ps1`; use
`-SkipPackageSmoke` only for non-packaging iteration.

If a slice repeatedly needs the same focused tests plus broader build/test/format checks, create a
small helper script and run that sequence through the script. Commit the helper only when it is useful
beyond a single throwaway investigation.

## Working Constraints

- The repo may have parallel agent/user edits. Do not revert unrelated changes.
- Prefer focused tests or isolated helpers when other agents are editing hot files.
- For parity investigations, verify against current Python symbols rather than stale line numbers.
- Keep response DTO type identity stable when it participates in structured LLM schema/cache keys.
- Preserve active-driver scoping through `UseGroupDriver` / `AsyncLocal`.
- Treat implicit allocations as part of the review surface in shared ingestion, search, parsing,
  serialization, embedding/vector, and provider paths.
- Keep note updates scoped: durable decisions in `decisions.md`, milestone history in
  `evolution.md`, current state or gotchas here, phase plan in `roadmap.md`, parity ground truth in
  `parity.md`, executable work orders in `.agents/plans/`, provider-specific details in
  `kuzu-driver-port.md`, and commit rules in `commit-policy.md`.

## Known Audited Areas

The detailed invariant coverage is in tests and git history. These areas have had focused parity or
allocation-sensitive coverage and should not be casually rewritten without targeted tests:

- Search ranking/fusion/reranking: RRF, MMR, cross-encoder ordering, node-distance,
  episode-mentions, fallback BM25, vector scoring, BFS origins, and result splitting.
- Driver/reference behavior: in-memory deterministic indexes/cloning/search, materialized fallback
  snapshots, and Ladybug statement/mapper/executor shapes.
- Ingestion and maintenance: extraction parsing, node/edge dedupe, invalidation windows, episode
  removal, saga association/summarization, community build/rebuild/update, and bulk ingestion.
- Serialization and provider infrastructure: structured LLM schema/cache identity, response-cache
  payload clone isolation, token usage, embedding validation/materialization, rate limiting,
  throttled work helpers, Polly resilience, and OpenTelemetry spans.
- Text utilities: token counting, chunking, dense-text detection, sentence truncation, and
  episode-concatenation formatting.

## Notes Coordination Protocol

- At task start, read the relevant note(s). For broad port work, read `decisions.md`, `handoff.md`,
  `roadmap.md`, and `evolution.md`; for provider work, also read `kuzu-driver-port.md`.
- Before finalizing work that changes direction, architecture, provider status, verification claims,
  milestone status, or roadmap scope, re-read or search the affected notes.
- If the newest user instruction conflicts with the notes, follow the user and update the notes so
  future agents do not inherit stale direction.
- Prefer replacing stale guidance over preserving contradictory history.
