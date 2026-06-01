# LadybugDB Provider / Kuzu Parity

The C# port's main graph-provider target is LadybugDB, using the LadybugDB NuGet package from the
alternative Kuzu fork. Kuzu remains the Python parity lineage and search keyword while the behavior is
being ported.

## Current Status

- LadybugDB provider support is partially implemented through the explicit optional
  `Graphiti.Core` package factory and provider-package DI helpers, but it is not wired into
  core DI/options or exposed as a supported `GraphProvider` path.
- Foundation-only schema, statement-builder, and record-mapper helpers now exist under
  `csharp/src/Graphiti.Core/Drivers/Ladybug/`. They pin Python Kuzu schema/table names, save/get/
  delete/retrieve/load-embedding Cypher, individual bulk-save statement expansion, JSON attributes,
  label-array behavior, simple-edge record mapping, and `RelatesToNode_` entity-edge representation,
  and now feed an internal executor-backed `LadybugGraphDriver` core.
- `LadybugGraphDriver` is internal and not wired to core DI/options. It executes the graph-driver
  surface through an abstract `ILadybugQueryExecutor`, which keeps the core path testable without
  adding the LadybugDB package or native assets to the core project. Its current projection path uses
  explicit loops for bulk-save phase statements, read records, saga contents, and first-seen group-id
  de-duplication. Factory-created drivers can be cloned for group-scoped Graphiti operations; those
  clones share the same executor/package database and leave disposal to the root driver.
- `Graphiti.Core` is now the core LadybugDB driver boundary. It references
  `Graphiti.Core`, `LadybugDB`, and `LadybugDB.Native`, exposes `LadybugDbGraphDriverFactory`, and
  implements the concrete package executor for `ILadybugQueryExecutor`. It also exposes
  `LadybugDbOptions` and `AddLadybugDbGraphDriver` helpers that configure
  `GraphitiOptions.GraphDriverFactory` from the optional package. This keeps native/package
  dependencies out of `Graphiti.Core` while letting callers opt into runtime-backed drivers
  explicitly. The factory-backed package path now has an initial `Graphiti` workflow proof for
  schema build, deterministic LLM extraction, episode ingestion, `SearchAdvancedAsync`, attribution
  lookup by episode, episode removal cleanup, and direct `AddTripletAsync` fact persistence with
  `SearchAsync`. It also proves `AddEpisodeBulkAsync` duplicate fact coalescing across two episodes
  with attribution lookup and search, plus saga association with `HAS_EPISODE` / `NEXT_EPISODE`
  edges and saga content retrieval, `SummarizeSagaAsync` summary/watermark persistence, and
  `BuildCommunitiesAsync` community construction/rebuild cleanup with community search. It still
  does not make `GraphProvider.Kuzu` valid through core provider validation.
- `LadybugRecordMapper` uses loop-built attribute/list materialization for Ladybug/Kuzu rows while
  preserving JSON clone semantics, ordinal dictionaries, source ordering, null handling, and
  invariant object conversion.
- `LadybugStatementBuilder` now builds bulk-save statement phases and list-valued parameters with
  explicit snapshots, copying `IReadOnlyList<T>` inputs by index where available while preserving
  Kuzu statement order and parameter list isolation.
- `LadybugStatementNormalizer` is the current concrete adapter strategy for LadybugDB package
  binding gaps: it rewrites package-unsupported list/array/null parameters into Cypher literals
  while leaving scalar values bound for prepared execution. It is package-independent and lives in
  core so the optional provider executor can share the same audited behavior.
- `LadybugSearchStatementBuilder` and `LadybugSearchExecutor` are internal. They pin Kuzu full-text
  index statements, `QUERY_FTS_INDEX` calls, `array_cosine_similarity` vector search, doubled-depth
  BFS plans over `RelatesToNode_`, per-UUID ranker statements, Kuzu label filters, score extraction,
  BFS dedup/limit behavior, and C# search-rank ordering over the abstract executor.
  `LadybugGraphDriver` now implements `ISearchGraphDriver` by delegating to this executor and
  provisions FTS extension/index statements during `BuildIndicesAndConstraintsAsync`. Repeated
  schema/index builds on the same driver instance are guarded as a no-op after the first successful
  build because LadybugDB FTS index creation is not modeled with an `IF NOT EXISTS` clause. The
  per-UUID ranker paths use first-seen loop-built statement and score maps, and search/vector/group
  parameter snapshots now copy read-only lists by index while preserving duplicate input collapse,
  center-node handling, unknown backend rows, last-row-wins score updates, and inclusive minimum-
  score filtering.
