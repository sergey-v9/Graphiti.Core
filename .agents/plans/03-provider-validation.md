# Plan 03 — Real-provider end-to-end validation

**Objective:** prove the C# library against a real LLM + embedder for the first time, find the
integration bugs the deterministic suite cannot see, and make that proof repeatable.

**Why:** the deterministic test suite runs against fake clients (`StaticJsonLlmClient`,
`HashEmbedder`, `IdentityCrossEncoderClient`). Structured-output handling, schema acceptance by real
providers, prompt efficacy, retry behavior, and embedding dimensionality have never been exercised.
Until this plan runs green, no parity claim about extraction quality is real.

**Prerequisite:** plan 01 complete (otherwise you are validating stub prompts). Plan 02 items 1–2
strongly recommended first.

**Non-goals:** no new provider SDK dependencies in `Graphiti.Core` (M.E.AI adapters or a sample
host project own provider packages); no CI wiring yet; no perf measurement.

## Live validation result — PASSED 2026-06-13

First successful end-to-end run against the real OpenAI API (key supplied locally via `.env`,
`gpt-4.1-mini` chat / `gpt-4.1-nano` reranker / `text-embedding-3-small` @1536). Run with
`.\eng\Run-OpenAIProviderValidation.ps1` (now auto-loads `.env`).

- **Both env-gated integration tests passed** (26s): every Graphiti structured response schema (11
  response models + the dynamic attribute schema) was accepted by the real provider — the schema
  acceptance the fake-client suite could never verify — and a real 2-episode ingest produced a
  resolved temporal graph with a `Maya` entity and a timestamped edge.
- **6-episode sample produced a sane graph:** clean entities with rich, accurate summaries (entity
  summary generation confirmed live); bi-temporal invalidation works (the "Atlas blocked by
  authentication regression" fact was `invalid_at` exactly the QA-clearance date 2026-03-22; a
  superseded "Maya manages Atlas" fact was contradiction-invalidated); hybrid search + the live
  cross-encoder reranker returned relevant, well-ordered results (owner query → Leo Chen first;
  current-date query → March 29 above the stale March 15).
- **Observation (not a port defect):** the "first rollout window 2026-03-15" fact was not invalidated
  by the "moved to March 29" episode, because that episode's March-29 info landed in a memory-note
  edge rather than a contradicting rollout-date edge. This is LLM extraction/entity-modeling variance
  (dates-as-entities) that would vary identically in Python; confirming it would need the item-4 eval
  harness. Tracked as a future-eval comparison, not a fix.

## Work items

- [x] 1. Sample host. New `samples/Graphiti.Sample.OpenAI` console project (separate from the
      core package): configures `MicrosoftExtensionsAIChatClient` + embedder via the OpenAI
      M.E.AI package, InMemory or LadybugDB driver, reads `OPENAI_API_KEY` from env. Ingests
      ~6-10 episodes of a small scripted conversation (borrow content from Python
      `tests/evals/` or examples/quickstart so results are comparable), then runs
      `SearchAsync` with a few queries and prints nodes/edges/summaries.
      Acceptance: a human (the user) can run it with a key and read a sane graph: correctly named
      entities, no pronoun/garbage nodes, facts with sensible valid_at, non-empty summaries
      (after plan 02 item 1), relevant search hits.
      - Done 2026-06-11: added `samples/Graphiti.Sample.OpenAI`, using
        `Microsoft.Extensions.AI.OpenAI`, `MicrosoftExtensionsAIChatClient`,
        `MicrosoftExtensionsAIEmbedderClient`, and `InMemoryGraphDriver`. The sample reads
        `OPENAI_API_KEY`, optional model/dimension env vars, ingests six Atlas-project episodes,
        and prints entities, facts, summaries, and search results. Compile-verified and no-key path
        verified; no live provider run was performed in this environment.
