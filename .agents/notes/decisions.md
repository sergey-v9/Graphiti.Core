# C# Port Decisions

These notes capture stable decisions for the C# Graphiti Core port. Python `graphiti_core/` remains
the behavioral source of truth, but the C# port should be idiomatic .NET where that does not break
Graphiti semantics, wire compatibility, or performance/allocation discipline.

## Porting Direction

- The C# port aims for functional parity with Python, not line-by-line translation.
- Keep Graphiti's temporal graph domain behavior custom: episode ingestion, extraction,
  deduplication, invalidation windows, search merge semantics, and graph driver contracts are the
  product.
- Replace commodity provider plumbing with maintained .NET infrastructure where it reduces custom
  code without changing Graphiti behavior.
- Modern .NET direction includes efficient code shape, not just newer abstractions. Prefer APIs and
  patterns that reduce unnecessary work and implicit allocations, especially in ingestion, search,
  parsing, serialization, embedding/vector, and provider hot paths.
- If a local `.agents/skills/*/SKILL.md` corpus is present, use those files as specialist C#/.NET
  references when the task matches them, but do not let generic skill guidance override
  Graphiti-specific port decisions.
- Use idiomatic C# public APIs. Old Python-style public aliases such as `Search_Async` and
  `DEFAULT_SEARCH_LIMIT` were removed; parity is preserved through behavior and wire values instead.
- Preserve Python wire values for serialized enums and configuration names, such as `fact_triple`
  and `reciprocal_rank_fusion`.

## Project Shape

- Target framework is currently `net10.0`.
- Nullable reference types are enabled.
- Warnings are treated as errors.
- Analyzer namespace/folder matching is enforced.
- Source files are organized into folder-aligned namespaces such as `Graphiti.Core.Drivers`,
  `Graphiti.Core.Search`, `Graphiti.Core.Models`, `Graphiti.Core.LlmClients`, and
  `Graphiti.Core.Embedding`.
- Root orchestration files stay in `Graphiti.Core`, especially `Graphiti.cs`, `Graphiti.*.cs`
  partials, and `Errors.cs`.
- Prefer one public type per file.
- XML comments are useful but missing-doc warnings are not enabled; add comments incrementally where
  they improve the public surface.

## Library Boundaries

- `Graphiti` should remain the main public orchestrator.
- Internal collaborators are preferred over growing `Graphiti.cs`: current services include
  `SagaService`, `CommunityService`, `AttributeExtractionService`, `EdgeResolutionService`,
  `EpisodeGraphExtractor`, and `NodeResolutionService`.
- Response DTOs used for LLM structured output may remain nested under `Graphiti` when type identity
  affects response-format names, schema fingerprints, or cache keys.
- `AsyncLocal<IGraphDriver?>` preserves active driver scoping for group-specific operations.
- Keep the deterministic `InMemoryGraphDriver` as a reference implementation and test backend; use
  real set collections for secondary index buckets and explicit ordering where index enumeration can
  affect public results.
- DI-created supported graph drivers should honor `GraphitiOptions.Database`; for InMemory the
  database label remains distinct from `DefaultGroupId`.
- Keep the official `Neo4j.Driver`; improve Cypher statement construction, session execution, and
  record mapping through internal helpers rather than replacing it with an OGM.

## Optional Local Skill Use

- These rules apply when the corresponding optional `.agents/skills` files exist in the checkout.
- Before changing test execution commands or filters, read `run-tests/SKILL.md`.
- Before writing or modernizing MSTest tests, read `writing-mstest-tests/SKILL.md`; use the test
  quality, coverage, and mutation-analysis skills only when that is the actual task.
- Before making performance claims from code inspection, read
  `analyzing-dotnet-performance/SKILL.md`; before adding BenchmarkDotNet benchmarks, read
  `microbenchmarking/SKILL.md` and the reference files it requires.
- Use the MCP, NuGet, P/Invoke, and file-based C# app skills only for those concrete task areas.
- Treat `technology-selection/SKILL.md` as broad .NET AI/ML background, not a product mandate. For
  Graphiti Core, `Microsoft.Extensions.AI` remains the adapter boundary; Microsoft Agent Framework,
  Semantic Kernel, RAG/vector-store abstractions, or MCP patterns are not replacements for Graphiti's
  temporal graph behavior.

## Provider And Infrastructure Choices

- Use `Microsoft.Extensions.AI` as the primary adapter boundary for chat and embeddings.
- Keep `ILLmClient`, `IEmbedderClient`, and `ICrossEncoderClient` as Graphiti-facing abstractions.
- Use official provider SDKs behind adapters or optional integration packages rather than hard-coding
  providers into core.
- Use `HybridCache` for LLM response caching and preserve cache-key semantics for parity-sensitive
  structured calls.
- Keep LLM cache storage as `JsonObject` payloads serialized with `GraphitiJsonSerializer.Options`
  through the shared cache payload helper. Cache implementations may repair invalid stored strings,
  but must not change cache-key inputs, prompt/schema identity, or JSON wire shape as incidental
  cleanup.
- Use typed/source-generated payloads for deterministic internal serialization paths such as LLM
  cache keys when possible, but preserve property order, JSON names, and hash bytes.
- Use Polly `ResiliencePipeline<T>` for provider retry/backoff/timeout behavior.
- Use `ActivitySource` and standard logging patterns; hosts choose exporters.
- Shared concurrency helpers should fail fast on invalid throttling settings and observe pre-canceled
  tokens before launching work.
- Bulk graph writes should observe cancellation before materializing caller-provided enumerables and
  between save/embedding phases, so canceled ingestion does not spend work enumerating later phases.
