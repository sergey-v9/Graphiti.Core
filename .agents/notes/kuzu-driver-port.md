# LadybugDB Provider / Kuzu Parity

LadybugDB is the C# port's main graph-provider target. Kuzu remains the Python parity lineage and
compatibility vocabulary while the driver-facing name moves toward LadybugDB.

## Current Status

- `Graphiti.Core` owns the LadybugDB package and native references.
- `Drivers/Ladybug/` owns schema, statement construction, record mapping, statement normalization,
  full-text query construction, Ladybug label-filter fragments, the concrete package executor, and
  executor-backed graph/search behavior.
- `LadybugGraphDriver` is internal, implements the graph-driver surface, and delegates search through
  `LadybugSearchExecutor`.
- `LadybugDbGraphDriverFactory` creates LadybugDB-backed drivers directly from core.
- `LadybugDbOptions` and `AddLadybugDbGraphDriver` provide host-facing `DatabasePath`
  configuration.
- `GraphProvider.Kuzu` is a supported core options/DI path and resolves to the LadybugDB-backed
  driver.
- Runtime proof covers the main ingest/search/removal/triplet/bulk/saga/community workflows,
  saga-scoped episode retrieval, saga predecessor lookup, directed endpoint-pair edge reads, incident
  entity-edge reads, group-id enumeration, file-backed `DatabasePath` persistence, core
  `GraphProvider.Kuzu` `Database` persistence, and Python Kuzu `':memory:'` sentinel compatibility.
  Treat tests as the detailed proof source.
- `LadybugPackageRuntimeTests` exercise the actual LadybugDB package/native path in normal
  verification, including schema creation, list/null normalization, FTS loading/search, vector
  search, filters, saga-scoped episode retrieval, directed endpoint-pair edge reads, incident edge
  reads, group-id enumeration, BFS/rankers, and delete/clear flows. Do not add a separate
  native-gated smoke suite unless it covers a new runtime requirement or CI/platform constraint the
  current package runtime tests do not cover.
- The LadybugDB package has a nearby source checkout at `W:\code\ladybug`; this is background
  provenance for the NuGet/API surface. Graphiti work operates against package-facing behavior and
  Graphiti tests. When package or binding behavior looks suspect, mark the symptom separately from
  Graphiti port gaps. The user has authorized local repair work in that checkout when it unblocks the
  C# driver: patch and commit LadybugDB changes only in `W:\code\ladybug`, do not push remotely,
  draft a nearby markdown request for `ladybug-dotnet`, build a local NuGet package, and connect
  Graphiti to that local package for validation.
- As of 2026-06-11, the nearby Ladybug checkout may show a locally advanced `extension` submodule and
  an untracked `tools/csharp_api/` checkout with reference material. Treat that as preserved
  recovery/provenance state; do not clean or overwrite it while working on Graphiti.

## Provider Policy

- LadybugDB is the provider investment target.
- Implement against Python Kuzu behavior for parity, but prefer LadybugDB naming for the final
  driver-facing product surface.
- Keep `GraphProvider.Kuzu` as the compatibility provider value until the final naming decision is
  explicit.
- Keep Neo4j, FalkorDB, InMemory, and Neptune policy in `decisions.md`; do not repeat it here.
- If runtime proof exposes behavior that looks like a LadybugDB package or binding bug, mark it
  separately from Graphiti port gaps. Work around proven backend limitations deliberately when useful,
  but keep them visible.

## Confirmed Package/API Facts

- Package id: `LadybugDB`; version comes from central package management.
- Native assets are packaged separately, for example `LadybugDB.Native` and RID-specific native
  packages.
- Exposed API includes `Database`, `Connection`, `Query`, `Prepare`, `Execute`,
  `PreparedStatement.Bind`, `QueryResult.Rows`, `Node`, and `Rel`.
- The API appears synchronous. A single `Connection` serializes operations internally according to
  package docs.
