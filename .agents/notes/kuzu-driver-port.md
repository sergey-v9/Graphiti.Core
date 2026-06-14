# LadybugDB Provider / Kuzu Parity

LadybugDB is the C# port's main graph-provider target. Kuzu remains the Python parity lineage and
compatibility vocabulary.

**Update 2026-06-14 (plan 05 B + E):** the driver-facing provider value is
`GraphProvider.LadybugDb`; `GraphProvider.Kuzu` is an `[Obsolete]` compatibility alias that still
resolves through core DI/options when `AddLadybugDbGraphDriver` is registered. The LadybugDB driver
was extracted into a separate opt-in package `src/Graphiti.Core.Drivers.Ladybug/`. `Graphiti.Core`
no longer references the LadybugDB packages; the new project owns them, `AddLadybugDbGraphDriver`,
driver-owned full-text query construction, and Ladybug label-filter syntax. The one remaining release
blocker is publishing/replacing the local `0.17.0-alpha.2-graphiti.1` package family (Step E.2):
patch+pack in `W:\code\ladybug\tools\csharp_api`, publish to a real feed (or keep the local feed for
dev), then point `Graphiti.Core.Drivers.Ladybug` at it.

## Native search adoption (deep-dive 2026-06-14)

Question posed: "what else can we take from the Ladybug provider — maybe vector search and text
search — if it aligns with the Python authors' intent." Verified answer: **already taken, and already
faithful.** Python's design intent is *search pushed down into the DB, rank in-app*
(`graphiti_core/graph_queries.py`: every backend computes cosine inline via
`get_vector_cosine_func_query` and runs native fulltext via `get_nodes_query`; RRF/MMR/cross-encoder
are the only in-app steps). The C# Ladybug driver already does exactly this — native
`array_cosine_similarity` over `FLOAT[]` columns and native `QUERY_FTS_INDEX` (BM25), a near-verbatim
port of Python's Kuzu branch (`LadybugSearchStatementBuilder.cs`, `LadybugFulltextQuery.cs`). Only the
**InMemory reference driver** computes cosine/BM25 in managed code (`TensorPrimitives` +
`Bm25TextScorer`), by design.

Net-new lever, NOT adopted: LadybugDB's **HNSW vector index** (`CREATE_VECTOR_INDEX` /
`QUERY_VECTOR_INDEX`, `vector` extension). Python uses **no** ANN index on any backend
(`get_range_indices` is `[]` for KUZU; cosine is always inline/full-scan), so adopting HNSW would be a
C#-only enhancement *beyond* Python. It trades exact→approximate (ANN recall/determinism cost) and
only pays off at large scale. Decision: **do not adopt now.** If ever taken, gate it behind an opt-in
flag with exact full-scan cosine as the default, justified by a BenchmarkDotNet before/after (perf
tier, post-moratorium, evidence-driven). FTS tuning (BM25 k/b, tokenizer/stemmer/stopwords) is
deliberately left at engine defaults — tuning would DIVERGE from Python parity.

Bindings feedback left for the binding agent at
`W:\code\ladybug\tools\csharp_api\GRAPHITI_SEARCH_EXTENSIONS_FEEDBACK.md` (uncommitted; their repo):
(1) their P0/WS-F Linux extension-symbol-visibility fix is on Graphiti's critical path — please verify
a full `fts` (and ideally `vector`) `CREATE/QUERY` round-trip on linux-x64, since Graphiti is only
validated on win-x64 today; (2) optional WS-C first-class `FLOAT[N]` array binding would let us drop
the inline `CAST($search_vector AS FLOAT[dim])`. The local binding has since advanced to `0.17.1` with
a `LadybugDB.Extensions` package and the P0–P3 `feature/parity-extensions-2026-06` initiative; Graphiti
still pins `0.17.0-alpha.2-graphiti.1` (C API unchanged 0.17.0→0.17.2).

## Current Status

- The LadybugDB package and native references are owned by the `Graphiti.Core.Drivers.Ladybug` project
  (superseding the historical "`Graphiti.Core` owns" bullets below).
- `Drivers/Ladybug/` owns schema, statement construction, record mapping,
  full-text query construction, Ladybug label-filter fragments, the concrete package executor, and
  executor-backed graph/search behavior.
- `LadybugGraphDriver` is internal, implements the graph-driver surface, and delegates search through
  `LadybugSearchExecutor`.
- `LadybugDbGraphDriverFactory` creates LadybugDB-backed drivers directly from core.
- `LadybugDbOptions` and `AddLadybugDbGraphDriver` provide host-facing `DatabasePath`
  configuration.
- `GraphProvider.Kuzu` is an obsolete but supported core options/DI alias that resolves to the
  LadybugDB-backed driver when the LadybugDB package registration is present; the concrete driver
  reports `GraphProvider.LadybugDb`.
- Runtime proof covers the main ingest/search/removal/triplet/bulk/saga/community workflows,
  direct driver bulk-save embedding/relationship persistence, namespace/model embedding reloads by
  UUID, saga-scoped episode retrieval, saga content filtering/order/limit behavior, saga predecessor
  lookup, paged node/edge group reads, directed endpoint-pair edge reads, incident entity-edge reads,
  group-id enumeration, public namespace community/saga reads and typed deletes, file-backed
  `DatabasePath` persistence for both `GraphProvider.LadybugDb` and the obsolete
  `GraphProvider.Kuzu` alias, and Python Kuzu `':memory:'` sentinel compatibility. Treat tests as
  the detailed proof source.
