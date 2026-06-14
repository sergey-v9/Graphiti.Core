# Eval Harness Proposal

> **ACCEPTED / IMPLEMENTED (2026-06-14).** The harness was built to this proposal's graph-building
> design as `samples/Graphiti.Eval` (`Program.cs`), and the four `eval.py` prompts shipped as the
> `internal static EvalPrompts` class in `src/Graphiti.Core/Prompts/EvalPrompts.cs`. It was run live
> on 2026-06-14 (see `parity.md` eval row). This document is retained as the design record; the
> "Open Decisions" below are resolved (see that section).

This was the buy-in proposal for Plan 03 item 4.

## Python Source Of Truth

Python has two eval surfaces under `graphiti_core` / `tests/evals`:

- `graphiti_core/prompts/eval.py` defines four prompt/response pairs:
  `query_expansion`, `qa_prompt`, `eval_prompt`, and `eval_add_episode_results`.
- `tests/evals/eval_e2e_graph_building.py` is the graph-building comparison harness. It reads
  `tests/evals/data/longmemeval_data/longmemeval_oracle.json`, builds a baseline graph with
  `gpt-4.1-mini`, builds a candidate graph, writes `baseline_graph_results.json` and
  `candidate_graph_results.json`, and uses `eval.eval_add_episode_results` as an LLM judge for each
  candidate add-episode result.
- `tests/evals/eval_cli.py` is a small CLI wrapper with `--multi-session-count`, `--session-length`,
  and `--build-baseline`.

The graph-building eval requires real provider calls and a real graph backend in Python (Neo4j via
`tests.test_graphiti_int` settings). It is not a deterministic unit-test replacement.
There is a current Python harness caveat: `eval_e2e_graph_building.py` imports Neo4j constants from
`tests.test_graphiti_int`, while the visible defaults live in `tests/helpers_test.py`; a C# port
should use explicit configuration rather than copying that import shape.

## Proposed C# Scope

Build a separate sample/tool project, not core library code:

- Add `samples/Graphiti.Evals.OpenAI` or `tools/Graphiti.Evals.OpenAI` as an executable project.
- Reuse the existing OpenAI M.E.AI adapters, `InMemoryGraphDriver` by default, and optional
  LadybugDB only after provider validation resumes.
- Read `OPENAI_API_KEY`, `OPENAI_CHAT_MODEL`, `OPENAI_SMALL_MODEL`, `OPENAI_EMBEDDING_MODEL`,
  `OPENAI_EMBEDDING_DIMENSIONS`, and optional input/output paths from env or CLI flags.
- Load a small checked-in fixture first, with an optional path to the Python LongMemEval JSON if the
  user wants the full dataset locally. Do not vendor large external datasets into the C# package.
- Emit baseline and candidate result JSON with stable snake_case Graphiti serialization.
- Port the eval prompt builders near-verbatim under a sample/tool namespace first. Move them into
  core only if a public eval API is explicitly approved later.
- Preserve Python structured response shapes: `query`, `ANSWER`, `is_correct` / `reasoning`, and
  `candidate_is_worse` / `reasoning`.

## Minimum Viable Harness

1. `build-baseline`: ingest N users x M messages through the current C# pipeline and write a
   baseline JSON artifact.
2. `eval-candidate`: ingest the same N x M messages, write candidate JSON, and call the LLM judge
   with `eval_add_episode_results`.
3. Print:
   - model/provider settings,
   - users/messages evaluated,
   - per-user score,
   - aggregate score,
   - judge reasoning for failures.
4. Exit non-zero only on harness/runtime failure. Do not fail on low quality score until a threshold
   decision is approved.

## Acceptance

- No `OPENAI_API_KEY`: prints setup guidance and exits with a distinct non-zero code, matching the
  existing provider-validation helper style.
- With `OPENAI_API_KEY`: produces baseline/candidate JSON and an aggregate score for a tiny fixture.
- Full verifier still passes with no key; live eval is manually invoked and not part of normal
  deterministic verification.
- Findings from the first live run are recorded in `parity.md` / `handoff.md` and turned into
  follow-up plan items only when they are Graphiti port issues rather than provider variance.

## Open Decisions (resolved)

These were resolved when the harness was built (see `parity.md` eval row); recorded here for history:

- Project location: `samples/` keeps it user-facing; `tools/` keeps it clearly non-library and
  non-sample. **Resolved: `samples/Graphiti.Eval`.**
- Dataset policy: tiny checked-in fixture only, or documented local path to LongMemEval. **Resolved:
  tiny self-contained in-code fixture (no external dataset vendored).**
- Baseline policy: compare C# current-vs-current first for repeatability, or compare Python-exported
  baseline JSON to C# candidate for stronger parity evidence. **Resolved: graph-building judges
  candidate per-episode `AddEpisodeResults` against a persisted baseline artifact (C# current-vs-
  current) via `eval_add_episode_results`.**
- Backend policy: start with `InMemoryGraphDriver`; add LadybugDB after Phase 4 provider
  productization work resumes. Strict Python parity uses Neo4j, but C# provider direction should be
  decided explicitly rather than inherited accidentally. **Resolved: started on `InMemoryGraphDriver`.**
- Scoring threshold: report-only initially, or fail below an approved quality floor. **Resolved:
  report-only (the live 2026-06-14 run reports scores; exits non-zero only on harness failure).**
