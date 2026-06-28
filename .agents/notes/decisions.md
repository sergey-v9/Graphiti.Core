# C# Port Decisions

These notes capture stable decisions for the C# Graphiti Core port. Milestone history for major
divergences from Python lives in `evolution.md`. Python `graphiti_core/` remains the behavioral
source of truth, but the C# port should be idiomatic .NET where that does not break Graphiti
semantics, wire compatibility, or performance/allocation discipline.

## What this project is (paradigm — Sergey, 2026-06-27)

This decision frames everything else; read it first.

- **It is our own embeddable library, not a release-bound package and not a contribution back to
  upstream.** This is a C# system whose *behavior* is derived from Python `graphiti_core`, but it is
  **not** offered as "a second way to use Graphiti", is **not** going into the `getzep/graphiti` repo,
  and will most likely be **renamed**. It is a component **we** own and maintain, consumed **in-process
  as an embeddable library** — an internal subsystem of a larger system. There is no MCP server, no REST
  host, and no public distribution requirement.
- **Therefore: de-emphasize release/publishing.** Versioning, nuget.org publication, alpha→beta cadence,
  and metapackage shape are **not** current goals and must not drive work selection. (The work already
  done toward a clean, stable surface is still worth keeping — a tidy public API helps *our* consumers —
  but "ship it" is no longer the organizing principle.) Release publishing stays **user-gated** and is
  effectively parked.
- **Behavioral / feature parity with Python stays the functional contract**, and it is essentially
  complete (`parity.md`; upstream re-checked clean). We keep tracking upstream cheaply
  (`upstream-sync-procedure.md`) so we stay current, and we still match new Python functionality when it
  lands. Parity is the floor, not the forward agenda.
