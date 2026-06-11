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
        schemas. No-key skip path was verified (`2` skipped, `0` failed). No live provider run was
        performed in this environment, so the phase-level "passed at least once" gate remains open.
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
- [ ] 4. (Optional, with user buy-in) Port the eval harness: Python `prompts/eval.py` +
      `tests/evals/` give scored end-to-end extraction comparisons. This is the durable answer to
      "is the C# port as good as Python" — propose before building.
      - Proposal drafted 2026-06-11 in `.agents/notes/eval-harness-proposal.md`. Await explicit
        user approval before building. Proposed scope is a separate sample/tool executable that
        ports the eval prompts near-verbatim, loads a tiny fixture or local LongMemEval path, emits
        baseline/candidate JSON, and reports LLM-judge scores without becoming part of normal
        deterministic verification.
- [ ] 5. Record everything found (provider quirks, schema failures, prompt issues) as new rows or
      notes in `parity.md` and file follow-up items; close the loop by updating `roadmap.md`
      Phase 3.

## Done when

Sample app demonstrates a sane real-provider graph; env-gated tests exist and have passed at least
once (checkpoint recorded); cross-encoder default decided; findings recorded.
