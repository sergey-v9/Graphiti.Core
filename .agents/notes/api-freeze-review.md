# Pre-freeze public API review (2026-06-14)

> **IMPLEMENTED / superseded.** All of plan 05 A–E landed; this audit is historical and kept for
> rationale only. Do **not** re-implement the items below — the decisions and final shapes are
> recorded in `decisions.md` ("Public API surface (plan 05)") and plan 05. Read this note as a
> record of *why*, not a to-do list.

Phase 5 readiness produced three things on `main`: XML docs across the consumer-facing surface, a
**public-API snapshot test** (`tests/Graphiti.Core.Tests/Api/PublicApiSnapshotTests.cs` +
`Graphiti.Core.approved.txt`, via `PublicApiGenerator`) that fails CI on any accidental surface
change, and a consumer `README.md` + `docs/search.md`. This note records the API-audit findings from
that pass so the freeze decisions are tracked. The changes below were **breaking public-API/product
decisions** at the time of writing; they have since been resolved and applied (see plan 05 /
`decisions.md`), with the snapshot baseline regenerated alongside.

Source: the `ws/j-api-audit` audit + ergonomics observations from writing the README (`ws/k-readme`).
The surface is broadly disciplined — `Internal*` namespaces are fully internal, schema/cache-identity
DTOs are appropriately public, the wire-value enums are intentional. These are polish-before-freeze.

## A. Safe internal hardening (recommended; pure quality, alpha is the time)

1. `LlmClients/TokenUsage.cs:4` — `InputTokens`/`OutputTokens` are public `{ get; set; }` on a type
   handed out by `TokenUsageTracker.GetTotalUsage()`. External mutation of usage stats is unintended;
   make the setters `init` (or internal). Binary-breaking later.
2. `Serialization/GraphitiJsonSerializer.cs:15` (`Options`) and `Telemetry/GraphitiTelemetry.cs`
   (`ActivitySource`) are public `static readonly` **fields**. Prefer get-only **properties**
   (field→property is binary-breaking, so do it pre-freeze). `Options` is already `MakeReadOnly()`, so
   this is mostly contract-shape cleanup.
3. `Text/ContentChunking.cs:46` — `public static ITokenCounter TokenCounter { get; set; }` is a
   process-wide settable global (with an internal `AsyncLocal` override). CONDITIONAL: confirm whether
   any caller/test legitimately sets it as a config seam (decisions.md notes callers may register
   `HeuristicTokenCounter(4)`). If the DI `IContentChunker`/`DefaultContentChunker` path covers that,
   make the static get-only/internal; if it is a real config point, leave it and document.
4. Internalize port-artifact helpers that are public but read as implementation detail and have **zero
   external/sample/benchmark use** (tests keep access via `InternalsVisibleTo`): `SearchEngine`,
   `SearchUtilities`, `SearchFilterQueryBuilder`, `MaintenanceUtilities`, `StructuredResponseValidator`,
   and the `*EnumExtensions` (`SearchConfigurationEnumExtensions`, `ComparisonOperatorExtensions`).
   KEEP public: `GraphDriverBase` and the `*Namespace` facades (documented extension/ORM surface);
   `EpisodeTypeExtensions` if its `*WireValue` is used as a public helper. This shrinks the contract to
   the intentional API.

## B. Provider naming (product decision — Phase 4)

`GraphProvider.Kuzu` selects the LadybugDB driver — the enum value name does not match the driver.
Options: add `GraphProvider.LadybugDb` as the primary value and keep `Kuzu` as an `[Obsolete]` alias;
finish the Kuzu→LadybugDB terminology transition in `Drivers/Ladybug/`. Enum members can't be removed
post-freeze, so settle the name now. (FalkorDb/Neptune members stay — they're intentional wire-compat
surfaces per `decisions.md`.)

## C. DI method name (product decision)

The registration method is `AddGraphitiCore(...)`. `AddGraphiti(...)` reads more naturally and matches
the `AddLadybugDbGraphDriver` style. Rename (or add `AddGraphiti` as the primary and keep
`AddGraphitiCore` as an alias).

## D. Constructor ergonomics (product decision)

`Graphiti(string? uri, string? user, string? password, ... IGraphDriver? graphDriver = null, ...)`
leads with Neo4j connection args and **silently builds a Neo4j driver when `graphDriver` is null**. For
a port whose primary driver is LadybugDB and whose reference driver is InMemory, a Neo4j default is
surprising; both samples must pass `graphDriver:` by name. Consider making the driver explicit
(required, or defaulting to InMemory) and moving the Neo4j-connection convenience to a factory. Also:
`AddEpisodeAsync` has ~15 optional parameters — a candidate for an extraction-options object.

## E. Packaging (product + effort decision)

`Graphiti.Core` references `LadybugDB`/`LadybugDB.Native` **unconditionally**, so even InMemory/Neo4j-
only consumers must resolve the local Ladybug feed — a plain off-machine `dotnet restore` fails. A
separate `Graphiti.Core.Drivers.Ladybug` package (or a conditional/opt-in reference) would let the core
restore from nuget.org alone. This is the main blocker to publishing a consumable package and ties into
the LadybugDB-package productization (the local `0.17.0-alpha.2-graphiti.1` family must also be
published or replaced before a real release).

## Recommended order

A (now, low-risk alpha cleanup) → B + C (cheap, high-clarity, do together) → D (design) → E (the real
packaging/publish blocker, largest effort, gates a NuGet release). Each A/B/C/D item updates the API
snapshot baseline; do them in deliberate, reviewable commits.
