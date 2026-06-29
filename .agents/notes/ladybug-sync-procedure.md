# LadybugDB binding/engine sync — repeatable procedure

The provider dependency is a **two-layer, two-repo loop that both evolve**, with Graphiti as the
reference consumer:

- **Engine** — `LadybugDB/ladybug` (third-party C/C++) at `W:\code\ladybug`. Kuzu-lineage; rides `main`.
- **Bindings** — OUR C# wrapper, fork `sergey-v9/ladybug-dotnet` (branch `dev`), a nested repo at
  `W:\code\ladybug\tools\csharp_api`. We develop these; they pin which engine commit they wrap
  (`tools/csharp_api/upstream-engine.pin`) and publish dev packages to the `github_ladybug` feed.
- **Consumer** — `Graphiti.Core`; its Ladybug driver lives in `src/Graphiti.Core/Drivers/Ladybug/`, pin
  in `Directory.Packages.props` (`LadybugDB` + `LadybugDB.Native`, kept identical).

We benefit because both layers improve; and because we are the **main consumer**, we **steer** the
bindings toward what's convenient for us. This is the loop.

## The loop (clear seam between cheap "bump+verify" and rare "adopt")

1. **BUMP + VERIFY** — the common case, low effort. When the binding publishes a new *fully restorable*
   version on the feed: bump both `Directory.Packages.props` lines, trust the **byte-identical C-API**
   guarantee in `upstream-engine.pin` (spot-check the binding's interop dir — `Native*.cs` / `NativeTypes`
   — for any *non-test-seam* change), run `.\eng\Verify-GraphitiCore.ps1` (the native-gated Ladybug suite
   is the real gate), ship. Perf/alloc wins ride along for free; no Graphiti code change.
2. **GATE on publish reality.** Never pin to a binding HEAD whose dev-feed CI run is red — the package
   must be **published across all RIDs** first. *Newest published ≠ newest committed*: verify against the
   actual feed, not `git log`. (2026-06-29: HEAD `d77c9de` is unpublishable — its source-built Linux
   natives fail; the newest fully-published version is `0.17.1-dev.14.1.gfe33adf`.)
3. **ADOPT** — the rare case, real work. Only when an engine/binding feature removes a *concrete* Graphiti
   workaround **and** the binding has round-trip test coverage for it **and** the consumer behavioral risk
   is checked (e.g. an FTS index DROP + re-index can shift BM25 scoring/ordering). Gated on the relevant
   green published feed existing.
4. **STEER continuously.** Every workaround Graphiti carries is a standing feature request logged in
   `tools/csharp_api/GRAPHITI_SEARCH_EXTENSIONS_FEEDBACK.md`. The binding closes the ones it can with
   tests; the engine closes the ones that need C-API surface. We drive direction because we're the
   reference consumer.

## How to find what changed

```
# binding changes since our pin (the gN in the version is the binding sha)
git -C W:/code/ladybug/tools/csharp_api log --oneline <pinnedSha>..HEAD
cat W:/code/ladybug/tools/csharp_api/version.txt              # binding version family
cat W:/code/ladybug/tools/csharp_api/upstream-engine.pin      # which engine commit it wraps + C-API note
# engine changes relevant to us (FTS / vector / index / query):
git -C W:/code/ladybug log --oneline <oldEngineCommit>..<newEngineCommit>
# what's actually PUBLISHED (do not trust git): check the github_ladybug feed / the fork's CI run
```

Version-scheme note: the **old** family is `0.17.1-dev.<run>.<attempt>.g<sha>` built from *downloaded*
v0.17.1 release natives. HEAD switched the dev feed to build natives **from source** at the pinned engine
commit and restamps as `0.18.0-dev.<run>.<attempt>.eng-<short>`. The new family carries the engine fixes
(double-free-on-destroy, delete/checkpoint CSR SIGSEGV) and new DDL (`DROP_FTS_INDEX`) — but only once a
green cross-RID publish exists.

## Standing steering backlog (we are the reference consumer)

- **TOP: ship a green `0.18.0-dev` feed** — fix the failing linux-x64 / linux-arm64 *from-source* native
  builds in the fork's `github-packages-dev.yml`. Until it exists, Graphiti can adopt **no** engine-level
  fix. (macOS RIDs already pass; Linux source build is the only blocker.)
- Add a binding `DROP_FTS_INDEX` round-trip test to `SearchExtensionsTests` *before* Graphiti rewrites its
  FTS-idempotency workaround (the engine `DROP_FTS_INDEX` throws on a missing index and must clean the
  auxiliary docs/terms tables — pin that behavior first).
- First-class fixed-size `FLOAT[N]` parameter binding to drop Graphiti's inline
  `CAST($v AS FLOAT[<dim>])` (3 sites). This is really an **engine** ask: `lbug.h` exposes only
  `lbug_value_create_list` (no fixed-ARRAY constructor) — so the request is a fixed-ARRAY value
  constructor in the C API, then a typed binding helper.
- A prepare-once / bind-many convenience on the binding's `Connection` (an `ExecuteMany`) so the bulk-save
  / rank-loop optimization is the obvious default and exercises the pooled-bind perf work.
- A one-line **consumer-impact** note per bump in `upstream-engine.pin` (interop-safe? FTS scoring
  touched? new DDL?) so "bump + verify" can be trusted without re-deriving it each cycle.

## Deferred / not actionable (recorded so they are not re-litigated each cycle)

- **FTS-idempotency cleanup** (replace the `"Index … already exists"` string-catch in
  `LadybugGraphDriver.ExecuteFulltextIndexStatementsAsync` / `IsDuplicateFulltextIndexError` with an
  explicit `CALL DROP_FTS_INDEX`-then-create): gated on a published `0.18.0-dev`; and even then it's not
  free idempotency (`DROP_FTS_INDEX` throws on missing), so it still needs a guard. The current catch is
  correct — keep it until then. Lateral clarity win, not a fix.
- **`FLOAT[N]` CAST drop**: engine ask above; the `List<float>` + `CAST` path is the supported,
  vector-test-pinned path. No change.
- **`ORDER BY` by projected alias**: standard parity-safe Cypher; reverting is pure risk.
- **`LadybugRecordMapper` `JsonElement`/`Convert.ChangeType` fallbacks**: effectively dead on the package
  path (`ReadCell` returns CLR types); they serve the InMemory driver. Leave as-is.

## Available-now consumer-side win (no bump needed, parity-neutral)

`LadybugDbQueryExecutor.ExecuteStatement` → `Connection.Execute(cypher, params)` **re-Prepares the
identical Cypher on every call** (no statement cache). For the hot repeated-shape statements (bulk-save
node/edge loops, by-uuid deletes, rank-per-uuid), a **prepare-once / bind-many** seam on
`ILadybugQueryExecutor` (e.g. `ExecuteManyAsync(cypher, IEnumerable<paramMap>)`) using the already-public
`Connection.Prepare` / `PreparedStatement.Bind` removes the redundant prepares — wire-identical Cypher and
bound values, byte-for-byte parity. This is the one real *adoption* item available without waiting on the
binding roadmap.
