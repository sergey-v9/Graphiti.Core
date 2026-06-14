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

Provider work is focused on LadybugDB. The focused provider state lives in `kuzu-driver-port.md`; do
not duplicate its proof matrix here.

## Current Layout

- `Graphiti.cs` and `Graphiti.*.cs`: public orchestrator, lifecycle, ingestion, search, removal,
  saga, community, infrastructure, and extraction parsing partials.
- `Models/`: node, edge, result DTO, entity type, entity attribute, and episode type models.
- `Drivers/` (in `Graphiti.Core`): only the driver contract/base (`IGraphDriver`, base driver),
  the deterministic in-memory reference driver, the Neo4j (legacy) driver and its statement
  builders/record mappers/session/executor helpers, the provider enum, and saga episode content.
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
  (InMemory reference, LadybugDB runtime proof, Neo4j legacy), search ranking/fusion/reranking,
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
  1–3 are complete; the remaining active work is plan-05 release infrastructure — E.2 (publish the
  local LadybugDB package family from `W:\code\ladybug`) and versioning. CI is intentionally out of
  scope. Performance work is benchmark-first and no longer on moratorium (`roadmap.md`).
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
Graphiti now consumes a local repaired LadybugDB package family
`0.17.0-alpha.2-graphiti.1` from `W:\code\ladybug\tools\csharp_api\artifacts` via
`NuGet.config`; that binding supports Graphiti's list/array/empty-list/null parameters directly, so
the former `LadybugStatementNormalizer` workaround has been removed.

For provider status, package facts, package bug recovery, runtime proof, and remaining work, read
`kuzu-driver-port.md`. If implementation uncovers a likely LadybugDB package/binding issue, mark it
separately from Graphiti port gaps. The current user-approved recovery path is local-only: patch and
commit the fix in `W:\code\ladybug`, do not push remotely, draft a nearby markdown request for
`ladybug-dotnet`, build a local NuGet package, and wire Graphiti to that local package for validation
when needed.

## Verification

Rerun verification before claiming the tree is green; historical test counts drift as coverage is
added.

Latest checkpoint, 2026-06-14:

`.\eng\Verify-GraphitiCore.ps1` is green after the node extraction prompt golden hardening: restore,
format, warning-clean build including `Graphiti.Sample.OpenAI`, full test suite (`984` passed,
`3` skipped, `987` total), `dotnet pack` for both shippable packages
(`Graphiti.Core.2.0.0-alpha.1.nupkg` + `.snupkg` and
`Graphiti.Core.Drivers.Ladybug.2.0.0-alpha.1.nupkg` + `.snupkg`), and fresh temp consumer
restore/build/setup/run checks for both packages. The verifier now packs both projects, then creates isolated
`net10.0` consumers with strict `NuGet.config` files (`<clear />`), temp `NUGET_PACKAGES`, and
`--no-cache`: the core consumer restores from the packed core output + nuget.org only, while the
Ladybug consumer restores from both packed Graphiti outputs + the local Ladybug feed + nuget.org and
embeds the packed LadybugDB driver in `Graphiti`. Both consumers call
`BuildIndicesAndConstraintsAsync()`, then run and assert their provider output (`InMemory`,
`LadybugDb`). Both csprojs set
`IncludeSymbols=true`/`SymbolPackageFormat=snupkg`, and `PackageReadinessTests` guards shared NuGet
metadata, README packing, symbol settings, SemVer-like same-version alignment, the two-project pack
loop, and the package-consumer smoke path. `OPENAI_API_KEY` was unset; the three skipped tests were
the env-gated `OpenAIProviderIntegrationTests`.

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
rerun with 968 passed, 3 skipped, 971 total after the known
`SearchEngineDriverBackedTests.SearchAsync_ExecutesConfiguredScopesConcurrently` flake failed once;
format with `dotnet format --verify-no-changes --no-restore`). Steps A–E complete plus the
B2/B3 follow-through: the shippable `Graphiti.Core` and `Graphiti.Core.Drivers.Ladybug` packages now
generate IntelliSense XML docs; the two-assembly public-API snapshot guard remains in
`tests/Graphiti.Core.Tests/Api/` (`PublicApiGenerator` — `Graphiti.Core.approved.txt` and
`Graphiti.Core.Drivers.Ladybug.approved.txt`; regenerate the relevant baseline deliberately on an
intended API change); a consumer `README.md`/`docs/search.md`; surface hardening; the
`GraphProvider.LadybugDb`/`AddGraphiti` names (with `Kuzu`/`AddGraphitiCore` `[Obsolete]` aliases;
`GRPH0001` and `GRPH0002` are locally suppressed only at deliberate compatibility-alias sites); the
InMemory-default constructor + `AddEpisodeOptions`; the
LadybugDB package split; and the retired shared Kuzu branches in generic search helpers. Remaining:
E.2 (publish the local LadybugDB package family — `W:\code\ladybug`), versioning, CI. See `plans/05`
and `decisions.md`. GOTCHA (still applies): do NOT run multiple worktree agents' `dotnet test`
concurrently — the LadybugDB native package deadlocks across worktrees (1.5h hang on 06-14; recovered
by killing orphaned testhost processes). Have worktree agents build/format-only and run the
consolidated test centrally.

WS-1 pre-bump audit checkpoint, 2026-06-14: direct local verification was green before any pin change
(`dotnet build -c Release -clp:ErrorsOnly`, `dotnet test -c Release --no-build` with 968 passed,
3 skipped, 971 total, and `dotnet format --verify-no-changes --no-restore`). Read-only subagent audits
confirmed the nearby `W:\code\ladybug\tools\csharp_api` `0.17.1` artifacts include the Graphiti
list/array/empty-list/null parameter-binding repair, FTS/vector regression coverage, and Unix
`RTLD_GLOBAL` native-loader work. Graphiti still pins `0.17.0-alpha.2-graphiti.1`; get explicit user
confirmation before bumping to `0.17.1`. Do not adopt `LadybugDB.Extensions` without a concrete
Graphiti Core need. Follow-up hardening made the known search concurrency proof deterministic and made
Ladybug file-backed setup idempotent across reopen by ignoring duplicate errors for the four exact
Graphiti FTS indexes; runtime coverage now proves build-write-close-reopen-build-search.

Upstream Python check, 2026-06-14: `.\eng\Check-PythonUpstreamDelta.ps1 -Fetch` confirmed
`origin/main` still equals the recorded parity anchor
`0ed90b72505c2a6a4f3ee953939888fb56572944`; there is no `graphiti_core/` delta to port. The helper
now implements the Step 1 upstream-sync log/stat/name-status check and supports `-FailOnDelta`.

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
  snapshots, Neo4j query/session telemetry boundaries, and Ladybug statement/mapper/executor shapes.
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
