# Plan 03 — Real-provider end-to-end validation

**Objective:** prove the C# library against a real LLM + embedder for the first time, find the
integration bugs the deterministic suite cannot see, and make that proof repeatable.

**Why:** all 887 tests run against fake clients (`StaticJsonLlmClient`, `HashEmbedder`,
`IdentityCrossEncoderClient`). Structured-output handling, schema acceptance by real providers,
prompt efficacy, retry behavior, and embedding dimensionality have never been exercised. Until
this plan runs green, no parity claim about extraction quality is real.

**Prerequisite:** plan 01 complete (otherwise you are validating stub prompts). Plan 02 items 1–2
strongly recommended first.

**Non-goals:** no new provider SDK dependencies in `Graphiti.Core` (M.E.AI adapters or a sample
host project own provider packages); no CI wiring yet; no perf measurement.

## Work items

- [ ] 1. Sample host. New `samples/Graphiti.Sample.OpenAI` console project (separate from the
      core package): configures `MicrosoftExtensionsAIChatClient` + embedder via the OpenAI
      M.E.AI package, InMemory or LadybugDB driver, reads `OPENAI_API_KEY` from env. Ingests
      ~6-10 episodes of a small scripted conversation (borrow content from Python
      `tests/evals/` or examples/quickstart so results are comparable), then runs
      `SearchAsync` with a few queries and prints nodes/edges/summaries.
      Acceptance: a human (the user) can run it with a key and read a sane graph: correctly named
      entities, no pronoun/garbage nodes, facts with sensible valid_at, non-empty summaries
      (after plan 02 item 1), relevant search hits.
- [ ] 2. Env-gated integration tests. xUnit collection gated on `OPENAI_API_KEY` presence
      (skip otherwise, like the `_int` convention in the Python repo): one test ingests two
      related episodes and asserts structural invariants (entities resolved across episodes, at
      least one edge with extracted timestamp, dedup did not duplicate the shared entity), one
      test exercises structured-output schema acceptance for every response model the pipeline
      uses (catches providers rejecting `additionalProperties`/required-field schemas).
      Record model + date + outcome in `handoff.md` checkpoint when run.
- [ ] 3. Cross-encoder reality check. Default DI currently injects lexical
      `IdentityCrossEncoderClient`. Implement the M.E.AI-based reranker path equivalent to
      Python `openai_reranker_client.py` (boolean classification; use logprobs if the adapter
      exposes them, otherwise the structured boolean+confidence variant) and use it in the
      sample. Decide+document the default (`decisions.md`).
- [ ] 4. (Optional, with user buy-in) Port the eval harness: Python `prompts/eval.py` +
      `tests/evals/` give scored end-to-end extraction comparisons. This is the durable answer to
      "is the C# port as good as Python" — propose before building.
- [ ] 5. Record everything found (provider quirks, schema failures, prompt issues) as new rows or
      notes in `parity.md` and file follow-up items; close the loop by updating `roadmap.md`
      Phase 3.

## Done when

Sample app demonstrates a sane real-provider graph; env-gated tests exist and have passed at least
once (checkpoint recorded); cross-encoder default decided; findings recorded.