- **The forward agenda is the code itself: idiomatic modern C# + allocation/GC discipline.** Use the
  current language and runtime to their strengths (C# 14 / .NET 10 today, toward .NET 11 / C# next) where
  it genuinely improves the code, and drive down unnecessary allocations so we put less pressure on the
  GC — this is now a **first-class** goal, not an incidental nicety. Rules of engagement: behavior, wire
  values, structured-schema / cache identity, and public surface stay unchanged; idiomatic/readability
  changes must be warning-clean under `TreatWarningsAsErrors`; hot-path allocation changes stay
  **benchmark-first** (BenchmarkDotNet before/after, recorded baselines). See the modernization work
  order in `.agents/plans/` and the long-term framing in `roadmap.md`.

This supersedes the release-centric framing of the 2026-06-19 roadmap (G6) and re-prioritizes the
robustness stream (plan 09) below the modernization stream.

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

## Parity without Python coupling in the code (added 2026-06-17)

The product goal is **behavioral / feature / wire parity** with Python `graphiti_core` — same results,
same wire values, same cache/schema identity. That parity is enforced by **tests** and tracked in
**`parity.md`**. The C# code must read as a first-class .NET library, **not** as a transliteration log
of Python. Tight *textual* coupling to Python is not allowed in `src/` or `tests/`:

- **No "Python" (or a Python symbol / `.py` file) in any identifier** — method, class, property, field,
  or **test** name. Forbidden examples that exist today and must be renamed: `FormatPythonStringList`,
  `AppendPythonStringLiteral`, `IsUpperLikePython`, `ValidateGroupId_MatchesPythonSafeIdentifierRules`,
  `BuildExtractAttributes_RendersPythonParityPrompt`, `..._LikePython`. Name by **what the code does or
  asserts** in C# terms: `FormatStringList`, `IsUpper`, `ValidateGroupId_RejectsInvalidCharacters`,
  `BuildExtractAttributes_RendersExpectedPrompt`.
- **No comment that justifies behavior by citing Python** — not "mirrors Python", "like Python",
  "Python does X", "Python's …", nor a `graphiti_core/…py:NN` file/line citation. Comments explain the
  **behavior and intent** (e.g. "`expired_at` stays null when no candidate contradicts the edge"),
  never the provenance. Python file/line pointers rot and are noise.
- **Commit messages** describe the change and its behavior, not "like python".

Where Python provenance legitimately lives:
- **`parity.md`** is the single home for "what is ported and how it maps to Python" — put any Python
  file/line mapping in the parity row, not in code.
- A golden/parity test class may carry **one** generic summary line ("golden tests pin the rendered
  prompt; reconcile against `parity.md`") — but individual test names and inline comments stay
  Python-free.
- A deliberate difference from Python is a documented **DIVERGENT** decision in this file, referenced
  generically; the code comment states the C# behavior, the rationale lives in the decision.

Feature parity ≠ naming parity. The code was fully de-coupled on 2026-06-18 (rename/reword only; behavior
and golden strings unchanged). The only intentionally-remaining tokens are the string literals in
`UpstreamSyncProcedureTests` asserting the `Check-PythonUpstreamDelta.ps1` tracking script. **Do not
re-introduce** "Python" (or a `.py` file) into any identifier or comment.

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
- The single shippable package project (`Graphiti.Core`) generates XML documentation files and a
  `.snupkg` symbol package. With warnings treated as errors, missing or broken XML docs on its public
  surface are build failures; tests/samples do not enable package XML generation.
  `PackageReadinessTests` guards NuGet metadata, README packing, symbol settings, the one-project
  `Verify-GraphitiCore.ps1` pack loop, and the strict package consumer restore/build/setup/run smoke
  path.

## Public API surface (plan 05, 2026-06-14)

The public surface is guarded by a snapshot test (`tests/Graphiti.Core.Tests/Api/`, via
`PublicApiGenerator`); any change fails CI and must regenerate `Graphiti.Core.approved.txt`
deliberately. Standing decisions from the plan-05 batch-1 cleanup (alpha-stage breaking changes;
no wire/prompt/cache/temporal behavior changed):

- `Graphiti` constructor driver selection precedence: explicit `graphDriver` > **InMemory default**.
  It no longer throws when the driver is omitted, so `new Graphiti()` backs onto the deterministic
  reference/test driver. New product/backend work should pass a LadybugDB driver explicitly.
  `AddEpisodeAsync(AddEpisodeOptions, ct)` is an additive overload over the 15-parameter positional
  one (which is unchanged).
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
- `Graphiti.Core` ships IntelliSense XML docs from its public XML comments. Keep docs complete when
  adding public members.
- **LadybugDB is built into `Graphiti.Core` (plan 06, 2026-06-26).** `Graphiti.Core` carries the driver
  contract (`IGraphDriver`, `GraphProvider`, `GraphDriverBase`), deterministic `InMemoryGraphDriver`,
  first-class LadybugDB driver, `LadybugDbOptions`, `AddLadybugDbGraphDriver`, and
  `LadybugDbGraphDriverFactory`. It owns the `LadybugDB` / `LadybugDB.Native` package refs and restores
  through the `github_ladybug` feed. `GraphProvider.LadybugDb` and the obsolete `GraphProvider.Kuzu`
  alias resolve directly to the built-in LadybugDB driver; `AddLadybugDbGraphDriver` remains the
  ergonomic way to configure `LadybugDbOptions.DatabasePath` and select LadybugDB through
  `GraphitiOptions.GraphDriverFactory`. The public-API snapshot guards one assembly, and the retired
  `GraphitiCoreOnlyTests` mode / `eng\Verify-GraphitiCoreOnly.ps1` / `.github/workflows/core-only.yml`
  lane are intentionally gone.

  **RESOLVED (Sergey, 2026-06-17):** CI lanes stay (keep as-is, do not expand). The LadybugDB feed is
  **GitHub Packages only** (`sergey-v9/ladybug-dotnet`) — NO local offline fallback; a `github_ladybug`
  `read:packages` credential is required for any Ladybug-inclusive restore, and that is intentional.
  **Self-service bindings:** `sergey-v9/ladybug-dotnet` is our fork, so if the Ladybug driver needs a
  capability that already exists in the LadybugDB engine but is missing from the C# bindings, implement
  it in `tools/csharp_api`, commit, and **push to the fork** — its dev-packages workflow builds a new
  version that Graphiti consumes by bumping the pin in `Directory.Packages.props`. This **supersedes**
  the old "do not push the ladybug repo remotely / keep changes local-only" rule.
  The LadybugDB package refs restore from the `sergey-v9/ladybug-dotnet` GitHub Packages feed,
  currently pinned to `0.17.1-dev.2.1.g53e5ab5`; restores require source `github_ladybug`
  credentials with `read:packages` (CI uses `GITHUB_TOKEN` plus package Actions access; local runs use
  `NuGetPackageSourceCredentials_github_ladybug`). `OPENAI_API_KEY` is optional in CI, so live-provider
  tests skip by default unless the secret is present.

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
- Neo4j was removed 2026-06-17; it is no longer a provider. The `Neo4jGraphDriver`, the
  `GraphProvider.Neo4j` enum member, the `uri`/`user`/`password` constructor parameters,
  `GraphitiOptions.Uri`/`User`/`Password`, the `Neo4j.Driver` package reference, and all Neo4j tests
  are gone. Do not reintroduce a Neo4j provider without an explicit ask.

## Provider And Infrastructure Choices

- Use `Microsoft.Extensions.AI` as the primary adapter boundary for chat and embeddings.
- Keep `ILlmClient`, `IEmbedderClient`, and `ICrossEncoderClient` as Graphiti-facing abstractions.
- `HashEmbedder` remains the C# constructor/DI default embedder so Graphiti Core works without an
  implicit OpenAI dependency. This deliberately differs from Python's default `OpenAIEmbedder`; real-
  provider hosts should opt into a provider-backed `MicrosoftExtensionsAIEmbedderClient` or register
  an `IEmbeddingGenerator`.
- `IdentityCrossEncoderClient` remains the C# constructor/DI default so Graphiti Core works without
  an external provider. This deliberately differs from Python's default `OpenAIRerankerClient`.
  Real-provider hosts should opt into `MicrosoftExtensionsAICrossEncoderClient`; the OpenAI sample
  does this. The M.E.AI cross-encoder uses a structured boolean+confidence response because generic
  M.E.AI does not expose OpenAI top-logprob controls needed for Python's exact reranker scoring.
- Use official provider SDKs behind adapters where possible. LadybugDB is the core graph-provider
  investment target; its package/native references and driver are owned by `Graphiti.Core`.
- Use `HybridCache` for LLM response caching and preserve cache-key semantics for parity-sensitive
  structured calls.
- LLM cache keys intentionally include the response schema fingerprint, response-model identity,
  resolved model/size, and generation knobs in addition to prepared messages. Python currently keys on
  model plus messages; the C# shape avoids cross-schema or settings collisions for typed
  Microsoft.Extensions.AI calls and should not be narrowed as incidental parity cleanup.
- Keep LLM cache storage as `JsonObject` payloads serialized with `GraphitiJsonSerializer.Options`
  through the shared cache payload helper. Cache implementations may repair invalid stored strings,
  but must not change cache-key inputs, prompt/schema identity, or JSON wire shape as incidental
  cleanup.
- Use typed/source-generated payloads for deterministic internal serialization paths such as LLM
  cache keys when possible, but preserve property order, JSON names, and hash bytes.
- Use Polly `ResiliencePipeline<T>` for provider retry/backoff/timeout behavior.
- Use `ActivitySource`, `Meter`, and standard logging patterns; hosts choose exporters. Core emits
  BCL diagnostics only and carries no OpenTelemetry exporter dependency; the OTLP wiring lives in the
  observability sample/docs.
- Shared concurrency helpers should fail fast on invalid throttling settings and observe pre-canceled
  tokens before launching work.
- Bulk graph writes should observe cancellation before materializing caller-provided enumerables and
  between save/embedding phases, so canceled ingestion does not spend work enumerating later phases.
- Use `Microsoft.ML.Tokenizers` for default model-aware token counting, while preserving
  `HeuristicTokenCounter` for Python-style chars-per-token behavior.
- Use tensor/vector primitives for low-level math when helpful, but keep Graphiti ranking algorithms
  custom and parity-tested. Reject non-finite embedding values at Graphiti-owned embedding boundaries
  before persistence or ranking.
- HNSW is **not needed** for the current InMemory reference/test backend target. The 2026-06-27
  win-x64 ShortRun baseline for exact full-scan node-vector search was 104.5 us at 500 candidates and
  387.4 us at 2,000 candidates, including filter checks, top-k selection, and result cloning. Keep
  exact cosine the default. The 2026-06-28 Plan 11 large-N run extended this to 10000 nodes / 30000
  edges: exact node-vector search was within budget, edge-vector search was not the dominant
  end-to-end cost after skipping unnecessary endpoint-node lookup, and the retained structural win was
  the lookup skip rather than approximate indexing. Only reopen an opt-in approximate tier if future
  same-machine benchmarks at a materially larger target graph size show full-scan cosine is the
  bottleneck.
- Provider concurrency, embedding batch size, and LLM response-cache defaults stay unchanged after the
  2026-06-28 Plan 11 fake-provider throughput run. With 25 ms injected latency, 16 LLM misses at
  provider concurrency 4 completed in 121.6 ms and emitted the expected G4 token metrics; warmed cache
  lookups asserted 16/16 hits and zero live provider calls. For 96 embedding inputs, batch size 8 took
  92.98 ms while batch sizes 32 and 128 both fit in one latency wave (30.52 ms and 30.47 ms). No cache
  key, TTL, schema identity, wire shape, or default changed.
- Provider-backed C# embedders also require exact provider output counts and exact configured vector
  dimensions before persistence/ranking. Python provider clients generally slice or forward returned
  vectors, but the C# port treats malformed provider output as an adapter boundary error so downstream
  graph state stays dimension-consistent. Graphiti-owned ingestion writes prevalidate and assign any
  missing entity node/edge embeddings before calling the graph driver's bulk-save path, so malformed
  provider vectors fail before episode/entity-edge graph content can be partially persisted.
- Use an internal BM25 scorer for in-memory/materialized fallback full-text search. Do not add
  Lucene.NET as a default core dependency for this path.

## Performance And Allocation Direction

- **Status: active, benchmark-first.** The early moratorium is lifted and the modernization +
  allocation program (plans 10–11) is complete. Further performance work is evidence-driven only:
  hot-path changes need a BenchmarkDotNet before/after and a recorded baseline; readability/idiom
  changes stay behavior- and wire-preserving and warning-clean. The guidance below is the standing bar.
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
- Direct Lucene full-text query construction returns an empty query for blank/whitespace input after
  sanitization. Python top-level search also skips blank input, but lower-level Lucene helpers can
  emit `()` / `group AND ()` query strings and let the backend decide. C# treats skipping these
  direct blank Lucene calls as intentional hardening to avoid invalid or backend-dependent no-op
  full-text queries.
- Property filters are preserved on the public DTO for Python wire-shape compatibility, but ignored
  by backend query construction and in-memory/materialized matching because Python currently exposes
  the field without applying it in search filter constructors. Treat enforcement as a future API and
  behavior decision, not as current C# behavior.
- Search recipe properties return fresh `SearchConfig` instances. Python exports module-level mutable
  recipe objects and `search()` mutates the selected object when applying the requested limit, so recipe
  state can leak between calls. C# deliberately avoids that public API footgun while preserving the same
  recipe values and search outcomes for equivalent per-call inputs.
- BFS retrieval skips driver calls when origins are null/empty or depth is below one. Python delegates
  those shapes to custom search interfaces before its fallback guard, but built-in backend results are
  empty. C# keeps the public driver boundary deterministic and side-effect-free for invalid BFS input
  instead of reproducing custom-driver-observable no-op calls.
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
  when they need Python's character-window chunking behavior.
- Content chunking preserves Python's zero/default overlap semantics for direct helper calls:
  `overlapTokens: 0` is treated like the default overlap, matching Python's
  `overlap_tokens or CHUNK_OVERLAP_TOKENS`. Hosts can still configure `ChunkOverlapTokens = 0` on
  `DefaultContentChunker`, which mirrors setting Python's environment-derived default overlap
  constant to zero. JSON chunk serialization intentionally uses compact System.Text.Json output
  instead of Python `json.dumps` separator spaces; the parsed JSON structure and chunk boundaries are
  the parity contract, not whitespace. Large covering-chunk generation is deterministic in C# rather
  than random-sampled; this is an intentional reproducibility hardening for a helper whose contract is
  pair coverage, not sampling order.
- Saga-scoped episode retrieval follows Python's public fallback for null or empty group lists:
  `saga` still selects the saga branch, but the group parameter is null, so normal grouped sagas do
  not match and the query does not fall through to generic episode retrieval. InMemory returns an
  empty result for this shape; Ladybug binds the null group in the grouped saga match. (Neo4j, removed
  2026-06-17, did the same while present.)
- Empty node-label and empty temporal filter branches are intentional C# hardening divergences.
  Python can emit malformed/backend-dependent fragments for these shapes (`n:`, `n: AND m:`, `(`,
  `()`, or dangling `OR` groups). C# treats empty node labels and empty temporal groups as no-op
  filters rather than reproducing invalid backend queries.
- Saga summaries hard-truncate like Python and persist the typed LLM `summary` field directly,
  including empty or whitespace-only strings. No deterministic episode-content fallback is synthesized
  for saga summaries. Community/entity summary paths keep sentence-aware truncation where Python does.
- Incremental community updates choose the mode community among neighboring entities. Ties keep the
  first community encountered from the neighbor traversal, matching Python's first-max behavior
  rather than sorting by UUID.
- Structured-output prompts include the JSON schema text in the final prompt message and may also
  pass response-format metadata to the provider. Source-generated JSON metadata may cover nested
  `Graphiti.*Response` DTOs, but DTO type identity and snake_case schema/wire names must stay stable.
- M.E.AI chat responses parse as the whole trimmed JSON payload, with support for stripping a
  markdown code fence only when it wraps the entire payload. The generic adapter does not scan prose
  for an embedded JSON object/array; prose-wrapped output is invalid and routes through the retry
  feedback loop. Provider-specific extraction fallbacks would need their own adapter decision.
- Token usage tracking keeps the idiomatic C# `InputTokens`/`OutputTokens` totals, and also exposes
  Python-equivalent per-prompt `CallCount`, `AvgInputTokens`, and `AvgOutputTokens` values. Live
  provider usage is recorded only after the response parses and passes structured validation; refused,
  malformed, empty, or schema-invalid retry attempts do not increment `TokenTracker`.
- Combined node+edge extraction is ported as an internal `EpisodeGraphExtractor` path, but public
  `Graphiti` ingestion stays on separate node then edge extraction by default. The Python baseline
  exposes `use_combined_extraction` only as an internal bulk helper flag defaulting to `False`, not
  on `Graphiti.__init__`, `add_episode`, or `add_episode_bulk`; adding a C# public option or changing
  the default is a future product/API decision.
- Multi-episode node attribution keeps Python's extracted-node UUID map semantics. If a node resolves
  to a different canonical UUID before episodic edges are built, the attribution map does not remap
  to that canonical UUID; `BuildEpisodicEdges` therefore falls back to linking the resolved node to
  all provided episodes.
- Structured edge attributes are edge-resolution behavior, not a separate ingestion-stage pass.
  Preserve Python's distinction: exact duplicate edge reuse returns before the edge-attribute prompt
  and keeps existing attributes, while non-fast-path resolution may replace or clear attributes
  according to the matched custom edge type.
- Exact duplicate edge reuse scans only reranked duplicate candidates, matching Python's
  `resolve_extracted_edge` placement. `ResolveEdgeWithLlmAsync` owns the fast path over
  `relatedEdges`, and `AddTripletAsync` first reranks raw between-node edges through
  `EDGE_HYBRID_SEARCH_RRF` before duplicate reuse or LLM resolution.
- Edge expiry bookkeeping follows Python's resolution-time clock sites: `EdgeResolutionService` uses
  a fresh `utc_now` callback for non-fast-path resolved-edge expiry, and contradiction invalidation
  calls that callback per invalidated candidate. A brand-new extracted edge that already has
  `invalid_at` but has no related/invalidation candidates keeps `expired_at = null`, matching
  Python's early return from `resolve_extracted_edge`.

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

Other accepted public-workflow divergences confirmed in a 2026-06-14 surface audit:

- **Episode removal is stronger than Python.** Python `remove_episode` deletes only entity edges whose
  first supporting episode is the removed episode and does not repair saga links. C# prunes the removed
  episode UUID from shared entity edges, deletes only unsupported edges/entities, removes saga
  membership/adjacency edges, repairs `NEXT_EPISODE` bypasses, and updates saga first/last pointers.
  This is intentional consistency work, pinned by `RemoveEpisode_PrunesEpisodeFromSharedEntityEdge`
  and the saga repair tests.
- **Bulk raw-content scrubbing follows the constructor option.** Python's `store_raw_episode_content`
  blanking runs through the single-ingest `_process_episode_data` path, so bulk episodes keep content.
  C# applies `storeRawEpisodeContent: false` to bulk after extraction as well, so stored bulk episodes
  are scrubbed consistently with single-ingest behavior while extraction still sees the original text.
- **Explicit/DI graph drivers stay caller-owned.** Python `close()` always closes `self.driver`. C#
  closes only drivers it constructed itself (the default InMemory driver); externally supplied
  or DI-scoped drivers remain owned by their caller/container. This preserves .NET lifetime semantics
  and is pinned by `Graphiti_DisposeAsync_DoesNotCloseExternalGraphDriver` and
  `AddGraphiti_DisposesScopedGraphDriverOnce`.
- **Incremental community updates return a flattened result.** Python `add_episode(update_communities=True)`
  destructures the per-node update results as though there were exactly two top-level values, so one,
  two, or three-plus updated nodes can throw or mis-shape the public `AddEpisodeResults` payload. C#
  deliberately flattens all per-node community updates into `Communities` and `CommunityEdges`,
  preserving a usable public result shape and pinning the one-node path in
  `AddEpisode_WithUpdateCommunities_ReturnsSingleNodeCommunityUpdate`.

## Tracked-but-unfixed divergences (low impact / latent; from the 2026-06-13 review)

These were confirmed real but left as-is, with a rationale. Revisit if the relevant path is wired or
the impact grows.

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
- **Bulk saga association avoids a `NEXT_EPISODE` self-loop** (promoted here from `handoff.md` so a
  future Python-alignment pass does not "correct" it back). `SagaService.AssociateBulkAsync` passes the
  first ordered episode's UUID as the current-episode argument to the predecessor lookup, excluding it;
  Python passes an empty `current_episode_uuid` for the first bulk episode and can create a
  `NEXT_EPISODE` self-loop when the only bulk item reuses an already-linked saga episode. C# is the
  safer (cycle-free) behavior — keep it; a regression test pinning "single bulk episode reusing a linked
  saga UUID produces no self-loop" would lock it.
- **`CommunityClustering.LabelPropagate` caps iterations** at `Math.Max(100, n*n)` and returns the
  (possibly non-converged) labeling on exhaustion; Python's `label_propagation` loops `while True` with
  no cap. The tie-break and threshold logic match exactly, so results are identical for normal inputs;
  the cap only differs for a graph that genuinely never converges (C# truncates; Python would hang). It
  is intentional infinite-loop protection — keep it.

## Public-surface decisions settled while still alpha (2026-06-27)

The package is `2.0.0-alpha.1`; these release-surface calls were settled before the stable-version
gate so future releases are not forced into avoidable breaking changes:

- **`CommunityEdgeNamespace.SaveBulkAsync` is KEPT** as a deliberate additive C# API. It has no Python
  counterpart, but it is symmetric with every other public node/edge namespace bulk-save path, already
  pinned in the API snapshot, and covered by namespace plus LadybugDB runtime tests. Removing only this
  one bulk method would make the namespace surface less coherent for consumers.
- **`EntityAttributeDefinition.MaxLength` and `EntityAttributeDefinition.Required` are public API.**
  `MaxLength` supplies a per-field cap override for extracted string and string-list values;
  `Required` controls whether the dynamic structured response schema requires the field and whether an
  over-cap value is retained rather than dropped. The default `Required = false` preserves the existing
  C# over-cap drop/restore behavior for current callers; callers can set `required: true` for fields
  that must survive the cap so subsequent validation can decide whether to reject them.
- **Plan 08 public-API freeze cleanup:** `EntityTypeDefinition.Attributes` exposes
  `IReadOnlyDictionary<string, EntityAttributeDefinition>` rather than the concrete `FrozenDictionary`
  backing store, and `GraphitiHelpers.SemaphoreGatherAsync` is internal helper surface. The obsolete
  `GraphProvider.Kuzu` and `AddGraphitiCore` aliases keep their GRPH0001/GRPH0002 diagnostics. Public
  XML docs describe product behavior and provider support, not porting status.
- **Package/RID truth:** package metadata remains on `2.0.0-alpha.1` with Apache-2.0, README packing,
  symbols, XML docs, and the built-in LadybugDB dependencies. The validated LadybugDB RID claim is
  exactly win-x64 via the full verifier and linux-x64 via the gated extension smoke; other native RID
  assets shipped by the Ladybug package family are not Graphiti-validated yet. The Linux smoke asserts
  x64 before exercising FTS/vector extension loading.

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

## Adopting upstream features that arrive on the FalkorDB driver (2026-06-28)

Upstream has deprecated Kuzu and made FalkorDB its primary backend, so new `graphiti_core` *capabilities*
will increasingly land FalkorDB-flavored, sometimes with **no Kuzu path to mirror**. Standing stance:

- **We want the features — by meaning, not by implementation.** When upstream adds a real library
  capability (a behavior a consumer observes), we realize the *same semantic behavior* on the LadybugDB
  driver (and the InMemory reference), regardless of which provider upstream used to deliver it and
  regardless of whether Python still has a Kuzu path. Equivalence is judged by **observable behavior /
  wire shape, not code shape**; the LadybugDB realization may differ — sometimes more exotically — because
  the engines differ. Record it in `parity.md`, and here if the mechanism diverges.
- **We do NOT port engine-protocol quirks.** Provider plumbing that exists only because of one engine's
  wire protocol (RediSearch token escaping, Redis NUL-byte stripping, FalkorDB-specific query syntax)
  carries no feature meaning for LadybugDB and is correctly skipped (record N/A). Narrow exception: a fix
  for a bug that *also provably* affects LadybugDB.
- **The classification test is feature vs. mechanism**, applied per change during the upstream sync
  (`upstream-sync-procedure.md` step 4): a *capability* the library now offers → adopt the meaning; a
  *workaround for one engine's protocol* → skip. As Kuzu fades upstream, "does Python apply it to Kuzu?"
  stops being a useful gate for features — use "is this a capability we want?". The 2026-06-14
  dispositions above are consistent: the `group_id` *validity* fix was a real bug we adopted; the
  RediSearch escaping and NUL-strip were engine quirks we skipped.

## Provider Status

- LadybugDB is the primary graph provider target for the C# port. It is the package/backend we will
  invest in; its package refs and driver live in `Graphiti.Core`.
- The LadybugDB provider uses the LadybugDB NuGet package, which comes from the alternative Kuzu fork.
  Kuzu remains the Python parity lineage and compatibility vocabulary, while the driver-facing provider
  name is `GraphProvider.LadybugDb`. See `kuzu-driver-port.md`.
- `GraphProvider.Neo4j` was removed 2026-06-17; Neo4j is no longer a provider in the C# port.
  `GraphProvider.InMemory` remains the deterministic reference/test
  backend rather than a product provider. `GraphProvider.FalkorDb` and `GraphProvider.Neptune` remain
  enum/helper compatibility surfaces and are rejected by default options validation unless a separate
  provider decision changes that. LadybugDB is the provider path to invest in.
- `GraphProvider.Kuzu` is a valid obsolete compatibility alias in core DI/options and resolves to the
  LadybugDB-backed driver. The concrete
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
