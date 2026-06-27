# C# Port Handoff

This is the working handoff for agents continuing the C# Graphiti Core port. Keep it current-state
focused; do not turn it back into a per-commit changelog.

## Current Goal

`csharp/src/Graphiti.Core` is **our own** embeddable C# library whose behavior is derived from Python
`graphiti_core/`: temporal context graphs for AI agents with episode ingestion, entity/fact extraction,
deduplication, invalidation, communities, sagas, and hybrid search. It is consumed **in-process** as an
internal subsystem (no MCP/REST host), will most likely be **renamed**, and is **not** a release-bound
package — read `decisions.md` → "What this project is (paradigm)" before picking work.

Python is the **functional contract** (behavioral / feature / wire parity), and parity is essentially
complete. The forward agenda is now the **code itself: idiomatic modern C# (C# 14 / .NET 10, toward
.NET 11) + allocation/GC discipline**, done parity-safely — behavior, wire values, schema/cache identity,
and public surface unchanged; readability changes warning-clean; hot-path allocation changes
benchmark-first. Release versioning/publishing is parked (user-gated), not a goal.

Provider work is focused on LadybugDB. InMemory is the deterministic reference/test backend. Neo4j was
removed 2026-06-17 and is no longer a provider. The focused provider state
lives in `kuzu-driver-port.md`; do not duplicate its proof matrix here.

> ⚠ **Supervisor review (paradigm shift, 2026-06-27):** Sergey re-set the project's purpose — this is an
> embeddable internal library we maintain ourselves, likely to be renamed, **not** a product we are
> shipping. So release/publishing is parked and the active work is idiomatic-modernization +
> allocation-reduction in the code (`decisions.md` "What this project is"; current work order
> `.agents/plans/10-idiomatic-allocation-modernization.md`). Standing constraints unchanged: CI stays
> as-is (do not expand); LadybugDB feed is GitHub-Packages-only (credential required); the code stays
> textually de-coupled from Python (`decisions.md` "Parity without Python coupling in the code");
> behavioral parity is enforced by tests + `parity.md`. The whole 2026-06-19 G1–G6 agenda and plans
> 05–08 are complete and the suite is green; plan 09 (robustness) is deferred below the modernization
> stream.

## Current Layout

- `Graphiti.cs` and `Graphiti.*.cs`: public orchestrator, lifecycle, ingestion, search, removal,
  saga, community, infrastructure, and extraction parsing partials.
- `Models/`: node, edge, result DTO, entity type, entity attribute, and episode type models.
- `Drivers/` (in `Graphiti.Core`): the driver contract/base (`IGraphDriver`, base driver), the
  deterministic in-memory reference/test driver, the provider enum, saga episode content, and the
  built-in LadybugDB driver under `Drivers/Ladybug/`.
- `Configuration/`: options, validators, DI registration, cache/resilience settings, and
  `LadybugDbOptions` / `AddLadybugDbGraphDriver`.
- `Namespaces/`: node and edge namespace facades over drivers.
- `Search/`: search configs/results, hybrid search engine, rerankers, filter builders/matchers,
  fallback graph materialization, search-result composition, and search-driver retrieval adapter.
- `Maintenance/`: entity deduplication and community clustering.
- `Text/`: chunking, token counting, text helpers, and Graphiti helper functions.
- `LlmClients/`, `Embedding/`, `CrossEncoder/`: provider abstractions, Microsoft.Extensions.AI
  adapters, deterministic/test implementations, cache/usage helpers, and rerankers.
- `Telemetry/`: `ActivitySource` spans, `Meter` metrics, and source-generated logging.
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
  message, while retry feedback and prompt labels stay out of the cache key and only final
  validated responses are cached. Edge attribute extraction was aligned 2026-06-11: C# no longer runs a separate
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
  `.\eng\Run-OpenAIProviderValidation.ps1` (auto-loads `.env`). See the Verification section below.
- **Eval harness: BUILT and run live (2026-06-14).** The harness shipped as `samples/Graphiti.Eval`
  with a graph-building regression design and was run live (6/6 no-regression on identical code; QA
  mode 3/7 honest, distractor correctly fails).
- **Phases 1–3 are DONE.** The performance/allocation moratorium is LIFTED; further performance work
  is evidence-driven (benchmark-first) only (`roadmap.md`).
