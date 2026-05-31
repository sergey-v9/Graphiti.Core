# C# Port Handoff

This is the working handoff for agents continuing the C# Graphiti Core port.

## Current Goal

`csharp/src/Graphiti.Core` is a C# port of Python `graphiti_core/`: temporal context graphs for AI
agents with episode ingestion, entity/fact extraction, deduplication, invalidation, communities,
sagas, and hybrid search.

Python is still the source of truth for behavior. The C# port should be idiomatic .NET where that is
compatible with Graphiti semantics, wire values, and performance/allocation discipline.

Modernization should not drift into abstraction for its own sake. For hot/shared paths, prefer C#
code that is explicit, measurable, and allocation-aware: simple loops and pre-sized collections are
often better than LINQ chains or helper layers that allocate; non-throwing parse paths are better
than exception-driven control flow; source-generated serializers/logging/regexes are preferred where
they avoid runtime reflection or repeated parsing. Keep this balanced with readability and parity.

If a local `.agents/skills` corpus is present in the checkout, use it as practical C#/.NET reference
material for the task at hand: test execution, MSTest authoring, performance scans,
BenchmarkDotNet, NuGet publishing, MCP work, file-based C# experiments, and similar specialties.
Those files are optional local tooling, not a second product roadmap. If a generic skill points
toward agent frameworks, Semantic Kernel, vector-store replacement, MCP server patterns, or broad
package churn, apply only the parts that fit Graphiti's existing boundaries and defer to
`decisions.md`.

Provider focus has shifted to LadybugDB. Treat Python's Kuzu behavior as the parity target, implement
with the LadybugDB NuGet package, and aim for LadybugDB to become the driver-facing/default provider
name. Existing Neo4j/FalkorDB work may remain as reference coverage, but do not spend new effort
improving those providers unless it directly supports shared abstractions or LadybugDB validation.

## Current Layout

- `Graphiti.cs` and `Graphiti.*.cs`: public orchestrator, lifecycle, ingestion, search, removal,
  saga, community, infrastructure, and extraction parsing partials.
- `Models/`: node, edge, result DTO, entity type, entity attribute, and episode type models.
- `Drivers/`: `IGraphDriver`, base driver, deterministic in-memory reference driver, Neo4j driver,
  statement builders, record mapper, session executor, provider enum, and saga episode content.
- `Namespaces/`: node and edge namespace facades over drivers.
- `Search/`: search configs/results, hybrid search engine, rerankers, filter builders/matchers,
  fallback graph materialization, search-result composition, search-driver retrieval adapter, and
  search/reranker telemetry spans.
- `Maintenance/`: entity deduplication and community clustering.
- `Text/`: chunking, token counting, text helpers, and Graphiti helper functions.
- `LlmClients/`: LLM abstraction, Microsoft.Extensions.AI chat adapter, cache implementations,
  structured response validation, messages, model size, and token usage.
- `Embedding/`: embedder abstraction, hash embedder, Microsoft.Extensions.AI embedder adapter, and
  embedding validation.
- `CrossEncoder/`: reranker abstraction and identity/default implementations.
- `Configuration/`: options, validators, DI registration, cache/resilience settings.
- `Telemetry/`: `ActivitySource` spans and source-generated logging.
- `Serialization/`: System.Text.Json serializer and source-generated context.
- `Internal/`: helper/services for extraction context, attribute merging, edge merging, type
  resolution, deterministic text, throttling, provider rate limiting, saga/community/attribute/edge/
  node services, and episode graph extraction.

## Recent State

- Folder and namespace modernization is complete. Source files now use folder-aligned namespaces.
- Bundle decomposition is complete. Former large files such as `Models.cs`, `GraphitiTypes.cs`,
  `Driver.cs`, and `MicrosoftAIAdapters.cs` were split by concern.
- `Graphiti.cs` has been decomposed substantially. The root file is now thin and most behavior lives
  in partials plus internal collaborators.
