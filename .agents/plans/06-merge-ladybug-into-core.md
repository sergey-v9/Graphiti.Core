# Plan 06 — Merge LadybugDB Driver Back Into Core

Created 2026-06-17. This is the separate pre-release-infrastructure work item surfaced by the
plan-folder backlog sweep and the directly linked provider notes. Sergey has scheduled reversing
plan-05 E: LadybugDB is first-class, so the separate `Graphiti.Core.Drivers.Ladybug` assembly/package
should be folded back into `Graphiti.Core`.

## Status

**Not started — and NOT a default work-selection pick.** Sergey scheduled this, but it is *not* top
priority and it carries a real consequence: after the merge `Graphiti.Core` depends on the LadybugDB
packages + the `github_ladybug` feed, so it no longer restores from nuget.org alone and **cannot be
published to nuget.org until LadybugDB is public there**. Therefore: **do NOT auto-pick this plan** via
the AGENTS.md "lowest-numbered plan with unchecked items" rule. Execute it only when Sergey explicitly
initiates it, as its own implementation stream (not a versioning/publishing step or a parity
micro-slice). Default ongoing work stays on parity hardening / docs / verification.

## Prerequisite Gate

Before starting this merge, re-run the plan-folder backlog gate from plan 05 Step F across
`.agents/plans/` and the directly linked notes. Anything concrete left in that folder or its linked
planning notes must be handled as a separate parity/provider/perf/docs slice first; do not fold it
into the Ladybug merge checklist below.

## Work Items

- [ ] Re-run the plan-folder backlog gate from plan 05 Step F and handle any concrete leftover in
  `.agents/plans/` or directly linked planning notes as a separate slice before continuing this merge.
- [ ] Move the Ladybug driver implementation from `src/Graphiti.Core.Drivers.Ladybug/` into
  `src/Graphiti.Core/Drivers/Ladybug/`.
- [ ] Fold `LadybugDbOptions`, `AddLadybugDbGraphDriver`, and `LadybugDbGraphDriverFactory` into
  `Graphiti.Core`.
- [ ] Move the `LadybugDB` and `LadybugDB.Native` package references into `Graphiti.Core`.
- [ ] Remove the separate `Graphiti.Core.Drivers.Ladybug` project/package from the solution, pack loop,
  package-consumer smoke path, and public API snapshot coverage.
- [ ] Collapse the public API snapshot back to one assembly and regenerate the baseline deliberately.
- [ ] Retire `GraphitiCoreOnlyTests`, `eng/Verify-GraphitiCoreOnly.ps1`, and the core-only CI lane because
  Core will intentionally require the `github_ladybug` feed after the merge.
- [ ] Update README/package docs and the provider notes so consumers know `Graphiti.Core` now pulls the
  LadybugDB native package family even for InMemory-only use.

## Non-Goals

- Do not change the pinned LadybugDB package version unless the merge uncovers a concrete binding bug.
- Do not change Graphiti package versioning, publish target, or metapackage shape; those remain
  user-gated release decisions.
- Do not broaden CI beyond retiring the now-obsolete core-only lane unless the user separately asks.
- Do not reintroduce Neo4j, FalkorDB, or Neptune provider implementations.

## Verification

- `.\eng\Verify-GraphitiCore.ps1` with `NuGetPackageSourceCredentials_github_ladybug` set from an
  active GitHub token.
- Public API snapshot diff reviewed and approved as part of the merge commit.
- Package-consumer smoke adjusted to the new one-package shape and run by the verifier.