- Work selection rule: follow `.agents/plans/` in order (see AGENTS.md "Current priority"). Phases
  1–3 are complete, plans 05–08 are complete, and the whole 2026-06-19 G1–G6 agenda is done (parity,
  merge, observability, linux-x64 proof, live-provider canary, the G3 perf program, sustained upstream
  reminder, and the non-gated release-surface finalization). Per the 2026-06-27 paradigm shift, the
  library is **not** release-bound, so release-versioning/publishing is parked. Plan 10 is complete.
  **The current actionable plan is `.agents/plans/09-robustness-hardening.md`, starting with item A:
  map the fragile real-LLM parsing/coercion boundary.**
  Full restore/test/pack requires GitHub Packages credentials for source `github_ladybug`. Performance
  work is benchmark-first and no longer on moratorium (`roadmap.md`).
- Decomposition context: `Graphiti` is the public orchestrator; behavior lives in partials plus
  internal services and helpers. Search boundaries: `SearchEngine` orchestrates,
  `SearchRetrievalRunner` retrieves, `SearchResultComposer` shapes results. Prompt builders live
  in `Prompts/` (one static class per Python prompt module).
- Optional local `.agents/skills` files are specialist references only. Use them for matching tasks,
  but do not let generic AI/ML/framework advice override `decisions.md`.

Latest plan-folder/backlog audit, 2026-06-26: plan 06's prerequisite sweep found no unchecked concrete
implementation item outside plan 06 itself and already-recorded decision-gated release/API items. The
Plan 06 merge then moved the Ladybug driver into `Graphiti.Core`, folded the options/DI helper/factory
and Ladybug package refs into Core, collapsed the public API snapshot to one assembly, retired
`GraphitiCoreOnlyTests` / `eng\Verify-GraphitiCoreOnly.ps1` / `.github/workflows/core-only.yml`, and
changed the package smoke to exercise both InMemory and LadybugDB from the packed `Graphiti.Core`
package. Latest 2026-06-26 upstream audit: `Check-PythonUpstreamDelta.ps1 -Fetch -FailOnDelta`
reported no `graphiti_core/` upstream delta from anchor `0ed90b7` to target
`413b9b2e140e22f4a6d155b30ddc9779a3d47fe2`. The current concrete search-filter drift is now closed:
the reference/materialized matcher requires every requested non-empty node label like the Ladybug/Kuzu
`list_has_all` provider predicate, including on both edge endpoints. The empty-node-label hardening
divergence remains unchanged. The follow-up edge-resolution block-order drift is also closed:
resolved edges now return before invalidated chunks like Python's `resolved_edges + invalidated_edges`
single-ingestion shape. The lower-level edge-embedding endpoint-scope drift is also closed: Ladybug,
InMemory, and materialized search now ignore endpoint filters when `groupIds` is null, matching
Python's Kuzu path. The materialized fallback ranker scope drift is also closed: node-distance and
episode-mentions ranker evidence is no longer limited to the candidate retrieval groups, matching
Python ranker queries. The model/wire audit found no safe internal code slice: UUIDv7 generation and
deterministic epoch/empty-string model defaults are test-pinned C# public-surface behavior; aligning
them to Python uuid4/required/ambient-time model construction is decision-gated and separate from
plan 06 or release infrastructure. The text utility audit also closed the heuristic chunking
under-split: `HeuristicTokenCounter` now drives character-window budget checks and overlap boundaries,
while deterministic large covering chunks remain documented C# hardening. The incremental-community
audit is also closed as a documented C# repair: `AddEpisodeAsync(updateCommunities: true)` returns
flattened community-update results instead of reproducing the source workflow's broken per-node
destructuring. A follow-up moved-docs/backlog audit found no remaining unchecked implementation item
outside plan 06's Ladybug merge and already-recorded decision-gated items. The package-feed
recheck found the old `0.17.1-dev.1.1.g6f3dbed` package pin before plan 07 published and consumed
`0.17.1-dev.2.1.g53e5ab5`. The only concrete
slice from this pass was test-only hardening for the search concurrency proof: after the fake-driver
barrier has proven concurrent startup, it now waits on the xUnit cancellation token instead of a
second fixed wall-clock timeout. A follow-up ontology-matching audit is also closed: custom entity
and edge type resolution now uses exact ontology keys/signatures, so case variants and type-name
aliases do not select custom attribute schemas. A follow-up search public/extensibility audit found no
result-composition code slice: fresh C# search recipe instances and BFS guard-skipped custom driver
calls are documented C# API hardening decisions. Plan 06 is complete (2026-06-26).

