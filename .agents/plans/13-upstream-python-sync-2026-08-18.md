# Plan 13 - Upstream core sync through 2026-08-18 - DONE

**Status:** DONE on 2026-08-18.

**Anchor moved:** `b59d4ba01118a91708fd6a6892200016168eeb5d` ->
`10374d6044f91b9ecae3586828abb1ecbf022c4f`.

## Result

- Reviewed and dispositioned all 11 upstream `graphiti_core/` commits in the range.
- Adopted group-scoped typed community cleanup and the RRF-bounded edge cross-encoder shortlist.
- Probed FalkorDB-adjacent behavior on the real LadybugDB package and suppressed its reproduced
  punctuation-only broad-match behavior while preserving verbatim searchable queries.
- Added runtime proof for group routing, relationship direction, BFS edge identity, and
  `ReferenceTime` hydration on LadybugDB/InMemory.
- Confirmed no prompt, public API, ingestion, schema/cache identity, default, or wire changes.
- Recreated the parent local `csharp-port` branch as current upstream plus one hookup commit; it was
  not pushed.

## Verification

`.\eng\Verify-GraphitiCore.ps1` passed with `1073` tests passed, `4` skipped, zero warnings, pack,
and the isolated package-consumer smoke. Two independent read-only audits found no remaining
behavioral omission after their findings were fixed and rechecked.

Per-commit evidence lives in `.agents/notes/parity.md`; current operating state lives in
`.agents/notes/handoff.md`.
