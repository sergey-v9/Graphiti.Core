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

- Decomposition is largely complete. `Graphiti` remains the public orchestrator; behavior lives in
  partials plus internal services and helpers.
- Search has stable internal boundaries: `SearchEngine` orchestrates, `SearchRetrievalRunner`
  delegates driver-backed retrieval, and `SearchResultComposer` owns fusion/reranking/result shaping.
- Performance work should preserve parity while avoiding avoidable hot-path allocations. Prefer
  explicit loops, pre-sized buffers, visitor-style token scanning, source generation, and non-throwing
  parse paths where they improve default implementation code without obscuring behavior.
- The in-memory driver is a real deterministic reference/test driver with broad persistence/search
  behavior. Keep it correct and deterministic, but do not treat it as a product provider investment
  target.
- Neo4j is present only as existing/reference behavior and is expected to be removed later. FalkorDB
  is not a current C# provider investment. LadybugDB is where provider design and workflow coverage
  should go.
- Optional local `.agents/skills` files are specialist references only. Use them for matching tasks,
  but do not let generic AI/ML/framework advice override `decisions.md`.

## LadybugDB / Kuzu

LadybugDB is the main provider target while Kuzu remains the Python parity lineage and compatibility
vocabulary. `GraphProvider.Kuzu` is a supported core options/DI enum path that creates the
LadybugDB-backed driver. `AddLadybugDbGraphDriver` remains the explicit host-facing configuration
helper for `DatabasePath`.

For provider status, package facts, package quirks, runtime proof, and remaining work, read
`kuzu-driver-port.md`. If implementation uncovers a likely LadybugDB package/binding issue, record it
separately from Graphiti port gaps and do not inspect local Ladybug sources unless a confirmed issue
blocks Graphiti work.

## Verification

Recent 2026-06-01 checkpoints recorded successful locked restore, format verification,
no-incremental build, full test runs, and package builds at different points. Historical counts in old
notes drifted as tests were added, so rerun verification before claiming the tree is green.

Latest checkpoint, 2026-06-02:
`.\eng\Verify-GraphitiCore.ps1 -FocusedFilter "FullyQualifiedName~Graphiti.Core.Tests.Namespaces.NamespaceTests"`
succeeded. It ran locked restore, focused namespace coverage (`10` passed), format verification,
no-incremental build, the full test suite (`869` passed), and `dotnet pack` for
`Graphiti.Core.2.0.0-alpha.1.nupkg`. Recent preceding checkpoints used the same verifier with
focused Ladybug mock-driver/runtime coverage (`18` passed), Graphiti workflow/telemetry coverage
(`98` passed), InMemory delete/cancellation coverage (`15` passed), and InMemory clone/read/search
coverage (`26` passed).

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
  `evolution.md`, current state or gotchas here, planned work in `roadmap.md`, provider-specific
  details in `kuzu-driver-port.md`, and commit rules in `commit-policy.md`.

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
