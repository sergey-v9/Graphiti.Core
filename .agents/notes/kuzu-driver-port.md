# LadybugDB Provider / Kuzu Parity

LadybugDB is the C# port's main graph-provider target. Kuzu remains the Python parity lineage and
compatibility vocabulary. The driver lives in `src/Graphiti.Core/Drivers/Ladybug/` inside the Core
assembly (plan 06 folded it back in): `Graphiti.Core` owns the `LadybugDB` / `LadybugDB.Native` package
refs, `AddLadybugDbGraphDriver`, `LadybugDbOptions`, the factory, full-text query construction, and
Ladybug label-filter syntax. The driver-facing provider value is `GraphProvider.LadybugDb`;
`GraphProvider.Kuzu` is an `[Obsolete]` compatibility alias resolving through core DI/options.

## Package pin & feed

Graphiti pins the fork-published dev package family **`0.18.0-dev.18.1.eng-d8277a8e5`** for both
`LadybugDB` and `LadybugDB.Native`, from the `sergey-v9/ladybug-dotnet` GitHub Packages feed
(`https://nuget.pkg.github.com/sergey-v9/index.json`, via `NuGet.config` + `Directory.Packages.props`).
Restores require a `read:packages` credential for source `github_ladybug` (passed as
`NuGetPackageSourceCredentials_github_ladybug`); there is no local/offline fallback (intentional). The C
API was unchanged 0.17.0→0.17.2. `LadybugDB.Extensions` is **not** adopted — Core already owns its DI
helper, options, factory, and driver boundary, so it would add host-level abstraction without a
demonstrated need. Bump the pin only when the binding repo publishes a newer dev version (see Self-service
bindings). The repeatable bump/adopt/steer loop is `ladybug-sync-procedure.md`.

**Bumped to `0.18.0-dev.18.1.eng-d8277a8e5` (2026-06-29, verified green).** The binding fork addressed all
six 2026-06-29 consumer wishes (`GRAPHITI_SEARCH_EXTENSIONS_FEEDBACK.md`) and shipped the first green
`0.18.0-dev` source-built publish across all 5 RIDs. Per its `upstream-engine.pin` `consumer_impact`:
**interop=none, fts_scoring=unchanged-from-v0.17.1**; it brings the engine fixes (double-free-on-destroy,
delete/checkpoint CSR SIGSEGV) and new DDL (`DROP_FTS_INDEX`, `DROP INDEX IF EXISTS`). The earlier Linux
ABI-mismatch (`INSTALL fts` pulling a 0.17.0 extension against a 0.18.0 engine) is fixed: the dev track
now source-builds the fts/vector extensions and **pre-seeds the engine's `~/.lbdb` extension cache** at
load, so `INSTALL/LOAD` works with no Graphiti change. Two new binding capabilities are now adoptable:
`Connection.ExecuteMany` (prepare-once/bind-many) and `DROP_FTS_INDEX` (lets the FTS-idempotency
message-catch become an explicit drop-then-create + missing-index guard). `FLOAT[N]` binding stays
engine-gated. See `ladybug-sync-procedure.md`.

## Native search — already taken, already faithful

Python's design intent is *search pushed down into the DB, rank in-app* (`graphiti_core/graph_queries.py`:
every backend computes cosine inline and runs native fulltext; RRF/MMR/cross-encoder are the only in-app
steps). The C# Ladybug driver does exactly this — native `array_cosine_similarity` over `FLOAT[]` columns
and native `QUERY_FTS_INDEX` (BM25), a near-verbatim port of Python's Kuzu branch
(`LadybugSearchStatementBuilder.cs`, `LadybugFulltextQuery.cs`). Only the **InMemory** reference driver
computes cosine/BM25 in managed code (`TensorPrimitives` + `Bm25TextScorer`), by design. FTS tuning (BM25
k/b, tokenizer/stemmer/stopwords) stays at engine defaults — tuning would diverge from parity. The
**HNSW** vector index is deliberately not adopted (Python uses no ANN on any backend); that gate is
closed — see `roadmap.md` "HNSW vector tier — closed".

## Cross-platform: win-x64 + gated linux-x64

Graphiti hard-codes no RID; the driver references the cross-platform meta packages. The linux-x64 loader
gap (plan 07) was reproduced in WSL2 (`LOAD EXTENSION FTS` undefined symbol
`_ZTIN4lbug7catalog12IndexAuxInfoE`), root-caused to a `ladybug-dotnet` native-resolver gap, and fixed in
the fork (commit `53e5ab5`: the resolver now probes `runtimes/<rid>/native` and loads with
`RTLD_NOW | RTLD_GLOBAL`), shipped in `0.17.1-dev.2.1.g53e5ab5`. Graphiti has an additive gated smoke
`PackageRuntime_LinuxFtsAndVectorExtensionsCreateAndQuery` (`Category=LinuxLadybugSmoke`) wired in
`.github/workflows/full.yml` behind `GRAPHITI_ENABLE_LINUX_LADYBUG_SMOKE=1` / env
`GRAPHITI_RUN_LINUX_LADYBUG_SMOKE=1`; the win-x64 full verifier stays unconditional.

## Self-service bindings