- Search decomposition is in progress but already has important boundaries:
  `SearchFallbackGraph`, `MaterializingSearchGraphDriver`, `SearchResultComposer`, and
  `SearchRetrievalRunner`. `SearchEngine` remains the orchestration layer, `SearchResultComposer`
  owns fusion/reranking/result shaping, and `SearchRetrievalRunner` owns driver-backed retrieval
  forwarding plus BFS guard behavior. In-memory and materialized fallback full-text search now use an
  internal corpus BM25 scorer while provider-backed full-text remains delegated. Search vector
  fallback paths use a reusable cosine scorer with cached query norm, and node-distance/episode-
  mentions reranker composition avoids duplicate dictionary lookups while preserving driver rank
  order and edge fan-out behavior.
- The fallback BM25 scorer now builds each document's matching query-term frequencies during the
  initial tokenization pass, avoiding a second per-document scan and preserving distinct query-term
  handling plus repeated document-term frequency scoring. Query-term de-duplication now also uses
  the shared token visitor to build the distinct term list and lookup in one pass, and final
  document score projection uses a pre-sized loop.
- The default `IdentityCrossEncoderClient` now creates one reusable `TextScorer` per rank call
  instead of tokenizing the query once per passage. Direct tests pin score ordering and duplicate
  passage index preservation for the indexed rank path.
- `SearchResultComposer.FuseRanks` now calls an internal direct RRF helper for ranked tuple inputs,
  avoiding per-list item-only materialization while preserving rank-position-only scores, inclusive
  min-score filtering, first-seen item retention, and stable tie order. The shared RRF projection
  now uses a pre-sized loop, and the internal ranked-tuple helper returns the final list directly to
  the composer instead of forcing an extra list copy.
- `SearchUtilities.TopByScoreCore` now avoids LINQ sort/projection chains by materializing the
  bounded priority-queue contents into a concrete buffer, sorting by descending score and first-seen
  index, then projecting with a pre-sized loop. This keeps RRF/MMR/top-score ordering semantics
  unchanged while reducing hot-path iterator allocations.
- `SearchUtilities` now uses the same indexed-score projection helper for RRF and MMR, and the
  item-only public MMR overload projects selected items with a pre-sized loop. The public
  `IReadOnlyList` return contracts and Python score/tie behavior are unchanged.
- `SearchEngine` now limits pure RRF branches inside fusion, truncates owned ranked lists with
  `SearchResultComposer.LimitRanked`, routes MMR through an internal ranked-tuple helper instead of
  materializing item-only lists, and uses a stable explicit helper for edge episode-mentions sorting.
  Cross-encoder, node-distance, and episode-mentions driver rerankers still see the full preliminary
  candidate pools needed for parity.
- `SearchResultComposer.ApplyCrossEncoderRerankerAsync` now builds passages from the existing
  indexed candidate list and projects final results with single-pass loops. It still preserves
  passage order, invalid/duplicate indexed-rank suppression, inclusive min-score filtering, and
  score-descending/original-index tie ordering. `ToRankedList` now also materializes search hits with
  a pre-sized loop instead of LINQ projection.
- `SearchResultComposer.MergeRankedCandidates` now sorts a concrete merged buffer and projects the
  final ranked tuple list with a pre-sized loop. It still keeps the first candidate object for a key,
  carries the maximum score across duplicates, and breaks score ties by first-seen order.
- `SearchEngine` now delegates final ranked tuple splitting to
  `SearchResultComposer.SplitRankedResults`, building result lists and reranker-score lists in one
  pass per search scope while preserving order, score conversion to `double`, and the public
  `SearchResults` list shape.
- `SearchUtilities` now has an internal token visitor used by `TextScorer` and fallback
  `Bm25TextScorer` document paths. This preserves the existing materialized `Tokenize` helper,
  Unicode token regex, invariant lowercase behavior, text-score formula, BM25 document-length
  accounting, and stable ranking while avoiding per-candidate token-list allocations in scorer hot
  paths.
