# C# Port Evolution

This note records milestone-level evolution of the C# port away from the original Python
`graphiti_core/` implementation. It is intentionally historical: use it to understand why the C#
library crossed important product, architecture, provider, or operating boundaries.

Keep this file separate from the other working notes:

- `decisions.md` records current standing rules.
- `handoff.md` records current state, verification context, and coordination gotchas.
- `roadmap.md` records open or recurring work.
- `kuzu-driver-port.md` records detailed LadybugDB/Kuzu provider state.
- This file records durable turning points and the evidence that they became project milestones.

## Why This Exists

The C# port started as a behavioral port of Python Graphiti, but it is no longer useful to describe
all important work as "catching up with Python." Some changes deliberately move the C# library into a
different shape:

- idiomatic .NET project structure instead of a literal flat translation;
- .NET infrastructure choices where they preserve Graphiti behavior;
- provider strategy centered on LadybugDB rather than Python's provider priorities;
- future product decisions that may make the C# package its own lineage.

Those moves should be traceable without turning `handoff.md` or `roadmap.md` into a changelog.

## Documentation Options Considered

1. Put milestone history in `roadmap.md`.

   Rejected because the roadmap is intentionally forward-looking. Completed milestone history would
   make open work harder to scan.

2. Expand `decisions.md` into a historical decision log.

   Rejected because `decisions.md` is most useful when it states today's rules. Keeping superseded
   history there would make active guidance ambiguous.

3. Add one ADR file per turning point.

   Deferred. ADRs are useful for large public/API decisions, but the current port needs a single
   readable project narrative more than many small files.

4. Rely on git history and commit bodies.

   Rejected because git history preserves detail, not the milestone-level story. Future agents need
   to know which commits were turning points and why.

5. Keep a milestone registry with structured entries.

   Chosen. A single registry gives the port a durable evolution history while keeping the other notes
   focused on their existing jobs.

## Milestone Process

Create or update a milestone entry when work changes the relationship between the C# port and Python
in a durable way. Examples:

- public API or package shape moves away from a literal Python translation;
- the preferred provider, dependency boundary, or runtime strategy changes;
- the source-of-truth relationship to Python changes for an area;
- a cross-cutting .NET modernization becomes part of the design contract;
- a previously temporary compatibility path becomes supported, deprecated, or removed.

Do not create a milestone entry for ordinary implementation slices, test additions, bug fixes, or
small refactors. Those belong in git history, tests, `handoff.md`, or the roadmap.

Each milestone should include:

- **Status**: Proposed, Active, Landed, or Superseded.
- **Date range**: When the milestone became visible and, if applicable, when it landed.
- **Thesis**: The short project-level claim.
- **Python baseline**: What the original Python lineage implies.
- **C# direction**: What the C# port deliberately does differently.
- **Evidence**: Code, tests, package shape, verification, or docs that prove the milestone is real.
- **Boundaries**: What still must remain compatible with Python or existing C# users.
- **Follow-up decisions**: Open decisions that may create the next milestone.

When a milestone changes:

1. Update this file's milestone entry.
2. Update `decisions.md` only if the current standing rule changes.
3. Update `handoff.md` only if the current operating context changes.
4. Update `roadmap.md` only if open work changes.
5. Update provider-specific notes such as `kuzu-driver-port.md` only for detailed provider facts.

Prefer replacing stale milestone claims over appending contradictory prose. If a milestone is no
longer true, mark it `Superseded` and explain the successor direction.

## Milestone Index