G4 is complete (2026-06-26): `GraphitiTelemetry` now exposes a public `Meter` alongside the existing
`ActivitySource`, with counters/histograms for episodes ingested, ingestion/search duration,
ingestion/search result counts, LLM token usage, and LLM response-cache hit/miss lookups. Core still
has no OpenTelemetry exporter dependency. Consumer DX additions are `docs/observability.md`,
`samples/Graphiti.Sample.Observability`, `samples/Graphiti.Sample.Quickstart`, and
`samples/Graphiti.Sample.GenericProvider`.

G3 first benchmark-baseline slice is complete (2026-06-26): `IngestionBenchmarks` now covers
single-episode ingestion, six sequential episodes, six-episode bulk ingestion, and a bulk ingest plus
search workflow with `[MemoryDiagnoser]`. The committed local win-x64 ShortRun baseline is
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-26-ingestion-win-x64.md`. No performance changes
were made in this slice; future wins should compare BenchmarkDotNet before/after and update/add
baselines only with fresh measurements.

G3 BM25 allocation slice is complete (2026-06-26): `Bm25TextScorer` now scans document tokens via the
span-token visitor and materializes only matching query tokens. Local ShortRun before/after for
`SearchBenchmarks.Bm25_Rank` dropped allocations from 182.16 KB to 88.21 KB at 200 candidates and from
444.94 KB to 207.01 KB at 500 candidates; see
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-26-search-bm25-win-x64.md`.

G3 uncached LLM cache-key slice is complete (2026-06-26): `LlmClient` skips cache-key JSON
serialization and SHA-256 hashing when no response cache is configured. Local `IngestionBenchmarks`
ShortRun before/after showed allocation drops across all four cases; see
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-26-uncached-llm-cachekey-win-x64.md`.

G3 bulk throttling list-copy slice is complete (2026-06-26): private throttling helpers now accept
`IReadOnlyList<T>`, removing two `AddEpisodeBulkAsync` `.ToList()` copies. The local before/after
allocation change is small and recorded in
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-26-bulk-throttling-list-win-x64.md`.

G3 RRF queue-capacity slice is complete (2026-06-26): `TopByScoreCore` now pre-sizes its bounded
priority queue when candidate count is known. Local `SearchBenchmarks.Rrf_*` ShortRun before/after
showed allocation drops across all six RRF cases; see
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-26-search-rrf-queue-win-x64.md`.

G3 TextScorer allocation slice is complete (2026-06-26): short-query distinct-match tracking now uses a
64-bit mask and keeps the existing hash-set fallback for very large query term sets. Local
`SearchBenchmarks.TextScorer_ScoreAll` ShortRun before/after dropped benchmark allocations from
55.84 KB to 0 B at 200 candidates and from 139.29 KB to 0 B at 500 candidates; see
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-26-search-textscorer-win-x64.md`.

G3 serialization baseline slice is complete (2026-06-26): `SerializationBenchmarks` now has a
committed win-x64 ShortRun baseline covering cache-key serialize/hash/hex and Graphiti JSON
serialize/parse/deep-clone paths. No implementation change was made in this slice; see
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-26-serialization-win-x64.md`.

G3 InMemory vector-search baseline slice is complete (2026-06-27):
`InMemoryVectorSearchBenchmarks` now covers the deterministic reference driver's full-scan node
embedding search, including filter checks, top-k selection, and final-hit cloning, with
`[MemoryDiagnoser]`. No implementation change was made in this slice; see
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-27-inmemory-vector-win-x64.md`.

G3 MMR merge allocation slice is complete (2026-06-27): `SearchBenchmarks` now covers
`Mmr_MergeCandidatesInFirstSeenOrder`, and `MergeCandidatesInFirstSeenOrder` keeps first-seen results
directly while updating duplicate scores through a key-to-result-index dictionary. Local ShortRun
before/after dropped allocations from 28.7 KB to 20.56 KB at 200 candidates and from 72.03 KB to
51.66 KB at 500 candidates; see
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-27-search-mmr-merge-win-x64.md`.

G3 bulk edge-dedupe baseline slice is complete (2026-06-27): `BulkEdgeDedupeBenchmarks` now covers a
public bulk-ingestion workflow with many extracted facts spread across endpoint pairs. No
implementation change was kept: an endpoint-pair bucketing trial did not show a material win in the
same workflow (16-pair case 10.016 ms to 9.972 ms, allocations unchanged). See
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-27-bulk-edge-dedupe-win-x64.md`.

