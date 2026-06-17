# LadybugDB Provider / Kuzu Parity

LadybugDB is the C# port's main graph-provider target. Kuzu remains the Python parity lineage and
compatibility vocabulary.

**Update 2026-06-14 (plan 05 B + E):** the driver-facing provider value is
`GraphProvider.LadybugDb`; `GraphProvider.Kuzu` is an `[Obsolete]` compatibility alias that still
resolves through core DI/options when `AddLadybugDbGraphDriver` is registered. The LadybugDB driver
was extracted into a separate opt-in package `src/Graphiti.Core.Drivers.Ladybug/`. `Graphiti.Core`
no longer references the LadybugDB packages; the new project owns them, `AddLadybugDbGraphDriver`,
driver-owned full-text query construction, and Ladybug label-filter syntax. Step E.2 now consumes the
`sergey-v9/ladybug-dotnet` fork's GitHub Packages feed rather than the sibling local artifact feed:
Graphiti pins `LadybugDB` / `LadybugDB.Native` to `0.17.1-dev.1.1.g6f3dbed` from
`https://nuget.pkg.github.com/sergey-v9/index.json`. Restores that include the Ladybug driver require
a NuGet credential for source `github_ladybug` with `read:packages`.

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
a `LadybugDB.Extensions` package and the P0–P3 `feature/parity-extensions-2026-06` initiative;
Graphiti now pins the fork-published `0.17.1-dev.1.1.g6f3dbed` package family (C API unchanged
0.17.0→0.17.2).

## WS-1 binding and cross-platform audit (2026-06-14)

Read-only audit result: the newer nearby `W:\code\ladybug\tools\csharp_api` checkout is on
`feature/parity-extensions-2026-06` at `0e709a095ad9d767a096cb8eb2a207b0091914f3`, clean, and has
local package artifacts for `LadybugDB` / `LadybugDB.Native` version `0.17.1` plus RID native packages
for Windows, Linux, and macOS. The earlier Graphiti parameter-binding repair is still present: commit
`d13d2e9` is an ancestor of that checkout, `PreparedStatement.Bind` routes nulls, lists, arrays, and
typed empty lists through native `lbug_value` handles, and the package tests cover `List<string>`,
`float[]`, `Array.Empty<string>()`, and null parameters. Search-extension tests in the binding repo
mirror Graphiti's `QUERY_FTS_INDEX(... $query, TOP := $limit)` and inline
`array_cosine_similarity(... CAST($search_vector AS FLOAT[3]))` paths; loader code now uses
`dlopen(RTLD_NOW | RTLD_GLOBAL)` on Unix-like systems and rethrows undefined-symbol failures.

Graphiti cross-platform audit result: Graphiti itself does not hard-code a `win-x64` package or runtime
identifier; the Ladybug driver references the cross-platform meta packages. Follow-up on 2026-06-14
added package-consumption smoke checks to `Verify-GraphitiCore.ps1`: fresh temp consumers
restore/build/setup/run `Graphiti.Core` from the packed core output + nuget.org only, and
`Graphiti.Core.Drivers.Ladybug` from both packed Graphiti outputs + the Ladybug GitHub Packages feed
and nuget.org, with strict `NuGet.config`, temp `NUGET_PACKAGES`, `--no-cache`, setup through
`Graphiti` with a packed LadybugDB driver, and asserted provider output. The remaining Linux risk is runtime validation on a Linux runner, not an
obvious source-level Windows dependency or a missing Windows package-consumer proof. Follow-up on
2026-06-14 made persisted Ladybug setup idempotent across reopen: duplicate errors for the four exact
Graphiti FTS indexes are ignored because LadybugDB's `CREATE_FTS_INDEX` has no `IF NOT EXISTS`/skip flag,
and `LadybugRuntimeDriverTests.FileBackedDriverCanRebuildIndicesAfterReopenAndSearch` proves
build-write-close-reopen-build-search on a file-backed database.

