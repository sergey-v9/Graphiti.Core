# LadybugDB Provider / Kuzu Parity

The C# port's main graph-provider target is LadybugDB, using the LadybugDB NuGet package from the
alternative Kuzu fork. Kuzu remains the Python parity lineage and search keyword while the behavior is
being ported.

## Current Status

- The LadybugDB provider has not been implemented yet.
- Foundation-only schema, statement-builder, and record-mapper helpers now exist under
  `csharp/src/Graphiti.Core/Drivers/Ladybug/`. They pin Python Kuzu schema/table names, save/get/
  delete/retrieve/load-embedding Cypher, individual bulk-save statement expansion, JSON attributes,
  label-array behavior, simple-edge record mapping, and `RelatesToNode_` entity-edge representation,
  and now feed an internal executor-backed `LadybugGraphDriver` core.
- `LadybugGraphDriver` is internal and not wired to DI/options. It executes the non-search graph
  driver surface through an abstract `ILadybugQueryExecutor`, which keeps the core path testable
  without adding the LadybugDB package or native assets to the core project. Its current projection
  path uses explicit loops for bulk-save phase statements, read records, saga contents, and
  first-seen group-id de-duplication. There is still no concrete LadybugDB package adapter in the
  project.
- `LadybugRecordMapper` uses loop-built attribute/list materialization for Ladybug/Kuzu rows while
  preserving JSON clone semantics, ordinal dictionaries, source ordering, null handling, and
  invariant object conversion.
- `LadybugStatementBuilder` now builds bulk-save statement phases and list-valued parameters with
  explicit snapshots, copying `IReadOnlyList<T>` inputs by index where available while preserving
  Kuzu statement order and parameter list isolation.
- `LadybugStatementNormalizer` is the current concrete adapter strategy for LadybugDB package
  binding gaps: it rewrites package-unsupported list/array/null parameters into Cypher literals
  while leaving scalar values bound for prepared execution. It is package-independent and lives in
  core so a future concrete executor can share the same audited behavior.
- `LadybugSearchStatementBuilder` and `LadybugSearchExecutor` are internal. They pin Kuzu full-text
  index statements, `QUERY_FTS_INDEX` calls, `array_cosine_similarity` vector search, doubled-depth
  BFS plans over `RelatesToNode_`, per-UUID ranker statements, Kuzu label filters, score extraction,
  BFS dedup/limit behavior, and C# search-rank ordering over the abstract executor. They are not
  wired into `LadybugGraphDriver` and do not make the driver implement `ISearchGraphDriver`. Their
  per-UUID ranker paths use first-seen loop-built statement and score maps, and search/vector/group
  parameter snapshots now copy read-only lists by index while preserving duplicate input collapse,
  center-node handling, unknown backend rows, last-row-wins score updates, and inclusive minimum-
  score filtering.
- The driver should use the LadybugDB NuGet package (https://www.nuget.org/packages/LadybugDB).
- `GraphProvider.Kuzu` remains in the enum today and should be treated as a pending compatibility
  value, not a rejected provider.
- `GraphProvider.Kuzu` is still unsupported by `GraphitiOptions`/DI unless callers provide an
  explicit `GraphDriverFactory`.
- Implement against Kuzu behavior for Python parity, but name the driver-facing provider LadybugDB as
  the port freezes. The end state should make LadybugDB the default first provider option.
- Existing Kuzu-specific query/filter branches preserve interim behavior until the driver lands.
  `SearchUtilities.BuildKuzuFulltextQuery` now matches Python Kuzu's whitespace word truncation.
- `GraphitiServiceCollectionExtensions` does not currently wire a LadybugDB/Kuzu driver.
- The test project has private `LadybugDB` and `LadybugDB.Native` references for runtime package
  proof only. Adding these to core still needs an explicit packaging/provider decision because
  native dependencies affect package layout.
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
  origin reaches the second logical `RELATES_TO` fact edge through `RelatesToNode_`.
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

## Facts To Confirm Before Driver Wiring

- Whether LadybugDB accepts all Python Kuzu Cypher shapes used by Graphiti beyond the current runtime
  proofs, including `DETACH DELETE`, direct `WHERE x IN $list` binding, and `UNION`.
- Whether parameter names and binding use `$name` across prepared and direct execution paths.
- Whether the normalizer's CLR literal strategy is enough for the remaining graph/search statements
  beyond the current Saga, entity-edge, episode retrieval, mention traversal, FTS/vector search, BFS,
  and ranker runtime proofs.
- Whether native package dependencies are acceptable in `Graphiti.Core` or require an optional
  core driver.

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
  forwarding, not-found behavior, non-search read mapping, saga/mention/community query forwarding,
  close/dispose behavior, and no clone support without an executor factory.
- Tests in `Drivers/Ladybug/LadybugSearchStatementTests.cs` and
  `Drivers/Ladybug/LadybugSearchExecutorTests.cs` pin the internal search statement/execution
  foundation, including search parameter snapshot behavior, while asserting `LadybugGraphDriver` is
  still not an `ISearchGraphDriver`.
- Tests in `Drivers/Ladybug/LadybugPackageRuntimeTests.cs` pin the current LadybugDB package runtime
  facts using private test-only package references, exercise the normalizer against real list/null
  graph writes and reads, and assert the core project still has no LadybugDB package reference.

## Expected Implementation Work

1. Decide package/native dependency shape for the LadybugDB provider. Do not add core package
   references until this is explicit.
2. Extend package-runtime proof from the current Saga/entity-edge/episode/FTS/vector/BFS/ranker
   coverage into remaining graph maintenance and search statements, especially delete/clear and
   `UNION` paths that are not already covered by real-package tests.
3. Add/use the LadybugDB package in the core driver/core boundary selected above and decide how
   its connections/options should be represented in `GraphitiOptions`.
4. Add a concrete LadybugDB package adapter for `ILadybugQueryExecutor`. Keep `GraphProvider.Kuzu`
   unsupported in DI/options until save/get/delete, bulk paths, saga episode queries, fulltext,
   vector search, BFS, rerankers, and DI construction are proven against the real backend. The search
   statement and abstract execution shapes now have focused real-package proof but still need
   concrete adapter coverage. Keep the
   adapter allocation-aware: avoid unnecessary per-row dictionaries/lists, closure-heavy query loops,
   repeated JSON/string conversions, and exception-driven type coercion where the package API allows
   direct mapping.
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