| ID | Status | Milestone | Summary |
|---|---|---|---|
| M1 | Landed | Deep C# modernization | The port moved from literal translation toward idiomatic .NET structure, infrastructure, and allocation-aware implementation while preserving Graphiti behavior. |
| M2 | Active | LadybugDB-first provider strategy | The C# port's provider investment target is a core LadybugDB-backed driver, unlike the Python provider priorities. |
| M3 | Landed | First real-provider validation | The port ran end-to-end against the real OpenAI API for the first time (2026-06-13): structured schemas accepted, sane temporally-correct graph, relevant reranked search. The LLM-facing layer is now empirically validated, not just unit-tested against fakes. |
| M4 | Landed | Neo4j retirement | The temporary legacy Neo4j path was removed from the C# product surface (2026-06-17): the driver, the `GraphProvider.Neo4j` member, the `uri`/`user`/`password` ctor params, `GraphitiOptions.Uri`/`User`/`Password`, the `Neo4j.Driver` package ref, and all Neo4j tests are gone. Supported providers are now LadybugDB, InMemory, and FalkorDB/Neptune (enum/wire-compat only). |

## M1: Deep C# Modernization

**Status:** Landed
**Visible range:** Early C# port through the current `net10.0` modular package shape
**Thesis:** The C# library should be a native-feeling .NET library, not a flat Python-shaped
translation, while preserving Graphiti's behavioral contract.

### Python Baseline

Python `graphiti_core/` remains the behavioral source of truth for temporal graph semantics,
extraction, deduplication, invalidation, search merge behavior, prompt/wire values, and graph driver
contracts. A literal port would have kept many Python-style names, broad modules, flat public
surfaces, and direct structural correspondence to Python files.

### C# Direction

The C# port now treats idiomatic .NET shape as part of the product:

- feature-aligned namespaces and folders under `Graphiti.Core.*`;
- `Graphiti` kept as the public orchestrator while behavior is split into partials, internal
  services, helpers, namespaces, drivers, search components, maintenance code, and provider adapters;
- one-public-type-per-file direction and source-breaking namespace migration documented for the
  `2.0.0` line;
- .NET-native infrastructure where it does not change Graphiti semantics, including
  `Microsoft.Extensions.AI`, `HybridCache`, Polly resilience pipelines, `ActivitySource`,
  source-generated logging/serialization, and `Microsoft.ML.Tokenizers`;
- allocation-aware implementation as a design constraint, especially in ingestion, search, parsing,
  serialization, vector math, and provider paths.

### Evidence

- `README.md` documents the project split, namespace layout, and migration away from the original flat
  `Graphiti.Core` namespace.
- `AGENTS.md` and `decisions.md` make idiomatic C# plus behavioral parity the standing port contract.
- `handoff.md` records the current modular layout and audited areas.
- `roadmap.md` treats modernization as ongoing hardening, not a future migration phase.
- `Graphiti.Core` generates and ships IntelliSense XML documentation; public XML documentation in the
  shippable package is enforced by the Release build.
- Tests cover parity-sensitive areas such as search ranking/fusion/reranking, text utilities,
  in-memory reference behavior, maintenance, serialization/cache identity, and provider abstractions.

### Boundaries

Modernization must not casually change:

- Graphiti temporal graph semantics;
- serialized enum/configuration wire values;
- prompt names, response-format names, schema fingerprints, or LLM cache-key inputs;
- deterministic ordering and parity-sensitive ranking behavior;
- active-driver scoping through `UseGroupDriver` / `AsyncLocal`;
- public behavior without deliberate migration notes and tests.

### Follow-Up Decisions

- Whether future provider integrations should live in core or outside the core package.
- Whether any remaining Python compatibility shims should be removed before a stable public release.

## M2: LadybugDB-First Provider Strategy

**Status:** Active
**Visible range:** Current LadybugDB/Kuzu provider work
**Thesis:** LadybugDB is the C# port's primary provider investment target, even though Python
Graphiti does not prioritize LadybugDB as its main driver.

### Python Baseline

Python Graphiti supports provider paths such as Neo4j, FalkorDB, and Kuzu. For the C# port, Python
Kuzu behavior remains the provider parity lineage and compatibility vocabulary, but Python's provider
priority is not the C# product direction.

### C# Direction