- [x] 2. Env-gated integration tests. xUnit collection gated on `OPENAI_API_KEY` presence
      (skip otherwise, like the `_int` convention in the Python repo): one test ingests two
      related episodes and asserts structural invariants (entities resolved across episodes, at
      least one edge with extracted timestamp, dedup did not duplicate the shared entity), one
      test exercises structured-output schema acceptance for every response model the pipeline
      uses (catches providers rejecting `additionalProperties`/required-field schemas).
      Record model + date + outcome in `handoff.md` checkpoint when run.
      - Done 2026-06-11: added `OpenAIProviderIntegrationTests`, gated by
        `OPENAI_API_KEY`, with a two-episode ingestion test and a structured-output schema
        acceptance test covering ingestion DTOs, optional/internal DTOs, and runtime attribute
        schemas. No-key skip path was verified (`2` skipped, `0` failed). Live run 2026-06-13: both
        tests PASSED against the real OpenAI API (see "Live validation result" above) — the
        phase-level "passed at least once" gate is now CLOSED.
- [x] 3. Cross-encoder reality check. Default DI currently injects lexical
      `IdentityCrossEncoderClient`. Implement the M.E.AI-based reranker path equivalent to
      Python `openai_reranker_client.py` (boolean classification; use logprobs if the adapter
      exposes them, otherwise the structured boolean+confidence variant) and use it in the
      sample. Decide+document the default (`decisions.md`).
      - Done 2026-06-11: added `MicrosoftExtensionsAICrossEncoderClient`, an opt-in
        M.E.AI-backed reranker that issues one structured boolean relevance classification per
        passage and converts decision confidence to relevance probability. Generic M.E.AI does not
        expose OpenAI top-logprob controls, so this uses the planned structured boolean+confidence
        fallback rather than Python's exact logprob scoring. The OpenAI sample now passes the real
        reranker into `Graphiti`; DI/default construction intentionally still uses
        `IdentityCrossEncoderClient` as the provider-free default and records that decision in
        `decisions.md`.
      - Repeatability support 2026-06-11: added `eng/Run-OpenAIProviderValidation.ps1`, which
        requires `OPENAI_API_KEY`, restores/builds, runs the focused OpenAI integration tests, and
        runs the OpenAI sample. No-key behavior is verified; no live provider run was performed.
- [x] 4. Eval harness — BUILT and RUN LIVE 2026-06-14 (user approved "do it"). The four `eval.py`
      prompts are ported with full-string golden tests (`Prompts/EvalPrompts.cs`), and
      `samples/Graphiti.Eval` implements the proposal's design:
      - **Graph-building regression eval (default)** mirrors Python `tests/evals/eval_e2e_graph_building.py`:
        ingest the fixture → capture per-episode `AddEpisodeResults` (candidate), persist a gitignored
        `eval-artifacts/baseline_graph_results.json` (first run establishes it), then judge candidate-vs-
        baseline per episode via the now-wired `eval_add_episode_results` judge. Live: establish then
        re-judge returned **6/6 not-worse (score 1.0)** on identical code, with the judge genuinely
        diffing extractions — proving the regression-detection path works end to end.
      - **Retrieval-QA mode (`--qa`)** is a secondary surface, fixed per review to use the top-1
        retrieved fact (not concatenate-all) and to include a distractor question. Live: **3/7 honest**,
        distractor correctly failed (`distractor_leak: false`). The lower-than-inflated score reflects
        the known reschedule-extraction gap and two ranking misses — real signal, not a harness flaw.
      An adversarial review caught that the first cut measured retrieval-QA instead of graph-building
      (the ported `eval_add_episode_results` was dead code); reworked to the proposal design before
      finalizing.
- [x] 5. Record everything found. Done 2026-06-13: the live run is recorded here, in `parity.md`
      (real-provider row → PASSED), `roadmap.md`, `handoff.md`, and `evolution.md` (milestone M3).
      `eng/Run-OpenAIProviderValidation.ps1` now auto-loads a gitignored `.env`, and `.env` was added
      to `.gitignore`. No provider quirks or schema failures were found; the one extraction
      observation is recorded above for the future eval comparison.

## Done when — MET 2026-06-14

Sample app demonstrates a sane real-provider graph; env-gated tests passed at least once (checkpoint
recorded); cross-encoder default decided; findings recorded; eval harness built and run live after
explicit user approval. No plan 03 checklist items remain open.
