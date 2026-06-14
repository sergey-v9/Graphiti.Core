# Plan 05 — Release readiness: API ergonomics, naming, and packaging

Created 2026-06-14. The library is functionally complete and validated (Phases 1–3), documented, and
guarded by a public-API snapshot test. This plan turns the pre-freeze review
(`.agents/notes/api-freeze-review.md`) into an ordered implementation plan. **Decision (user,
2026-06-14): no API freeze — implement all of A–E**, then the release infrastructure. The snapshot
test is kept as a drift guard, not a freeze: each step that changes the public surface regenerates
`tests/Graphiti.Core.Tests/Api/Graphiti.Core.approved.txt` in the same commit.

## Status — A–E COMPLETE (2026-06-14)

All five steps landed on `main` and verified green. Latest direct full verification after package
metadata/symbols follow-through is `.\eng\Verify-GraphitiCore.ps1` on 2026-06-14: 973 passed,
3 skipped, 976 total, with restore/format/build clean and both packages packed as `.nupkg` +
`.snupkg`:
- **A** — `init` setters, `Options`/`ActivitySource`/`TokenCounter` get-only, 7 port-artifact helpers
  internalized, and shippable package projects generating IntelliSense XML documentation.
- **B+C** — `GraphProvider.LadybugDb=5` and `AddGraphiti` primary; `Kuzu`/`AddGraphitiCore` `[Obsolete]`
  aliases. `GRPH0001` and `GRPH0002` are suppressed only at deliberate compatibility-alias sites.
- **D** — constructor defaults to InMemory (precedence: explicit driver > `uri`→Neo4j > InMemory); additive
  `AddEpisodeOptions` overload.
- **E.1 + E.3** — LadybugDB extracted to `src/Graphiti.Core.Drivers.Ladybug/`; `Graphiti.Core` is LadybugDB-free
  and restores from nuget.org alone; core resolves `LadybugDb`/`Kuzu` via `GraphDriverFactory` (set by
  `AddLadybugDbGraphDriver`) and throws a clear error if the package is absent. README/samples updated.
- The public-API snapshot now guards BOTH assemblies (`Graphiti.Core` + `Graphiti.Core.Drivers.Ladybug`).

**Remaining (release infra; partly gated on external work):**
- **E.2 — publish the LadybugDB package family.** The `Graphiti.Core.Drivers.Ladybug` package still consumes
  the local `0.17.0-alpha.2-graphiti.1` feed (`NuGet.config`). A real off-machine release needs that family
  published to / replaced on a real feed — work in the separate `W:\code\ladybug` repo (see
  `kuzu-driver-port.md`). WS-1 audit on 2026-06-14 found that the nearby
  `W:\code\ladybug\tools\csharp_api` checkout has clean `0.17.1` artifacts with the Graphiti
  parameter-binding repair and Linux/macOS native loader work, but Graphiti has not yet changed the pin;
  get explicit user confirmation before replacing the `0.17.0-alpha.2-graphiti.1` pin. `Graphiti.Core` +
  samples are already off-machine-restorable.
- **Versioning** (confirm 2.0.0 line / alpha→beta cadence) and **CI**. NuGet metadata, README packing,
  XML docs, and symbol package generation are now present and guarded for both shippable packages. CI
  for the full suite is itself gated on E.2 (the native Ladybug tests need the package); a
  `Graphiti.Core`-only CI lane (build/format/pack + non-Ladybug tests) could run now. Remember the
  parallel-`dotnet test` deadlock.

## Standing constraints (apply to every step)

- **Parity & wire compatibility unchanged.** Do not change prompt text, JSON/snake_case wire values,
  enum serialized values, cache-key inputs, or ranking/temporal semantics. These are renames/shape
  changes to the C# surface only.
- **Update the snapshot baseline deliberately.** Any public-surface change fails
  `PublicApiSnapshotTests`; review the printed diff, confirm it's intended, copy
  `Graphiti.Core.received.txt` over the `.approved.txt` baseline, and commit it WITH the change.
- **Tests/benchmarks/eval see internals** via `InternalsVisibleTo` (Graphiti.Core.Tests,
  Graphiti.Core.Benchmarks, Graphiti.Eval) — internalizing a helper won't break them; confirm no
  `samples/` use before internalizing.
- **Keep `Verify-GraphitiCore.ps1` green** (build, format, full test, pack) after each step.
- **Workflow:** if parallelizing across worktrees, do NOT run multiple `dotnet test` runs at once —
  the LadybugDB native package deadlocks across concurrent worktrees (caused a 1.5h hang on 06-14).
  Stagger the test step, or have worktree agents build-only and run the consolidated test centrally.

## Execution order & parallelizability

A → (B + C together) → D → E → release infra. A/B/C are independent edits and could run on parallel
branches IF the test-concurrency rule above is honored; D and E are larger and should land on their
own reviewed branches. Each is one coherent slice with the snapshot-baseline update included.

