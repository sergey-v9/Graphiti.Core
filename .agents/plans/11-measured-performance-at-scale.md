# Plan 11 — Measured performance & throughput at realistic scale

**DONE 2026-06-28.** Measure-first pass at 10k nodes / 30k edges (InMemory) plus latency-injecting
fake-provider throughput benchmarks with G4 metric assertions. One structural win landed: ordinary edge
search now skips the endpoint-node dictionary lookup when no node-label filter is requested (edge hybrid
RRF 75.3→45.2 ms, edge vector 16.3→8.6 ms, edge hybrid MMR 79.5→45.0 ms, win-x64 ShortRun). HNSW stayed
closed (exact cosine was not the dominant cost at this size); provider concurrency, response-cache, and
embedding-batch defaults were measured within budget and left unchanged.

Durable record: the HNSW decision in `roadmap.md`, the baselines
`benchmarks/Graphiti.Core.Benchmarks/baselines/2026-06-28-inmemory-scale-win-x64.md` and
`...-provider-throughput-win-x64.md`, git history. (Stub per `doc-hygiene.md`, 2026-06-28.)
