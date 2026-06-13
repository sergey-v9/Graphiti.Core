# Plan 04 — Parity-hardening follow-ups

Created 2026-06-13 after the supervisor review of the 2026-06-11 agent work. The high/medium reachable
defects that review found are already fixed and integrated to `main` (see `parity.md` "2026-06-13
parity-hardening pass"). This plan tracks the remainder. Pick the lowest-numbered open item; one
reviewable slice per commit; verify against Python before editing; update `parity.md`/`decisions.md`
in the same change.

## Priority order

The single highest-value remaining step is **Phase 3 real-provider validation (plan 03)** — the port
has still never run against a real LLM, and that is the acceptance gate for all the parity work.
Everything in this plan is lower priority than that, and Phase 3 needs an `OPENAI_API_KEY` the agent
cannot supply, so it requires the user. Do the items below only when Phase 3 is blocked on the key.

## Items (all low/latent — confirmed real but deferred; rationale in `decisions.md`)

1. [ ] Extend the full-string golden-test pattern to the remaining prompt builders. The combined-
   prompt test was converted from substring to full-string equality and immediately caught four more
   defects; the other prompt tests that still use `Assert.Contains` should get the same treatment so
   transcription drift cannot recur. This is the most valuable item here (prevents regressions).
2. [ ] Multi-episode attribution parity (`EpisodeAttribution.cs`): reconcile reference-time selection,
   the resolved-vs-extracted index-map remap, and dropped-duplicate episode merging with
   `node_operations.py:104-112` / `edge_operations.py:170-181,290-313`. ONLY relevant if/when
   multi-episode extraction is wired (today every extraction call processes one episode). If it stays
   single-episode, instead record in `decisions.md` that the attribution prompt blocks are
   intentionally unported.
3. [ ] Edge signature resolution: DB-fetch endpoint nodes missing from the resolved-node set before
   signature matching (`edge_operations.py:439-455`), and fall back to `["Entity"]` labels, so a
   custom edge type is not silently lost for override/cross-pair endpoints.
4. [ ] Combined-edge parsing: drop the `?? "RELATES_TO"` default for a missing `relation_type` and
   reject/skip the edge instead, matching Python's required-field validation — invented relation names
   are exactly the fabrication class the port avoids elsewhere.
5. [ ] Cross-encoder: add an exact-prompt golden assertion and (when `OPENAI_API_KEY` is present) an
   integration smoke test wiring `MicrosoftExtensionsAICrossEncoderClient` through Graphiti search.
6. [ ] Cosmetic: truncate the unknown-entity warning log to 30 chars (`node_operations.py:1001-1004`);
   remove the Ladybug reranker test's impossible foreign-row assertions (test hygiene).

## Decisions deliberately NOT actioned (kept as documented divergences)

Do not "fix" these toward Python without an explicit product decision — they are recorded as
intentional in `decisions.md` ("Deliberate divergences accepted…"):

- Bulk cross-episode edge invalidation being more aggressive than Python.
- Bulk episodes populating `episode.EntityEdges` (Python bulk leaves it empty).
- Ladybug `RetrieveEpisodes` oldest-first ordering (C# matches the documented contract).
- Property-filter Cypher emission (pre-existing intentional C# feature).

## Done when

Item 1 is done (regression guard in place); items 2–6 are either closed or explicitly recorded as
intentional/deferred in `decisions.md`; and Phase 3 (plan 03) has had a successful real-provider run
recorded in `handoff.md`.