---

## Step A — Safe internal hardening (low risk; do first)

**Goal:** shrink the accidental public surface and remove mutable-global footguns.

1. `src/Graphiti.Core/LlmClients/TokenUsage.cs:4` — change `InputTokens`/`OutputTokens` to `{ get; init; }`
   (or `{ get; internal set; }` if the tracker mutates them post-construction — check
   `TokenUsageTracker`). Verify `TokenUsageTracker.GetTotalUsage()` and `PromptTokenUsage` still compile.
2. `src/Graphiti.Core/Serialization/GraphitiJsonSerializer.cs:15` — `Options` field → get-only property
   returning the same `MakeReadOnly()`-frozen instance. `src/Graphiti.Core/Telemetry/GraphitiTelemetry.cs`
   — `ActivitySource` field → get-only property. Update all internal references (these are heavily used;
   a field→property change is source-compatible for readers).
3. `src/Graphiti.Core/Text/ContentChunking.cs:46` — `public static ITokenCounter TokenCounter { get; set; }`.
   First determine whether the public **setter** is a real config seam: grep `samples/`, `tests/`,
   `benchmarks/` and the DI path. If the `IContentChunker`/`DefaultContentChunker` DI path + the internal
   `AsyncLocal` override fully cover token-counter selection, make the static get-only or internal. If a
   caller legitimately sets it (e.g. registering `HeuristicTokenCounter(4)` per decisions.md), LEAVE it
   and record that it's an intentional config point in `decisions.md` instead.
4. Internalize port-artifact helpers with zero `samples/` use (tests keep access via InternalsVisibleTo):
   `Search/SearchEngine.cs`, `Search/SearchUtilities.cs`, `Search/SearchFilterQueryBuilder.cs`,
   `Search/MaintenanceUtilities.cs`, `LlmClients/StructuredResponseValidator.cs`,
   `Search/SearchConfigurationEnumExtensions.cs`, `Search/ComparisonOperator*Extensions`. KEEP public:
   `GraphDriverBase`, the `Namespaces/*Namespace` facades, and `EpisodeTypeExtensions` if its `*WireValue`
   is a used public helper (verify). Change `public` → `internal` on the type decls only; fix any now-broken
   public signatures that referenced them (there should be none if they're truly internal helpers).

**Verify:** full Verify green; regenerate the snapshot baseline (it should SHRINK). Commit per logical
group (e.g. "internalize search helper utilities", "harden TokenUsage/serializer/telemetry surface").

---

## Step B — Provider naming: GraphProvider.LadybugDb (+ Kuzu obsolete alias)

**Goal:** the driver-facing name matches the driver. `GraphProvider.Kuzu` remains an obsolete alias
that selects LadybugDB through the same registration path.

- `src/Graphiti.Core/Drivers/GraphProvider.cs` — add `LadybugDb` as the primary member for the
  LadybugDB-backed driver. Keep `Kuzu` as a still-functional `[Obsolete("Use GraphProvider.LadybugDb")]`
  alias that resolves to the same driver. **Do NOT renumber existing members** and do not remove
  `FalkorDb`/`Neptune` (intentional wire-compat surfaces per `decisions.md`). Confirm whether
  `GraphProvider` is serialized anywhere (cache keys/options persistence); if so, `LadybugDb` must map to
  the same effective behavior and `Kuzu` must keep working.
- Update DI/options resolution (`Configuration/GraphitiServiceCollectionExtensions.cs`,
  `LadybugDbServiceCollectionExtensions.cs`) and `Drivers/Ladybug/LadybugDbGraphDriverFactory.cs` to accept
  both, preferring `LadybugDb`.
- Advance the Kuzu→LadybugDB terminology transition in comments/notes where it doesn't touch wire
  values. The concrete Ladybug driver reports `GraphProvider.LadybugDb`; generic
  `SearchUtilities`/`CompiledSearchFilter` no longer carry `GraphProvider.Kuzu` compatibility branches
  because active Ladybug query/filter syntax lives in the Ladybug driver package.
- Update `kuzu-driver-port.md`/`decisions.md` to record `LadybugDb` as the driver-facing name and `Kuzu` as
  the obsolete compatibility alias. Add a test asserting both enum values resolve to a working LadybugDB
  driver. Update README references.

**Verify:** full Verify green; snapshot baseline updated (adds `LadybugDb`).

## Step C — DI method: AddGraphiti (AddGraphitiCore → alias)

- `src/Graphiti.Core/Configuration/GraphitiServiceCollectionExtensions.cs` — add `AddGraphiti(...)` as the
  primary registration method; keep `AddGraphitiCore(...)` as a thin `[Obsolete("Use AddGraphiti")]`
  forwarder (or vice-versa — primary = `AddGraphiti`). Update samples/README/eval to call `AddGraphiti`.
- Do B and C in one branch (both are cheap renames with obsolete aliases). Snapshot baseline updated.

---

## Step D — Constructor & ingestion ergonomics (design change)

**Goal:** remove the surprising "silently build a Neo4j driver when `graphDriver` is null" default for a
LadybugDB/InMemory-first port.

- `src/Graphiti.Core/Graphiti.cs:51-63` constructor. Decide and implement ONE of:
  - (preferred) make `graphDriver` effectively required — no driver and no `uri` throws today; instead
    make the driver an explicit first-class parameter and move the Neo4j `uri/user/password` convenience
    into a `Neo4jGraphDriver` factory / a separate `Graphiti.ForNeo4j(...)` helper; OR
  - default to `InMemoryGraphDriver` when nothing is supplied (matches the reference-driver story) and keep
    Neo4j only via an explicit driver/uri.
  Keep a migration path: don't silently change behavior for callers passing `uri` — if kept, document it;
  if removed, it's a deliberate breaking change (record in `decisions.md`/README migration notes).
- Optional within D: introduce an `AddEpisodeOptions`-style object for `AddEpisodeAsync`'s ~15 optional
  params (`entityTypes`, `edgeTypes`, `edgeTypeMap`, `excludedEntityTypes`, `customExtractionInstructions`,
  saga fields). Keep the existing overload (or `[Obsolete]` it) so it's non-breaking; the options object is
  additive ergonomics. This is the largest ergonomics win for callers — but verify it doesn't disturb the
  ingestion call sites or the bulk path.

**Verify:** full Verify green; snapshot baseline updated; samples/README updated to the new shape; a test
covering the new default/explicit-driver behavior.

---

## Step E — Split LadybugDB into its own package (the publish blocker)

**Goal:** `Graphiti.Core` restores from nuget.org alone; LadybugDB becomes opt-in so InMemory/Neo4j-only
consumers aren't forced onto the local Ladybug feed.

This is the largest item and gates a real NuGet release. Sub-steps:
1. **Extract** `Drivers/Ladybug/` into a new project `src/Graphiti.Core.Drivers.Ladybug/` that references
   `Graphiti.Core` and owns the `LadybugDB`/`LadybugDB.Native` package references and
   `AddLadybugDbGraphDriver`. `Graphiti.Core` keeps the driver CONTRACT (`IGraphDriver`, `GraphProvider`)
   but NOT the Ladybug implementation or its packages. Resolve the `GraphProvider.LadybugDb`→driver wiring
   so core options validation doesn't hard-depend on the Ladybug assembly (factory/DI registration lives in
   the Ladybug package; core fails cleanly with a clear message if `LadybugDb` is selected without the
   package). Move the Ladybug tests to a matching test project (or keep in the suite with a project ref).