Decision point resolved 2026-06-17: the user directed Graphiti to consume packages published by the
`sergey-v9/ladybug-dotnet` fork's GitHub Packages workflow. Graphiti now pins the normalized fork dev
package family `0.17.1-dev.1.1.g6f3dbed` instead of the local
`0.17.0-alpha.2-graphiti.1` artifact family. `LadybugDB.Extensions` should not be adopted by default
in Graphiti Core: the current Graphiti package already owns its DI helper, options, factory, and
driver boundary, and adopting the Extensions package would add host-level abstractions without a
demonstrated Graphiti Core requirement.

2026-06-17 recheck: the nested binding repo is still clean on
`feature/parity-extensions-2026-06` at `0e709a0`. The actual local NuGet artifacts and their nuspecs
are versioned `0.17.1` (`LadybugDB`, `LadybugDB.Native`, all RID native packages, `LadybugDB.Arrow`,
and `LadybugDB.Extensions`), even though binding-side `version.txt`/README text says package family
`0.17.1.0`. The fork's `github-packages-dev.yml` run
`https://github.com/sergey-v9/ladybug-dotnet/actions/runs/27654947039` published normalized dev
version `0.17.1-dev.1.1.g6f3dbed`; Graphiti consumes that published version. A 2026-06-17 GitHub
Packages recheck reports only that version for both `LadybugDB` and `LadybugDB.Native`. With the
active `read:packages` GitHub token passed as `NuGetPackageSourceCredentials_github_ladybug`,
`dotnet restore src\Graphiti.Core.Drivers.Ladybug\Graphiti.Core.Drivers.Ladybug.csproj --locked-mode`
and `.\eng\Verify-GraphitiCore.ps1` are green (`1021` passed, `3` skipped; both Graphiti packages and
fresh package-consumer smoke builds succeeded).

## Self-service bindings (2026-06-17)

`sergey-v9/ladybug-dotnet` is **our fork** — we own it and publish from it. The LadybugDB *engine*
already supports far more than the C# bindings currently wrap. So when the Ladybug driver needs a
capability that exists in the engine but is **missing from the C# bindings** (we just haven't wrapped
it yet), we don't have to wait on anyone: implement the wrapper in `W:\code\ladybug\tools\csharp_api`,
commit, and **push to `sergey-v9/ladybug-dotnet`**. Its dev-packages GitHub Actions workflow builds a
new normalized dev package version (e.g. `0.17.1-dev.N.g<sha>`), which Graphiti consumes by bumping the
pin in `Directory.Packages.props` and restoring from the `github_ladybug` feed. This **supersedes** the
earlier "patch locally, do not push remotely, keep a local-only package" rule. (Binding changes belong
in the binding repo, not in Graphiti; first verify the capability really exists in the LadybugDB
engine — bindings gap, not an engine gap — and prefer wrapping the existing engine feature over
inventing new surface.)

## SCHEDULED: merge the Ladybug driver back into Graphiti.Core (2026-06-17)

Decision (Sergey): the plan-05 E package split is being **reversed**. LadybugDB is the first-class
provider, so a separate assembly/package (`Graphiti.Core.Drivers.Ladybug`) has lost its point — the
driver should move into its own `src/Graphiti.Core/Drivers/Ladybug/` folder inside the Core assembly,
one build. When executed this collapses the two-assembly public-API snapshot to one, retires the
`GraphitiCoreOnlyTests` mode and the core-only CI lane, folds the `LadybugDB`/`LadybugDB.Native`
package refs into `Graphiti.Core.csproj`, and moves `AddLadybugDbGraphDriver`/`LadybugDbOptions`/the
factory into Core. **Consequence to accept:** `Graphiti.Core` then depends on the LadybugDB packages +
the `github_ladybug` feed — it no longer restores from nuget.org alone, every consumer pulls the
native binaries and needs the credential, and Core can't be published to nuget.org until LadybugDB is
public there. Acceptable for the current private-fork workflow; revisit only if public nuget.org
publishing of `Graphiti.Core` becomes a goal (that is the still-user-gated release decision).