- `LadybugPackageRuntimeTests` exercise the actual LadybugDB package/native path in normal
  verification, including schema creation, direct list/array/empty-list/null parameter binding, FTS loading/search, vector
  search, filters, direct driver bulk-save embedding/relationship persistence, saga-scoped episode
  retrieval, saga content filtering/order/limit behavior, namespace/model embedding reloads by UUID,
  paged node/edge group reads, directed endpoint-pair edge reads, incident edge reads, group-id
  enumeration, public namespace community/saga group reads and typed-delete isolation, BFS/rankers,
  and delete/clear flows. Do not add a separate native-gated smoke suite
  unless it covers a new runtime requirement or CI/platform constraint the current package runtime
  tests do not cover.
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
- Use `GraphProvider.LadybugDb` as the driver-facing provider value. Keep `GraphProvider.Kuzu` only as
  the `[Obsolete]` compatibility alias for callers that have not renamed yet.
- Keep Neo4j, FalkorDB, InMemory, and Neptune policy in `decisions.md`; do not repeat it here.
- If runtime proof exposes behavior that looks like a LadybugDB package or binding bug, mark it
  separately from Graphiti port gaps. Work around proven backend limitations deliberately when useful,
  but keep them visible.

## Confirmed Package/API Facts

- Package id: `LadybugDB`; version comes from central package management. Graphiti currently uses
  the local repaired package family `0.17.0-alpha.2-graphiti.1` from
  `../../ladybug/tools/csharp_api/artifacts` via `NuGet.config`.
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

## Package Bug Recovery

- A LadybugDB .NET binding gap blocked Graphiti's Kuzu-style statements: package
  `PreparedStatement.Bind(object?)` rejected `List<string>`, arrays, empty lists, and null values.
  The local `W:\code\ladybug\tools\csharp_api` checkout now has a C# binding repair that wraps
  native `lbug_value` creation and `lbug_prepared_statement_bind_value`. Graphiti consumes the local
  `0.17.0-alpha.2-graphiti.1` package family and executes statements with bound parameters directly.
- `LadybugStatementNormalizer` was removed from Graphiti after the local package repair. If the
  local package source is missing, restore will fail instead of silently falling back to literal
  rewriting.
- The current Python Kuzu code has provider-specific inconsistencies around Saga schema/query fields,
  entity-edge `reference_time`, and source-only `EntityEdge.get_by_node_uuid` reads. The C#
  foundation intentionally uses the full `SagaNode` shape, saves/returns entity-edge
  `reference_time`, and keeps `GetEntityEdgesByNodeUuidAsync` incident to either endpoint like the
  public graph-driver contract.

## Existing Touchpoints

- `Drivers/GraphProvider.cs`: keeps `GraphProvider.Kuzu` as compatibility vocabulary.
- `src/Graphiti.Core.Drivers.Ladybug/Drivers/Ladybug/`: schema, statement, record mapper, driver,
  concrete executor, search full-text query helper, search-filter adapter, statements, search
  executor, and driver factory.
- `src/Graphiti.Core.Drivers.Ladybug/Configuration/LadybugDbOptions.cs`: host-facing LadybugDB driver options.
- `src/Graphiti.Core.Drivers.Ladybug/Configuration/LadybugDbServiceCollectionExtensions.cs`: LadybugDB DI helper.
- `Configuration/GraphitiServiceCollectionExtensions.cs`: core delegates LadybugDb/Kuzu to the
  Ladybug-package-registered `GraphDriverFactory` and throws if absent.
- `Search/CompiledSearchFilter.cs`: uses the shared Neo4j-style label syntax for generic callers;
  active Ladybug search owns Ladybug/Kuzu label-filter fragments in
  `Drivers/Ladybug/LadybugSearchFilter`.
- `Search/SearchUtilities.cs`: no longer has a `GraphProvider.Kuzu` full-text branch because active
  Ladybug full-text construction lives in `Drivers/Ladybug/LadybugFulltextQuery`.
- `tests/Graphiti.Core.Tests/Drivers/Ladybug/`: foundation, internal driver, runtime, search
  statement, search executor, and core DI coverage.

## Remaining Work

1. Broaden workflow coverage only where it exercises behavior not already covered by the current
   ingest/search/removal/triplet/bulk/saga/community tests.
2. Add host-facing options only when real runtime requirements appear. `DatabasePath` exists; avoid
   speculative options.
3. Add native-gated integration smoke tests only if they provide coverage beyond the current package
   runtime tests or solve a CI/platform constraint.
4. DONE (plan-05 B): the final driver-facing naming is `GraphProvider.LadybugDb`, with
   `GraphProvider.Kuzu` retained as an `[Obsolete]` compatibility alias.
5. DONE (plan-05 B2): shared Kuzu compatibility helpers were retired from `SearchUtilities` and
   `CompiledSearchFilter`; active Ladybug full-text and label-filter behavior lives in the
   Ladybug driver package.
6. DONE (plan-05 B2): the Kuzu-to-LadybugDB terminology transition is complete for the C# provider
   surface. `GraphProvider.Kuzu` remains only as an obsolete compatibility alias.
7. Update `decisions.md`, `evolution.md`, `handoff.md`, and `roadmap.md` when provider status or
   support level changes.

This note supersedes older comments that described Kuzu as intentionally out of scope for the C# port.
