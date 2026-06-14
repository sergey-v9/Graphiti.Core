# Plan 04 — Parity-hardening follow-ups

Created 2026-06-13 after the supervisor review of the 2026-06-11 agent work. The high/medium reachable
defects that review found are already fixed and integrated to `main` (see `parity.md` "2026-06-13
parity-hardening pass"). This plan tracks the remainder. Pick the lowest-numbered open item; one
reviewable slice per commit; verify against Python before editing; update `parity.md`/`decisions.md`
in the same change.

## Status — essentially complete (2026-06-14)

Items 1, 3, 4, 5, 6 landed via branches `ws/f-followups` and `ws/h-review-fixes`; item 2 is now
closed by code alignment and tests. Phase 3 real-provider validation passed 2026-06-13 (see plan 03).
The adversarial review of these branches itself found and fixed follow-on defects (eval-prompt
interior trailing spaces, F3 over-scoping by group_id, entity-type-descriptions verbatim) — all
integrated. The eval harness was reworked to the proposal's graph-building regression design and run
live (plan 03 item 4).

## Items

1. [x] Extend the full-string golden-test pattern to the remaining prompt builders. Done (`ws/f`):
   the entity-summary prompt tests were the last substring holdouts and were converted to full-string
   equality, which surfaced and fixed a dropped-trailing-newline divergence. All main prompt builders
   now have full-string golden assertions.
2. [x] Multi-episode attribution parity (`EpisodeAttribution.cs`). Done: edge `reference_time`
   follows Python's first-raw-index rule, and node attribution now remains keyed to extracted-node
   UUIDs through episodic-edge construction. A resolved canonical UUID mismatch therefore falls back
   to all provided episodes like Python.
3. [x] Edge signature resolution: DB-fetch missing endpoint nodes + `["Entity"]` fallback. Done
   (`ws/f`), with the group-scoping corrected in `ws/h` to fetch by UUID only like Python's default
   driver (`nodes.py:609-632` ignores group_id on the core path).
4. [x] Combined-edge parsing: dropped the `?? "RELATES_TO"` default; missing `relation_type` now skips
   the edge, matching Python's required-field validation. Done (`ws/f`); a second fabrication site in
   edge resolution was removed too.
5. [x] Cross-encoder: exact-prompt golden assertion + key-gated provider smoke test (ranks a relevant
   passage above an irrelevant one). Done (`ws/f`); the smoke test passed live 2026-06-13.
6. [x] Truncate the unknown-entity warning log to 30 chars; remove the Ladybug reranker test's
   impossible foreign-row assertions. Done (`ws/f`).

## Decisions deliberately NOT actioned (kept as documented divergences)

Do not "fix" these toward Python without an explicit product decision — they are recorded as
intentional in `decisions.md` ("Deliberate divergences accepted…"):

- Bulk cross-episode edge invalidation being more aggressive than Python.
- Bulk episodes populating `episode.EntityEdges` (Python bulk leaves it empty).
- Ladybug `RetrieveEpisodes` oldest-first ordering (C# matches the documented contract).
- Property-filter Cypher emission (pre-existing intentional C# feature).

## Done when — MET 2026-06-14

Items 1–6 done; Phase 3 (plan 03) real-provider run recorded in `handoff.md`. This plan is closed.
