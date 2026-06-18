# Plan 05 — Release readiness: API ergonomics, naming, and packaging

Created 2026-06-14. The library is functionally complete and validated (Phases 1–3), documented, and
guarded by a public-API snapshot test. This plan turns the pre-freeze review
(`.agents/notes/api-freeze-review.md`) into an ordered implementation plan. **Decision (user,
2026-06-14): no API freeze — implement all of A–E**, then triage any remaining `.agents/plans/`
backlog separately before release infrastructure. The snapshot test is kept as a drift guard, not a
freeze: each step that changes the public surface regenerates
`tests/Graphiti.Core.Tests/Api/Graphiti.Core.approved.txt` in the same commit.

## Status — A–E COMPLETE (2026-06-14)

All five steps landed on `main` and verified green. Latest direct full verification after the
package-consumer workflow smoke hardening is `.\eng\Verify-GraphitiCore.ps1` on 2026-06-14: 984 passed,
3 skipped, 987 total, with restore/format/build clean, both packages packed as `.nupkg` +
`.snupkg`, and fresh temp consumers restored/built/run setup plus a public triplet/search workflow
from strict package sources:
- **A** — `init` setters, `Options`/`ActivitySource`/`TokenCounter` get-only, 7 port-artifact helpers
  internalized, and shippable package projects generating IntelliSense XML documentation.
- **B+C** — `GraphProvider.LadybugDb=5` and `AddGraphiti` primary; `Kuzu`/`AddGraphitiCore` `[Obsolete]`
  aliases. `GRPH0001` and `GRPH0002` are suppressed only at deliberate compatibility-alias sites.
- **D** — constructor defaults to InMemory (precedence: explicit driver > InMemory); additive
  `AddEpisodeOptions` overload. (Update 2026-06-17: the legacy `uri` Neo4j path was removed.)
- **E.1 + E.3** — LadybugDB extracted to `src/Graphiti.Core.Drivers.Ladybug/`; `Graphiti.Core` is LadybugDB-free
  and restores from nuget.org alone; core resolves `LadybugDb`/`Kuzu` via `GraphDriverFactory` (set by
  `AddLadybugDbGraphDriver`) and throws a clear error if the package is absent. README/samples updated.
  The verifier now creates fresh package consumers: core uses only the packed core output + nuget.org;
  Ladybug uses both packed Graphiti outputs + the fork GitHub Packages feed + nuget.org and runs setup
  through `Graphiti` with the packed driver. Both consumers are restored, built, run through
  `BuildIndicesAndConstraintsAsync()`, `AddTripletAsync`, and `SearchAsync`, then checked for the
  expected provider plus inserted hit UUID.
- The public-API snapshot now guards BOTH assemblies (`Graphiti.Core` + `Graphiti.Core.Drivers.Ladybug`).

**Remaining (separate backlog + release infra):**
- **Plan-folder backlog triage (Step F below):** before asking for release/version decisions, sweep
  `.agents/plans/` and the directly linked notes, separate actionable parity/provider/perf slices
  from decision-gated release work, and close or record each item in its owning note. Do not blend
  those follow-ups into the versioning/publish decision.
- **Versioning** (confirm 2.0.0 line / alpha→beta cadence). NuGet metadata, README packing,
  XML docs, symbol package generation, and package-consumption smoke checks are now present and guarded
  for both shippable packages. CI now has both a `Graphiti.Core`-only lane
  (`.github/workflows/core-only.yml`, `eng\Verify-GraphitiCoreOnly.ps1`, strict nuget.org-only restore)
  and a full Ladybug-inclusive lane (`.github/workflows/full.yml`, `eng\Verify-GraphitiCore.ps1`,
  authenticated `github_ladybug` package restore on Windows). The full lane uses the repository
  `GITHUB_TOKEN` with packages read permission and requires the LadybugDB packages to grant this
  repository Actions access. `OPENAI_API_KEY` remains optional; provider tests skip unless the secret is
  present. Keep the full verifier single-job/serialized because parallel `dotnet test` runs can hang
  the native Ladybug package.