- `Database(string databasePath, SystemConfig)` uses an empty string for in-memory databases; Python
  Kuzu uses `':memory:'`. `LadybugDbGraphDriverFactory` normalizes the Kuzu sentinel to the
  LadybugDB empty-string path at the Graphiti boundary.
- FTS calls require explicit `INSTALL FTS; LOAD EXTENSION FTS;` before `CREATE_FTS_INDEX` /
  `QUERY_FTS_INDEX`.
- LadybugDB can reject post-projection ordering by node variables such as `ORDER BY n.uuid`; use
  projected aliases such as `uuid`, `valid_at`, and `created_at` after `RETURN`.

## Package Quirks And Workarounds

- Current package binding does not accept the list/array and null parameter shapes Graphiti uses in
  Kuzu-style statements. `List<string>`, arrays, and `object[]` throw `NotSupportedException`; null
  parameters throw `ArgumentNullException`.
- `LadybugStatementNormalizer` is the current execution strategy for those binder gaps. It rewrites
  list/array/null values into Kuzu literals while leaving scalar values bound for prepared execution.
- The current Python Kuzu code has provider-specific inconsistencies around Saga schema/query fields,
  entity-edge `reference_time`, and source-only `EntityEdge.get_by_node_uuid` reads. The C#
  foundation intentionally uses the full `SagaNode` shape, saves/returns entity-edge
  `reference_time`, and keeps `GetEntityEdgesByNodeUuidAsync` incident to either endpoint like the
  public graph-driver contract.

## Existing Touchpoints

- `Drivers/GraphProvider.cs`: keeps `GraphProvider.Kuzu` as compatibility vocabulary.
- `Drivers/Ladybug/`: schema, statement, normalizer, record mapper, driver, concrete executor, search
  full-text query helper, search-filter adapter, statements, search executor, and driver factory.
- `Configuration/LadybugDbOptions.cs`: host-facing LadybugDB driver options.
- `Configuration/LadybugDbServiceCollectionExtensions.cs`: LadybugDB DI helper.
- `Configuration/GraphitiServiceCollectionExtensions.cs`: core `GraphProvider.Kuzu` driver creation.
- `Search/CompiledSearchFilter.cs`: keeps Kuzu label-query behavior for node and edge filters for
  non-driver compatibility callers; active Ladybug search uses `Drivers/Ladybug/LadybugSearchFilter`.
- `Search/SearchUtilities.cs`: keeps the `GraphProvider.Kuzu` full-text branch and
  `BuildKuzuFulltextQuery` for non-driver compatibility callers; active Ladybug search uses
  `Drivers/Ladybug/LadybugFulltextQuery`.
- `tests/Graphiti.Core.Tests/Drivers/Ladybug/`: foundation, internal driver, runtime, search
  statement, search executor, and core DI coverage.

## Remaining Work

1. Broaden workflow coverage only where it exercises behavior not already covered by the current
   ingest/search/removal/triplet/bulk/saga/community tests.
2. Add host-facing options only when real runtime requirements appear. `DatabasePath` exists; avoid
   speculative options.
3. Add native-gated integration smoke tests only if they provide coverage beyond the current package
   runtime tests or solve a CI/platform constraint.
4. Decide the final driver-facing naming beyond the current `GraphProvider.Kuzu` compatibility value.
5. Decide whether shared Kuzu compatibility helpers should remain or be retired after final
   LadybugDB naming. Active Ladybug full-text query construction now lives in
   `Drivers/Ladybug/LadybugFulltextQuery`, and active Ladybug label-filter fragments now live in
   `Drivers/Ladybug/LadybugSearchFilter`; shared Kuzu branches remain for compatibility callers.
6. Finish the Kuzu-to-LadybugDB terminology transition once the provider is stable.
7. Update `decisions.md`, `evolution.md`, `handoff.md`, and `roadmap.md` when provider status or
   support level changes.

This note supersedes older comments that described Kuzu as intentionally out of scope for the C# port.
