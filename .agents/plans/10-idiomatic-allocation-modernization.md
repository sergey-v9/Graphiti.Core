# Plan 10 — Idiomatic + allocation modernization

**DONE 2026-06-28.** Two tracks — idiomatic modern C# (C# 14 / .NET 10) and allocation/GC discipline —
swept all of `Graphiti.Core` from an 8-area codebase audit, parity-safe and warning-clean, with hot-path
changes benchmarked. Representative wins: lazy community fallback, `IsCleanInput`→`SearchValues`,
immutable-message reuse, the bulk word-overlap HashSet hoist, Ladybug direct-embedding read /
`params ReadOnlySpan` params / delete-array throwaways, `AttributeMerger.TryGetPropertyValue`,
O(n²)→reverse overlap, snapshot-helper collapse + dead-code removal, bulk node/edge dedupe indexing,
`MaterializeVector` via `CollectionsMarshal`, MMR dimension hoist, MinHash single-encode, the
collection-expression sweep, `System.Threading.Lock`, and the InMemory 18-arg ctor → `SharedStore`.

Durable record: the "headlines" summary in `handoff.md`, the win-x64 baselines under
`benchmarks/Graphiti.Core.Benchmarks/baselines/`, git history. (Stub per `doc-hygiene.md`, 2026-06-28.)
