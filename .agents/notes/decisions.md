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
- The two shippable package projects (`Graphiti.Core` and `Graphiti.Core.Drivers.Ladybug`) generate
  XML documentation files and `.snupkg` symbol packages. With warnings treated as errors, missing or
  broken XML docs on their public surface are build failures; tests/samples do not enable package XML
  generation. `PackageReadinessTests` guards shared NuGet metadata, README packing, symbol settings,
  same-version alignment, the two-project `Verify-GraphitiCore.ps1` pack loop, and the strict package
  consumer smoke path.

## Public API surface (plan 05, 2026-06-14)

The public surface is guarded by a snapshot test (`tests/Graphiti.Core.Tests/Api/`, via
`PublicApiGenerator`); any change fails CI and must regenerate `Graphiti.Core.approved.txt`
deliberately. Standing decisions from the plan-05 batch-1 cleanup (alpha-stage breaking changes;
no wire/prompt/cache/temporal behavior changed):

- `Graphiti` constructor driver selection precedence: explicit `graphDriver` > non-null `uri`
  (builds Neo4j) > **InMemory default**. It no longer throws when both are omitted, so `new Graphiti()`
  backs onto the deterministic reference driver. `AddEpisodeAsync(AddEpisodeOptions, ct)` is an additive
  overload over the 15-parameter positional one (which is unchanged).
- Driver-facing provider value is `GraphProvider.LadybugDb` (new ordinal `5`); `GraphProvider.Kuzu` is an
  `[Obsolete]` alias that still resolves to the LadybugDB driver. The DI method is `AddGraphiti`;
  `AddGraphitiCore` is an `[Obsolete]` alias. `GraphProvider` is not wire/cache-serialized (only an OTel
  tag), so renaming is safe; existing ordinals are pinned. FalkorDb/Neptune stay as wire-compat members.
- Obsoletions use custom `DiagnosticId`s (`GRPH0001` provider, `GRPH0002` DI method). `GRPH0001` is
  suppressed only at deliberate Kuzu compatibility-alias sites, so external package consumers still
  receive the provider obsoletion. `GRPH0002` is also suppressed only at the deliberate
  `AddGraphitiCore` compatibility-alias test; ordinary in-repo callers use `AddGraphiti`.
- The accidental public surface was reduced: `SearchEngine`, `SearchUtilities`,
  `SearchFilterQueryBuilder`, `MaintenanceUtilities`, `StructuredResponseValidator`, and the
  enum-extension helpers are now `internal` (port artifacts; tests reach them via `InternalsVisibleTo`).
  `TokenUsage` setters are `init`; `GraphitiJsonSerializer.Options`, `GraphitiTelemetry.ActivitySource`,
  and `ContentChunking.TokenCounter` are get-only (token-counter selection is the DI/`IContentChunker`
  path). `GraphDriverBase`, the `*Namespace` facades, `EpisodeTypeExtensions` (wire-value helpers), and
  the schema/cache-identity DTOs stay public.
- `Graphiti.Core` and `Graphiti.Core.Drivers.Ladybug` ship IntelliSense XML docs from their public XML
  comments. Keep docs complete when adding public members to either package.
- **LadybugDB is a separate package.** `Graphiti.Core` carries only the driver contract (`IGraphDriver`,
  `GraphProvider`, `GraphDriverBase`, `InMemoryGraphDriver`, `Neo4jGraphDriver`) and depends only on
  nuget.org packages — it restores off-machine without the local Ladybug feed. The LadybugDB driver,
  `LadybugDbOptions`, `AddLadybugDbGraphDriver`, and `LadybugDbGraphDriverFactory` live in
  `src/Graphiti.Core.Drivers.Ladybug/` (which owns the `LadybugDB`/`LadybugDB.Native` package refs).
  Core resolves `GraphProvider.LadybugDb`/`Kuzu` via `GraphitiOptions.GraphDriverFactory` (set by
  `AddLadybugDbGraphDriver`) and throws a clear `InvalidOperationException` if the package is not
  referenced/registered. `Graphiti.Core` adds `InternalsVisibleTo("Graphiti.Core.Drivers.Ladybug")`; the
  Ladybug package adds `InternalsVisibleTo("Graphiti.Core.Tests")`. The public-API snapshot guards both
  assemblies. A real off-machine release still requires the local `0.17.0-alpha.2-graphiti.1` LadybugDB
  package family to be published/replaced (plan 05 Step E.2; `kuzu-driver-port.md`).

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
  investment target; its package/native references and driver are owned by the separate
  `Graphiti.Core.Drivers.Ladybug` project (see plan-05 Step E split above), not `Graphiti.Core`.
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
- Kuzu-compatible full-text query construction matches Python's `fulltext_query` KUZU branch
  (`search_utils.py:88-92`) exactly inside the active Ladybug driver: it counts words by splitting on
  a single space, returns an empty string (no search) when the count exceeds
  `SearchUtilities.MaxQueryLength` (128), and otherwise returns the query **verbatim** with no
  whitespace normalization or per-term truncation. (Corrected 2026-06-13: an earlier note and the
  original `LadybugFulltextQuery` wrongly collapsed whitespace and truncated over-limit queries while
  still searching; Python rejects over-limit queries outright.) The compatibility logic lives in
  `Drivers/Ladybug/LadybugFulltextQuery`; shared `SearchUtilities` no longer has a
  `GraphProvider.Kuzu` branch because no configured driver self-reports Kuzu.
