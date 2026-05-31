# C# Port Roadmap

This roadmap collects planned or useful follow-up work from the retired docs. It is intentionally
shorter than the old working plans; expand items only when they become active.

## Near-Term

1. Verify the current tree after parallel work settles.

   Run a focused build/test pass before making assumptions from older notes. The repo has had
   parallel edits, and historical docs contained both transient build failures and later successful
   full-suite runs.

2. Re-center modernization work on Graphiti-shaped .NET idioms.

   If a local `.agents/skills` corpus is present, use it when a slice actually needs its domain
   knowledge: test running, MSTest authoring, performance scanning, BenchmarkDotNet, NuGet
   publishing, MCP work, or quick C# API experiments. Keep modernization concrete and reviewable:
   prefer maintained .NET infrastructure at existing adapter boundaries, source generation where it
   removes reflection or repeated parsing, allocation-aware code in hot paths, and targeted tests/
   benchmarks for risky changes. Do not turn this into a sweep for fashionable frameworks, broad
   provider abstractions, or AI agent/RAG replacements for Graphiti's temporal graph model.

3. Port the LadybugDB provider.

   This is the main provider focus. Use the LadybugDB NuGet package from the alternative Kuzu fork,
   match Python Kuzu behavior during parity work, and make LadybugDB the driver-facing/default
   provider name as the implementation stabilizes. A foundation-only schema/statement/mapper slice
   now covers Kuzu save/get/delete/retrieve/load-embedding shapes, individual bulk-save expansion,
   simple-edge mapping, JSON attributes, label arrays, and `RelatesToNode_`, with an internal
   executor-backed non-search driver core layered on top. A search statement foundation now pins Kuzu
   full-text indexes, full-text/vector search, BFS plans, ranker query shapes, and fake-executor
   result mapping without making the driver searchable. Next provider work should decide package/
   native dependency shape, add a concrete LadybugDB executor adapter, prove the statements against
   the real backend, prove the full C# Saga and entity-edge `reference_time` projections, and avoid
   marking DI support complete too early. Details are in `kuzu-driver-port.md`.

4. Finish low-risk XML documentation gaps.

   Remaining gaps are mostly internal utilities, deeper namespace member docs, and internal Graphiti
   helper details. Avoid touching hot extraction files while another agent is active there.

5. Add isolated parity tests where coverage is still thin.

   The common helper candidates are now covered directly (`LuceneSanitize`, `ParseDbDate` /
   `TryParseDbDate`,
   `NormalizeL2`, group-id validation, node-label validation, `EpisodeType` wire values, and the
   maintenance helper edge-construction/pointer-resolution paths). Add new isolated parity tests when
   provider-specific query helpers change while adding Kuzu or when a newly touched behavior has weak
   coverage. Provider-rate-limiter coverage now includes direct helper behavior, adapter-level
   rejected permits, batch concurrency, queued cancellation, and post-provider parse/validation
   lease release. Recent parity coverage also pins shared full-text query limit behavior and
   extraction handling for Python-style entity type descriptions, numeric-string type ids, and
   allocation-light entity-key normalization. LLM node dedupe coverage now also includes the default
   generic entity-type description and numeric-string resolution ids. Entity dedupe coverage now
   also pins the allocation-light entropy term counter and short multi-token fuzzy-match path. Edge
   dedupe coverage now includes allocation-light duplicate/contradiction id parsing with
   numeric-string coercion. Extraction string parsing now also pins allocation-light non-string JSON
   fallback behavior. Optional LLM edge timestamp parsing now uses a non-throwing date parser while
   preserving public `ParseDbDate` exception behavior for invalid values. Entity type validation now
   uses a frozen protected-name set and allocation-light valid-path loops for node-label and excluded
   entity-type validation, with direct helper coverage for declared type aliases, distinct sorted
   invalid names, and case-insensitive protected attributes.

