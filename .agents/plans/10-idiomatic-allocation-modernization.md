# Plan 10 — Idiomatic + allocation modernization of the code

Created 2026-06-27. This is the **current-priority** work order following the 2026-06-27 paradigm shift
(`decisions.md` → "What this project is"): the library is functionally complete and faithful to Python,
it is an embeddable internal component we maintain (not a release product), so the forward work is the
**code itself** — making it the best modern C# it can be and keeping GC pressure low. Two interlocking
tracks, run together, file-area by file-area.

## Status

**Current priority (2026-06-27).** Parity is the floor and is essentially complete; release is parked.
This plan does not change behavior, wire values, structured-schema / cache identity, or the public API —
it changes how the code is written and how much it allocates.

## Rules of engagement (read before every slice)

- **Parity is sacred.** No change may alter runtime behavior, LLM prompt/wire text, structured-output
  JSON schema identity, response-cache key identity, or the public API snapshot. If a change *could*
  touch any of those, it is out of scope for this plan — route it through `parity.md`/`decisions.md`
  instead.
- **Warning-clean, always.** `TreatWarningsAsErrors=true` with analyzers at Recommended. Every slice
  builds clean and `dotnet format --verify-no-changes` passes.
- **Clarity first.** A "modern" form that is less clear or less correct is *not* an improvement — skip
  it. Idiom for its own sake is churn. Prefer changes that make the code simpler to read *and* tighter.
- **Hot-path allocation changes are benchmark-first.** Anything in ingestion, search, extraction
  parsing, serialization, embedding/vector, or provider plumbing needs a BenchmarkDotNet before/after
  and a recorded baseline under `benchmarks/Graphiti.Core.Benchmarks/baselines/`. Obviously-free,
  zero-risk reductions (e.g. deleting a redundant `.ToList()`) may land without a benchmark, but never at
  the cost of clarity or parity.
- **Small, reviewable slices.** One coherent theme per commit (e.g. "collection expressions in Search",
  "remove redundant materialization in bulk dedupe"), verified centrally, checked off here. Do not mix a
  behavior-neutral idiom sweep with a measured perf change in the same commit.
- **No new default runtime dependency** in `src/`. Test/benchmark-only deps are fine.
- The CRLF/line-ending and `dotnet format` gotchas in `commit-policy.md` apply — prefer `Edit` over
  whole-file rewrites on existing files.

## Track I — idiomatic modern C# (C# 14 / .NET 10, toward .NET 11)

Apply where it genuinely improves the code and is correct; this is a menu, not a mandate:

- Collection expressions (`[..]`, spreads) for array/list/span construction.
- Primary constructors for simple state-holding types; the **C# 14 `field` keyword** for
  property-backed logic without an explicit backing field.