The C# port uses a LadybugDB-centered provider model, with the LadybugDB backend built into
`Graphiti.Core` as of plan 06 (2026-06-26):

- The LadybugDB package references (`LadybugDB`, `LadybugDB.Native`), the driver implementation, the
  `LadybugDbOptions` host-facing `DatabasePath` configuration, and the `AddLadybugDbGraphDriver` DI
  registration live in `Graphiti.Core`. The driver helpers (statement, schema, mapping, full-text
  query, label filters, package execution, executor-backed behavior) live under
  `src/Graphiti.Core/Drivers/Ladybug/`, and `LadybugDbOptions` lives under
  `src/Graphiti.Core/Configuration/`.
- `Graphiti.Core` still owns the driver contract (`IGraphDriver`/`GraphDriverBase` and
  `GraphProvider`) and now also constructs the built-in LadybugDB driver directly for
  `GraphProvider.LadybugDb` / `GraphProvider.Kuzu` when no custom `GraphDriverFactory` is supplied.
- `GraphProvider.LadybugDb` is the driver-facing name; `GraphProvider.Kuzu` is an `[Obsolete]`
  compatibility alias that resolves to the same LadybugDB-backed driver. The concrete driver reports
  `GraphProvider.LadybugDb`; file persistence is configured through `LadybugDbOptions.DatabasePath`.
- Neo4j was removed 2026-06-17 and is no longer a C# provider, FalkorDB is not a C# provider
  investment target, and InMemory remains the deterministic reference/test driver.

### Evidence

- `decisions.md` names LadybugDB as the primary provider target, keeps InMemory as the deterministic
  reference/test backend, records that Neo4j was removed 2026-06-17, and blocks provider investment in
  FalkorDB and Neptune.
- `kuzu-driver-port.md` records detailed package facts, provider policy, runtime proof, quirks, and
  remaining work.
- `GraphProvider.cs` confirms `GraphProvider.LadybugDb` (value 5) is the driver-facing name and
  `GraphProvider.Kuzu` (value 2) is the `[Obsolete]` compatibility alias.
- `src/Graphiti.Core/Graphiti.Core.csproj` carries the `LadybugDB`/`LadybugDB.Native` package
  references and the driver implementation; `Graphiti.Core`'s provider-resolution switch constructs
  LadybugDB for `LadybugDb`/`Kuzu`.
- Tests provide runtime proof for main ingest/search/removal/triplet/bulk/saga/community workflows,
  package/native execution, direct driver bulk-save embedding/relationship persistence,
  namespace/model embedding reloads by UUID, direct package list/null binding, public namespace
  community/saga reads and typed deletes, saga-scoped retrieval and content reads, paged group
  reads, directed endpoint-pair and incident entity-edge reads, LadybugDB DI registration via
  `AddLadybugDbGraphDriver`, `GraphProvider.LadybugDb`/`GraphProvider.Kuzu`
  resolution, file-backed `DatabasePath` persistence for both provider values, `':memory:'` sentinel
  compatibility, and active Ladybug-owned full-text/label-filter construction.

### Boundaries

The LadybugDB milestone must still preserve:

- Python-compatible graph behavior where Kuzu parity is the relevant baseline;
- clear separation between Graphiti port gaps and LadybugDB package/binding limitations.

### Follow-Up Decisions

**RESOLVED**

- Neo4j removal (M4, 2026-06-17): the temporary legacy Neo4j path was removed from the C# product
  surface — driver, `GraphProvider.Neo4j`, `uri`/`user`/`password` ctor params,
  `GraphitiOptions.Uri`/`User`/`Password`, the `Neo4j.Driver` package ref, and tests.
- Final driver-facing naming (decided in plan-05 B): `GraphProvider.LadybugDb` is the driver-facing
  name and `GraphProvider.Kuzu` is the `[Obsolete]` compatibility alias.
- Shared Kuzu compatibility branches in generic search helpers were retired in plan-05 B2; active
  Ladybug query/filter syntax lives in the Ladybug driver package.

