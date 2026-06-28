# Plan 11 — Measured performance & throughput at realistic scale

Created 2026-06-28. The backlog is exhausted: parity (Phases 1–3), plans 05–10, and the whole
2026-06-19 G1–G6 agenda are complete; the library is mature (green `1071/4/1075`, modernized,
robustness-hardened). Plan 10 captured the **static-audit micro-allocation** wins (each benchmarked in
isolation). Plan 11 moves to the next, deeper level of the performance track the paradigm calls out:
**measured, profile-driven performance at realistic scale**, covering both the in-process hot paths and
the LLM/embedding concurrency that actually dominates real throughput.

## Status

**Complete (2026-06-28).** This measure-first pass closed with one retained structural win and several
"within budget" decisions. Large-N InMemory benchmarks at 10000 nodes / 30000 edges showed ordinary
edge search was paying for endpoint-node dictionaries even when no endpoint label filter was present;
skipping that lookup moved edge hybrid RRF 75.275 ms -> 45.198 ms, edge vector 16.334 ms -> 8.591 ms,
and edge hybrid MMR 79.466 ms -> 45.000 ms on win-x64 ShortRun. Full-scan exact cosine did not dominate
at this size, so the HNSW gate stays closed. Provider-throughput benchmarks with latency-injecting fake
M.E.AI providers and G4 metric assertions showed the current LLM cache/rate-limit and embedding batch
defaults are within budget; no cache-key, schema identity, wire, public-surface, or default setting
changed. Detailed tables live in:
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-28-inmemory-scale-win-x64.md` and
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-28-provider-throughput-win-x64.md`.

> Note for the supervisor/user: this is the diminishing-returns frontier. The code is already
> well-optimized; payoff is uncertain. The value is in *deciding the performance question with data* at
> scales we have never actually measured, and in catching any structural cliff before the larger system
> leans on this component. If the answer is "fine at scale N", we record that and stop — it is not a
> failure.

## Track 1 — In-process performance at scale (deterministic, InMemory)

- [x] **1A. Establish realistic-scale benchmarks.** Extend the BenchmarkDotNet harness with scenarios at
  representative sizes (e.g. build a graph of ~10k nodes / ~30k edges; then ingest-a-batch and
  search-over-it at that size) on the **InMemory** driver so CPU/allocation are isolated from network and
  the DB. Record win-x64 baselines under `benchmarks/.../baselines/`. (Today's `IngestionBenchmarks` /
  `SearchBenchmarks` / `InMemoryVectorSearchBenchmarks` are small-N; this adds the large-N tier.)
- [x] **1B. Profile and find what actually dominates.** With `[MemoryDiagnoser]` and timing at scale,
  identify the **dominant** in-process costs end-to-end — not micro-allocations. Structural candidates the
  2026-06-27 audit flagged but did not chase: InMemory **O(n) full-scan cosine**, bulk dedupe at scale,
  **MMR O(n²)**, JSON-object chunking serialization, fallback-graph BFS. Write a short profile note
  (what dominates at which size, with numbers).
- [x] **1C. Land only measured structural wins.** For each candidate the profile proves dominant, apply a
  parity-safe fix with BenchmarkDotNet before/after + a recorded baseline. **Re-open the HNSW gate
  decision only if 1B shows full-scan cosine actually dominating at realistic N** (it was closed at
  small N); if so, scope an **opt-in** HNSW tier as its own plan and keep exact cosine the default. If
  nothing dominates unacceptably, record "within budget at scale N" in `decisions.md` and stop.

## Track 2 — LLM / embedding throughput (the real production cost)

In production this component's cost is dominated by LLM + embedding round-trips, not in-process CPU. The
machinery already exists (Polly resilience, the throttle/rate-limiter, response cache, batched
embeddings, 30-node summary flights, true-batch bulk ingestion) and G4 added the `Meter` — use it.

- [x] **2A. Measure the concurrency/batching/caching machinery.** With a **latency-injecting fake
  provider** (deterministic, no real key) and the G4 metrics (ingestion/search duration, LLM tokens, cache
  hit/miss), answer: does the configured concurrency actually *saturate* within the configured rate limit,
  or are we leaving parallelism on the table / overrunning it? Are embedding batches and the 30-node
  summary flights sized well? What is the response-cache **hit rate** on a realistic re-ingest? Record a
  short measurements note.
- [x] **2B. Tune only what the measurements justify.** Adjust concurrency caps, batch sizes, or cache
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
it. Keep exact-cosine the default vector path unless a future larger same-machine run explicitly reopens
HNSW.