- LadybugDB/Kuzu foundation uses the full C# `SagaNode` model shape for Saga schema/save/get,
  includes entity-edge `reference_time` in save/get/search projections, and returns entity edges
  incident to either endpoint from `GetEntityEdgesByNodeUuidAsync`. This deliberately fixes Python
  Kuzu operation mismatches so the C# runtime path can persist and read Graphiti's model fields
  consistently.
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

## Deliberate divergences accepted in the 2026-06-13 parity-hardening pass

A supervisor-driven adversarial review of the 2026-06-11 agent work found two bulk-ingestion
behaviors where C# deliberately differs from Python. They are KEPT as intentional divergences (not
defects) because each is a defensible improvement, and they are recorded here so they are not later
"corrected" toward Python or mistaken for accidental drift. **Decision finalized 2026-06-13:** the
user reviewed the flag and opted to proceed; these two stay as documented divergences. If a future
product decision prefers strict Python-bulk parity, align them and update this note plus `parity.md`.

- **Bulk cross-episode edge invalidation is more aggressive than Python.** In `add_episode_bulk`,
  C# threads each episode's freshly resolved edges back as `existingEdgesOverride` so a later episode
  in the same batch can dedupe/contradict an earlier episode's edge. Python's `_resolve_nodes_and_edges_bulk`
  passes no override and resolves episodes in parallel against the live (not-yet-written) graph, so
  its bulk path applies cross-episode temporal invalidation *less* aggressively (Python's own bulk
  docstring concedes this). C# is the stronger behavior; it is pinned by
  `AddEpisodeBulk_DoesNotReinvalidateAlreadyInvalidatedSnapshotEdge`. Note: override edges feed only
  the duplicate/invalidation-candidate *resolution*, not a Python-absent invalidation-candidate
  *search* — that separate over-reach was removed in the hardening pass.
- **Bulk episodes own their entity edges (`episode.EntityEdges`); Python's bulk leaves it empty.**
  C# populates `episode.EntityEdges` in the bulk path (as the single-episode path and Python's single
  `add_episode` do), so bulk-ingested episodes report and remove their edges consistently. Python's
  `add_episode_bulk` never sets `entity_edges`, so bulk episodes there own no edges for
  `remove_episode`/`get_nodes_and_edges_by_episode`. C# deliberately makes bulk consistent with
  single-episode removal semantics rather than replicating Python's bulk/single inconsistency.

Other accepted, smaller divergences confirmed in the same pass:

- The `MicrosoftExtensionsAICrossEncoderClient` user prompt adds one sentence — "Return your decision
  and confidence as JSON matching the provided schema." — absent from Python's `openai_reranker_client`.
  This is a necessary consequence of the already-documented structured boolean+confidence scoring
  divergence (generic M.E.AI lacks OpenAI top-logprob controls); the system prompt and first user
  sentence remain verbatim.