**Completed E.2 checkpoint (2026-06-17):** `Graphiti.Core.Drivers.Ladybug` restores `LadybugDB` /
`LadybugDB.Native` from the `sergey-v9/ladybug-dotnet` GitHub Packages feed in `NuGet.config`, pinned
to `0.17.1-dev.1.1.g6f3dbed`. GitHub Packages currently reports only that published version for both
packages. With `NuGetPackageSourceCredentials_github_ladybug` set from the active GitHub token,
`.\eng\Verify-GraphitiCore.ps1` is green (`1021` passed, `3` skipped; both shippable packages packed
and both fresh package-consumer smoke builds succeeded). Future binding fixes should land in the
separate `W:\code\ladybug` repo, push the fork's `dev` branch, let the GitHub Packages workflow publish
a new dev version, and then bump Graphiti to that published version.

**CI checkpoint (2026-06-17):** both workflow YAML files parse locally, the core-only verifier is green
(`937` passed, `0` skipped; core pack succeeded), and the full verifier is green locally with GitHub
Packages credentials (`1025` passed, `3` skipped; both packages and consumer smokes succeeded).

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

A → (B + C together) → D → E → F → release infra. A/B/C are independent edits and could run on
parallel branches IF the test-concurrency rule above is honored; D and E are larger and should land
on their own reviewed branches. F is a documentation/coordination gate that may produce separate
implementation slices; keep those slices independent from release-version and publish decisions.
Each public-surface step is one coherent slice with the snapshot-baseline update included.

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
LadybugDB/InMemory-first port. Resolved by defaulting to InMemory. (Update 2026-06-17: Neo4j was
removed entirely, so the `uri`/`user`/`password` params are gone and the constructor selects explicit
`graphDriver` > InMemory default.)

- `src/Graphiti.Core/Graphiti.cs` constructor. Resolved shape: explicit `graphDriver` wins, omitted
  driver defaults to `InMemoryGraphDriver`. New backend work should pass LadybugDB explicitly. The
  Neo4j removal landed 2026-06-17 (M4 in `evolution.md`).
- Optional within D: introduce an `AddEpisodeOptions`-style object for `AddEpisodeAsync`'s ~15 optional
  params (`entityTypes`, `edgeTypes`, `edgeTypeMap`, `excludedEntityTypes`, `customExtractionInstructions`,
  saga fields). Keep the existing overload (or `[Obsolete]` it) so it's non-breaking; the options object is
  additive ergonomics. This is the largest ergonomics win for callers — but verify it doesn't disturb the
  ingestion call sites or the bulk path.

**Verify:** full Verify green; snapshot baseline updated; samples/README updated to the new shape; a test
covering the new default/explicit-driver behavior.

---

## Step E — Split LadybugDB into its own package (the publish blocker)

**Goal:** `Graphiti.Core` restores from nuget.org alone; LadybugDB becomes opt-in so core-only
consumers are not forced onto Ladybug package credentials. InMemory remains the reference/test backend.
(Update 2026-06-17: Neo4j was removed entirely and is no longer a provider.)

This is the largest item and gates a real NuGet release. Sub-steps:
1. **Extract** `Drivers/Ladybug/` into a new project `src/Graphiti.Core.Drivers.Ladybug/` that references
   `Graphiti.Core` and owns the `LadybugDB`/`LadybugDB.Native` package references and
   `AddLadybugDbGraphDriver`. `Graphiti.Core` keeps the driver CONTRACT (`IGraphDriver`, `GraphProvider`)
   but NOT the Ladybug implementation or its packages. Resolve the `GraphProvider.LadybugDb`→driver wiring
   so core options validation doesn't hard-depend on the Ladybug assembly (factory/DI registration lives in
   the Ladybug package; core fails cleanly with a clear message if `LadybugDb` is selected without the
   package). Move the Ladybug tests to a matching test project (or keep in the suite with a project ref).