- `SearchEngine` now computes implicit BFS origin lists only inside the BFS branch. Explicit
  `bfsOriginNodeUuids` still pass through unchanged, and implicit node/edge origins keep
  text-before-vector, first-seen distinct ordering over node UUIDs and edge source-node UUIDs.
- Materialized fallback BFS and ranker shaping now uses allocation-light loops instead of
  grouping/distinct/order LINQ chains. Node/edge BFS results keep the first traversal hit per target,
  node-distance ranks use the known 10/1/0 score buckets over first-seen distinct inputs, episode-
  mentions ranks use a counting dictionary plus explicit stable sort, and fallback traversal graph
  and endpoint lookups preserve first-wins/group-filter behavior with loop-built dictionaries.
- Search fallback in-memory snapshot projection now uses explicit typed loops over cloned driver
  snapshots instead of `OfType`/`Select` chains, preserving clone isolation, type filtering,
  embedding stripping flags, and stable order. Edge endpoint lookup now accepts
  `IReadOnlyList<EntityEdge>`, so materialized full-text fallback can pass its existing candidate
  buffer without an extra list copy.
- Shared full-text query construction now avoids split-array allocation on Lucene/Kuzu/Falkor paths
  while preserving Python's literal-space query limit behavior for Lucene/Falkor, Kuzu whitespace
  normalization and truncation, group-id validation, and provider-specific syntax.
- `MaintenanceUtilities` has parity coverage for episodic/community edge construction and pointer
  resolution. `ResolveEdgePointers` now accepts all `Edge` subtypes, matching Python's generic helper,
  while still mutating the existing edge objects in place and doing one-hop UUID replacement.
- Search tracing now emits child spans for edge, node, episode, and community search scopes, plus
  retrieval driver calls and async cross-encoder, node-distance, and episode-mentions reranker work.
  Tags include scope, methods, reranker, limits, candidate counts, min scores, query/vector sizes,
  BFS depths/origin counts, group ids, and result counts where applicable.
- Ingestion tracing now emits aggregate child spans for episode graph extraction, node/edge
  extraction, node/edge resolution, and node/edge attribute extraction. Tags cover bounded input,
  ontology, previous-episode, fallback/skipped, and result counts.
- Ingestion write tracing now emits `Graphiti.GraphWrite.SaveBulk` spans around episode, bulk
  episode, and direct triplet persistence. Tags include group id, provider, database, write phase,
  node/edge type counts, and total node/edge counts.
- Microsoft.Extensions.AI adapters now emit per-attempt provider-call spans for chat and embeddings.
  These spans sit inside Polly delegates, so retries produce one span per actual downstream attempt;
  failed attempts record exceptions while the parent logical LLM/embedder span can still complete.
- Neo4j session execution now emits low-level graph-provider spans for read, write, write-scalar,
  and write-batch operations. Tags intentionally include operation, database, query length,
  parameter count, statement/result counts, and avoid query text or parameter values. Tests also pin
  pre-canceled calls before session creation or activity emission.
- Neo4j driver internals have been split into statement construction, search statement construction,
  record mapping, session execution, and shared statement payloads.