- Refusal detection is best-effort: M.E.AI exposes no structured refusal field, so only a
  `ChatFinishReason.ContentFilter` is surfaced as the non-retryable `LlmRefusalException` (mirroring
  Python's non-retried `RefusalError`). A textual refusal returned with a normal finish reason cannot
  be distinguished and is retried like any malformed response.

## Tracked-but-unfixed divergences (low impact / latent; from the 2026-06-13 review)

These were confirmed real but left as-is, with a rationale. Revisit if the relevant path is wired or
the impact grows.

- **Multi-episode attribution internals** diverge from Python in several latent ways (reference-time
  first-valid vs first-element, resolved-vs-extracted index-map remap, dropped-duplicate episode
  merging). Unreachable today: both Python and C# extract exactly one episode per extraction call, so
  every path reduces to a single episode index `[0]`. If multi-episode extraction is ever wired,
  port `node_operations.py:104-112` / `edge_operations.py:170-181` attribution blocks and reconcile
  the helpers in `EpisodeAttribution.cs`.
- **A single `now` is shared across a batch's edges** rather than Python's per-edge `utc_now()`;
  expired_at/invalid_at values are identical within a batch (arguably preferable for determinism).
- **Ladybug distance/mention rerankers** dedup input UUIDs and would surface backend-only UUIDs; not
  reachable through the real per-UUID Cypher (which constrains `n.uuid = $node_uuid`). A test enshrines
  the divergent shape using impossible foreign rows — test hygiene, not a production defect.
- **Ladybug `RetrieveEpisodes` returns oldest-first**, matching the documented `retrieve_episodes`
  contract (`graph_operations.py:223`, `graph_data_operations.py` `list(reversed(...))`); Python's
  Kuzu operations-interface path returns newest-first, violating its own contract. C# is the
  defensible choice; do not "fix" it toward newest-first.
- **Combined-edge parsing skips edges with a missing `relation_type`** (closed/aligned with Python).
  `Graphiti.ExtractionParsing.cs` reads `relation_type` (or the C#-lenient `name` alias) and only emits
  an `ExtractedEdge` when source, target, and relation are all non-blank; it never fabricates a
  `"RELATES_TO"` relation, matching Python's rejection of the required Pydantic field
  (`extract_edges.py`/`extract_nodes_and_edges.py`) and the no-fabrication parity contract. Resolution
  of the kept edges then proceeds through `EdgeResolutionService.cs`.

## Deliberate divergences from the 2026-06-14 upstream sync

The 5 `graphiti_core` commits added upstream between anchor `34f56e6` and `origin/main` `ff7e29c`
were reviewed (full per-commit disposition in `parity.md`). Two are deliberate divergences:

- **Default LLM model stays `gpt-4.1-mini`** (upstream #1551 moved Python's default to `gpt-5.5` with
  reasoning effort `'none'`). C# does **not** follow. Reasoning-effort and temperature-omission for
  reasoning models are not Graphiti's concern in the C# architecture — they live in the consumer's
  `Microsoft.Extensions.AI` chat client. Defaulting to `gpt-5.5` without C# emitting
  `reasoning_effort:'none'` would let the API apply its *medium* default reasoning (expensive/slow) —
  the opposite of Python's intent. `gpt-4.1-mini` is a supported non-reasoning model and the safe,
  cheap default. Consumers wanting `gpt-5.5` should configure reasoning off on their M.E.AI client.
- **Kuzu is NOT deprecated** (upstream #1548 deprecated the Kuzu backend because the *upstream Kuzu
  project* is unmaintained). The C# port's primary provider is **LadybugDB**, a maintained
  Kuzu-lineage engine we build/repair locally; the deprecation rationale does not apply. No
  `DeprecationWarning`/`[Obsolete]` on the Ladybug driver. (`GraphProvider.Kuzu` is an `[Obsolete]`
  alias of `LadybugDb` for an unrelated naming reason — plan 05 B.)

The other three: the FalkorDB default-`group_id` fix (#1549) was adopted (one-char
`GetDefaultGroupId` change); the generic-client structured-output rework (#1537) is N/A (M.E.AI is the
sole adapter) with its empty-response-is-retryable bit already matched by C#; the FalkorDB nul-byte
strip (#1531) is N/A (no FalkorDB driver).

## Provider Status

- LadybugDB is the primary graph provider target for the C# port. It is the package/backend we will
  invest in; its package refs and driver live in the separate `Graphiti.Core.Drivers.Ladybug` project
  (plan-05 Step E split above), while `Graphiti.Core` carries only the driver contract.
- The LadybugDB provider uses the LadybugDB NuGet package, which comes from the alternative Kuzu fork.
  Kuzu remains the Python parity lineage and compatibility vocabulary, while the driver-facing provider
  name is `GraphProvider.LadybugDb`. See `kuzu-driver-port.md`.
- `GraphProvider.Neo4j` and `GraphProvider.InMemory` may remain in the current provider surface for
  now. Keep existing Neo4j behavior from regressing, but do not plan provider improvements there:
  Neo4j is expected to be removed, and InMemory is a reference/test driver rather than a product
  provider. `GraphProvider.FalkorDb` and `GraphProvider.Neptune` remain enum/helper compatibility
  surfaces and are rejected by default options validation unless a separate provider decision changes
  that. LadybugDB is the provider path to invest in.
- `GraphProvider.Kuzu` is a valid obsolete compatibility alias in core DI/options and, when
  `AddLadybugDbGraphDriver` is registered, resolves to the LadybugDB-backed driver. The concrete
  driver reports `GraphProvider.LadybugDb`; file persistence is configured through
  `LadybugDbOptions.DatabasePath`, with the Python Kuzu `':memory:'` sentinel normalized by the
  Ladybug driver factory. See `kuzu-driver-port.md` for current runtime coverage.
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