- `params ReadOnlySpan<T>` (C# 13) on hot varargs APIs to avoid array allocation at call sites.
- `System.Threading.Lock` (.NET 9) instead of `lock (object)` where a dedicated lock is used.
- `SearchValues<T>` for repeated char/byte membership checks; UTF-8 string literals (`"..."u8`) for
  ASCII byte constants.
- Switch expressions, list/property patterns, target-typed `new`, `is`-patterns over manual casts.
- `ArgumentNullException.ThrowIfNull` / `ArgumentException.ThrowIfNullOrEmpty` / `ThrowIfNullOrWhiteSpace`
  for guard clauses.
- `FrozenDictionary`/`FrozenSet` for build-once/read-many lookups (several already exist — extend
  where it pays).
- `static` lambdas/local functions to make non-capture explicit; explicit `CancellationToken` flow;
  `ValueTask`/`IAsyncEnumerable` where the shape genuinely fits.

## Track A — allocation / GC discipline

Drive down per-operation allocations in the hot paths (benchmark-first as noted above):

- Remove redundant `ToList()`/`ToArray()`/`AsEnumerable()` materialization; iterate or pass
  `IReadOnlyList<T>`/`Span<T>` instead.
- Replace LINQ chains in hot loops with direct loops where it removes iterator + closure allocation.
- Hoist captured closures to `static` lambdas with passed state; avoid boxing (struct→interface, enum
  in interpolation/`string.Format`).
- Build strings with interpolation handlers / `StringBuilder` / spans instead of `+` concatenation in
  loops; pre-size `StringBuilder`/collections when the count is known.
- Reuse buffers via `stackalloc` (small, bounded), `ArrayPool<T>` (large, transient), and `Span<T>`/
  `Memory<T>` slicing instead of substring/array copies.
- Prefer struct enumerators and avoid re-enumerating sequences; cache the count where cheap.

## Concrete target inventory (seeded by the 2026-06-27 codebase audit)

From a per-area audit of all of `Graphiti.Core` (8 areas). Headline: the code is **already well
optimized** (10 prior allocation slices show), so there are no sweeping wins — these are real, specific
sites. Worked in priority order; check off as slices land. `bench` = attach BenchmarkDotNet before/after;
`parity?` = verify against Python/dedup tests before adopting because results could shift.

### Tier 1 — high value, high confidence, parity-safe (do first)

- [x] **CommunityService.GeneratePairSummaryAsync / GenerateCommunityNameAsync** (`Internal/Services/
  CommunityService.cs` ~315/337) — the deterministic fallback summary/name (StringBuilder + array +
  truncation) is computed **eagerly on every pairwise LLM reduction** but only used in the NoOp fallback.
  Make it lazy (compute only in the fallback branch). *parity none.*
- [x] **LlmClient.IsCleanInput → `SearchValues<char>`** (`LlmClients/LlmClient.cs` ~380) — the clean-path
  check runs a per-char `Rune.DecodeFromUtf16` loop over every prepared message of every LLM call.
  Replace with `!input.AsSpan().ContainsAny(RemovableChars)` (all removable scalars are single UTF-16
  units); keep the rune path for the rare dirty case. *parity low; bench.*
- [x] **LlmClient.PrepareMessages** (`LlmClients/LlmClient.cs` ~334) — drop the eager
  `new Message(...)` clone of every message; `Message` is an immutable record and all mutations already
  use `with`, so alias the originals. *parity none; bench.*
- [x] **FactsHaveWordOverlap in bulk edge dedupe** (`Graphiti.Ingestion.cs` ~1208) — invoked O(edges²);
  hoist the left-fact word `HashSet` out of the inner candidate loop and span-tokenize the right side
  (keep `RemoveEmptyEntries|TrimEntries` semantics). *parity low; bench.*
- [x] **Drop redundant `CopyList(episodes)`** (`Graphiti.Search.cs` ~209) — `episodes` is already
  `IReadOnlyList`; pass it straight into `SelectThrottledAsync`. *parity none.*
- [x] **SearchAsync group-id sentinel** (`Search/SearchEngine.cs` ~61) — replace
  `!groupIds.SequenceEqual(new[]{ string.Empty })` (array + LINQ per search) with a direct
  `Count == 1 && groupIds[0].Length == 0` check. *parity none.*
- [x] **Ladybug embedding-load reads whole entity** (`Drivers/Ladybug/LadybugGraphDriver.cs` ~564/583) —
  `Load*EmbeddingsByUuidAsync` maps a full `EntityNode`/`EntityEdge` (lists, attribute dict, date parses)
  to read one embedding column. Add a `LadybugRecordMapper.GetFloatList(record, "name_embedding")` and
  read the column directly. *parity none; bench.*
- [x] **AttributeMerger.ExtractDeclaredAttributes** (`Internal/Helpers/AttributeMerger.cs` ~45) — drop the
  full copy of the response `attributes` object into a `Dictionary`; call `source.TryGetPropertyValue`
  directly per declared name. *parity low.*
- [x] **ContentChunking.GetOverlapMessages** (`Text/ContentChunking.cs` ~936) — O(n²) `Insert(0, …)`;
  switch to `Add` + one `Reverse()` like its two sibling helpers. *parity none.*
- [x] **Collapse duplicate snapshot copy helpers** (`Text/Helpers.cs` ~785/910) — `SnapshotEmbedding` /
  `SnapshotOperations` keep byte-identical `CopyReadOnlyList`/`CopyList`/`CopyOperationList` loops and a
  List-then-array fallback; collapse the non-`ICollection` paths to `[.. x]` and delete the dead helpers.
  *parity none.*
- [x] **Dead-code removal** — unused `WhitespaceRegex()` (`Text/Helpers.cs` ~987); verify-then-remove
  `FindFirstStoredSagaByName` / `GroupMatches` (`Drivers/InMemoryGraphDriver.cs`). *parity none.*
- [x] **TextUtilities.AppendEpisode** (`Text/TextUtilities.cs` ~104) — `builder.Append(index)` instead of
  `builder.Append(index.ToString(...))`. *parity none.*

### Tier 2 — real wins, benchmark- or parity-gated (medium confidence)

- [x] **Ladybug `Parameters(...)` → `params ReadOnlySpan<(string,object?)>`** (`LadybugStatementBuilder.cs`
  ~872 and `LadybugSearchStatementBuilder.cs` ~528) — stack-allocate the arg list per statement build.
  *parity none; bench.*
- [x] **Ladybug one-element-array `[0]` throwaways in deletes** (`LadybugGraphDriver.cs` delete paths) —
  add single-statement overloads for non-Entity node types. *parity none.*
- [ ] **AddEpisodeBulkAsync `CopyDictionaryValues` per iteration** (`Graphiti.Ingestion.cs` ~365) —
  O(E·N) snapshot of the growing canonical-edge dict each episode; widen the callee param to
  `IReadOnlyCollection` and pass `.Values`, or reuse one buffer (cross-file: `EdgeResolutionService`).
  *parity low; bench.*
- [ ] **Bulk node dedupe O(n²)** (`Graphiti.Ingestion.cs` `DedupeBulkNodesAsync` ~589 /
  `FindCanonicalNodeByNormalizedName` ~1094) — per-node full-dict copy + repeated `NormalizeEntityKey`;
  index canonical nodes by a precomputed normalized (Ordinal) key for O(1) lookup; reuse scratch buffers.
  *parity? ; bench.*
- [ ] **EdgeResolutionService.TryGetNodeByExactExtractedName** (`Internal/Services/EdgeResolutionService.cs`
  ~348) — O(edges×nodes) full-dict scan because the map is `OrdinalIgnoreCase`; build a one-time
  `Ordinal` lookup for O(1) endpoint resolution (mind the first-winner nuance). *parity? ; bench.*
- [ ] **EmbeddingVectorValidation.MaterializeVector** (`Embedding/EmbeddingVectorValidation.cs` ~80/113)
  and **MicrosoftExtensionsAIEmbedderClient.CreateBatchAsync** (~121) — fill a pre-sized backing span via
  `CollectionsMarshal.SetCount`/return the pre-built array instead of element-by-element `Add`/re-copy.
  *parity low; bench.*
- [ ] **MMR pairwise dimension re-validation** (`Search/SearchUtilities.cs` ~636) — hoist the invariant
  dimension check out of the O(n²) loop; `TensorPrimitives.Dot` directly. *parity low; bench.*
- [ ] **Pre-size build-once lookups** — `SearchFallbackGraph` adjacency dicts (~277/296);
  `MaintenanceUtilities.BuildEpisodicEdges` result list (~32). *parity none.*
- [ ] **MinHash redundant UTF-8 encoding (32×)** (`Maintenance/EntityNodeDeduplication.cs` ~284) — encode
  each shingle once, vary only the cheap seed mix. **Changes hash/LSH bucket values** — adopt only if
  dedup tests + Python parity hold. *parity? ; bench.*
- [ ] **EntitySummaryService.ApplySummaries early-out** (~184); **EpisodeGraphExtractor.
  MergeAttributionIndices** `SortedSet`→ existing `DistinctSorted` (~331); **NodeResolutionService** hoist
  single-element group-id array (~261). *parity low/none; bench where marked.*
- [ ] **De-dup `NamespaceDriverHelpers.CopyFloatList`** → call existing
  `EmbeddingVectorValidation.CopyNullableVector`. *parity none.*

### Tier 3 — readability/consistency sweeps (free, `parity none`, batch as one commit each)

- [ ] **Collection-expression sweep.** Convert hand-rolled copies/array literals to `[.. x]` / `[x]`:
  `Graphiti.Ingestion.CopyList`→`[.. source]`; `EntityNodeDeduplication.ToNodeList`/`CopyBucket`;
  `NamespaceDriverHelpers.Build*UuidList`; `BuildEntityTypeNamesById`→`["Entity", .. keys]`;
  Ladybug `LadybugSchema`/statement `new[]{…}`; `EpisodeAttribution`/`ExtractionContextBuilder`
  singletons; `Models` `Load*EmbeddingAsync(new[]{Uuid})`→`[Uuid]`; `Node.Labels` `[]`. **Prompt array
  literals** in `Prompts/*` and `CrossEncoder`/`LlmClient` message arrays: convert the **array
  construction only** — the prompt **text is golden-pinned and must stay byte-identical.**
- [ ] **Static-readonly default arrays / single-pass aggregation.** Hoist default cache tags to a shared
  `static readonly`/`[…]` (`HybridCacheLlmResponseCache` ~28, `GraphitiServiceCollectionExtensions` ~156);
  `TokenUsageTracker.GetTotalUsage` two `.Sum()`→one `foreach`; `GraphitiCacheOptionsValidator` `.Any(…)`→
  `foreach`; `GraphDriverBase` cache the default-group single-element array.
- [ ] **`System.Threading.Lock` + loop-invariant hoists.** `EdgeResolutionService` shared mutation gate
  `object`→`Lock` (~165); `EdgeMergeHelpers.ResolveEdgeContradictions` hoist `resolvedValidAt/InvalidAt`
  out of the loop (~41). (Note: Ladybug `SchemaLock` is an **async** `SemaphoreSlim` — do **not** convert.)
- [ ] **InMemoryGraphDriver 18-parameter clone ctor → a single `SharedStore` object** (mirroring
  `LadybugGraphDriver.SharedState`); collapses the clone call and removes the positional-arg hazard.
  *internal refactor; parity low — keep shared-mutable-store semantics exactly.*
- [ ] **TextUtilities.ConcatenateEpisodes** — factor the two near-identical overloads into one generic
  helper with static-lambda projections (verify the pre-size math is unchanged).

### Out of scope for plan 10 (route elsewhere)

- A possible **correctness bug** in the episode-mentions **edge** reranker (positional score mismatch),
  flagged by the Search auditor — this is behavior, not modernization. Verify against Python and the
  reranker tests separately (a background task was spawned for it); do **not** fold a behavior fix into a
  style/alloc slice.

## Verification

`.\eng\Verify-GraphitiCore.ps1` green on win-x64 after each slice (restore, format-verify, warning-clean
build, full tests, pack, fresh-consumer smoke). Track A hot-path slices additionally attach a
BenchmarkDotNet before/after and a recorded baseline. The public-API snapshot must **not** change — if a
slice would move it, stop: that means the change touched the surface and is out of scope here.

## Non-goals

- No behavior, wire, schema, cache-key, or public-API change (those are parity-governed, not style).
- No release versioning / publishing (parked, user-gated).
- No new `src/` runtime dependency; no swapping Graphiti's custom domain logic for framework
  abstractions (see `decisions.md` Replacement Policy).
- Not robustness/fuzz hardening (that is the deferred plan 09).
