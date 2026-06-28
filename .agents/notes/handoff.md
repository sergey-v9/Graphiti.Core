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

> ⚠ **Supervisor review (2026-06-28):** Sergey's 2026-06-27 paradigm holds — this is an embeddable
> internal library we maintain ourselves, likely to be renamed, **not** a product we are shipping;
> release/publishing is parked (`decisions.md` "What this project is"). **The backlog is now exhausted:
> the whole 2026-06-19 G1–G6 agenda and work-order plans 05–11 are complete and the suite is green.** The
> realistic forward posture is **maintenance** — track the Python upstream for parity
> (`upstream-sync-procedure.md`) and apply opportunistic parity-safe modernization as the language moves;
> a new direction comes from Sergey. Standing constraints unchanged: CI stays as-is (do not expand);
> LadybugDB feed is GitHub-Packages-only (credential required); the code stays textually de-coupled from
> Python; behavioral parity is enforced by tests + `parity.md`.

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

Verified against the Python baseline (anchor in `parity.md`); the suite is green at the verification
checkpoint below. The library is **complete and mature** — `parity.md` holds the row-by-row parity
truth and `decisions.md` the documented divergences; this is the current-state digest only.

- **Parity (Phases 1–3) complete:** prompts ported with golden tests; the full ingestion pipeline
  (entity summaries, constrained LLM-failure fallbacks, broad invalidation, multi-episode attribution,
  true-batch bulk, validation-failure re-prompting, edge-attribute extraction inside edge resolution)
  is ported; real-provider (OpenAI) validation passed and `samples/Graphiti.Eval` ran live. A notable
  documented divergence: public ingestion stays on separate extraction because Python exposes
  `use_combined_extraction` only as an internal default-`False` flag (see `decisions.md`).
- **Productionized & modernized:** InMemory reference + LadybugDB runtime proof (win-x64 + gated
  linux-x64), search ranking/fusion/reranking, observability (`Meter`/OTLP sample), the
  idiomatic+allocation modernization (plan 10), robustness hardening (plan 09), and the measured
  scale-perf pass (plan 11) are all complete. Performance work is benchmark-first (moratorium lifted).
- Work selection rule: follow `.agents/plans/` in order (see AGENTS.md "Current priority"). Phases
  1–3 are complete, plans 05–08 are complete, and the whole 2026-06-19 G1–G6 agenda is done (parity,
  merge, observability, linux-x64 proof, live-provider canary, the G3 perf program, sustained upstream
  reminder, and the non-gated release-surface finalization). Per the 2026-06-27 paradigm shift, the
  library is **not** release-bound, so release-versioning/publishing is parked. Plan 10
  (idiomatic + allocation modernization) and plan 09 (robustness; Step D fixed a real bulk-ingestion
  partial-persistence defect by prevalidating missing entity embeddings before driver bulk save) are
  both **complete**. Plan 11 is also **complete**: the large-N InMemory and fake-provider throughput
  pass landed one measured edge-search win and recorded the current HNSW/provider-defaults budget
  decisions. **The whole backlog (plans 05–11) is done — there is no open plan.** The forward posture is
  **maintenance**: run the upstream-parity sweep (`upstream-sync-procedure.md`) to keep current, and
  apply opportunistic parity-safe modernization as the language moves; a new direction comes from Sergey.
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
package. Latest 2026-06-28 upstream audit: `Check-PythonUpstreamDelta.ps1 -Fetch` reported no
`graphiti_core/` upstream delta from anchor `0ed90b7` to target
`b59d4ba01118a91708fd6a6892200016168eeb5d`; `parity.md` now uses that target as the next anchor. The
current concrete search-filter drift is now closed:
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

The benchmark-first performance program (G3) and the modernization + robustness/performance streams
(plans 09, 10, and 11) are complete; the slice-by-slice detail lives in git history and the committed BenchmarkDotNet
baselines under `benchmarks/Graphiti.Core.Benchmarks/baselines/` (do not re-expand it here). Headlines:

- **G3 perf program (2026-06-26/27):** `IngestionBenchmarks` / `SearchBenchmarks` /
  `InMemoryVectorSearchBenchmarks` / `BulkEdgeDedupeBenchmarks` / `SerializationBenchmarks` with
  `[MemoryDiagnoser]` and recorded win-x64 baselines; measured allocation wins (BM25, uncached
  cache-key, RRF queue, TextScorer, MMR merge, bulk-throttling).
- **Plan 10 idiomatic + allocation modernization (2026-06-27):** the full audit inventory landed —
  lazy community fallback, `IsCleanInput`->`SearchValues`, immutable-message reuse, bulk word-overlap
  HashSet hoist, group-sentinel alloc, Ladybug direct-embedding read / `params ReadOnlySpan` params /
  delete-array throwaways, `AttributeMerger.TryGetPropertyValue`, O(n^2)->reverse overlap, snapshot-
  helper collapse + dead-code removal, bulk node/edge dedupe indexing, `MaterializeVector` via
  `CollectionsMarshal`, MMR dimension hoist, MinHash single-encode, the collection-expression sweep,
  shared cache tags, `System.Threading.Lock`, InMemory 18-arg ctor -> `SharedStore`. Hot-path changes
  carry before/after baselines.
- **Plan 09 robustness (2026-06-27/28):** HNSW gate closed (exact cosine stays default at current
  scale); G5 reminder wrapper `eng/Invoke-UpstreamDeltaReminder.ps1`; LLM-boundary risk map
  (`.agents/notes/llm-boundary-risk-map.md`) + `LlmBoundaryFuzzTests`; `ProviderResilienceWorkflowTests`
  covering the fake-provider failure modes. Step D fixed a real defect: `Graphiti` now materializes and
  validates missing entity node/edge embeddings **before** driver bulk save, so a malformed provider
  embedding cannot leave an episode persisted with dangling entity-edge UUIDs.
- **Plan 11 measured scale pass (2026-06-28):** `InMemoryScaleBenchmarks` added a 10000-node /
  30000-edge large-N fixture plus bulk-ingestion/search workflows; `ProviderThroughputBenchmarks`
  added latency-injecting fake M.E.AI chat/embedding providers with G4 metric assertions. The retained
  structural win skips endpoint-node dictionaries for edge filters that do not request node labels
  (edge hybrid RRF 75.275 ms -> 45.198 ms; edge vector 16.334 ms -> 8.591 ms; edge hybrid MMR 79.466 ms
  -> 45.000 ms on win-x64 ShortRun). HNSW stays closed at this size, and provider/cache/embedding
  defaults stay unchanged.

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

**Current verifier checkpoint (2026-06-28):** `.\eng\Verify-GraphitiCore.ps1` is green with GitHub
Packages credentials for the Ladybug feed — `1071` passed, `4` skipped, `1075` total. The verifier
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
- Keep the notes lean: summarize and prune rather than only appending. On stream/plan completion,
  collapse per-slice detail to a headline and stub the finished plan; run the hygiene checkpoint at the
  start of a new stream. The roles, soft budgets, and compaction steps are in `doc-hygiene.md`.
