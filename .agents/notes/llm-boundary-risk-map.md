# LLM Boundary Risk Map

Created 2026-06-27 for plan 09 item A. This is the inventory of places where real provider output
crosses from untrusted model text into typed Graphiti state. It is a planning note only; it does not
change behavior.

## Boundary Model

- **Adversarial / untrusted:** provider chat text, structured JSON returned by a model, response-cache
  payloads read from durable storage, and reranker JSON.
- **Semi-adversarial prompt input:** episode content, previous episode content, and custom extraction
  instructions are intentionally sent to the model. The client appends Graphiti schema/language
  instructions and strips problematic control characters, but it does not sanitize meaning.
- **Trusted by contract:** caller-supplied ontology definitions, prompt builders, `NoOpLlmClient`, and
  custom `ILlmClient` implementations. Note that only `LlmClient` subclasses get the shared schema
  validation, retry, cache, token, and telemetry pipeline automatically.
- **Current rule:** invalid model output should either be retried, ignored in a documented narrow
  fallback, or surface a typed exception. It must not fabricate graph content.

## Current Guard Inventory

| Boundary | Sites | Current guard |
|---|---|---|
| Provider text to JSON | `MicrosoftExtensionsAIChatClient.ParseJsonResponse` | Rejects empty text, strips only a wrapping code fence, parses the whole payload with `GraphitiJsonSerializer.Options`, rejects prose-wrapped JSON, and wraps non-object JSON under `value`. `ContentFilter` finish reason becomes non-retryable `LlmRefusalException`. |
| Structured validation / retry / cache | `LlmClient.GenerateValidatedResponseWithRetryAsync`, `StructuredResponseValidator` | Static or runtime schema validation runs before returning and before cache storage. `JsonException` gets two repair attempts with feedback. Cache hits are revalidated. Token usage is recorded only after validation. Static node/combined extraction has the one deliberate numeric-string `entity_type_id` coercion. |
| Provider retry and cache durability | `GraphitiServiceCollectionExtensions`, `MemoryLlmResponseCache`, `SqliteLlmResponseCache`, `HybridCacheLlmResponseCache` | Provider retries happen only through the configured Polly pipeline. The LLM cache key includes messages plus model/schema identity. Invalid cache payloads are treated as misses/regeneration paths by the cache implementations rather than trusted graph input. |
| Typed response materialization | `LlmClientResponseExtensions.ToTypedResponse` | After validation, deserializes to typed DTOs. If deserialization still fails, public writable properties are filled leniently: strings can receive JSON text and invalid properties are skipped. Direct custom `ILlmClient` implementations can bypass validation, so this path is also the guard for trusted-test/default clients. |
| Node / edge extraction JSON to domain values | `Graphiti.ExtractionParsing.cs`, `EpisodeGraphExtractor` | Requires structured schemas for real `LlmClient` providers, then reads known arrays and fields. It accepts legacy aliases in the parser, converts non-string values to compact JSON text, throws for missing/invalid `entity_type_id`, defaults out-of-range entity type IDs to `Entity`, drops blank entities, drops edges without endpoints/fact/relation, ignores invalid dates, and normalizes episode indices later. |
| Wire serialization / enum values | `GraphitiJsonSerializer`, `WireValueJsonConverter`, `EpisodeTypeJsonConverter` | Graphiti-owned JSON uses snake_case, relaxed escaping, and strict wire-value converters. Unknown or wrong-cased enum strings throw instead of being coerced. |
| Dynamic node/edge attributes | `ExtractionContextBuilder`, `AttributeExtractionService`, `EdgeResolutionService.ExtractEdgeAttributesAsync`, `AttributeMerger` | Runtime schema has `additionalProperties: false`, exact declared field names, nullable declared JSON types, and required fields only when configured. The merger reads either root fields or an `attributes` object, ignores undeclared or case-mismatched fields, recursively converts JSON to .NET values, drops over-cap optional strings/lists, and retains over-cap required fields for later validation. |
| Node dedupe decisions | `NodeResolutionService.ReadNodeResolutions` | Schema requires `entity_resolutions`, `id`, `name`, and `duplicate_candidate_id`. The reader accepts integer or numeric-string IDs, ignores malformed items, ignores duplicate response IDs after the first, ignores out-of-range IDs, and falls back unresolved nodes to themselves. |
| Edge duplicate / contradiction decisions | `EdgeResolutionService.ResolveEdgeWithLlmAsync`, `EdgeMergeHelpers.ReadIntArray` | Schema requires `duplicate_facts` and `contradicted_facts`. Readers accept integer or numeric-string indexes, ignore non-integers, negative indexes, and out-of-range indexes. First valid duplicate wins. Contradiction indexes map across related then existing candidates. |
| Edge timestamps | `EdgeResolutionService.ExtractEdgeTimestampsAsync`, `EpisodeGraphExtractor.ExtractBatchTimestampsAsync` | Single-edge timestamp failures are logged and ignored except cancellation. Batch timestamp failures are ignored except cancellation. Invalid date text becomes `null`; batch count mismatches update only the overlapping prefix. |
| Entity/community/saga summaries | `EntitySummaryService`, `CommunityService`, `SagaService` | Schemas require the expected summary/name fields for real `LlmClient` providers. Unknown entity-summary names are logged and ignored; known summaries are truncated. Community summary/name empty strings throw for real providers, with deterministic fallback only for `NoOpLlmClient`. Saga summaries are hard-truncated and may be empty. |
| Cross-encoder reranker JSON | `MicrosoftExtensionsAICrossEncoderClient.ScorePassageAsync` | Uses provider JSON schema hint and validates the parsed object. It is stricter than the main LLM client: no code-fence stripping, no prose extraction, and no validation retry layer. Non-finite confidence clamps to `0`; finite confidence clamps into `[0, 1]`. Provider exceptions can be retried only by an optional Polly pipeline. |