- LadybugDB/Kuzu provider foundation work has started without changing provider status. Internal
  `Drivers/Ladybug` schema, statement-builder, and record-mapper helpers now pin Python Kuzu table
  shapes, Kuzu save/get/delete/retrieve/load-embedding Cypher, individual bulk-save statement
  expansion, JSON attribute storage, label-array behavior, simple-edge record mapping, and
  `RelatesToNode_` entity-edge representation. An internal, non-wired `LadybugGraphDriver` now uses
  those helpers through an abstract `ILadybugQueryExecutor` for non-search graph-driver operations,
  including schema execution, save/bulk save, delete, get-by-id/group, retrieve episodes, mention/
  community reads, and saga reads. Internal `LadybugSearchStatementBuilder` and
  `LadybugSearchExecutor` helpers now pin and exercise Kuzu full-text index/search statements, vector
  search, BFS statement plans, per-UUID ranker statements, Kuzu label filters, `RelatesToNode_`
  search shapes, result score extraction, BFS dedup/limit behavior, cancellation, and C# search-rank
  ordering over the abstract executor. No LadybugDB package reference, native dependency, concrete
  package adapter, DI wiring, `ISearchGraphDriver` implementation, or `GraphProvider.Kuzu` options
  validation support has been added yet. The C# foundation now resolves the current Python Kuzu
  saga schema/query and entity-edge `reference_time` inconsistencies ahead of runtime wiring by using
  the full `SagaNode` shape and returning entity-edge `reference_time`; these still need real-backend
  proof.
- DI-created graph drivers now consistently receive `GraphitiOptions.Database` for both supported
  providers, InMemory and Neo4j. For InMemory this sets the driver `Database` label but intentionally
  does not change the provider default group id.
- `GraphDriverBase.SaveBulkAsync` now observes cancellation before materializing bulk inputs and
  between node/edge save plus embedding phases, avoiding wasted enumeration/work after cancellation.
- In-memory driver secondary indexes now use `HashSet<string>` buckets instead of
  dictionary-as-set values. Public entity-edge lookups, saga lookup tie-breaks, and BFS adjacency are
  UUID-ordered to keep behavior deterministic.
- LLM structured-response handling has a typed helper while preserving nested `Graphiti.*Response`
  DTO type identities for cache/schema stability. Schema validation now materializes `JsonElement`
  directly instead of serializing to text and reparsing. The nested structured-response DTOs are now
  covered by the System.Text.Json source-generation context without renaming them or changing their
  snake_case schema shape.
- LLM cache-key construction now uses an internal typed `LlmCacheKeyPayload` covered by the
  System.Text.Json source-generation context instead of an anonymous-object payload. Existing hash
  tests pin byte-compatible cache-key JSON.
- LLM response caches now share one payload serializer so memory, SQLite, and HybridCache entries
  preserve the same JSON options. Memory and SQLite single-flight fills recheck the backing cache
  before running the expensive factory, and memory cache corrupt/non-object string payloads are
  removed and regenerated once under concurrent misses.
- Token usage tracking now mirrors Python's per-prompt prompt usage more closely: accumulated prompt
  usage records call counts and average input/output tokens while preserving the existing C# total
  token properties.
- Embedding validation now rejects provider vectors containing NaN or infinite values before assigning
  them to models or returning them from the Microsoft.Extensions.AI embedder adapter.
- Resilience options validation now rejects undefined `ProviderQueueProcessingOrder` values before
  they reach `ConcurrencyLimiterOptions`.
- `AIProviderRateLimiter` has direct helper coverage for null limiters, acquired lease ownership,
  pre-canceled acquisition, rejected lease disposal, and limiter exception propagation. The
  Microsoft.Extensions.AI embedder adapter now also pins that the shared provider rate limiter bounds
  concurrently scheduled batch chunks, not only single embedding calls. Chat and embedding adapters
  now also pin rejected-permit behavior, queued cancellation skipping provider calls, and lease
  disposal after post-provider parse/validation failures.
- Shared concurrency helpers now fail fast on invalid throttling limits, preserve input order for
  throttled selection, and avoid starting unbounded `SemaphoreGatherAsync` work when already
  canceled.
- Saga summarization now uses the deterministic episode-content fallback when a typed LLM response is
  unavailable or contains no summary, so the default no-op LLM no longer clears saga summaries.
- Entity extraction context now uses Python's stronger default `Entity` type description, including
  concrete-entity guardrails. Entity extraction parsing also accepts numeric-string
  `entity_type_id` values, matching Pydantic-style coercion before falling back to `Entity` for
  invalid or out-of-range ids.
