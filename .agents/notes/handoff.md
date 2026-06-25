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
> **Ladybug→Core merge (plan 06) is APPROVED, in scope (Sergey, 2026-06-19) — the agent can pick it up
> directly** as a normal stream; the accepted consequence is that Core then depends on the
> `github_ladybug` feed and can't publish to nuget.org until LadybugDB is public there (see `roadmap.md`
> Phase 4 / plan 06).

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
  1–3 are complete; the active plan-05 surface now has an explicit Step F plan-folder backlog triage
  gate before plan 06 or release infrastructure. Anything newly found in `.agents/plans/` or directly
  linked notes should be handled as its own parity/provider/perf/docs slice first, not bundled into the
  optional Ladybug merge or release-version decisions. E.2 now consumes the fork-published LadybugDB dev
  package family, and CI has both the core-only lane
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

Latest plan-folder/backlog audit, 2026-06-19: `Check-PythonUpstreamDelta.ps1 -Fetch -FailOnDelta`
reported no `graphiti_core/` upstream delta from anchor `0ed90b7` to target
`b9a74644fb641910a03d325ec2b8f669d3db75dc`. The current concrete search-filter drift is now closed:
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
outside plan 06's (now approved, in-scope) Ladybug merge and already-recorded decision-gated items. The package-feed
recheck also confirmed that `0.17.1-dev.1.1.g6f3dbed` is still the only published GitHub Packages
version for `LadybugDB` and `LadybugDB.Native`, matching the current Graphiti pin. The only concrete
slice from this pass was test-only hardening for the search concurrency proof: after the fake-driver
barrier has proven concurrent startup, it now waits on the xUnit cancellation token instead of a
second fixed wall-clock timeout. A follow-up ontology-matching audit is also closed: custom entity
and edge type resolution now uses exact ontology keys/signatures, so case variants and type-name
aliases do not select custom attribute schemas. A follow-up search public/extensibility audit found no
result-composition code slice: fresh C# search recipe instances and BFS guard-skipped custom driver
calls are documented C# API hardening decisions. Plan 06 is approved and in scope (2026-06-19).

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
added. This section holds the single authoritative live count and the standing verify commands — do
not turn it back into a per-checkpoint changelog (git history holds the slice-by-slice detail).

**Current verifier checkpoint (2026-06-19):** `.\eng\Verify-GraphitiCore.ps1` is green with GitHub
Packages credentials for the Ladybug feed — `1024` passed, `3` skipped, `1027` total. The run covers
restore, format verification, a warning-clean build including `Graphiti.Sample.OpenAI`, the full test
suite, `dotnet pack` for both shippable packages (`Graphiti.Core` and
`Graphiti.Core.Drivers.Ladybug`, `.nupkg` + `.snupkg`), and both fresh package-consumer smoke builds
(core from the packed core output + nuget.org; Ladybug from both packed outputs + the fork GitHub
Packages feed + nuget.org). The three skips are the env-gated `OpenAIProviderIntegrationTests`, which
skip cleanly when `OPENAI_API_KEY` is unset. The upstream-delta check reports no `graphiti_core/`
changes from anchor `0ed90b7` to `origin/main` `b9a74644fb641910a03d325ec2b8f669d3db75dc`.

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
