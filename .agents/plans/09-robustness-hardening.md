# Plan 09 — Robustness & resilience hardening of the LLM / parse boundary

Created 2026-06-27. This opens a **new in-scope stream** now that the entire 2026-06-19 forward agenda
is complete: parity (Phases 1–3), plan 06 (Ladybug→Core merge), G4 (observability/DX), G1/plan 07
(linux-x64 proof), G2 (live-provider + eval canary), the G3 perf/allocation program (10 measured
slices; all named hot paths profiled), and plan 08/G6 non-gated release-surface finalization. The
library is release-ready (`1032/4/1036` green; pack + fresh-consumer dry run green). Publishing stays
user-gated.

## Status

**ACTIVE (2026-06-27) — plan 10 is complete.** Step 0's housekeeping residuals are now complete:
the HNSW gate is settled and G5 has a committed non-blocking reminder wrapper. The next slice is the
robustness risk map in item A.

The substance still stands: the highest remaining *real-world* risk is the layer the deterministic
golden tests (which use fake LLMs) cannot fully stress — parsing and coercing **actual** LLM output into
typed graph results, and surviving **provider failure modes** (exactly where upstream Python has
repeatedly added guards). This plan does **not** touch the parked publish line.

## Step 0 — close the two roadmap residuals first (housekeeping)

- [x] **0a. Settle the HNSW gate with the new baseline data.** `2026-06-27-inmemory-vector-win-x64.md`
  now measures the InMemory full-scan vector search. Read it and **decide**: if full-scan cosine is
  comfortably within budget at the target graph size, formally close HNSW as *not needed — exact cosine
  stays the default* in `decisions.md`/`roadmap.md` (record the numbers). If the baseline shows a cliff
  at realistic N, scope an opt-in HNSW tier as its own plan (do **not** implement it by default). Either
  way, convert the open "G-future HNSW gate" into a decided item, not a standing maybe.
- [x] **0b. Land G5 as a committed artifact, not a faked CI lane.** The in-session scheduler tool isn't
  available and the notes constrain CI expansion, so don't invent a workflow. Instead commit a small
  `eng/` wrapper around `Check-PythonUpstreamDelta` plus a short doc note in `upstream-sync-procedure.md`
  describing how Sergey wires the recurring, **non-blocking** reminder (OS scheduled task / cron / manual
  dispatch). Goal: the reminder is one setup step away, with zero code work pending.

## Robustness stream (one slice each — implement → verify → commit → check off)

- [x] **A. Map the fragile boundary.** Inventory every site where real LLM output is parsed/coerced into
  typed results: episode-graph extraction parsing partials, structured-response coercion, JSON extraction
  / markdown-fence handling, enum + entity/edge type resolution, attribute extraction, and the
  dedup/edge/invalidation response shapes. Produce a short risk map (trusted vs adversarial inputs, and
  the current guard at each site). Record it in a note; no code change required for this slice.
- [x] **B. Property-based / fuzz coverage of the parsers.** Add generator-driven tests (FsCheck or a
  hand-rolled generator — no new default dependency in `src/`) that throw malformed-but-plausible LLM
  responses at the coercion layer: truncated JSON, prose wrapped around JSON, wrong-cased enums,
  missing/extra fields, null-vs-empty, duplicate keys, wrong types, oversized strings, unicode edge cases,
  deep nesting. Assert the library degrades gracefully — re-prompt path, documented fallback, or a clean
  typed exception — and **never fabricates graph content** or throws unhandled. Pin every surprising input
  as a regression test.
- [ ] **C. Provider-resilience tests.** Exercise ingestion/search under provider failure modes via the
  fake clients: transient errors (Polly retry), 429 / rate-limit, partial batch failures, empty
  responses, schema-validation failures past the two repair attempts, embedding dimension mismatch, and
  cross-encoder failure. Assert the documented behavior holds — no partial-graph corruption, correct error
  surfaced, cache only stores validated responses.
  > ⚠ **IN PROGRESS — drafted, uncommitted, does NOT yet compile.** A starting draft lives in the working
  > tree at `tests/Graphiti.Core.Tests/ProviderResilienceWorkflowTests.cs` (uncommitted, 234 lines:
  > schema-validation-failure + embedding-dimension-failure cases with in-file fake clients
  > `InvalidNodeExtractionLlmClient`/`ValidExtractionLlmClient`/`WrongDimensionBatchEmbedder`). It fails to
  > build with **CS0121 at line ~107**: `InvalidNodeExtractionLlmClient : LlmClient` makes an ambiguous
  > `base(...)` call between `LlmClient(LlmConfig?, ILlmResponseCache?)` and `LlmClient(LlmConfig?, bool,
  > string)` — disambiguate the base ctor (e.g. cast the null, or pick the intended overload). Then finish
  > the remaining failure-mode cases above, verify, and commit. **Run in-tree** (not a worktree) so this
  > draft is visible; if you do start from a worktree the draft is recoverable by re-deriving it from this
  > step.
- [ ] **D. Fix what the pass surfaces.** Any real defect → minimal, parity-safe fix + regression test
  (verify against Python behavior where one exists; if C# intentionally differs, record it in
  `decisions.md` rather than "fixing" toward Python). Any intentional limit → document, don't silently
  leave it.

## Explicit non-goals (user-gated / out of scope)

- No `<Version>` stamp, no tag, no publish — those wait for Sergey (`roadmap.md` G6).
- No expansion of the **required** CI lanes; the linux-x64 and live-provider lanes stay additive/gated.
- No new default runtime dependency in `src/` (test-only fuzzing deps are fine).
- Keep exact-cosine the default vector path unless **0a**'s data explicitly justifies otherwise.

## Verification

`.\eng\Verify-GraphitiCore.ps1` green on win-x64 after each slice. All new tests are **deterministic**
and run in the standard suite (crafted inputs / fakes — no live provider key needed). Slices that change
structured-schema or coercion behavior must regenerate the API snapshot / golden schema in the same
commit and reconcile `parity.md` if parity state moves.