- Extraction and node-dedupe string parsing now use `JsonValue.TryGetValue<string>` plus the existing
  JSON text fallback for non-string nodes, avoiding exception-driven parsing while preserving
  lenient LLM output handling.
- LLM node dedupe context now falls back to Python's `"Default Entity Type"` description when a node
  has only generic labels or no configured entity type description. Node dedupe response parsing also
  accepts numeric-string `id` and `duplicate_candidate_id` values before applying existing invalid
  id fallback behavior.
- Edge-resolution duplicate/contradiction id parsing now uses `JsonValue.TryGetValue` plus invariant
  numeric-string coercion instead of exception-driven parsing, preserving invalid-value skip behavior
  for LLM edge dedupe responses.
- Shared database-date parsing now exposes an internal non-throwing `TryParseDbDate` path. Optional
  LLM edge timestamps use it instead of `FormatException` control flow, while the public
  `ParseDbDate` helper still returns null for null/blank values and throws `FormatException` for
  invalid nonblank values.
- Shared ontology validation now keeps protected entity-node attribute names in a `FrozenSet` and
  validates node labels / excluded entity types with allocation-light loops on the valid path. Invalid
  excluded entity types remain distinct and sorted in error messages, and protected attribute checks
  remain case-insensitive.
- Entity-key normalization now uses a single-pass helper instead of a regex replace while preserving
  Python-style trimming, lowercase conversion, and whitespace collapse. This remains the shared key
  path for entity deduplication and fact comparison.
- Entity dedupe's entropy gate now counts whitespace-separated terms without allocating split arrays,
  while preserving Python's rule that short but multi-token high-entropy names may still use the
  fuzzy MinHash path.
- `ContentChunking.TextLikelyDense` now scans whitespace-delimited words in one pass instead of
  allocating a regex split array, while preserving Python's raw previous-word sentence detection,
  ASCII punctuation trimming, all-caps exclusion, supplied-token denominator, and strict threshold
  comparison.
- `HybridCacheLlmResponseCache.GetOrCreateAsync` now coalesces the whole per-key get/create path
  through Graphiti's `AsyncSingleFlight`, so corrupt or sentinel cache payload repair shares one
  cancellation-isolated factory call instead of fanning out late repair callers. Cache-key inputs and
  serialized JSON payload shape stay unchanged.
- XML docs have been added across much of the public surface. Remaining low-priority gaps include
  some internal utilities, deeper namespace member docs, and internal `Graphiti` helper details.

## Verification History

Past notes record successful runs for locked restore, format verification, no-incremental builds,
full test suites, pack, and package audits at several checkpoints. Later entries recorded 587-588
tests passing after search and Neo4j decompositions.

Latest checkpoint on 2026-06-01 after allocation-light fallback snapshot projection:

- `dotnet restore csharp/Graphiti.Core.CSharp.slnx --locked-mode` passed.
- `dotnet format csharp/Graphiti.Core.CSharp.slnx --verify-no-changes --verbosity minimal` passed.
- `dotnet build csharp/Graphiti.Core.CSharp.slnx --no-restore --no-incremental --verbosity minimal`
  passed with 0 warnings.
- `dotnet test csharp/Graphiti.Core.CSharp.slnx --no-build --verbosity minimal` passed with 760
  tests.
- `dotnet pack csharp/src/Graphiti.Core/Graphiti.Core.csproj --configuration Release --verbosity
  minimal` passed at the previous structured-response serializer checkpoint.
- `dotnet list csharp/Graphiti.Core.CSharp.slnx package --deprecated`, `--vulnerable`, and
  `--outdated` passed with no findings at the previous embedding/resilience checkpoint.

There was also an older transient warning that parallel work had broken `Graphiti.cs` around episode
extraction response types. Treat that as historical context, not as current truth. Re-run the relevant
build/test command before assuming the tree is red or green.

