# C# Port Roadmap

This roadmap lists open or recurring work for the C# port. Keep completed slice history out of this
file; use `evolution.md` for milestone history and `handoff.md` for current-state context.

## Near-Term

1. Verify the current tree before relying on older checkpoints.

   The C# submodule has had parallel work and local-only commits. Run the relevant restore/format/
   build/test pass before claiming the tree is green or red.

2. Continue LadybugDB provider work.

   LadybugDB is the provider investment target and is integrated into `Graphiti.Core`. Current
   provider status and remaining work live in `kuzu-driver-port.md`.

3. Keep modernization Graphiti-shaped.

   Use modern .NET where it improves correctness, operability, or measured/simple performance at
   existing Graphiti boundaries. Avoid abstraction churn, broad framework replacement, or provider
   work that does not serve LadybugDB or parity.

4. Finish low-risk XML documentation gaps.

   Remaining gaps are mostly internal utilities, deeper namespace member docs, and internal Graphiti
   helper details. Avoid touching hot extraction files when another agent is active there.

5. Add isolated parity tests when touched behavior is thin.

   Most common helpers already have direct coverage. Add focused tests when changing provider query
   helpers, prompt/DTO parsing, search ranking, ingestion edge cases, or any allocation-sensitive path
   where behavior could regress.

6. Revisit analyzer suppressions only if they accumulate again.

   Namespace/folder alignment is currently enforced. If future suppressions spread across many files,
   centralize them deliberately instead of adding one-off pragmas.

## Driver And Provider Direction

- LadybugDB is the main/default provider target.
- Kuzu remains the Python parity lineage and compatibility vocabulary until naming is settled.
- `GraphProvider.Kuzu` is supported by core options validation and resolves to the LadybugDB-backed
  driver.
- `Graphiti.Core` owns the LadybugDB package references, native dependency boundary, factory helpers,
  host-facing options, and integration proof.
- InMemory is a deterministic reference/test driver. Keep it correct and deterministic, but do not
  polish it as a product provider unless tests or LadybugDB-facing abstractions require it.
- Neo4j may remain only as existing/reference behavior while present; avoid new investment because it
  is expected to be removed later.
- FalkorDB is not a C# provider investment target.
- Neptune is enum/wire compatibility only unless a separate decision changes that.

## Search And Retrieval Direction

- Preserve the current split: `SearchEngine` orchestrates, `SearchRetrievalRunner` delegates
  driver-backed retrieval, and `SearchResultComposer` owns ranking/fusion/result shaping.
- Keep Graphiti ranking/search semantics custom and parity-tested: RRF, MMR, cross-encoder ordering,
  node-distance, episode-mentions, filters, BFS, and result merge behavior.
- Delegate provider-backed full-text/vector behavior to graph providers where a provider supports it.
  Keep fallback materialization deterministic and suitable for tests/reference paths.
- Do not make Lucene.NET a default core dependency.
- Do not replace Neo4j driver code with an OGM while Neo4j remains present.

## Provider And Infrastructure Work

- Continue using `Microsoft.Extensions.AI` for chat and embeddings.
- Keep non-graph provider SDKs in external adapters or host configuration helpers where possible.
- Preserve `ILLmClient`, `IEmbedderClient`, and `ICrossEncoderClient` compatibility while adapters
  mature.
- Use `HybridCache` for expensive deterministic LLM responses.
- Keep Polly resilience pipelines around provider calls and allow host apps to replace pipelines.
- Use `ActivitySource` and source-generated logging where useful; future telemetry work should be
  driven by concrete gaps rather than broad span volume.
- Use `Microsoft.ML.Tokenizers` for known model tokenization and keep a heuristic fallback.
- Use tensor/vector primitives for low-level math when helpful, with parity-sensitive tests in place.
- Consider System.Text.Json-aligned schema libraries only if the existing structured-output validator
  becomes too limited.

## Performance And Allocation Direction

- Treat performance and unnecessary allocations as first-class C# port concerns, not a separate
  polish phase.
- Focus on hot/shared paths: ingestion/extraction parsing, search ranking/fusion, fallback text
  scoring, graph-driver mapping, serialization/cache keys, vector math, provider adapters, and
  high-frequency text utilities.
- Prefer readable allocation-light shapes: explicit single-pass loops, pre-sized buffers,
  non-throwing parse helpers, source-generated serializers/logging/regexes, span-friendly helpers, and
  direct provider memory copies where they fit naturally.
- Be cautious with implicit allocation sources before accepting a modern-looking refactor: LINQ
  projection/sort chains on hot paths, closure captures, iterator materialization, regex/split-array
  helpers, boxing through interface-heavy paths, and serialize-to-string-then-reparse workflows.
- Do not chase micro-optimizations in cold code or change public behavior for performance. Preserve
  Python parity, deterministic ordering, cancellation points, telemetry safety, and maintainability.

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

- Whether to add a distinct LadybugDB provider enum/name beyond the current Kuzu compatibility value.
- Whether to support Neptune later. Current decision: not implemented, enum kept for compatibility.
- Whether to expose external adapters for OpenAI, Azure OpenAI, Azure AI Search, Qdrant, or Semantic
  Kernel.
- Whether strict byte-for-byte Python query compatibility is ever needed for Lucene group filtering.
- Whether to add a compatibility option that defaults chunking to the Python chars-per-token
  heuristic instead of tiktoken.