6. Revisit analyzer suppressions only if they accumulate again.

   Namespace/folder alignment is currently enforced. If future suppressions spread across many files,
   centralize them deliberately instead of adding one-off pragmas.

## Driver And Search Work

- Keep improving the driver abstraction only where it serves Graphiti's temporal graph contract and
  the LadybugDB provider work.
- Preserve the in-memory driver as a deterministic reference backend.
- Keep existing Neo4j and FalkorDB behavior if already implemented, but stop treating them as
  improvement targets. They can remain useful reference providers to confirm shared behavior and
  direction.
- Preserve the current search split: `SearchEngine` orchestrates, `SearchRetrievalRunner` forwards
  driver-backed retrieval, and `SearchResultComposer` owns ranking/fusion/result shaping.
- Fallback full-text retrieval for in-memory/materialized non-search drivers now uses a small
  internal corpus BM25 scorer. Document query-term frequencies are computed in the candidate
  tokenization pass, preserving repeated-document-term scoring and distinct query-term behavior
  without rescanning each document during scoring. BM25 query-term de-duplication now also uses the
  shared token visitor to build the distinct term list and lookup in one pass, and final document
  score projection uses a pre-sized loop. Keep provider-backed full-text delegated to each graph
  backend.
- The default identity cross-encoder now reuses one query `TextScorer` per ranking call, preserving
  deterministic lexical reranking and duplicate-passage index handling while avoiding repeated query
  tokenization.
- RRF fusion from `SearchResultComposer` now runs directly over ranked `(Item, Score)` tuples instead
  of materializing item-only lists, while preserving rank-position scoring, inclusive min-score
  filtering, and first-seen tie order. The shared RRF projection path now uses a pre-sized loop and
  the internal ranked-tuple helper returns its final list directly to the composer.
- Shared bounded top-k ranking in `SearchUtilities.TopByScoreCore` now materializes the priority
  queue into a concrete buffer, sorts by score/first-seen index, and projects results with pre-sized
  loops instead of LINQ sort/projection chains. This preserves the ordering semantics used by RRF,
  MMR, and direct top-score helpers.
- `SearchUtilities` now shares the same allocation-light indexed-score projection helper across RRF
  and MMR, and the item-only public MMR overload projects selected items with a pre-sized loop while
  preserving the public `IReadOnlyList` return shape and Python scoring/tie behavior.
- `SearchEngine` now keeps RRF limiting inside fusion for pure RRF branches, uses an owned-list
  limiter for final result truncation, and routes MMR through a ranked-tuple internal helper instead
  of projecting preliminary candidates to item-only lists. Edge episode-mentions sorting now uses a
  stable explicit sort helper. Cross-encoder, node-distance, and episode-mentions driver rerankers
  still receive their full preliminary candidate pools.
- `SearchResultComposer` now builds cross-encoder passage and result lists in single-pass loops over
  the existing indexed candidate list, preserving indexed duplicate/invalid-rank handling,
  inclusive minimum-score filtering, and stable score/index ordering without extra candidate copies
  or LINQ sort/projection chains.
- `SearchResultComposer.MergeRankedCandidates` now uses an explicit sorted buffer and pre-sized
  result projection, preserving first-seen item retention, maximum-score merge behavior, and
  first-seen tie ordering without a LINQ sort/projection chain.
- `SearchEngine` now uses `SearchResultComposer.SplitRankedResults` to split ranked tuples into
  result and score lists in one pass per scope, preserving list order, score conversion, and public
  `SearchResults` shape while keeping result shaping in the composer boundary.
- Shared search token scanning now has an internal visitor path used by `TextScorer` and fallback
  BM25 document scoring, avoiding per-candidate token-list materialization while keeping the
  list-returning `Tokenize` helper and Unicode/invariant lowercase token behavior intact.