Useful commands from the C# repo root:

```powershell
dotnet build csharp/src/Graphiti.Core/Graphiti.Core.csproj --no-restore --verbosity minimal
dotnet test csharp/Graphiti.Core.CSharp.slnx
dotnet test csharp/Graphiti.Core.CSharp.slnx --no-restore --verbosity minimal
```

## Working Constraints

- The repo may have parallel agent/user edits. Do not revert unrelated changes.
- Avoid fighting active work in `Graphiti.cs` or extraction paths unless the user asks for that area.
- Prefer new focused tests or isolated helpers when other agents are editing hot files.
- For parity investigations, verify against current Python symbols rather than stale line numbers.
- Keep response DTO type identity stable when it participates in structured LLM cache keys.
- Preserve active-driver scoping through `UseGroupDriver` / `AsyncLocal`.
- When changing shared ingestion, search, parsing, serialization, embedding/vector, or provider
  paths, consider implicit allocations part of the review surface. Watch for split arrays, regex
  matches, closure captures, iterator materialization, boxing, LINQ projection/sort chains, and
  serialize/reparse loops before calling a modernization complete.

## Notes Coordination Protocol

The markdown notes under `.agents/notes/` are live coordination files, not static background. They may
be edited by users, another agent, or an external worker while a session is in progress.

- At task start, read the relevant note(s). For broad port work, read `decisions.md`, `handoff.md`,
  and `roadmap.md`; for provider work, also read any focused provider note such as
  `kuzu-driver-port.md`.
- Before finalizing work that changes direction, architecture, provider status, verification claims,
  or roadmap scope, re-read the relevant note sections or search the notes for the affected symbols.
- If the newest user instruction conflicts with the notes, follow the user and update the notes so
  future agents do not inherit the old direction.
- If notes changed externally and now conflict with your in-progress plan, reconcile before editing.
  If the right direction is not obvious, stop and ask instead of guessing.
- Prefer replacing stale guidance over preserving contradictory history. Use a short dated bullet in
  this handoff only when the timing/context matters to future agents.
- When adding a known direction change: put durable rules in `decisions.md`, current state or gotchas
  in `handoff.md`, planned work in `roadmap.md`, and provider-specific details in the focused note.

## Known Findings And Outcomes

- Python parity aliases on the public API were removed in favor of idiomatic C# names.
- Foldered namespaces are now the intended public API shape.
- `LuceneSanitize` escaping of uppercase operator letters is intentionally preserved for parity.
- Parenthesized Lucene group filters are intentional C# hardening.
- Token counting defaults to tiktoken; heuristic counting remains available.
- `HeuristicTokenCounter` floor division matches Python.
- Chunk helper constants alias the live chunking defaults.
- Cosine similarity now delegates the core operation to `TensorPrimitives.CosineSimilarity`, returns
  zero for missing/zero/non-finite vectors, and fails fast on mismatched non-empty dimensions.
- Community label propagation now preserves input order for initial community ids.
- LLM retry/backoff defaults were aligned more closely with Python's attempt/backoff shape.
- Structured-output prompt schema text was restored.
- Date-filter Cypher uses unique params across OR branches by design.
- Property filters are intentionally enforced in C#.
- `ConcatenateEpisodes` timestamp formatting now follows Python's `isoformat()` shape where possible.
- LLM input cleaning now drops malformed surrogate code units.
- LLM node dedup fallback was added after deterministic dedupe.
- Saga summary truncation now hard-truncates like Python.
- Saga summary generation falls back to deterministic episode content when the typed LLM path yields
  no summary.

## Areas Already Checked

These were previously audited and found faithful or intentionally different:

- `LuceneSanitize`
- `ParseDbDate` / `TryParseDbDate`
- `NormalizeL2`
- `ValidateGroupId`, `ValidateNodeLabels`, `ValidateExcludedEntityTypes`, and `ValidateEntityTypes`
- `NormalizeEntityKey` trimming, whitespace collapse, and lowercase behavior
- Cosine similarity finite/non-finite, zero, and mismatched-dimension behavior
- Reusable cosine scorer snapshot/cached-norm behavior
- Reciprocal rank fusion
- Direct ranked-tuple RRF fusion in `SearchResultComposer`, including ignored input scores,
  first-seen tie order, inclusive minimum-score filtering, direct final-list return, and
  allocation-light RRF score projection
- Shared bounded top-k ranking in `SearchUtilities.TopByScoreCore`, including score-descending
  ordering, first-seen tie order, limit/min-score filtering, and parity with the RRF/MMR full-sort
  oracles
- Non-RRF ranked-candidate merging in `SearchResultComposer`, including first-seen item retention,
  maximum-score duplicate merging, and first-seen tie ordering
- Final search result list splitting in `SearchResultComposer`, including item order preservation and
  score conversion to public `double` score lists
- Maximal marginal relevance
- Allocation-light MMR projection in `SearchUtilities`, including public item-only projection and
  scored-result parity with the full-sort oracle
- SearchEngine limiting/MMR shaping, including pure-RRF fusion limits, owned-list final truncation,
  ranked-tuple MMR input preservation, input-score-ignoring MMR behavior, stable edge episode-
  mentions sorting, and preserving full preliminary pools for cross-encoder/node-distance/episode-
  mentions rerankers
- In-memory/materialized fallback BM25 full-text ranking, including repeated-document-term scoring,
  distinct repeated query terms, stable ties, and single-pass document query-term frequency
  construction, allocation-light query-term de-duplication, pre-sized final score projection, and
  non-query tokens still counted for length normalization
- Node-distance reranker
- Episode-mentions reranker
- Identity cross-encoder lexical scoring and indexed duplicate-passage rank preservation
- `SearchResultComposer` cross-encoder reranking, including candidate passage order, invalid/duplicate
  indexed rank suppression, inclusive minimum-score filtering, and score/original-index ordering
- `SearchResultComposer` node-distance/episode-mentions rank mapping, including missing ranks,
  driver-rank ordering, and edge fan-out by source UUID
- Entity dedup deterministic stage, including the allocation-light entropy term counter and short
  multi-token fuzzy-match path
- Maintenance helper episodic/community edge construction, including attributed multi-episode
  ordering, invalid-index suppression, group propagation, single-episode behavior, and generic
  one-hop in-place pointer resolution for entity, episodic, and community edges
- `EpisodeType` wire values
- Embedding text newline replacement
- Search config constants
- `TruncateAtSentence`
- `ContentChunking.TextLikelyDense` whitespace splitting, raw previous-word sentence boundary
  behavior, Python punctuation trimming, all-caps exclusion, long sparse/entity-rich text behavior,
  supplied-token denominator, and strict threshold comparison
- Label-propagation core after the input-order fix
- In-memory date-filter matching and deterministic HashSet-backed secondary indexes
- DI database option propagation to InMemory and Neo4j graph drivers, with InMemory database kept
  distinct from its default group id
- LadybugDB/Kuzu foundation schema and save/get/delete/retrieve/load-embedding statement shapes,
  including `RelatesToNode_`, full Saga model fields, entity-edge `reference_time` save/get/search
  projections, label-array storage, JSON attribute serialization/deserialization, simple-edge record
  mapping, individual bulk-save statement expansion, internal executor-backed non-search driver
  forwarding/mapping, search statement plans for full-text/vector/BFS/rankers, internal search
  execution/mapping over `ILadybugQueryExecutor`, and keeping provider status unsupported in
  DI/options.
- Full-text query construction preserves Python-compatible Lucene literal-space query limits,
  Falkor RedisSearch stopword/operator query limits, and Kuzu whitespace word splitting/truncation
  while avoiding split-array allocation in the shared C# helper.