2. **Consume the fork-published LadybugDB package family.** The build now pins the fork-published
   `0.17.1-dev.1.1.g6f3dbed` package family via the `sergey-v9/ladybug-dotnet` GitHub Packages feed in
   `NuGet.config`. Restores that include the Ladybug driver require source `github_ladybug`
   credentials with `read:packages`. Coordinate future package fixes through the separate
   `W:\code\ladybug` repo, push the fork's `dev` branch, then bump Graphiti to the new published dev
   version — see `kuzu-driver-port.md`.
3. Update `Graphiti.Core.CSharp.slnx`, `Directory.Packages.props`, samples (Sample.OpenAI/Eval reference the
   Ladybug package), README install section, and the verify script. Confirm `Graphiti.Core` alone restores
   with only nuget.org configured.

**Verify:** `Graphiti.Core` restores/builds/runs without Ladybug package credentials;
`Graphiti.Core.Drivers.Ladybug` restores/builds/runs with the GitHub Packages feed and its tests pass;
full Verify green; both packages `pack`. This is a milestone (`evolution.md`:
"Provider surface freeze" / "Stable public API release").

---

## Step F — Plan-folder backlog triage (before release infra)

**Goal:** anything still visible from `.agents/plans/` or directly linked plan notes is handled as
its own stream before release-version/publish decisions. This is a coordination step, not a request to
fold unrelated work into plan 05.

Recorded sweep result, 2026-06-17:

1. Plans 01-04 have no unchecked checklist items and remain closed. Plan 03's stale "remaining eval
   harness" wording was corrected after rechecking the checked item and roadmap state. Reopen these
   plans only for a confirmed Python-vs-C# regression or upstream `graphiti_core/` delta.
2. Plan 05 A-E and E.2 are implemented; CI wiring is present. Remaining release infrastructure is
   decision-gated: version cadence, publish path, and whether to ship a metapackage.
3. Concrete parity fixes surfaced during this gate landed as separate slices, not as release-infra
   work: Python `normalize_l2` non-finite behavior and `EntityEdge.get_by_group_ids` empty-result
   exceptions.
4. Non-decision implementation work should continue to land as separate slices before release
   decisions when it is concrete and parity-safe: focused Python parity fixes from ongoing audits,
   repeatable upstream-sync checks, deterministic hardening of any confirmed flaky test, and
   benchmark-first performance wins.
5. Decision-gated follow-ups stay separate and should be surfaced explicitly before implementation:
   `CommunityEdgeNamespace.SaveBulkAsync` public API shape, empty node-label filter bug-compatibility,
   entity-attribute per-field max-length/required metadata, `GRPH0002` / `AddGraphitiCore` alias
   migration, larger real-provider eval expansion, Linux/CI validation scope,
   versioning, publish path, and metapackage shape. (Neo4j retirement is no longer pending — it
   landed 2026-06-17, M4 in `evolution.md`.)
6. `kuzu-driver-port.md` remaining-work bullets are conditional provider follow-ups, not unhandled
   release-plan tasks: broaden Ladybug workflow coverage only for uncovered behavior, add
   host-facing options only for real runtime needs, and add native-gated smoke tests only for a new
   platform/CI requirement or coverage gap.
   The scheduled reverse plan-05 E merge is now owned by
   `.agents/plans/06-merge-ladybug-into-core.md` and should be handled as its own pre-release stream.
7. Follow-up sweep during the 2026-06-17 parity audit split confirmed issues into separate streams.
   Concrete implementation slices now handled: InMemory typed node/edge UUID storage boundaries,
   `BuildCommunitiesAsync` explicit-empty `groupIds` semantics, and default empty-string group
   discovery for community builds, and namespace all-miss plural-read behavior for entity/episodic
   edge namespaces while preserving static model helper exceptions, and lazy search-config numeric
   validation/zero-limit behavior, negative-limit text truncation, and entity/fact prompt type-key
   rendering, MMR first-seen candidate ordering, and edge episode-mentions score-list ordering.
   Resolved/invalidated edge result duplicate preservation and bulk-ingestion type-validation
   asymmetry were also split out and closed. A Ladybug provider clear-data empty-list no-op drift was
   then split out and closed. Top-level community-search empty-method embedding fallback was also
   split out and closed. Community summary same-layer pair fan-out was also split out and closed. No
   earlier concrete non-decision follow-up from this audit remains in plan 05. New read-only audit
   candidates after that checkpoint are tracked in `handoff.md` and should land as separate verified
   slices rather than release-infra work.
