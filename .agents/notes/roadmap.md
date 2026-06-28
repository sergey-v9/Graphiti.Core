# C# Port Roadmap

The backlog is complete (see "Completed agenda" below) and the library is mature. This file now holds the
**current posture**, the **standing scope decisions and direction**, and the **open future questions**.
Milestone history lives in `evolution.md`, durable decisions in `decisions.md`, parity ground truth in
`parity.md`, and the slice-by-slice detail in git. Keep this file lean (`doc-hygiene.md`).

## Current posture (2026-06-28)

Parity (Phases 1–3) and the whole 2026-06-19 G1–G6 productionization agenda plus work-order plans 05–11
are **done**; the suite is green and the library is parity-complete, productionized, cross-platform,
observable, modernized, robustness-hardened, and perf-measured at scale. Per the 2026-06-27 paradigm
shift this is **our own embeddable internal library** (likely to be renamed), **not** a release-bound
product — read `decisions.md` → "What this project is (paradigm)". Release versioning/publishing is
**parked** (user-gated).

With the backlog exhausted, the realistic forward posture is **maintenance**: keep tracking the Python
upstream for parity (`upstream-sync-procedure.md`; the `eng/Invoke-UpstreamDeltaReminder.ps1` reminder
exists), and apply opportunistic, parity-safe modernization as the language/runtime move (C# next /
.NET 11) — every change warning-clean, hot-path changes benchmark-first. Behavioral/feature parity with
Python stays the functional floor and is essentially complete. A genuinely new direction comes from Sergey.

## Resolved scope decisions (Sergey, 2026-06-17 → 06-27)

- **CI — keep as-is, do not expand.** The full authenticated `eng\Verify-GraphitiCore.ps1` Windows lane,
  the separately gated linux-x64 LadybugDB extension smoke (behind `GRAPHITI_ENABLE_LINUX_LADYBUG_SMOKE=1`),
  and the gated `live-provider.yml` (OpenAI key; weekly/dispatch). Do not add lanes unless Sergey asks.
- **LadybugDB feed — GitHub Packages only.** `NuGet.config` points at `sergey-v9/ladybug-dotnet`; no local
  offline fallback. A `read:packages` credential for source `github_ladybug` is required for any
  Ladybug-inclusive restore. Intentional.
- **Self-service bindings (standing).** `sergey-v9/ladybug-dotnet` is our fork: implement a missing binding
  capability in `tools/csharp_api`, push the fork (builds a dev package), bump the pin.
- **Neo4j removed (2026-06-17); Ladybug merged into Core (plan 06).** LadybugDB is first-class inside
  `Graphiti.Core/Drivers/Ladybug/`.
- **Still user-gated:** release publishing/versioning (2.0.0 line, alpha→beta cadence, metapackage shape).
  Do not self-authorize.

## HNSW vector tier — closed

Exact full-scan cosine stays the default vector path. Measured 2026-06-27 (104.5 µs @500, 387.4 µs @2000
candidates, win-x64 ShortRun) and again under plan 11 at 10k nodes / 30k edges (2026-06-28), full-scan
cosine was not the dominant cost. Reopen an opt-in approximate tier only if a future same-machine
benchmark at a materially larger target shows full-scan cosine is the bottleneck.

## Standing direction

- **Track upstream Python** at `getzep/graphiti` `origin/main` HEAD (not tags), `graphiti_core/` only.
  Pull a batch via `upstream-sync-procedure.md` (delta → classify → incorporate → other-provider
  adaptation check → verify centrally → adversarial audit → record → advance the anchor). The current
  sync anchor is in `parity.md`.
- Parity hardening stays **bounded**: only real, reachable divergences verified against the Python source,
  never speculative churn.
- LadybugDB is the main provider; InMemory is the deterministic reference/test driver; FalkorDB/Neptune
  are enum/wire compatibility surfaces only.
- Search stays custom and parity-tested (RRF, MMR, cross-encoder, node-distance, episode-mentions,
  filters, BFS, result merge). `Microsoft.Extensions.AI` stays the chat/embedding adapter boundary;
  `ILlmClient`/`IEmbedderClient`/`ICrossEncoderClient` stay the Graphiti-facing contracts.
- Keep custom: episode ingestion, temporal invalidation, the graph-driver contract, search-merge
  semantics, and prompt/result DTO wire compatibility with Python.
- Do not add Lucene.NET as a default core dependency; do not replace Graphiti with agent-framework memory
  abstractions (`decisions.md` Replacement Policy).
- **Build/test gotcha:** never run multiple worktree agents' `dotnet test` concurrently — the LadybugDB
  native package serializes poorly across worktrees and deadlocks. Build-only in worktrees; run the
  consolidated test centrally.

## Future decisions to revisit

- Whether to expose external adapters (OpenAI, Azure OpenAI, Qdrant, Semantic Kernel) as separate packages.
- Whether to add a compatibility option defaulting chunking to Python's chars-per-token heuristic.

## Completed agenda (history — detail in `evolution.md` + git)

Phases 1–3 (prompt parity, pipeline semantic parity, real-provider validation) completed 2026-06-11→14.
Plans 05–11 complete: 05 release-readiness surface, 06 Ladybug→Core merge, 07 linux-x64 proof, 08
release-surface finalization, 09 robustness hardening, 10 idiomatic+allocation modernization, 11 measured
performance at scale. Long-term goals G1–G6 (cross-platform proof, continuous-quality canary,
perf/allocation program, observability + DX, sustained upstream parity, release-readiness non-gated part)
all done. The completed plan files are DONE stubs in `.agents/plans/`; full history is in `evolution.md`
and git.