## Current Status

- The LadybugDB package and native references are owned by the `Graphiti.Core.Drivers.Ladybug` project
  (superseding the historical "`Graphiti.Core` owns" bullets below). **(Scheduled to change — see the
  merge-into-Core decision above.)**
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
  `GraphProvider.Kuzu` alias, file-backed index rebuild/search after reopening, and Python Kuzu
  `':memory:'` sentinel compatibility. Treat tests as the detailed proof source.
- `LadybugPackageRuntimeTests` exercise the actual LadybugDB package/native path in normal
  verification, including schema creation, direct list/array/empty-list/null parameter binding, FTS loading/search, vector
  search, filters, direct driver bulk-save embedding/relationship persistence, saga-scoped episode
  retrieval, saga content filtering/order/limit behavior, namespace/model embedding reloads by UUID,
  paged node/edge group reads, directed endpoint-pair edge reads, incident edge reads, group-id
  enumeration, public namespace community/saga group reads and typed-delete isolation, BFS/rankers,
  and delete/clear flows, including Python-compatible empty group-list clear no-op versus null
  clear-all. Do not add a separate native-gated smoke suite
  unless it covers a new runtime requirement or CI/platform constraint the current package runtime
  tests do not cover.
- The LadybugDB package has a nearby source checkout at `W:\code\ladybug`; this is background
  provenance for the NuGet/API surface. Graphiti work operates against package-facing behavior and
  Graphiti tests. When package or binding behavior looks suspect, mark the symptom separately from
  Graphiti port gaps. The user has authorized repair work in that checkout when it unblocks the C#
  driver: patch and commit LadybugDB changes there, push the fork's `dev` branch when a fresh package
  is needed, let the fork workflow publish a new GitHub Packages dev version, then bump Graphiti to
  that published version.
- As of 2026-06-11, the nearby Ladybug checkout may show a locally advanced `extension` submodule and
  an untracked `tools/csharp_api/` checkout with reference material. Treat that as preserved
  recovery/provenance state; do not clean or overwrite it while working on Graphiti.

## Provider Policy

- LadybugDB is the provider investment target.
- Implement against Python Kuzu behavior for parity, but prefer LadybugDB naming for the final
  driver-facing product surface.
- Use `GraphProvider.LadybugDb` as the driver-facing provider value. Keep `GraphProvider.Kuzu` only as
  the `[Obsolete]` compatibility alias for callers that have not renamed yet.
- Keep FalkorDB, InMemory, and Neptune policy in `decisions.md`; do not repeat it here. (Neo4j was
  removed 2026-06-17 and is no longer a provider.)
- If runtime proof exposes behavior that looks like a LadybugDB package or binding bug, mark it
  separately from Graphiti port gaps. Work around proven backend limitations deliberately when useful,
  but keep them visible.

## Confirmed Package/API Facts

- Package id: `LadybugDB`; version comes from central package management. Graphiti currently uses
  the fork-published dev package family `0.17.1-dev.1.1.g6f3dbed` from the
  `sergey-v9/ladybug-dotnet` GitHub Packages feed via `NuGet.config`. Restores that include the
  Ladybug driver require credentials for source `github_ladybug` with `read:packages`. `Graphiti.Core`
  itself remains Ladybug-free and restores from nuget.org alone.
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
  The `sergey-v9/ladybug-dotnet` fork's published `0.17.1-dev.1.1.g6f3dbed` package family includes
  the C# binding repair that wraps native `lbug_value` creation and
  `lbug_prepared_statement_bind_value`. Graphiti consumes that package family and executes statements
  with bound parameters directly.
- `LadybugStatementNormalizer` was removed from Graphiti after the package repair. If the GitHub
  Packages feed cannot be authenticated, restore will fail instead of silently falling back to
  literal rewriting.
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
- `Search/CompiledSearchFilter.cs`: uses the shared Cypher-style colon-label syntax for generic callers;
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