2. **Publish/replace the LadybugDB package family.** The build currently pins the local repaired
   `0.17.0-alpha.2-graphiti.1` via `NuGet.config`. Before a real release this must be either published to a
   real feed or upstreamed; until then the new Ladybug package keeps the local-feed caveat (document it).
   Coordinate with `W:\code\ladybug\tools\csharp_api` (the repaired binding) — see `kuzu-driver-port.md`.
3. Update `Graphiti.Core.CSharp.slnx`, `Directory.Packages.props`, samples (Sample.OpenAI/Eval reference the
   Ladybug package), README install section, and the verify script. Confirm `Graphiti.Core` alone restores
   with only nuget.org configured (test by temporarily disabling the local feed).

**Verify:** `Graphiti.Core` restores/builds without the local Ladybug feed; the Ladybug package restores/
builds/tests with it; full Verify green; both packages `pack`. This is a milestone (`evolution.md`:
"Provider surface freeze" / "Stable public API release").

---

## Release infrastructure (after A–E)

- **Versioning:** confirm the `2.0.0` line (the namespace migration is already documented as 2.0.0). Decide
  alpha→beta→rc cadence. Package metadata (authors, license=Apache-2.0, repo URL, README as
  `PackageReadmeFile`, XML docs, and `.snupkg` symbols) is already set and covered by
  `PackageReadinessTests`.
- **CI:** a build/format/test/pack pipeline (GitHub Actions or equivalent). Encode the native-package
  test-concurrency gotcha (single-threaded or serialized Ladybug tests). Gate the key-dependent OpenAI
  integration tests behind a secret (skip by default).
- **Publish prerequisites:** the LadybugDB package family must be publishable (Step E.2); decide whether to
  ship a metapackage (`Graphiti.Core` + `Graphiti.Core.Drivers.Ladybug`).

## Done when

A–E implemented (each with snapshot baseline updated and Verify green), the constructor/naming/packaging
shapes are the intended public contract, `Graphiti.Core` restores from nuget.org alone, the LadybugDB
package is separately consumable, and the release-infra decisions (versioning, CI, publish path) are
recorded. Then re-snapshot as the de facto stable surface and update `evolution.md` with the milestone.