Plan 10's first Tier 1 slice is complete (2026-06-27): community summary/name deterministic fallback
text is now computed only in the NoOp fallback branch, with no prompt/schema/cache/public API change.
The focused community suite and the full verifier were green.

Plan 10 LLM clean-input slice is complete (2026-06-27): `LlmClient.CleanInput` now uses a
`SearchValues<char>` clean-path check with surrogate validation; local ShortRun before/after dropped
clean prompt-message checks from 3.967 us to 228.5 ns (ASCII) and 3.414 us to 309.9 ns (Unicode), with
dirty cleanup allocation unchanged. See
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-27-llm-cleaninput-win-x64.md`.

Plan 10 LLM prepare-messages slice is complete (2026-06-27): `PrepareMessages` now keeps a new
prepared list but aliases unchanged immutable `Message` records until a `with` replacement is needed.
Local ShortRun before/after for clean no-schema preparation dropped from 628.8 ns / 800 B to
498.6 ns / 672 B. See
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-27-llm-preparemessages-win-x64.md`.

Plan 10 bulk edge word-overlap slice is complete (2026-06-27): bulk edge dedupe now builds the
left-fact word set once per edge and span-scans candidate facts, preserving the existing split/trim
semantics. Local ShortRun allocations dropped from 4.95 MB to 4.52 MB at 8 endpoint pairs and
11.18 MB to 10.39 MB at 16 endpoint pairs, while wall-clock timings remained noisy. See
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-27-bulk-edge-wordoverlap-win-x64.md`.

Plan 10 episode-attribution search copy slice is complete (2026-06-27): `GetNodesAndEdgesByEpisodeAsync`
now passes the fetched episode list directly into throttled edge lookup instead of materializing a
second list; focused episode-attribution tests and the full verifier were green.

Plan 10 search group-sentinel slice is complete (2026-06-27): `SearchEngine.SearchAsync` now checks the
single empty-group sentinel with direct count/index access instead of allocating an array and using
LINQ; focused search-engine suites and the full verifier were green.

Plan 10 Ladybug embedding-load slice is complete (2026-06-27): namespace embedding loads now read the
projected embedding column directly instead of mapping full entity nodes/edges. Local ShortRun
before/after dropped node record mapping from 795.4 ns / 1.66 KB to 108.9 ns / 312 B and edge record
mapping from 730.5 ns / 1.66 KB to 109.0 ns / 312 B. See
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-27-ladybug-embedding-load-win-x64.md`.

Plan 10 attribute extraction slice is complete (2026-06-27): `AttributeMerger` now probes declared
attribute names directly from the response `JsonObject` instead of copying all response attributes into
a temporary dictionary; focused AttributeMerger tests and the full verifier were green.

Plan 10 content overlap slice is complete (2026-06-27): speaker-message overlap collection now uses
append plus one reverse instead of repeated front insertion, matching the sibling JSON overlap helpers;
focused content-chunking tests and the full verifier were green.

Plan 10 snapshot-helper slice is complete (2026-06-27): `GraphitiHelpers` now uses collection
expressions for non-`ICollection` embedding/operation snapshots and the duplicate copy helpers are
gone; focused helper/concurrency tests and the full verifier were green.

Plan 10 dead-code slice is complete (2026-06-27): removed unused `Text.Helpers` whitespace regex
generation and unused in-memory saga/group helper methods after reference checks; focused
Graphiti helper / in-memory saga / routing tests and the full verifier were green.

Plan 10 AppendEpisode slice is complete (2026-06-27): episode concatenation now appends the
non-negative loop index directly to the `StringBuilder` instead of allocating an invariant string.
Exact-header text tests, a non-invariant culture probe, and the full verifier were green.