- `SearchEngine` now derives implicit BFS origins only when BFS is enabled. Explicit origin lists are
  still passed through as provided, while implicit node/edge origins preserve text-before-vector,
  first-seen distinct ordering over node UUIDs and edge source-node UUIDs.
- Microsoft.Extensions.AI embedding vector materialization now copies directly from provider
  `ReadOnlyMemory<float>` into validated Graphiti-owned lists. Preserve dimension/non-finite checks,
  returned-vector isolation, batch ordering, retry telemetry, and rate-limit lease behavior if this
  path changes again.
- Materialized fallback BFS/ranker shaping now avoids grouping/distinct/order LINQ chains on the
  hot path. Node and edge BFS results are accumulated in one pass with first traversal hit
  de-duplication, node-distance ranking uses score buckets over first-seen distinct inputs, episode-
  mentions ranking uses a counting dictionary and explicit stable sort, and traversal graph/endpoint
  lookup construction uses first-wins loops while preserving existing BFS and group-filter
  semantics.
- `InMemoryGraphDriver` BFS/ranker shaping now mirrors that allocation-light approach with loop-built
  candidate lookups, seen sets, and rank buckets. Preserve shortest first traversal hits,
  origin-group filtering, stable first-seen de-duplication, node-distance buckets, episode-mention
  sort semantics, and final hit cloning when changing those reference-backend paths.
- Search fallback in-memory snapshot projection now uses explicit typed loops over cloned driver
  snapshots instead of `OfType`/`Select` chains. Preserve the clone isolation, type filtering,
  embedding stripping flags, stable order, and `IReadOnlyList<EntityEdge>` endpoint lookup shape.
- Extraction JSON parsing now uses stable key arrays and explicit `JsonArray` loops in
  `Graphiti.ExtractionParsing`. Future parser cleanup can continue into `NodeResolutionService`, but
  must preserve key priority, non-object skipping, JSON text fallback, numeric-string coercion, and
  DTO/cache identity.
- Attribute extraction target/context construction now uses loop-built first-wins maps, target
  buffers, schema caches, JSON string arrays, and type-map membership checks. Preserve prompt JSON
  shape, sorted schema/attribute order, schema reuse, bounded concurrency, and case-insensitive
  ontology lookup if changing `AttributeExtractionService`, `EntityTypeResolver`, or
  `ExtractionContextBuilder`.
- Add provider boundaries only where they preserve Graphiti semantics and help LadybugDB or parity
  testing.
- Optional search provider candidates include Neo4j indexes and Azure AI Search behind explicit
  configuration, but they are not current roadmap priorities.
- Optional vector-provider candidates include `Microsoft.Extensions.VectorData` or Qdrant behind a
  Graphiti-owned semantic boundary.
- Do not make Lucene.NET a default core dependency.
- Do not replace Neo4j driver code with an OGM.

## Provider And Infrastructure Work

- Continue using `Microsoft.Extensions.AI` for chat and embeddings.
- Keep provider SDKs in optional packages or host configuration helpers where possible.
- Preserve `ILLmClient` and `IEmbedderClient` compatibility while adapters mature.
- Use `HybridCache` for expensive deterministic LLM responses.
- Keep Polly resilience pipelines around provider calls and allow host apps to replace pipelines.
- OpenTelemetry coverage now includes search scope, retrieval-driver, async reranker, ingestion
  extraction/resolution/attribute aggregate spans, ingestion graph writes, and
  Microsoft.Extensions.AI provider-call attempts for chat and embeddings. Neo4j session executor
  read/write/write-scalar/write-batch spans cover the main lower-level graph-provider boundary.
  Future telemetry work should be driven by concrete gaps rather than adding broad span volume.
- Use `Microsoft.ML.Tokenizers` for known model tokenization and keep a heuristic fallback.
- Use tensor primitives for vector math optimizations only after parity-sensitive tests are in place.
  L2 normalization and cosine similarity now use tensor primitives with parity guards.
