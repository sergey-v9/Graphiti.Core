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

- How much XML documentation should be added before considering the public surface polished.
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

The C# port is moving toward a LadybugDB-centered provider model:

- `Graphiti.Core` owns the LadybugDB package and native references.
- Internal core helpers under `Drivers/Ladybug/` own shared statement, schema, mapping, normalizer,
  active full-text query construction, active label-filter fragments, concrete package execution, and
  executor-backed behavior.
- `Configuration/LadybugDbOptions.cs` and `AddLadybugDbGraphDriver` provide host-facing
  `DatabasePath` configuration.
- `GraphProvider.Kuzu` remains compatibility vocabulary and is a supported core options/DI path that
  resolves to the LadybugDB-backed driver and honors `GraphitiOptions.Database`.
- Neo4j is retained only as existing/reference behavior while present, FalkorDB is not a C# provider
  investment target, and InMemory remains a deterministic reference/test driver.

### Evidence

- `decisions.md` names LadybugDB as the primary provider target and limits investment in Neo4j,
  FalkorDB, and InMemory.
- `kuzu-driver-port.md` records detailed package facts, provider policy, runtime proof, quirks, and
  remaining work.
- Tests provide runtime proof for main ingest/search/removal/triplet/bulk/saga/community workflows,
  package/native execution, directed endpoint-pair and incident entity-edge reads, core DI
  registration, `GraphProvider.Kuzu` resolution, file-backed `DatabasePath` persistence, core
  `GraphProvider.Kuzu` `Database` persistence, `':memory:'` sentinel compatibility, and active
  Ladybug-owned full-text/label-filter construction.

### Boundaries

The LadybugDB milestone must still preserve:

- Python-compatible graph behavior where Kuzu parity is the relevant baseline;
- clear separation between Graphiti port gaps and LadybugDB package/binding limitations;
- explicit naming decisions before replacing the current Kuzu compatibility provider value.

### Follow-Up Decisions

- Final driver-facing naming: Kuzu compatibility vocabulary versus LadybugDB product naming.
- Whether Neo4j removal becomes its own milestone.
- Whether shared Kuzu compatibility helpers remain indefinitely after the final LadybugDB provider
  surface is named.

## Candidate Future Milestones

Use this list as a prompt, not as committed direction:

- **Provider surface freeze:** final naming, DI, package, and support policy for LadybugDB.
- **Neo4j retirement:** removal or deprecation of Neo4j from the C# product surface.
- **Stable public API release:** API, namespace, docs, migration notes, and packaging expectations
  are stable enough for external consumers.
- **C#-native operations posture:** telemetry, verification scripts, package build, and provider
  runtime checks become part of the normal release process.