Plan 10 Ladybug parameter-map slice is complete (2026-06-27): the two local Ladybug
`Parameters(...)` helpers now use `params ReadOnlySpan<(string, object?)>` while preserving fresh
mutable `StringComparer.Ordinal` dictionaries. Local ShortRun allocations dropped by 40 B for
one-parameter maps, 72 B for three-parameter/vector maps, and 616 B for repeated rank maps; BFS was
unchanged because it uses `SearchParameters`. See
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-27-ladybug-statement-builder-win-x64.md`.

Plan 10 Ladybug delete statement slice is complete (2026-06-27): broad and typed non-Entity node
delete paths now use single-statement builders instead of allocating one-element statement arrays
and indexing `[0]`; Entity deletes still use the two-statement RelatesTo cleanup path. Focused
Ladybug builder/driver tests and the full verifier were green.

Plan 10 bulk edge override slice is complete (2026-06-27): bulk final edge resolution now passes
`allEdgesByUuid.Values` as the existing-edge override collection instead of copying dictionary values
for every episode. This is safe while the per-episode final resolution loop remains sequential; if
that loop is parallelized, reassess the live `Dictionary.ValueCollection` handoff. Local ShortRun
allocations for `BulkEdgeDedupeBenchmarks` dropped from 10.41 MB to 10.38 MB at 16 endpoint pairs
(8-pair case rounded unchanged at 4.52 MB). See
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-27-bulk-edge-override-values-win-x64.md`.

Plan 10 bulk node dedupe slice is complete (2026-06-27): final bulk node dedupe now keeps a
first-admission normalized-name index and canonical node list, avoiding repeated normalized scans
and canonical-value snapshots while preserving first input winner semantics. Focused bulk workflow
coverage pins out-of-order extraction completion. Local ShortRun allocations for
`BulkEdgeDedupeBenchmarks` dropped from 4.52 MB to 4.46 MB at 8 endpoint pairs and 10.31 MB to
10.05 MB at 16 endpoint pairs. See
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-27-bulk-node-dedupe-win-x64.md`.

Plan 10 edge endpoint lookup slice is complete (2026-06-27): extracted-edge candidate preparation
now builds one `StringComparer.Ordinal` lookup from the node map's enumerated keys/current values
instead of scanning the case-insensitive map for every source/target endpoint. This preserves exact
case-sensitive endpoint membership, the `OrdinalIgnoreCase` dictionary update nuance, and the
empty-episode fast return. Local ShortRun for `EdgeResolutionBenchmarks` dropped from 584.1 us to
187.7 us at 64 nodes / 512 edges and from 1,640.8 us to 188.9 us at 256 nodes / 512 edges, with
allocations down about 48-54 KB. See
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-27-edge-resolution-endpoint-lookup-win-x64.md`.

Plan 10 embedding materialization slice is complete (2026-06-27): embedding vector validation now
fills pre-sized `List<float>` backing storage, and `MicrosoftExtensionsAIEmbedderClient.CreateBatchAsync`
returns its validated ordered array directly instead of copying into a second list. Existing provider
tests pin defensive provider-vector copies, count/dimension/non-finite validation, chunk ordering,
rate limiting, cancellation, and concurrency. Local ShortRun for `EmbeddingBenchmarks` dropped
single-vector materialization from 265.7 ns to 186.9 ns at 256 dimensions and 1,001.3 ns to
633.9 ns at 1024 dimensions; the adapter batch fixture dropped from 20.10 us to 8.90 us at 256
dimensions and 46.48 us to 33.59 us at 1024 dimensions. See
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-27-embedding-materialization-win-x64.md`.

Plan 10 MMR dimension-check slice is complete (2026-06-27): `SearchUtilities` now validates non-empty
candidate/query vector dimensions once before MMR scoring, then uses zero-or-known-same-dimension dot
products inside the pairwise and relevance loops. Focused tests pin candidate mismatch, query
mismatch, and empty-vector zero-similarity behavior. Local ShortRun for `SearchBenchmarks.Mmr_Rerank`
dropped from 386.6 us to 379.0 us at 200 candidates and 2,282.1 us to 2,222.8 us at 500 candidates,
with allocations unchanged. See
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-27-search-mmr-dimension-win-x64.md`.

Plan 10 lookup/list pre-sizing slice is complete (2026-06-27): `SearchFallbackGraph` now pre-sizes
entity/episodic edge adjacency dictionaries to the edge snapshot count and starts per-source lists at
capacity 1; `MaintenanceUtilities.BuildEpisodicEdges` now estimates result-list capacity from mapped
or default episode links. This is a behavior-neutral capacity hint slice; focused maintenance and
search fallback/traversal tests plus the full verifier were green.