- The driver should use the LadybugDB NuGet package (https://www.nuget.org/packages/LadybugDB).
- `GraphProvider.Kuzu` remains in the enum today and should be treated as a pending compatibility
  value, not a rejected provider.
- `GraphProvider.Kuzu` is still unsupported by `GraphitiOptions`/DI unless callers provide an
  explicit `GraphDriverFactory`; the optional package DI helper does exactly that without changing
  core validation.
- Implement against Kuzu behavior for Python parity, but name the driver-facing provider LadybugDB as
  the port freezes. The end state should make LadybugDB the default first provider option.
- Existing Kuzu-specific query/filter branches preserve interim behavior until the driver lands.
  `SearchUtilities.BuildKuzuFulltextQuery` now matches Python Kuzu's whitespace word truncation.
- `GraphitiServiceCollectionExtensions` does not currently wire a LadybugDB/Kuzu driver.
- The test project has private `LadybugDB` and `LadybugDB.Native` references for runtime package
  proof only; the optional `Graphiti.Core` package has the real provider references. Do not
  add these package/native references to `Graphiti.Core`.
- If driver implementation uncovers behavior that looks like a LadybugDB package/backend bug rather
  than a C# port bug, mark it separately in this note or a focused issue. The port may work around
  proven backend limitations, but should not blur them with Graphiti parity gaps.
- Local LadybugDB package repair is allowed when a confirmed backend/package bug blocks Graphiti:
  use `W:\code\ladybug`, do not push remotely, keep those edits/commits separate from Graphiti
  commits, add a nearby markdown draft describing the upstream `ladybug-dotnet` request, pack a
  local NuGet, and connect Graphiti to that local package source only for the repaired package path.
- Neo4j and FalkorDB can stay as reference/confirmation paths if already working, but they are not
  where future provider improvements should go.
- Neptune is a separate provider decision and remains not implemented.
- The current Python Kuzu code has two provider-specific inconsistencies: the `Saga` schema/
  operation path is minimal while shared saga query templates name summary/watermark fields, and the
  shared entity-edge save template names `reference_time` while the Python Kuzu operation omits that
  parameter. The C# foundation resolves these for runtime wiring by using the full `SagaNode` model
  shape in schema/save/get projections and by saving and returning entity-edge `reference_time`.

## Confirmed Package/API Facts

- Package id: `LadybugDB`; locally cached at
  `C:\Users\sergey\.nuget\packages\ladybugdb\0.17.0-alpha.1`.
- Native assets are packaged separately, for example `LadybugDB.Native` and
  `LadybugDB.Native.win-x64`.
- The exposed API includes `LadybugDB.Database`, `Connection`, `Query`, `Prepare`, `Execute`,
  `PreparedStatement.Bind`, `QueryResult.Rows`, `Node`, and `Rel`.
- The API appears synchronous. A single `Connection` serializes operations internally according to
  local package docs.
- `Database(string databasePath, SystemConfig)` uses an empty string for in-memory databases; Python
  Kuzu uses `':memory:'`.
- A test-only in-memory package smoke confirms basic Cypher, current schema execution through the
  internal driver, scalar Saga save/read projections, `QueryResult.ColumnNames` / `Rows()` record
  projection, `DateTime` parameters, literal `array_cosine_similarity`, and explicit FTS extension
  loading/search after `INSTALL FTS; LOAD EXTENSION FTS;`. The internal `LadybugSearchExecutor` now
  also has package proof for FTS-backed entity-node, entity-edge, episodic, and community search plus
  entity-node, entity-edge, and community vector search using normalized list parameters. Node/edge
  BFS statements and node-distance/episode-mentions ranker statements also run against the real
  package. The entity-origin edge BFS proof currently pins the Python Kuzu shape where a depth-2
  origin reaches the second logical `RELATES_TO` fact edge through `RelatesToNode_`. Graph
  maintenance proof now covers community-edge `UNION` saves, normalized list-backed edge/node
  deletes, grouped clear, and full clear through the internal driver. Non-empty Kuzu search filters
  are also package-proved for `list_has_all(...)`, `e.name in $edge_types`, `e.uuid in $edge_uuids`,
  and grouped entity/edge search.
- Current package binding does not accept the list/array and null parameter shapes Graphiti
  statements use today. `List<string>`, `string[]`, `float[]`, and `object[]` throw
  `NotSupportedException`; `null` throws `ArgumentNullException`. The normalizer handles these by
  inlining Kuzu literals at package execution time, and the test-only runtime executor proves this
  strategy for entity-edge `reference_time`, list-valued `episodes` / embeddings, episode
  retrieval/group filters, `MENTIONS` traversals, and null temporal fields.
- FTS calls fail before extension loading with a catalog error, then `CALL CREATE_FTS_INDEX` and
  parameterized `CALL QUERY_FTS_INDEX` work after `INSTALL FTS; LOAD EXTENSION FTS;`.
- LadybugDB can reject post-projection ordering by node variables (`ORDER BY n.uuid` / `e.valid_at`).
  Use projected aliases such as `uuid`, `valid_at`, and `created_at` in Kuzu/Ladybug statements after
  `RETURN`.
- When runtime proof exposes behavior that looks like a LadybugDB package bug rather than a Graphiti
  port bug, mark it separately in this note or a focused test skip/issue note. Do not bury suspected
  package bugs as ordinary port TODOs; we can fix or patch those driver/package issues later.

## Local LadybugDB Repair Workflow

The Ladybug repository is available locally at `W:\code\ladybug`. It is the full repository,
including the base C library and wrappers. The C# bindings live under
`W:\code\ladybug\tools\csharp_api`.

If Graphiti provider implementation exposes a likely LadybugDB package or C# binding bug:

- Reproduce it as narrowly as possible and record why it is likely a LadybugDB/package issue rather
  than a Graphiti port issue.
- Create a separate local branch in `W:\code\ladybug` for the fix. Do not push that repository unless
  the user explicitly asks.
- Commit the Ladybug fix locally with a focused message and a useful description.
- Draft a pull request description for the Ladybug fix next to the local work, including the problem,
  fix, Graphiti scenario that exposed it, and verification. This can be a local markdown note if no
  remote PR is being opened yet.
- Build local NuGet packages from the Ladybug repo as needed and consume those packages from the
  Graphiti C# work to continue provider implementation.
- Keep Graphiti commits and Ladybug fix commits separate. The Graphiti side may depend on a local
  Ladybug package while the upstream fix branch remains local.

## Facts To Confirm Before Driver Wiring

- Whether LadybugDB accepts any Python Kuzu Cypher shapes used by Graphiti that are not represented
  in the current internal-driver and internal-search runtime proofs.
- If community-cluster maintenance is ported into the Ladybug driver, prove the `collect(DISTINCT
  ...)` and `WITH count(*)` query shapes against the package before wiring it.
- Whether the concrete adapter should use prepared execution everywhere or split direct/prepared
  paths for package-specific performance and diagnostics.
- Whether the normalizer's CLR literal strategy is enough for the remaining graph/search statements
  beyond the current Saga, entity-edge, episode retrieval, mention traversal, FTS/vector search,
  BFS/ranker, `UNION`, delete, and clear runtime proofs.
- Whether the core LadybugDB driver needs additional host-facing options beyond `DatabasePath`
  before core provider wiring.

## Existing Touchpoints

- `Drivers/GraphProvider.cs` documents Kuzu as planned but pending; it will need the LadybugDB naming
  transition when the provider is implemented.
- `Search/CompiledSearchFilter.cs` has Kuzu label-query behavior for node and edge filters.
- `Search/SearchUtilities.cs` has the `GraphProvider.Kuzu` full-text branch and
  `BuildKuzuFulltextQuery`.
- Tests in `SearchUtilitiesTests.cs`, `SearchFilterTests.cs`, `GraphitiHelperTests.cs`, and
  `GraphitiOptionsValidationTests.cs` pin current pending-driver behavior.
- Tests in `Drivers/Ladybug/LadybugFoundationTests.cs` pin the foundation schema, save/get/delete/
  retrieve/load-embedding statements, individual bulk-save expansion, parameter snapshot behavior,
  and record-mapper behavior.
- Tests in `Drivers/Ladybug/LadybugGraphDriverTests.cs` pin the internal executor-backed driver core:
  schema execution, sequential Kuzu-style bulk writes with embedding backfill, node/edge deletion
  forwarding, not-found behavior, read mapping, saga/mention/community query forwarding,
  search-surface delegation, close/dispose behavior, no clone support without an executor factory,
  and factory-clone shared executor/disposal semantics.
- Tests in `Drivers/Ladybug/LadybugSearchStatementTests.cs` and
  `Drivers/Ladybug/LadybugSearchExecutorTests.cs` pin the internal search statement/execution
  foundation, including search parameter snapshot behavior.
- Tests in `Drivers/Ladybug/LadybugPackageRuntimeTests.cs` pin the current LadybugDB package runtime
  facts using private test-only package references, exercise the normalizer against real list/null
  graph writes, reads, search, delete, and clear paths, and assert the core project still has no
  LadybugDB package reference.
- Tests in `Drivers/Ladybug/LadybugProviderPackageTests.cs` pin the core LadybugDB driver factory
  and provider-package DI helper over the concrete LadybugDB package executor without changing core
  DI/options support, including the first runtime-backed `Graphiti` ingest/search workflow with a
  deterministic `StaticJsonLlmClient`, runtime-backed episode attribution/removal cleanup, and
  direct triplet persistence/search, bulk duplicate-fact coalescing, and saga association.

## Expected Implementation Work

1. Continue the optional `Graphiti.Core` core driver path. Do not add LadybugDB
   package references to `Graphiti.Core`.
2. Extend package-runtime proof only when a concrete adapter or newly ported feature introduces a
   Ladybug/Kuzu statement shape not already covered by the current Saga/entity-edge/episode/
   FTS/vector/BFS/ranker/filter/`UNION`/delete/clear tests. If a proof fails because the LadybugDB
   package appears to reject valid Kuzu behavior or mishandle binding/projection/materialization,
   record that as a suspected package bug separately from Graphiti port work, and only then use the
   local `W:\code\ladybug` repair/package workflow.
3. Extend optional-package host options only when real connection/runtime requirements appear; the
   current optional package exposes `LadybugDbOptions.DatabasePath` and DI helper registration.
4. Keep `GraphProvider.Kuzu` unsupported in core DI/options until broader end-to-end provider
   workflow is proven against the real backend and the driver-facing LadybugDB naming is settled.
   Save/get/delete, bulk paths, saga episode queries, saga summarization, fulltext, vector search,
   BFS, rerankers, graph maintenance, concrete adapter execution, optional-package DI, and the
   first runtime-backed `Graphiti` ingest/search, episode-removal, direct triplet,
   bulk duplicate-fact, saga association/summarization, and community build/rebuild/search workflows
   now have focused proof. Keep the adapter allocation-aware: avoid unnecessary
   per-row dictionaries/lists, closure-heavy query loops, repeated JSON/string conversions, and
   exception-driven type coercion where the package API allows direct mapping.
5. Prove the full C# Saga schema/save/get projections and entity-edge `reference_time` projections
   against the real LadybugDB backend as part of the first runtime execution slice.
6. Add optional native-gated integration smoke tests.
7. Wire LadybugDB through `GraphitiServiceCollectionExtensions.CreateGraphDriver` and make it the
   default first provider option when safe.
8. Revisit interim Kuzu query/filter helpers and move provider-specific behavior into the driver when
   appropriate.
9. Add focused driver tests and parity tests against Python Kuzu behavior.
10. Once the driver is stable, finish the Kuzu-to-LadybugDB terminology transition.
11. Update this note, `decisions.md`, `handoff.md`, and `roadmap.md` when the driver lands.

This note supersedes older comments that described Kuzu as intentionally out of scope for the C# port.