- LLM generate-response pipeline order
- Source-generated serializer metadata coverage for nested structured LLM response DTOs, while
  preserving snake_case prompt/schema names such as `entity_resolutions` and
  `duplicate_candidate_id`
- Source-generated serializer metadata coverage for the typed LLM cache-key payload, while
  preserving existing cache-key hash bytes.
- LLM response cache cloned payload, cancellation-isolated fill, stale-miss recheck, and
  corrupt-memory-payload repair behavior
- HybridCache LLM response cache corrupt/sentinel payload repair coalescing and cancellation-isolated
  shared fill behavior
- Token usage per-prompt totals, call counts, average tokens, snapshot immutability, and int64
  accumulation behavior
- Embedding dimension and finite-value validation for model assignment and M.E.AI adapter outputs
- Resilience options validation, including provider queue ordering enum values
- AI provider rate-limiter helper behavior for null/acquired/rejected/canceled/faulted acquisitions,
  plus chat/single-embed retry reacquisition and batch-embed concurrency bounding through the
  Microsoft.Extensions.AI adapters. Adapter-level rejected permits and queued cancellations skip
  provider calls, and leases are released after chat parse failures and embedding validation
  failures.
- Saga summarization no-summary fallback with the default no-op LLM
- Edge temporal invalidation and self-expiration
- Similarity-search min-score operators
- LLM edge and node deduplication passes, including edge duplicate/contradiction numeric-string id
  coercion and invalid-value skipping
- LLM node dedupe default generic entity-type description and numeric-string resolution ids
- Entity extraction default `Entity` prompt description and numeric-string `entity_type_id`
  coercion/fallback behavior, plus non-string JSON text fallback for extracted entity and edge
  fields, and optional extracted/LLM edge timestamp parsing of valid dates plus invalid-date
  suppression
- Search driver-backed retrieval forwarding, including vector/fulltext argument propagation, BFS
  empty-origin/max-depth guards, explicit BFS-origin pass-through, and implicit node/source-origin
  first-seen distinct ordering
- Materialized fallback BFS/ranker shaping, including shortest first traversal hit retention,
  origin-group filtering, first-seen input de-duplication, stable ranker ties, node-distance score
  buckets, and episode-mention count ranking
- In-memory fallback snapshot projection, including typed filtering from cloned snapshots,
  embedding stripping flags, stable projection order, and read-only edge endpoint lookup inputs
- Content chunking tests that mutate the static token counter are serialized through a shared test
  collection to avoid parallel test pollution.
- Shared throttling helpers validate max degree of parallelism, preserve select result order, and
  honor pre-canceled tokens before starting unbounded gather operations.
- `GraphDriverBase.SaveBulkAsync` cancellation before enumerable materialization and between bulk
  save/embedding phases.
- Search scope, retrieval-driver, async reranker, ingestion extraction/resolution/attribute,
  graph-write, Microsoft.Extensions.AI provider-call, and Neo4j session OpenTelemetry spans are
  covered by tests. Neo4j session tests also cover pre-canceled no-session/no-span behavior and
  guard against query text or parameter-value telemetry leakage.

## LadybugDB / Kuzu

LadybugDB is the main provider target, using the LadybugDB NuGet package from the alternative Kuzu
fork while preserving Kuzu behavior for Python parity. Keep the separate `kuzu-driver-port.md` note
visible for agents working on driver/provider work. The schema/statement/mapper foundation exists;
the internal executor-backed non-search driver core exists; the internal search statement foundation
now has fake-executor execution/mapping coverage but is not exposed by `LadybugGraphDriver`. The next
safe provider increment is deciding package/native dependency shape, adding a concrete LadybugDB
executor adapter, proving the pinned statements and full Saga/`reference_time` projections against
the real backend, and keeping `GraphProvider.Kuzu` unsupported until behavior is proved end to end.
