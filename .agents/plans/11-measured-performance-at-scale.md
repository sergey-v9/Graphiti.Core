# Plan 11 — Measured performance & throughput at realistic scale

Created 2026-06-28. The backlog is exhausted: parity (Phases 1–3), plans 05–10, and the whole
2026-06-19 G1–G6 agenda are complete; the library is mature (green `1071/4/1075`, modernized,
robustness-hardened). Plan 10 captured the **static-audit micro-allocation** wins (each benchmarked in
isolation). Plan 11 moves to the next, deeper level of the performance track the paradigm calls out:
**measured, profile-driven performance at realistic scale**, covering both the in-process hot paths and
the LLM/embedding concurrency that actually dominates real throughput.

## Status

**Current priority (2026-06-28).** This is a **measure-first** pass with an honest two-way outcome: find
real structural/throughput wins, *or* prove with data that the current shape is within budget at the
target scale (a legitimate, decision-closing result, like the HNSW gate). No speculative churn. Parity,
wire, schema/cache identity, and the public surface stay unchanged. Release stays parked.

> Note for the supervisor/user: this is the diminishing-returns frontier. The code is already
> well-optimized; payoff is uncertain. The value is in *deciding the performance question with data* at
> scales we have never actually measured, and in catching any structural cliff before the larger system
> leans on this component. If the answer is "fine at scale N", we record that and stop — it is not a
> failure.

## Track 1 — In-process performance at scale (deterministic, InMemory)

- [ ] **1A. Establish realistic-scale benchmarks.** Extend the BenchmarkDotNet harness with scenarios at
  representative sizes (e.g. build a graph of ~10k nodes / ~30k edges; then ingest-a-batch and
  search-over-it at that size) on the **InMemory** driver so CPU/allocation are isolated from network and
  the DB. Record win-x64 baselines under `benchmarks/.../baselines/`. (Today's `IngestionBenchmarks` /
  `SearchBenchmarks` / `InMemoryVectorSearchBenchmarks` are small-N; this adds the large-N tier.)
- [ ] **1B. Profile and find what actually dominates.** With `[MemoryDiagnoser]` and timing at scale,
  identify the **dominant** in-process costs end-to-end — not micro-allocations. Structural candidates the
  2026-06-27 audit flagged but did not chase: InMemory **O(n) full-scan cosine**, bulk dedupe at scale,
  **MMR O(n²)**, JSON-object chunking serialization, fallback-graph BFS. Write a short profile note
  (what dominates at which size, with numbers).
- [ ] **1C. Land only measured structural wins.** For each candidate the profile proves dominant, apply a
  parity-safe fix with BenchmarkDotNet before/after + a recorded baseline. **Re-open the HNSW gate
  decision only if 1B shows full-scan cosine actually dominating at realistic N** (it was closed at
  small N); if so, scope an **opt-in** HNSW tier as its own plan and keep exact cosine the default. If
  nothing dominates unacceptably, record "within budget at scale N" in `decisions.md` and stop.

## Track 2 — LLM / embedding throughput (the real production cost)

In production this component's cost is dominated by LLM + embedding round-trips, not in-process CPU. The
machinery already exists (Polly resilience, the throttle/rate-limiter, response cache, batched
embeddings, 30-node summary flights, true-batch bulk ingestion) and G4 added the `Meter` — use it.

- [ ] **2A. Measure the concurrency/batching/caching machinery.** With a **latency-injecting fake
  provider** (deterministic, no real key) and the G4 metrics (ingestion/search duration, LLM tokens, cache
  hit/miss), answer: does the configured concurrency actually *saturate* within the configured rate limit,
  or are we leaving parallelism on the table / overrunning it? Are embedding batches and the 30-node
  summary flights sized well? What is the response-cache **hit rate** on a realistic re-ingest? Record a
  short measurements note.
- [ ] **2B. Tune only what the measurements justify.** Adjust concurrency caps, batch sizes, or cache
  TTL/key shape **only** where 2A shows a clear win; keep defaults unchanged unless the data says
  otherwise (and if a default moves, record it in `decisions.md`). Preserve cache-key / schema identity
  and parity exactly.

## Explicit non-goals (user-gated / out of scope)

- No `<Version>` stamp, no tag, no publish (parked, user-gated).
- No new default runtime dependency in `src/` (benchmark/test-only deps are fine).
- No behavior / wire / schema / cache-key / public-API change; defaults unchanged unless measured.
- Do **not** add the large-N scale benchmarks to the *required* verifier gate if they are slow — keep
  them opt-in/manual, like the other benchmarks.

## Verification

`.\eng\Verify-GraphitiCore.ps1` green on win-x64 throughout. Every code change carries a BenchmarkDotNet
before/after and a recorded baseline; every "no change" conclusion carries the measurement that justifies
it. Keep exact-cosine the default vector path unless 1B's data explicitly reopens HNSW.