- Use `Microsoft.ML.Tokenizers` for default model-aware token counting, while preserving
  `HeuristicTokenCounter` for Python-style chars-per-token behavior.
- Use tensor/vector primitives for low-level math when helpful, but keep Graphiti ranking algorithms
  custom and parity-tested. Reject non-finite embedding values at Graphiti-owned embedding boundaries
  before persistence or ranking.
- Use an internal BM25 scorer for in-memory/materialized fallback full-text search. Do not add
  Lucene.NET as a default core dependency for this path.

## Performance And Allocation Direction

- Treat avoidable allocations as design feedback in shared or repeated paths. This includes hidden
  allocations from LINQ chains, iterator/async state machines, closure captures, interface
  enumeration over value types, regex/split-array helpers, exception-driven control flow, and
  serialize-to-string-then-parse workflows.
- Prefer explicit loops, pre-sized collections, `Span`/`ReadOnlySpan`-friendly helpers,
  source-generated serializers/logging/regexes, non-throwing parse methods, and single-pass
  transformations when they keep the code readable and tested.
- Do not overfit cold administrative code or public API examples for micro-allocations. The priority
  is allocation-aware default implementation code on hot/shared paths without sacrificing Graphiti
  parity, deterministic ordering, cancellation behavior, or maintainability.
- Adding a modern .NET library is only a win when it improves correctness, interoperability,
  operability, or measured/simple performance. Avoid dependency or abstraction churn that makes the
  code fancier while adding allocations, indirection, or provider lock-in.

## Parity Decisions

- `LuceneSanitize` intentionally preserves Python's escape behavior, including escaping individual
  uppercase `O`, `R`, `N`, `T`, `A`, and `D` characters.
- Lucene group filters are parenthesized in C# even though Python's flat string shape can let the
  first group match without the query text. This is an intentional semantic hardening.
- Property filters are enforced in C# even though Python currently exposes the field without using
  it. Treat this as an intentional C# feature.
- Date-filter Cypher uses unique parameter names across OR branches; Python's reset-per-branch
  behavior can collide.
- Kuzu full-text query construction follows Python's raw-whitespace semantics: it splits on
  whitespace and truncates to the first `SearchUtilities.MaxQueryLength` words instead of using
  Lucene/Falkor sanitization or rejecting over-limit queries.
- LadybugDB/Kuzu foundation uses the full C# `SagaNode` model shape for Saga schema/save/get and
  includes entity-edge `reference_time` in save/get/search projections. This deliberately fixes
  Python Kuzu operation mismatches so the C# runtime path can persist and read Graphiti's model
  fields consistently when backend wiring lands.
- Real tiktoken-based chunking is the default, but callers can register `HeuristicTokenCounter(4)`
  when they need Python's exact token estimate.
- Saga summaries hard-truncate like Python. Community/entity summary paths keep sentence-aware
  truncation where Python does.
- Saga summarization treats a missing or blank typed LLM summary the same as an unavailable typed
  LLM response and falls back to deterministic episode-content concatenation.
- Structured-output prompts include the JSON schema text in the final prompt message and may also
  pass response-format metadata to the provider. Source-generated JSON metadata may cover nested
  `Graphiti.*Response` DTOs, but DTO type identity and snake_case schema/wire names must stay stable.
- Token usage tracking keeps the idiomatic C# `InputTokens`/`OutputTokens` totals, and also exposes
  Python-equivalent per-prompt `CallCount`, `AvgInputTokens`, and `AvgOutputTokens` values.

## Provider Status

- LadybugDB is the primary graph provider target for the C# port and should become the default first
  provider option once implemented. It is the package/backend we will invest in.
- The LadybugDB provider should use the LadybugDB NuGet package, which comes from the alternative
  Kuzu fork. Kuzu remains the Python parity lineage and compatibility vocabulary, but the driver-facing
  provider name should move to LadybugDB as the port freezes. See `kuzu-driver-port.md`.
- `GraphProvider.Neo4j`, `GraphProvider.FalkorDb`, and `GraphProvider.InMemory` may remain in the
  current provider surface. Keep existing implemented behavior, but do not plan more Neo4j/FalkorDB
  improvements unless needed to validate shared abstractions or avoid regressions while building
  LadybugDB.
- LadybugDB/Kuzu foundation helpers may land before final provider naming/wiring decisions, but they
  must not make `GraphProvider.Kuzu` valid in core DI/options or imply full provider support until the
  end-to-end driver is proven. The optional `Graphiti.Core` package may own the concrete
  LadybugDB package adapter, DI helper, and searchable driver surface while core owns the LadybugDB dependency
  and `GraphProvider.Kuzu` remains unsupported by core provider validation. First factory-backed
  `Graphiti` ingest/search/removal, direct triplet, bulk duplicate-fact, and saga association
  workflows are proved, but broader workflow coverage and the driver-facing LadybugDB naming decision
  are still required before core provider wiring.
- LadybugDB package/backend behavior that appears buggy during driver implementation should be marked
  separately from C# port gaps. Work around proven backend limitations deliberately when useful, but
  keep them visible for later LadybugDB fixes.
- Neptune is not implemented in the C# port and remains present only for enum/wire compatibility
  unless a separate decision changes that.

## Replacement Policy

Do not replace Graphiti with Semantic Kernel memory, Microsoft Agent Framework, LangChain-style
memory, or a vector DB memory abstraction. Adapters are fine. Replacement is not.

The dependency strategy is to make Graphiti Core a modern .NET library internally while preserving
the temporal graph behavior that makes Graphiti distinct.
