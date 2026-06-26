# Plan 08 — Release-surface finalization (settle the public API while still alpha)

Created 2026-06-26. This is the work order for the **non-gated** portion of roadmap goal **G6 — release
readiness**. It becomes the current actionable plan now that every HIGH productionization goal is
complete: parity (Phases 1–3), plan 06 (Ladybug→Core merge), G4 (observability/DX), G1/plan 07
(linux-x64 proof), G2 (fail-loud live-provider + eval canary), and the first wave of G3 perf slices.

## Status

**Current priority (2026-06-26).** The package is `2.0.0-alpha.1`; public-surface changes are **cheap
now and breaking after a stable line**, so close the open surface decisions in this alpha window. This
plan stops **before** the user-gated line: do every surface/decision/docs/packaging-dry-run step, but
**do not stamp a release version and do not publish** — those wait for Sergey to initiate (G6).

## Why this is the next stream

The HIGH productionization axes are done and green (1028/4/1032; linux-x64 smoke proven; live canary
wired). The one thing that gets *more expensive over time* is the public API: once a stable version
ships, every removed/renamed/retyped member is a consumer break. `decisions.md` → "Open public-surface
decisions to settle while still alpha" flags two concrete open calls plus a general freeze. Settle them
now so that when Sergey says "ship", it is a version stamp, not a scramble of breaking changes.

## Steps (one slice each — implement → verify → commit → check off)

- [ ] **A. `CommunityEdgeNamespace.SaveBulkAsync` — keep-or-remove.** It is an additive C# public method
  with **no Python counterpart**, pinned in the API snapshot and Ladybug-tested. Make the explicit call:
  if it earns its place (real consumer value, symmetric with the other bulk save paths), **keep** it and
  record it in `decisions.md` as a deliberate additive-API decision so the snapshot diff is justified;
  otherwise **remove** it before the versioning gate. Update the API snapshot either way.
- [ ] **B. Attribute `MaxLength` + required-field carve-out — close or formally document.** Python's
  `apply_capped_attributes` has a **per-field cap override** and a **required-field retain** path; C#
  `AttributeMerger` applies only the single global cap and unconditionally drops over-cap fields, and
  `EntityAttributeDefinition` exposes neither. Behavioral parity is the product contract, so the default
  is to **close** this divergence: expand `EntityAttributeDefinition` (per-field cap + required flag),
  honor both in `AttributeMerger`, and land it as **one bundle** — API snapshot + golden structured
  schema + a required-field-over-cap test. If, with evidence, closing it is the wrong call, instead
  record it in `decisions.md` as a deliberate divergence (mirroring the model-default entry) so it is a
  decision, not a gap.
- [ ] **C. Public-API freeze pass.** With A/B settled, do a deliberate read of the full
  `PublicApiGenerator` snapshot for the single `Graphiti.Core` assembly: confirm every public type/member
  is intended for a stable surface, obsolete aliases (`GraphProvider.Kuzu`, `AddGraphitiCore`/GRPH0001-2)
  carry correct messages, init-only/get-only/required modifiers are right, and XML docs are present on
  the public surface. Fix what is wrong now; regenerate the baseline; record the freeze rationale.
- [ ] **D. Package metadata + RID truth.** Audit the `.nuspec`/csproj metadata (description, tags,
  authors, `RepositoryUrl`, license expression, README, icon if any) for accuracy. Reflect that
  linux-x64 is now a proven RID (plan 07) alongside win-x64 — keep the claim exactly as strong as the
  gated smokes prove, no more. Do **not** change `<Version>`.
- [ ] **E. Clean pack + fresh-consumer dry run.** Run `.\eng\Verify-GraphitiCore.ps1` end to end
  (restore/format/build/test/pack + fresh temp-consumer smoke against both InMemory and LadybugDB from
  the packed package), confirm green, and write the result into the handoff Verification section as the
  release-readiness dry run. This proves the surface is shippable on Sergey's go without further code
  work.

## Explicit non-goals (user-gated — do NOT do)

- No `<Version>` change / no tag / no `2.0.0` (or any) release stamp.
- No `dotnet nuget push` / no publish to nuget.org or any feed.
- No turning the live-provider CI lane's schedule into a release pipeline.
These wait for Sergey to initiate (`roadmap.md` G6).

## Optional fold-in (cheap insurance)

If a slice finishes early, advance **G5** by adding the low-cost recurring `Check-PythonUpstreamDelta`
reminder (non-blocking notification on delta) per `upstream-sync-procedure.md` — but never expand the
required CI lanes to do it.

## Verification

`.\eng\Verify-GraphitiCore.ps1` must be green on win-x64 after each slice; A/B that touch structured
schemas or the snapshot must regenerate the API baseline and golden schema in the same commit. The
linux-x64 and live-provider lanes stay additive and gated. Keep exact-cosine the default vector path;
this plan does not touch the deferred HNSW gate (G3).