`sergey-v9/ladybug-dotnet` is **our fork** — we own it and publish from it. When the driver needs a
capability the LadybugDB *engine* supports but the C# bindings don't yet wrap, implement the wrapper in
`W:\code\ladybug\tools\csharp_api`, commit, and push to `sergey-v9/ladybug-dotnet`; its dev-packages
workflow builds `0.17.1-dev.N.g<sha>`, which Graphiti consumes by bumping `Directory.Packages.props`.
First confirm it's a *bindings* gap (the engine already supports it), and prefer wrapping the existing
engine feature over inventing new surface. Binding changes belong in the binding repo, not in Graphiti.
(Supersedes the earlier "patch locally, don't push" rule.) The nearby `W:\code\ladybug` checkout is
preserved provenance — do not clean or overwrite it while working on Graphiti.

## Current status

- `Graphiti.Core` owns the LadybugDB package/native refs. `src/Graphiti.Core/Drivers/Ladybug/` owns
  schema, statement construction, record mapping, full-text query construction, label-filter fragments,
  the concrete package executor, and executor-backed graph/search behavior. `LadybugGraphDriver` is
  internal and delegates search through `LadybugSearchExecutor`. `LadybugDbGraphDriverFactory` creates
  drivers directly from core; `LadybugDbOptions` / `AddLadybugDbGraphDriver` give host-facing
  `DatabasePath` config. The concrete driver reports `GraphProvider.LadybugDb`; `GraphProvider.Kuzu` is
  the obsolete alias.
- `LadybugPackageRuntimeTests` exercise the real package/native path in normal verification: schema
  creation, list/array/empty-list/null parameter binding, FTS load/search, vector search, filters, direct
  bulk-save embedding/relationship persistence, saga-scoped retrieval + content filter/order/limit,
  namespace/model embedding reloads by UUID, paged group reads, directed endpoint-pair + incident edge
  reads, group-id enumeration, public namespace community/saga reads + typed-delete isolation, BFS/rankers,
  delete/clear flows (incl. Python-compatible empty-list no-op vs null clear-all and Saga-preserving scoped
  clear), file-backed `DatabasePath` persistence + index rebuild after reopen, and Python Kuzu `':memory:'`
  sentinel compatibility (normalized to the empty-string path at the Graphiti boundary). Treat the tests
  as the detailed proof source; don't add a separate native-gated smoke suite unless it covers a genuinely
  new runtime/CI/platform requirement.

## Provider policy

- LadybugDB is the provider investment target. Implement against Python Kuzu behavior for parity, but use
  LadybugDB naming for the driver-facing surface. Keep `GraphProvider.Kuzu` only as the `[Obsolete]`
  alias. FalkorDB/InMemory/Neptune policy lives in `decisions.md`; Neo4j was removed 2026-06-17.
- C# intentionally diverges from current Python Kuzu provider-specific inconsistencies: it uses the full
  `SagaNode` shape, saves/returns entity-edge `reference_time`, and keeps `GetEntityEdgesByNodeUuidAsync`
  incident to either endpoint (per the public graph-driver contract).
- If runtime proof exposes behavior that looks like a LadybugDB package/binding bug, mark it separately
  from Graphiti port gaps; work around proven backend limits deliberately but keep them visible.

## Confirmed package/API facts

- Package id `LadybugDB`; native assets in `LadybugDB.Native` + RID-specific packages. Exposed API:
  `Database`, `Connection`, `Query`, `Prepare`, `Execute`, `PreparedStatement.Bind`, `QueryResult.Rows`,
  `Node`, `Rel`. The API is synchronous; a single `Connection` serializes operations internally.
- `Database(databasePath, SystemConfig)` uses an empty string for in-memory (Python Kuzu uses
  `':memory:'`; the factory normalizes the sentinel).
- FTS requires explicit `INSTALL FTS; LOAD EXTENSION FTS;` before `CREATE_FTS_INDEX` / `QUERY_FTS_INDEX`.
  Persisted setup is idempotent across reopen: duplicate-index errors for the four Graphiti FTS indexes
  are ignored because `CREATE_FTS_INDEX` has no `IF NOT EXISTS`.
- LadybugDB can reject post-projection ordering by node variables (`ORDER BY n.uuid`); use projected
  aliases (`uuid`, `valid_at`, `created_at`) after `RETURN`.
- The binding once rejected `List<string>`/arrays/empty-lists/null in `PreparedStatement.Bind`; the fork
  package family wraps native `lbug_value` creation and binds parameters directly, so Graphiti executes
  Kuzu-style statements with bound parameters (the old `LadybugStatementNormalizer` workaround was
  removed — an unauthenticated feed now fails the restore rather than silently rewriting literals).

## Touchpoints

`Drivers/GraphProvider.cs` (Kuzu alias); `src/Graphiti.Core/Drivers/Ladybug/` (schema, statement, record
mapper, driver, executor, full-text helper, search-filter adapter, search executor, factory);
`Configuration/LadybugDbOptions.cs` + `LadybugDbServiceCollectionExtensions.cs`;
`Configuration/GraphitiServiceCollectionExtensions.cs` (constructs LadybugDb/Kuzu unless a
`GraphDriverFactory` override is set); `Search/CompiledSearchFilter.cs` (shared colon-label syntax for
generic callers; Ladybug owns its label-filter fragments in `Drivers/Ladybug/LadybugSearchFilter`);
`Search/SearchUtilities.cs` (no Kuzu full-text branch — that lives in `Drivers/Ladybug/LadybugFulltextQuery`);
`tests/Graphiti.Core.Tests/Drivers/Ladybug/`.

## Remaining work (only when a real need appears)

Broaden workflow coverage, add host-facing options, or add native-gated smokes **only** where they
exercise behavior the current package-runtime tests don't already cover. Update `decisions.md` /
`evolution.md` / `handoff.md` / `roadmap.md` when provider status or support level changes.