## Existing Coverage

- `ModernInfrastructureTests` covers main provider JSON parsing, wrapping-fence stripping, prose rejection,
  arrays/scalars, structured schema validation, validation retry feedback, rate-limit cleanup on parse
  failure, cache behavior, and provider resilience basics.
- `GraphitiExtractionParsingTests` covers required structured fields, entity type ID coercion/rejection,
  extraction aliases, JSON-text fallback for non-string values, invalid-date-to-null behavior, no
  relation fabrication, and episode-index normalization.
- `AttributeMergerTests` covers over-cap retention/drop behavior, exact declared attribute names, and
  case mismatch handling.
- `EdgeResolutionEndpointFetchTests` and related internal tests cover many index-based edge resolution
  outcomes, custom edge attribute schema selection, invalidation ordering, timestamp failure tolerance,
  and no-fabrication edge behavior.
- `MicrosoftExtensionsAICrossEncoderClientTests` covers the reranker response schema and invalid
  structured responses.
- `ProviderResilienceWorkflowTests` covers public ingestion/search behavior under transient chat retry,
  rejected rate-limit permits, empty provider responses, schema failures after repair retries, partial
  bulk failure, embedding dimension mismatch, and cross-encoder failure.

## Fuzz / Property-Test Targets For Plan 09 B

1. **Provider text parsing:** empty/whitespace, truncated JSON, malformed fences, non-wrapping fences,
   prose-wrapped objects/arrays, duplicate keys, deep nesting, very large strings, escaped braces, invalid
   unicode/surrogates, arrays/scalars when a model schema is expected.
2. **Schema/coercion boundary:** wrong-cased property names, missing required fields, extra fields,
   null-vs-empty arrays, numeric strings versus non-numeric strings, oversized dynamic attributes, wrong
   JSON types inside declared attribute fields.
3. **Extraction domain readers:** non-object array items, non-string names/types/facts, blank endpoints,
   relation aliases, invalid temporal strings, duplicate and out-of-range episode indices, huge index
   arrays, and mixed valid/invalid extraction rows. Assert invalid rows are skipped or retried, not
   converted into invented graph content.
4. **Maintenance decision readers:** duplicate node-resolution IDs, mismatched names, negative and
   out-of-range duplicate/contradiction indexes, wrong scalar types, repeated contradiction indexes, and
   responses that mix valid and malformed items.
5. **Typed response materialization:** custom `ILlmClient` responses that bypass `LlmClient` validation,
   especially empty objects, wrong scalar types, nested objects in string fields, and malformed typed
   summary/reranker shapes.
6. **Reranker strictness:** fenced JSON, empty responses, prose-wrapped JSON, non-object JSON, non-finite
   or out-of-range confidence, and provider failures under optional retry.

## Provider-Resilience Targets For Plan 09 C

Status 2026-06-28: complete. The workflow tests above cover these targets, and the pass surfaced one
fix now recorded in `decisions.md`: missing entity embeddings are prevalidated before driver bulk save
so malformed vectors cannot partially persist episode/entity-edge graph content.

- Confirm transient chat failures retry only through configured Polly pipelines and do not store cache
  entries until a validated response exists.
- Confirm schema-validation failures after the two repair attempts surface cleanly and leave no partial
  graph writes for single and bulk ingestion.
- Confirm timestamp extraction failure remains non-fatal while extraction, dedupe, and attribute
  extraction failures remain fail-loud.
- Confirm embedding count/dimension/non-finite failures abort before graph persistence in workflows that
  have already obtained LLM output.
- Confirm cross-encoder failure behavior in search is explicit: either propagated or deliberately
  bypassed by the configured search path, but not silently converted to misleading high relevance.