- Consider JSON schema libraries for structured-output validation if the existing validator becomes
  too limited. Prefer System.Text.Json-aligned libraries.

## Performance And Allocation Work

- Treat performance and unnecessary allocations as first-class C# port concerns, not a separate
  polish phase. This especially applies to ingestion/extraction parsing, search ranking/fusion,
  fallback text scoring, graph-driver mapping, serialization/cache keys, vector math, and provider
  adapters.
- Prefer allocation-light implementation shapes that are still readable: explicit single-pass loops,
  pre-sized buffers, non-throwing parse helpers, source-generated serializers/logging/regexes, and
  span-friendly helpers where they fit naturally.
- Be cautious with implicit allocation sources before accepting a modern-looking refactor: LINQ
  projection/sort chains on hot paths, closure captures, iterator materialization, regex/split-array
  helpers, boxing through interface-heavy paths, and serialize-to-string-then-reparse workflows.
- Do not chase micro-optimizations in cold code or change public behavior for performance. Preserve
  Python parity, deterministic ordering, cancellation points, telemetry safety, and maintainability;
  add focused tests or benchmarks when a performance-sensitive rewrite could regress behavior.
- Bulk edge invalidation snapshots now replace stale graph/search copies with in-batch snapshot
  overrides by UUID while preserving graph-first ranking/limit behavior, related-edge offsets,
  ordered repeated-episode semantics, and canonical coalescing.
- Search orchestration method plumbing now avoids disabled `Task.FromResult(new List<...>())`
  placeholders and per-method two-task list allocations by using nullable task locals, typed empty
  ranked arrays, and a two-task await helper. Focused driver-backed tests pin first-fault rethrow,
  sibling cancellation on faults, cancellation-only behavior without internal sibling cancellation,
  disabled node/edge/community driver calls, and the existing `SearchRetrievalRunner` telemetry
  boundary.
- Direct ranked-list composition in `SearchResultComposer`/`SearchUtilities` now avoids the small
  `IReadOnlyList[]` allocations that previously connected `SearchEngine` branches to fusion/merge.
  Tests pin one-list RRF, two-list RRF/merge, three-list parity, and the existing RRF/merge ordering
  semantics.
- Good next allocation slice from the 2026-06-01 scans: carefully tested top-level `SearchAsync`
  scope orchestration cleanup. Preserve first-fault rethrow, sibling scope cancellation on faults,
  cancellation-only behavior, result assignment order, telemetry boundaries, and query-vector
  materialization timing before changing behavior-sensitive async coordination.

## Graphiti Decomposition

Completed decomposition should be preserved:

- `Graphiti` remains the public orchestrator.
- `Graphiti.*.cs` partials own public-facing areas.
- Stateless helpers live under `Internal/`.
- Stateful services own saga, community, attribute extraction, edge resolution, episode graph
  extraction, and node resolution behavior.

Future decomposition should keep public APIs, telemetry names/tags, active-driver scoping, prompt
names, and LLM response-model identities stable.

## Keep Custom

These should remain custom unless the user explicitly chooses a different product direction:

- Episode ingestion workflow and public `Graphiti` behavior.
- Entity, episode, edge, community, and search result models.
- Temporal fact invalidation and edge validity windows.
- Graph driver semantic contract.
- Search configuration names and merge semantics.
- RRF, MMR, cross-encoder result ordering, node-distance, and episode-mentions semantics.
- Prompt/result DTO compatibility with Python.
- In-memory reference driver.

## Future Decisions To Revisit

- Whether to support Neptune later. Current decision: not implemented, enum kept for compatibility.
- Whether to expose core LadybugDB drivers for OpenAI, Azure OpenAI, Azure AI Search, Qdrant, or
  Semantic Kernel adapters.
- Whether strict byte-for-byte Python query compatibility is ever needed for Lucene group filtering.
- Whether to add a compatibility option that defaults chunking to the Python chars-per-token
  heuristic instead of tiktoken.