8. Follow-up sweep on 2026-06-18 kept this gate ahead of plan 06 and release decisions: upstream
   `graphiti_core/` delta was empty, search-result merge/context helpers showed no confirmed drift,
   and the model/namespace CRUD audit found no confirmed, undocumented drift. Continue to treat any
   newly found plan-folder leftover as a separate slice first; do not bundle it into the Ladybug merge,
   versioning, publish path, or metapackage decisions.
9. Follow-up sweep after the moved-docs recheck found no unchecked implementation checklist items in
   plans 01-05 and only plan 06's opt-in Ladybug merge checklist. The concrete `Edge.Equals` audit
   candidate was handled as its own parity slice; the bulk-saga predecessor self-loop remains
   decision-gated, and release/API/provider decisions remain separate from this gate.
10. Follow-up removal/LLM audit on 2026-06-18 kept the same gate shape. The concrete node-delete
    non-positive batch-size drift was handled as its own parity/provider slice. LLM cache-key breadth is
    now recorded as an intentional infrastructure decision; live extraction required-field tightening
    was handled as its own parity slice. Schema-description metadata remains compatibility-sensitive,
    not release infrastructure, because it changes schema JSON, fingerprints, and cache keys.
11. Follow-up plan-folder audit after the moved-docs request found no unchecked implementation items
    outside plan 06's opt-in Ladybug merge checklist. The newly confirmed remaining structured response
    required-field drift was handled as its own parity slice before plan 06/release decisions, preserving
    this gate's "separate slice first" rule.
12. Follow-up ingestion/namespace audits kept the same gate shape. The combined extraction prompt's
    custom fact-type map drift was handled as its own parity slice. Namespace save-bulk handling for
    non-positive `batchSize` remains a separate concrete slice before plan 06/release decisions, not
    release infrastructure.

**Verify:** this coordination gate is now recorded. Code changes from resulting slices get their own
tests and commits; this coordination step needs only a docs review.

---

## Release infrastructure (after A–E and Step F)

- **Versioning:** confirm the `2.0.0` line (the namespace migration is already documented as 2.0.0). Decide
  alpha→beta→rc cadence. Package metadata (authors, license=Apache-2.0, repo URL, README as
  `PackageReadmeFile`, XML docs, `.snupkg` symbols, and fresh package-consumer restore/build/setup/run
  checks) is
  already set and covered by `PackageReadinessTests` plus `Verify-GraphitiCore.ps1`.
- **CI:** wired. The core-only GitHub Actions lane runs `eng\Verify-GraphitiCoreOnly.ps1`; the full
  Ladybug-inclusive lane runs `eng\Verify-GraphitiCore.ps1` on Windows with the `github_ladybug`
  credential sourced from `GITHUB_TOKEN`. The full lane is intentionally a single serialized job for
  the native Ladybug package, and `OPENAI_API_KEY` is an optional secret so live-provider tests skip by
  default.
- **Publish prerequisites:** the LadybugDB package family is publishable and currently consumed from
  the fork GitHub Packages feed; decide whether to ship a metapackage (`Graphiti.Core` +
  `Graphiti.Core.Drivers.Ladybug`).

## Done when

A–E implemented (each with snapshot baseline updated and Verify green), the plan-folder backlog
triaged into separate streams, the constructor/naming/packaging shapes are the intended public
contract, `Graphiti.Core` restores from nuget.org alone, the LadybugDB package is separately
consumable, and the release-infra decisions (versioning, CI, publish path) are recorded. Then
re-snapshot as the de facto stable surface and update `evolution.md` with the milestone.
