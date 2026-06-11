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
- `Drivers/`: `IGraphDriver`, base driver, deterministic in-memory reference driver, Neo4j driver,
  LadybugDB driver/factory/executor, statement builders, record mappers, session/executor helpers,
  provider enum, and saga episode content.
- `Namespaces/`: node and edge namespace facades over drivers.
- `Search/`: search configs/results, hybrid search engine, rerankers, filter builders/matchers,
  fallback graph materialization, search-result composition, and search-driver retrieval adapter.
- `Maintenance/`: entity deduplication and community clustering.
- `Text/`: chunking, token counting, text helpers, and Graphiti helper functions.
- `LlmClients/`, `Embedding/`, `CrossEncoder/`: provider abstractions, Microsoft.Extensions.AI
  adapters, deterministic/test implementations, cache/usage helpers, and rerankers.
- `Configuration/`: options, validators, DI registration, LadybugDB driver options, cache/resilience
  settings.
- `Telemetry/`: `ActivitySource` spans and source-generated logging.
- `Serialization/`: System.Text.Json serializer and source-generated context.
- `Internal/`: helper/services for extraction context, attribute merging, edge merging, type
  resolution, deterministic text, throttling, rate limiting, saga/community/attribute/edge/node
  services, and episode graph extraction.

## Current State

Reassessed 2026-06-11 against Python baseline `7514b44` (see `parity.md` for the full matrix):

- **Solid and verified:** project/infrastructure shape (net10.0, analyzers, packaging), drivers
  (InMemory reference, LadybugDB runtime proof, Neo4j legacy), search ranking/fusion/reranking,
  community label propagation, text utilities, serialization/cache identity, DI/options. The
  deterministic suite is green in the latest verification checkpoint below.
- **Phase 2 active:** the LLM-facing semantic layer has moved from scaffold prompts to ported
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
  cached.
- **Never exercised live:** any real LLM/embedding/reranker provider, end to end.
  `samples/Graphiti.Sample.OpenAI` now provides a runnable OpenAI host and
  `OpenAIProviderIntegrationTests` provides env-gated provider tests. The sample is
  compile/no-key-path verified, uses `MicrosoftExtensionsAICrossEncoderClient` for real reranking,
  and the tests skip cleanly without `OPENAI_API_KEY`, but no provider call has been executed. The
  deterministic suite cannot see prompt or schema-acceptance problems (plan 03).
- Work selection rule: follow `.agents/plans/` in order (see AGENTS.md "Current priority").
  Performance/allocation rework is on moratorium (`roadmap.md`).
- Decomposition context: `Graphiti` is the public orchestrator; behavior lives in partials plus
  internal services and helpers. Search boundaries: `SearchEngine` orchestrates,
  `SearchRetrievalRunner` retrieves, `SearchResultComposer` shapes results. Prompt builders live
  in `Prompts/` (one static class per Python prompt module).
- Optional local `.agents/skills` files are specialist references only. Use them for matching tasks,
  but do not let generic AI/ML/framework advice override `decisions.md`.

## LadybugDB / Kuzu

LadybugDB is the main provider target while Kuzu remains the Python parity lineage and compatibility
vocabulary. `GraphProvider.Kuzu` is a supported core options/DI enum path that creates the
LadybugDB-backed driver. `AddLadybugDbGraphDriver` remains the explicit host-facing configuration
helper for `DatabasePath`.

For provider status, package facts, package quirks, runtime proof, and remaining work, read
`kuzu-driver-port.md`. If implementation uncovers a likely LadybugDB package/binding issue, mark it
separately from Graphiti port gaps. The current user-approved recovery path is local-only: patch and
commit the fix in `W:\code\ladybug`, do not push remotely, draft a nearby markdown request for
`ladybug-dotnet`, build a local NuGet package, and wire Graphiti to that local package for validation
when needed.

## Verification

Rerun verification before claiming the tree is green; historical test counts drift as coverage is
added.

Latest checkpoint, 2026-06-11:

Succeeded after pinning combined extraction as an internal-only path and public separate extraction
as the Python-baseline default:

```powershell
.\eng\Verify-GraphitiCore.ps1
```

Restore, format verification, solution build including `Graphiti.Sample.OpenAI`, full test suite
(`928` passed, `2` skipped, `930` total), and `dotnet pack` for
`Graphiti.Core.2.0.0-alpha.1.nupkg`. `OPENAI_API_KEY` was unset; the two skipped tests were
`OpenAIProviderIntegrationTests.StructuredResponseSchemas_WithOpenAIProvider_AreAccepted` and
`OpenAIProviderIntegrationTests.AddEpisodeAsync_WithOpenAIProvider_IngestsResolvedTemporalGraph`.
No live provider run has passed yet. Focused combined-extraction/default-behavior tests also
passed:

```powershell
dotnet test Graphiti.Core.CSharp.slnx --filter "FullyQualifiedName~GraphitiWorkflowTests.AddEpisodeBulk_UsesSeparateExtractionByDefault|FullyQualifiedName~GraphitiWorkflowTests.AddEpisode_UsesSourceSpecificNodePromptThenSeparateEdgePrompt|FullyQualifiedName~EpisodeGraphExtractorTests.ExtractCombinedEpisodeGraph" --verbosity minimal
```

with `6` passed.

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
```

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