Plan 10 MinHash UTF-8 slice is complete (2026-06-27): entity-node fuzzy dedupe now encodes each
shingle once per MinHash signature and reuses those bytes across the 32 seed hashes, while preserving
the exact existing `"{seed}:{shingleUtf8Bytes}"` hash payload and therefore LSH bucket values.
Focused entity-deduplication tests and the fuzzy AddEpisode workflow were green. Local ShortRun for
`EntityDeduplicationBenchmarks` dropped from 4.308 ms to 4.044 ms at 64 nodes and 19.822 ms to
18.856 ms at 192 nodes with allocations unchanged. See
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-27-entity-dedupe-minhash-win-x64.md`.

Plan 10 small allocation-hoist slice is complete (2026-06-27): `EntitySummaryService.ApplySummaries`
returns before building the name lookup when there are no nodes or summaries; `EpisodeGraphExtractor`
merges attribution indices via list sort plus existing `DistinctSorted` instead of `SortedSet`; and
`NodeResolutionService` reuses one single-entry group-id array while awaiting each node candidate
search. Focused summary/extractor/node-dedup tests plus the full verifier were green.

Plan 10 namespace vector-copy de-dup slice is complete (2026-06-27): `NamespaceDriverHelpers` now
uses the shared `EmbeddingVectorValidation.CopyNullableVector` helper for loaded node/edge
embeddings instead of carrying a duplicate local copy loop. This is behavior-neutral; focused
namespace/fallback tests and the full verifier were green.

Plan 10 collection-expression sweep is complete (2026-06-27): the listed copy/array targets now use
collection expressions, including graph list copies, dedupe bucket snapshots, Ladybug schema/statement
arrays, singleton embedding-load inputs, node-label empty/singleton lists, and prompt/cross-encoder
message arrays. Prompt text was not changed. Focused prompt/extraction/Ladybug/namespace/model tests
and the full verifier were green.

Plan 10 static defaults / single-pass aggregation slice is complete (2026-06-27): default LLM cache
tags are shared through `GraphitiCacheOptions.DefaultLlmResponseTags`; cache-tag validation now uses a
single loop; `TokenUsageTracker.GetTotalUsage` totals input/output tokens in one checked pass; and
`GraphDriverBase` reuses the default-group singleton list. Focused config/cache/token/group-id tests
and the full verifier were green.

Plan 10 Lock / loop-invariant slice is complete (2026-06-27): `EdgeResolutionService` now uses
`System.Threading.Lock` for the shared synchronous edge-mutation gate, and
`EdgeMergeHelpers.ResolveEdgeContradictions` normalizes the resolved edge's valid/invalid timestamps
once per call instead of once per candidate. Focused edge-resolution/merge tests and the full verifier
were green.

Plan 10 InMemory shared-store slice is complete (2026-06-27): `InMemoryGraphDriver` clone state now
flows through one nested `SharedStore` object instead of a long private constructor argument list,
while preserving the existing shared mutable dictionaries/lock semantics between clones. Focused
in-memory/clone/routing tests and the full verifier were green.

Plan 10 TextUtilities concatenate-overload slice is complete (2026-06-27): both
`TextUtilities.ConcatenateEpisodes` overloads now share one generic helper with static-lambda
projections while preserving the single-episode fast path, timestamp formatting, blank-line separators,
and capacity estimate. Focused text/prompt tests and the full verifier were green. This completes the
Plan 10 checklist; roadmap now points next work back to the outstanding non-release robustness stream.

Plan 09 Step 0a is complete (2026-06-27): the HNSW gate is closed for the current InMemory
reference/test backend target. The committed exact full-scan node-vector benchmark baseline measured
104.5 us at 500 candidates and 387.4 us at 2,000 candidates, so exact cosine remains the default and an
opt-in approximate tier should only be reopened if future larger-target benchmarks prove a bottleneck.

Plan 09 Step 0b is complete (2026-06-27): `eng/Invoke-UpstreamDeltaReminder.ps1` is the committed G5
artifact. It wraps `Check-PythonUpstreamDelta`, fetches by default, warns on `graphiti_core` deltas, and
exits `0` for both no-delta and delta cases so Sergey can wire it through a scheduled task, cron, or
manual dispatch without adding a CI lane. The next unchecked plan item is the robustness risk map.

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
`0.17.1-dev.2.1.g53e5ab5` from the `sergey-v9/ladybug-dotnet` GitHub Packages feed via
`NuGet.config`; that binding supports Graphiti's list/array/empty-list/null parameters directly, so
the former `LadybugStatementNormalizer` workaround has been removed. Restores that include the
Ladybug driver require a NuGet credential for source `github_ladybug` with `read:packages`.

Plan 07 linux-x64 proof is complete. The original failure was an FTS extension undefined-symbol error
because the binding resolver did not globally load the NuGet `runtimes/linux-x64/native/liblbug.so`
asset before extension `dlopen`; `LD_PRELOAD` of that file proved the classification. The fix was
committed in `W:\code\ladybug\tools\csharp_api` as `53e5ab5`, published by the fork dev workflow, and
consumed here. Graphiti has a gated linux-x64 `fts` + `vector` create/query smoke tagged
`Category=LinuxLadybugSmoke`; the workflow job is disabled until repo variable
`GRAPHITI_ENABLE_LINUX_LADYBUG_SMOKE=1` is set.

For provider status, package facts, package bug recovery, runtime proof, and remaining work, read
`kuzu-driver-port.md`. If implementation uncovers a likely LadybugDB package/binding issue, mark it
separately from Graphiti port gaps. The current user-approved recovery path is to patch and commit
the fix in `W:\code\ladybug`, push the fork's `dev` branch when a fresh package is needed, let the
`sergey-v9/ladybug-dotnet` workflow publish a new GitHub Packages dev version, and bump Graphiti to
that published version.

## Verification

Rerun verification before claiming the tree is green; historical test counts drift as coverage is
added. This section holds the single authoritative live count and the standing verify commands — do
not turn it back into a per-checkpoint changelog (git history holds the slice-by-slice detail).

**Current verifier checkpoint (2026-06-27):** `.\eng\Verify-GraphitiCore.ps1` is green with GitHub
Packages credentials for the Ladybug feed — `1043` passed, `4` skipped, `1047` total. The verifier
covers restore, format verification, warning-clean build, full tests, `dotnet pack` for the single
shippable `Graphiti.Core` package, and a fresh package-consumer smoke that exercises both InMemory and
LadybugDB through the packed package. The skips are the env-gated
`OpenAIProviderIntegrationTests`, which skip cleanly when `OPENAI_API_KEY` is unset, plus the
Linux-only LadybugDB extension smoke when running on win-x64.

The gated linux-x64 LadybugDB extension smoke also passed 2026-06-26 in WSL against the published
`0.17.1-dev.2.1.g53e5ab5` GitHub Packages feed from a clean NuGet cache, with
`GRAPHITI_RUN_LINUX_LADYBUG_SMOKE=1` and no `LD_PRELOAD`: `1` passed, `0` skipped, `0` failed.

This is the one authoritative live count for the repo; other notes (`parity.md`, plan-05,
`kuzu-driver-port.md`) say "full suite green" and point here rather than embedding their own counts.

**Live OpenAI validation:** the Phase 3 real-provider gate PASSED 2026-06-13 and is re-runnable. With
`OPENAI_API_KEY` supplied locally via gitignored `.env`, both `OpenAIProviderIntegrationTests` pass
against the real OpenAI API (all structured schemas accepted; real resolved temporal graph) and the
6-episode `Graphiti.Sample.OpenAI` produces a sane graph (rich summaries, correct bi-temporal
invalidation, relevant reranked search). Re-run with `.\eng\Run-OpenAIProviderValidation.ps1`
(auto-loads `.env`, runs restore/build, the focused OpenAI integration tests, and the OpenAI sample;
exits `2` when the key is absent). Running the sample directly without the key
(`dotnet run --project samples\Graphiti.Sample.OpenAI\Graphiti.Sample.OpenAI.csproj --no-restore`)
prints setup guidance and exits `2` as expected.
The dedicated `.github/workflows/live-provider.yml` workflow now runs the provider validation and
fail-loud eval canary on `workflow_dispatch` plus a weekly schedule; it requires `OPENAI_API_KEY` and
does not change normal PR CI skip behavior.
The same live G2 loop passed locally on 2026-06-26 using `.env`: `OpenAIProviderIntegrationTests`
`3/3`, OpenAI sample completed, graph-building eval was `6/6` not-worse against the committed
baseline, and retrieval-QA was `4/7` with the distractor correctly failing.

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
