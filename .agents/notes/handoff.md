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

> ⚠ **Supervisor review (updated 2026-06-18):** Sergey RESOLVED the previously user-gated items —
> CI lanes stay (keep as-is, do not expand); the LadybugDB feed is GitHub-Packages-only (no local
> fallback, credential required); Neo4j removed (2026-06-17). See `roadmap.md` → "Resolved scope
> decisions". The code was also de-coupled from Python textually (names + comments) on 2026-06-18 —
> keep it that way (`decisions.md` "Parity without Python coupling in the code"). Library work is solid
> and the full suite is green. Still user-gated: **release versioning/publishing**. The
> **Ladybug→Core merge (plan 06) is complete (2026-06-26)**; Core now depends on the `github_ladybug`
> feed and can't publish to nuget.org until LadybugDB is public there (see `roadmap.md` Phase 4 /
> plan 06).

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
  1–3 are complete, plan 06 is complete, G4 is complete, and the first wave of G3 perf/allocation slices
  landed 2026-06-26, plan 07/G1 linux-x64 proof is complete, and G2 fail-loud live-provider/eval CI is
  wired. **The next roadmap priority is the remaining G3 benchmark-first performance/allocation work.**
  Release versioning/publishing remains user-gated.
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

**Current verifier checkpoint (2026-06-26):** `.\eng\Verify-GraphitiCore.ps1` is green with GitHub
Packages credentials for the Ladybug feed — `1028` passed, `4` skipped, `1032` total. The verifier
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
