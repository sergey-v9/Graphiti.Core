# C# Port Decisions

These notes capture stable decisions for the C# Graphiti Core port. Milestone history for major
divergences from Python lives in `evolution.md`. Python `graphiti_core/` remains the behavioral
source of truth, but the C# port should be idiomatic .NET where that does not break Graphiti
semantics, wire compatibility, or performance/allocation discipline.

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

## Prompt Parity Contract (added 2026-06-11)

- Python prompt instruction text in `graphiti_core/prompts/` is product behavior, the same way
  ranking math is. Porting a prompt means transcribing its system and user text near-verbatim into
  a builder under `src/Graphiti.Core/Prompts/`, pinned by a golden full-text rendering test. The
  earlier scheme — a one-line system message plus the raw JSON data context — was scaffolding and
  is being replaced (plan 01); do not introduce new call sites that follow it.
- Allowed mechanical divergences from Python rendering, and only these: collections interpolate as
  compact JSON via `PromptJson.Serialize` (Python uses `json.dumps` spacing or repr); timestamps
  render ISO-8601 round-trip format; em-dash may render as a plain hyphen; insignificant trailing
  whitespace before a final newline is not reproduced; dictionary-ordered collections may render in
  deterministic sorted order where Python relies on insertion order. Anything else requires a
  recorded decision here.
- `promptName` strings, response DTO identity, and cache-key inputs stay stable when prompt text
  changes; prompt content is deliberately part of the LLM cache key, so content changes invalidate
  cached responses — that is correct behavior, not a regression.
- Do not add LLM-failure fallbacks that fabricate graph content (heuristic entity names, synthetic
  edges). Empty extraction results are valid outputs. Deterministic text fallbacks are acceptable
  only where the LLM client explicitly reports the capability as unimplemented, never for real
  provider errors.

## Project Shape

- Target framework is currently `net10.0`.
- Nullable reference types are enabled.
- Warnings are treated as errors.
- Analyzer namespace/folder matching is enforced.
- Source files are organized into folder-aligned namespaces such as `Graphiti.Core.Drivers`,
  `Graphiti.Core.Search`, `Graphiti.Core.Models`, `Graphiti.Core.LlmClients`, and
  `Graphiti.Core.Embedding`.
- Root orchestration files stay in `Graphiti.Core`, especially `Graphiti.cs`, `Graphiti.*.cs`
  partials, and `Exceptions.cs`.
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
- Keep the deterministic `InMemoryGraphDriver` as a real reference driver and test backend. It
  implements broad persistence/search behavior and is useful for parity coverage, examples, and
  ephemeral graphs. It is not a product provider investment target, so avoid deep optimization or
  feature-polishing work unless that directly supports tests or LadybugDB.
- DI-created supported graph drivers should honor `GraphitiOptions.Database`; for InMemory the
  database label remains distinct from `DefaultGroupId`.
- Keep existing official `Neo4j.Driver` code working while it remains present, but do not invest in
  Neo4j beyond avoiding regressions. Neo4j is expected to be removed later, not polished into another
  first-class C# provider.

## Provider And Infrastructure Choices

- Use `Microsoft.Extensions.AI` as the primary adapter boundary for chat and embeddings.
- Keep `ILlmClient`, `IEmbedderClient`, and `ICrossEncoderClient` as Graphiti-facing abstractions.
- `IdentityCrossEncoderClient` remains the C# constructor/DI default so Graphiti Core works without
  an external provider. This deliberately differs from Python's default `OpenAIRerankerClient`.
  Real-provider hosts should opt into `MicrosoftExtensionsAICrossEncoderClient`; the OpenAI sample
  does this. The M.E.AI cross-encoder uses a structured boolean+confidence response because generic
  M.E.AI does not expose OpenAI top-logprob controls needed for Python's exact reranker scoring.
- Use official provider SDKs behind adapters where possible. LadybugDB is the core graph-provider
  investment target and its package/native references are owned by `Graphiti.Core`.
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

- **Status 2026-06-11: paused.** Allocation/performance rework is on moratorium until roadmap
  Phases 1–3 (prompt parity, pipeline parity, real-provider validation) are complete; it resumes
  benchmark-first in Phase 5. The guidance below applies to writing new code and to that future
  phase — it is not a license to refactor existing code now.
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
- Kuzu-compatible full-text query construction follows Python's raw-whitespace semantics: it splits
  on whitespace and truncates to the first `SearchUtilities.MaxQueryLength` words instead of using
  Lucene/Falkor sanitization or rejecting over-limit queries. Active Ladybug search owns this behavior
  in `Drivers/Ladybug/LadybugFulltextQuery`; `SearchUtilities` keeps the `GraphProvider.Kuzu` branch
  for compatibility callers outside the driver.
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
- Incremental community updates choose the mode community among neighboring entities. Ties keep the
  first community encountered from the neighbor traversal, matching Python's first-max behavior
  rather than sorting by UUID.
- Structured-output prompts include the JSON schema text in the final prompt message and may also
  pass response-format metadata to the provider. Source-generated JSON metadata may cover nested
  `Graphiti.*Response` DTOs, but DTO type identity and snake_case schema/wire names must stay stable.
- Token usage tracking keeps the idiomatic C# `InputTokens`/`OutputTokens` totals, and also exposes
  Python-equivalent per-prompt `CallCount`, `AvgInputTokens`, and `AvgOutputTokens` values.
- Combined node+edge extraction is ported as an internal `EpisodeGraphExtractor` path, but public
  `Graphiti` ingestion stays on separate node then edge extraction by default. The Python baseline
  exposes `use_combined_extraction` only as an internal bulk helper flag defaulting to `False`, not
  on `Graphiti.__init__`, `add_episode`, or `add_episode_bulk`; adding a C# public option or changing
  the default is a future product/API decision.
- Structured edge attributes are edge-resolution behavior, not a separate ingestion-stage pass.
  Preserve Python's distinction: exact duplicate edge reuse returns before the edge-attribute prompt
  and keeps existing attributes, while non-fast-path resolution may replace or clear attributes
  according to the matched custom edge type.

## Provider Status

- LadybugDB is the primary graph provider target for the C# port and is owned by `Graphiti.Core`.
  It is the package/backend we will invest in.
- The LadybugDB provider should use the LadybugDB NuGet package, which comes from the alternative
  Kuzu fork. Kuzu remains the Python parity lineage and compatibility vocabulary, but the driver-facing
  provider name should move to LadybugDB as the port freezes. See `kuzu-driver-port.md`.
- `GraphProvider.Neo4j` and `GraphProvider.InMemory` may remain in the current provider surface for
  now. Keep existing Neo4j behavior from regressing, but do not plan provider improvements there:
  Neo4j is expected to be removed, and InMemory is a reference/test driver rather than a product
  provider. `GraphProvider.FalkorDb` and `GraphProvider.Neptune` remain enum/helper compatibility
  surfaces and are rejected by default options validation unless a separate provider decision changes
  that. LadybugDB is the provider path to invest in.
- `GraphProvider.Kuzu` is valid in core DI/options and creates the LadybugDB-backed driver. It honors
  `GraphitiOptions.Database` for LadybugDB-backed file persistence. Kuzu remains the compatibility
  enum value until the final driver-facing LadybugDB naming decision is explicit. See
  `kuzu-driver-port.md` for current runtime coverage.
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