## M3: First Real-Provider Validation

**Status:** Landed
**Visible range:** 2026-06-13, after the parity-hardening pass
**Thesis:** A port of an LLM-driven library is unverified until it has talked to a real LLM. On
2026-06-13 the C# port did, successfully — moving the LLM-facing layer from "unit-tested against fake
clients" to "empirically validated against the real OpenAI API."

### Evidence

- Both env-gated `OpenAIProviderIntegrationTests` passed live: every Graphiti structured response
  schema (11 response models + the dynamic attribute schema) was accepted by the real provider, and a
  real 2-episode ingest produced a resolved temporal graph.
- The 6-episode `Graphiti.Sample.OpenAI` produced a sane graph: clean entities with rich, accurate
  summaries; correct bi-temporal invalidation (a "blocked" fact `invalid_at` exactly the QA-clearance
  date; a superseded fact contradiction-invalidated); and relevant, well-ordered hybrid + cross-encoder
  search results.
- Re-runnable via `eng/Run-OpenAIProviderValidation.ps1` (auto-loads a gitignored `.env`); `.env` is
  now gitignored to protect the key.

### Boundaries

- Schema acceptance and graph sanity are validated; a quantitative quality comparison against Python
  is NOT yet done — that needs the optional eval harness (plan 03 item 4), still gated on user
  approval.
- One extraction observation (a reschedule not invalidating the prior date fact) is LLM/entity-modeling
  variance, not a confirmed port divergence; the eval harness is the way to attribute such cases.

### Follow-Up Decisions

- Whether to build the eval harness for a durable C#-vs-Python quality score.
- Whether to wire a key-gated provider-validation job into CI.

## M4: Neo4j Retirement

**Status:** Landed
**Visible range:** 2026-06-17
**Thesis:** Neo4j was never the C# port's provider investment target and was only carried as temporary
legacy compatibility. With LadybugDB established as the first-class backend and InMemory as the
reference/test driver, the temporary Neo4j path was removed from the C# product surface.

### Python Baseline

Python `graphiti_core/` still ships a Neo4j driver and treats it as a supported backend. The C# port
does not mirror Python's provider priorities (see M2); the Neo4j removal is a deliberate C#-only
direction, not a Python parity change.

### C# Direction

The temporary legacy Neo4j path is gone:

- the `Neo4jGraphDriver` and its helpers (statement builders, record mappers, session/executor
  helpers) are removed;
- the `GraphProvider.Neo4j` enum member is removed;
- the `uri`/`user`/`password` `Graphiti` constructor parameters and the
  `GraphitiOptions.Uri`/`User`/`Password` options are removed (constructor driver selection is now
  explicit `graphDriver` > InMemory default);
- the `Neo4j.Driver` package reference and all Neo4j tests are removed.

Supported providers are now LadybugDB (first-class target), InMemory (deterministic reference/test),
and FalkorDB/Neptune (enum/wire-compat only, no real driver).

### Evidence

- `decisions.md` "Provider Status" and "Library Boundaries" record the removal and the new constructor
  precedence.
- The public-API baseline (`tests/Graphiti.Core.Tests/Api/`) was regenerated; `parity.md` and the
  consumer `README.md` no longer list Neo4j.
- Build is 0-warning and the full suite is green.

### Boundaries

- Do not reintroduce a Neo4j provider without an explicit ask.

## Candidate Future Milestones

Use this list as a prompt, not as committed direction:

- **Provider surface freeze:** final naming, DI, package, and support policy for LadybugDB.
- **Neo4j retirement:** COMPLETED 2026-06-17 (landed as M4) — the temporary legacy Neo4j path was
  removed from the C# product surface.
- **Stable public API release:** API, namespace, docs, migration notes, and packaging expectations
  are stable enough for external consumers.
- **C#-native operations posture:** telemetry, verification scripts, package build, and provider
  runtime checks become part of the normal release process.
