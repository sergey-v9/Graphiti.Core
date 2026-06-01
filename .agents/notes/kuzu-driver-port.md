# LadybugDB Provider / Kuzu Parity

LadybugDB is the C# port's main graph-provider target. Kuzu remains the Python parity lineage and
compatibility vocabulary while the driver-facing name moves toward LadybugDB.

## Current Status

- Core remains free of LadybugDB package/native references.
- Internal core helpers under `Drivers/Ladybug/` own schema, statement construction, record mapping,
  statement normalization, and executor-backed graph/search behavior over `ILadybugQueryExecutor`.
- `LadybugGraphDriver` is internal, implements the graph-driver surface, and delegates search through
  `LadybugSearchExecutor`.
- `Graphiti.Core` is the core LadybugDB driver. It owns `LadybugDB` and native package
  references, the concrete package executor, `LadybugDbGraphDriverFactory`, `LadybugDbOptions`, and
  `AddLadybugDbGraphDriver` helpers.
- Optional-package DI works by setting `GraphitiOptions.GraphDriverFactory`. The core
  `GraphProvider.Kuzu` enum/options path remains unsupported by core validation.
- runtime-backed proof covers the main ingest/search/removal/triplet/bulk/saga/community workflows
  and file-backed `DatabasePath` persistence. Treat tests as the detailed proof source.
- The test project has private LadybugDB package references for runtime facts. Do not add those
  references to `Graphiti.Core`.
- Ladybug sources are available at `W:\code\ladybug`, including C# bindings and the base library. Do
  not inspect them during Graphiti work unless a confirmed Ladybug issue blocks progress; if that
  happens, keep any Ladybug fix as a separate local commit.

## Provider Policy

- LadybugDB is the provider investment target.
- Implement against Python Kuzu behavior for parity, but prefer LadybugDB naming for the final
  driver-facing product surface.
- Do not wire `GraphProvider.Kuzu` through core DI/options until the naming/support decision is
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
  Kuzu uses `':memory:'`.
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
- The current Python Kuzu code has provider-specific inconsistencies around Saga schema/query fields
  and entity-edge `reference_time`. The C# foundation intentionally uses the full `SagaNode` shape and
  saves/returns entity-edge `reference_time`.

## Existing Touchpoints

- `Drivers/GraphProvider.cs`: keeps `GraphProvider.Kuzu` as pending compatibility vocabulary.
- `Drivers/Ladybug/`: schema, statement, normalizer, record mapper, internal driver, search
  statements, and search executor.
- `Search/CompiledSearchFilter.cs`: Kuzu label-query behavior for node and edge filters.
- `Search/SearchUtilities.cs`: `GraphProvider.Kuzu` full-text branch and
  `BuildKuzuFulltextQuery`.
- `src/Graphiti.Core/`: core LadybugDB driver and DI/factory surface.
- `tests/Graphiti.Core.Tests/Drivers/Ladybug/`: foundation, internal driver, runtime package,
  core driver, search statement, and search executor coverage.
- `GraphitiOptionsValidationTests.cs`: current unsupported enum/options behavior.

## Remaining Work

1. Broaden optional-package workflow coverage only where it exercises behavior not already covered by
   the current runtime-backed ingest/search/removal/triplet/bulk/saga/community tests.
2. Add host-facing options only when real runtime requirements appear. `DatabasePath` exists; avoid
   speculative options.
3. Add native-gated integration smoke tests if they provide coverage beyond the current package
   runtime tests.
4. Decide the final driver-facing naming and whether/when core should expose a supported enum/DI path.
5. Revisit interim Kuzu query/filter helpers and move provider-specific behavior into the driver when
   appropriate.
6. Finish the Kuzu-to-LadybugDB terminology transition once the provider is stable.
7. Update `decisions.md`, `evolution.md`, `handoff.md`, and `roadmap.md` when provider status or
   support level changes.

This note supersedes older comments that described Kuzu as intentionally out of scope for the C# port.
