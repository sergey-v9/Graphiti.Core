# Graphiti Core for .NET

A faithful C# port of [Graphiti](https://github.com/getzep/graphiti)'s Python `graphiti_core` — a
framework for building **temporally-aware knowledge graphs** for AI agents. Graphiti ingests
episodes (chat messages, JSON, free text), extracts entities and facts with an LLM, deduplicates them
against what it already knows, and answers questions with hybrid search over the resulting graph.

The Python library is the behavioral source of truth for this port. The C# wire shape (snake_case
JSON), prompt identity, ranking semantics, and cache keys all mirror Python so the two stay
compatible.

> **Status:** `2.0.0-alpha.1`. The library is functionally complete and validated against a real
> OpenAI provider, but it is **not yet published to NuGet**. `Graphiti.Core` is now LadybugDB-free —
> InMemory/Neo4j consumers depend only on nuget.org packages. The optional LadybugDB driver lives in
> the separate `Graphiti.Core.Drivers.Ladybug` package, which still consumes a **local** package feed.
> See [Install / reference](#install--reference).

## Contents

- [Why Graphiti](#why-graphiti)
- [Install / reference](#install--reference)
- [Quickstart](#quickstart)
- [Using a real provider (OpenAI)](#using-a-real-provider-openai)
- [Dependency injection](#dependency-injection)
- [Drivers](#drivers)
- [Search](#search)
- [Custom entity & edge types](#custom-entity--edge-types)
- [The temporal model](#the-temporal-model)
- [Samples & evaluation](#samples--evaluation)
- [Building & verifying](#building--verifying)
- [Project layout](#project-layout)
- [Relationship to Python Graphiti](#relationship-to-python-graphiti)
- [License](#license)

## Why Graphiti

- **Bi-temporal facts.** Every fact (an `EntityEdge`) tracks when it became true (`valid_at`), when it
  stopped being true (`invalid_at`), and when it was superseded in the graph (`expired_at`). New
  information that contradicts an existing fact invalidates it instead of overwriting it, so the graph
  keeps an auditable history. See [The temporal model](#the-temporal-model).
- **Hybrid search.** Retrieval combines semantic similarity (embeddings), keyword search (BM25), and
  graph traversal, then fuses and reranks the candidates (reciprocal rank fusion, MMR, cross-encoder,
  node-distance, episode-mentions). See [Search](#search).
- **Custom ontologies.** Supply your own entity and edge types so extraction populates typed
  attributes you define. See [Custom entity & edge types](#custom-entity--edge-types).
- **Communities & sagas.** Build communities (clusters of related entities with summaries) and sagas
  (ordered chains of episodes) on top of the base graph.
- **Incremental.** Each `AddEpisodeAsync` integrates new data without recomputing the whole graph.

## Install / reference

Graphiti Core is **not on NuGet yet**. Reference it from source today by adding a project reference to
`src/Graphiti.Core/Graphiti.Core.csproj`, exactly as the samples do:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/csharp/src/Graphiti.Core/Graphiti.Core.csproj" />
</ItemGroup>
```

The library targets **`net10.0`**.

`Graphiti.Core` is **LadybugDB-free**: it carries only the driver *contract*
(`IGraphDriver`, `GraphProvider`, `GraphDriverBase`) plus the InMemory and Neo4j drivers, and depends
only on packages available from nuget.org. InMemory/Neo4j-only consumers can restore it without any
local feed. To use the LadybugDB backend, add a second project/package reference to
`src/Graphiti.Core.Drivers.Ladybug/Graphiti.Core.Drivers.Ladybug.csproj` — that package owns the
`LadybugDB` / `LadybugDB.Native` references and the `AddLadybugDbGraphDriver` DI helper:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/csharp/src/Graphiti.Core.Drivers.Ladybug/Graphiti.Core.Drivers.Ladybug.csproj" />
</ItemGroup>
```

Selecting `GraphProvider.LadybugDb` (or the obsolete `GraphProvider.Kuzu` alias) without referencing
that package throws a clear `InvalidOperationException` telling you to add it and call
`AddLadybugDbGraphDriver()`.

### LadybugDB local-package caveat

This caveat applies **only** to the `Graphiti.Core.Drivers.Ladybug` package; `Graphiti.Core` itself is
unaffected. The LadybugDB driver depends on the `LadybugDB` / `LadybugDB.Native` packages (the C# port's
primary graph backend). On this branch those packages are restored from a **local feed** configured in
[`NuGet.config`](NuGet.config):

```xml
<add key="ladybug-local" value="../../ladybug/tools/csharp_api/artifacts" />
```

That feed points at a sibling Ladybug checkout. A plain `dotnet restore` of the Ladybug package (or its
tests) on a machine **without** that local artifacts directory will fail to resolve `LadybugDB`.
Referencing only `Graphiti.Core` (InMemory/Neo4j) restores from nuget.org alone and does **not** need
the local feed. To produce the Ladybug artifacts, build the Ladybug package family from the sibling
checkout:

```powershell
.\build.ps1 --target Pack --package-version 0.17.0-alpha.2-graphiti.1
```

(The exact version is pinned in [`Directory.Packages.props`](Directory.Packages.props).) Publishing a
real off-machine release of `Graphiti.Core.Drivers.Ladybug` still requires this
`0.17.0-alpha.2-graphiti.1` LadybugDB package family to be published to (or replaced on) a real feed —
that remaining publish prerequisite is tracked as Step E.2 of the release-readiness plan.

## Quickstart

The smallest end-to-end example: construct `Graphiti` with the deterministic **in-memory** driver,
build indices, ingest an episode, and search. This uses the built-in default clients (a no-op LLM, a
deterministic hash embedder, and an identity cross-encoder), so it runs with **no API key** — but
because there is no real LLM, extraction produces no entities or facts. It is the right shape to start
from; swap in a real provider (next section) to get real extraction.

```csharp
using Graphiti.Core;
using Graphiti.Core.Drivers;
using Graphiti.Core.Models;

await using var graphiti = new Graphiti(
    graphDriver: new InMemoryGraphDriver("quickstart"),
    maxCoroutines: 2);

// Create the indices/constraints once on a fresh database.
await graphiti.BuildIndicesAndConstraintsAsync(deleteExisting: true);

// Ingest an episode.
await graphiti.AddEpisodeAsync(
    name: "Intro",
    episodeBody: "User: My name is Maya Patel. I manage the Atlas migration project at Nimbus Health.",
    sourceDescription: "chat transcript",
    referenceTime: DateTime.UtcNow,
    source: EpisodeType.Message,
    groupId: "quickstart");

// Retrieve the most relevant facts for a query.
var facts = await graphiti.SearchAsync(
    "Who manages Atlas?",
    groupIds: new[] { "quickstart" },
    numResults: 5);

foreach (var edge in facts)
{
    Console.WriteLine(edge.Fact);
}
```

The public constructor (from `src/Graphiti.Core/Graphiti.cs`) accepts the clients you want to supply
and defaults the rest:

```csharp
public Graphiti(
    string? uri = null,
    string? user = null,
    string? password = null,
    ILlmClient? llmClient = null,        // defaults to a no-op client
    IEmbedderClient? embedder = null,    // defaults to a deterministic hash embedder
    ICrossEncoderClient? crossEncoder = null, // defaults to an identity reranker
    bool storeRawEpisodeContent = true,
    IGraphDriver? graphDriver = null,    // see driver selection below
    int? maxCoroutines = null,           // optional cap on concurrent operations
    TimeProvider? timeProvider = null,
    ILogger<Graphiti>? logger = null,
    string database = "");
```

Driver selection by precedence: an explicit `graphDriver` is used as-is (recommended); otherwise a
non-null `uri` builds a Neo4j driver; otherwise it **defaults to an in-memory `InMemoryGraphDriver`**.
So `new Graphiti()` and `new Graphiti(llmClient: x, embedder: y)` work out of the box on the
deterministic reference driver — pass `graphDriver:` (LadybugDB/InMemory) or `uri:` (Neo4j) for a
specific backend.

## Using a real provider (OpenAI)

Real entity/fact extraction needs a real LLM, embedder, and (for cross-encoder reranking) a chat model.
Graphiti's client contracts (`ILlmClient`, `IEmbedderClient`, `ICrossEncoderClient`) are adapted over
[`Microsoft.Extensions.AI`](https://www.nuget.org/packages/Microsoft.Extensions.AI). The wiring below
is copied from the canonical sample at `samples/Graphiti.Sample.OpenAI/Program.cs`.

Add the OpenAI integration package to your project:

```xml
<PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.6.0" />
```

```csharp
using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Drivers;
using Graphiti.Core.Embedding;
using Graphiti.Core.LlmClients;
using Microsoft.Extensions.AI;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set OPENAI_API_KEY.");

var chatModel = "gpt-4.1-mini";
var embeddingModel = "text-embedding-3-small";
var embeddingDimensions = 1536;

// Microsoft.Extensions.AI adapters over the OpenAI SDK.
var chatClient = new OpenAI.Chat.ChatClient(chatModel, apiKey).AsIChatClient();
var embeddingGenerator = new OpenAI.Embeddings.EmbeddingClient(embeddingModel, apiKey)
    .AsIEmbeddingGenerator(embeddingDimensions);

// Graphiti-facing clients. Temperature 0 keeps extraction deterministic.
var llmClient = new MicrosoftExtensionsAIChatClient(
    chatClient,
    new LlmConfig
    {
        Model = chatModel,
        SmallModel = chatModel,
        Temperature = 0
    });

var embedder = new MicrosoftExtensionsAIEmbedderClient(
    embeddingGenerator,
    embeddingDimensions,
    modelId: embeddingModel,
    batchSize: 16,
    batchConcurrency: 2);

var crossEncoder = new MicrosoftExtensionsAICrossEncoderClient(
    chatClient,
    new LlmConfig
    {
        Model = MicrosoftExtensionsAICrossEncoderClient.DefaultModel, // "gpt-4.1-nano"
        SmallModel = MicrosoftExtensionsAICrossEncoderClient.DefaultModel,
        Temperature = 0,
        MaxTokens = 64
    });

await using var graphiti = new Graphiti(
    llmClient: llmClient,
    embedder: embedder,
    crossEncoder: crossEncoder,
    graphDriver: new InMemoryGraphDriver("sample-openai"),
    maxCoroutines: 2);

await graphiti.BuildIndicesAndConstraintsAsync(deleteExisting: true);
// ... AddEpisodeAsync / SearchAsync as in the Quickstart.
```

The sample reads optional model overrides from environment variables: `OPENAI_CHAT_MODEL`,
`OPENAI_SMALL_MODEL`, `OPENAI_RERANKER_MODEL`, `OPENAI_EMBEDDING_MODEL`, and
`OPENAI_EMBEDDING_DIMENSIONS`. `gpt-5`-family reasoning models require `temperature = 0`; the client
handles that automatically.

## Dependency injection

For ASP.NET Core / generic-host apps, register Graphiti with the extensions in
`Graphiti.Core.Configuration`. Register your own `Microsoft.Extensions.AI` chat client and embedding
generator in the container and Graphiti picks them up; if none are registered it falls back to the
built-in no-op/deterministic clients.

```csharp
using Graphiti.Core.Configuration;
using Graphiti.Core.Drivers;
using Microsoft.Extensions.AI;

// Your provider clients (e.g. OpenAI), registered as M.E.AI abstractions:
services.AddSingleton<IChatClient>(_ =>
    new OpenAI.Chat.ChatClient("gpt-4.1-mini", apiKey).AsIChatClient());
services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
    new OpenAI.Embeddings.EmbeddingClient("text-embedding-3-small", apiKey)
        .AsIEmbeddingGenerator(1536));

// Graphiti itself:
services.AddGraphiti(options =>
{
    options.Provider = GraphProvider.InMemory;   // or .LadybugDb / .Neo4j
    options.EmbeddingDimension = 1536;
    options.MaxCoroutines = 4;
});

// To use the LadybugDB driver with a persistent file:
services.AddLadybugDbGraphDriver(o => o.DatabasePath = "graphiti.db");
```

Then resolve `Graphiti` from the provider (it is registered as a scoped service).

`AddGraphiti` also has an overload that binds from `IConfiguration` — it reads the `Llm`,
`Embedding`, `ContentChunking`, `Cache`, and `Resilience` sections in addition to `GraphitiOptions`:

```csharp
services.AddGraphiti(configuration);
```

(`AddGraphitiCore` remains as an `[Obsolete]` alias of `AddGraphiti`.)

Key option types (all under `Graphiti.Core.Configuration` / `Graphiti.Core.LlmClients`):

| Type | Purpose | Notable members |
|---|---|---|
| `GraphitiOptions` | Which backend and instance behavior | `Provider`, `Uri`/`User`/`Password`, `Database`, `EmbeddingDimension` (default `1024`), `MaxCoroutines`, `StoreRawEpisodeContent`, `GraphDriverFactory` |
| `LadybugDbOptions` | LadybugDB driver (in the `Graphiti.Core.Drivers.Ladybug` package) | `DatabasePath` (empty or `:memory:` = in-memory) |
| `LlmConfig` | Bound from the `Llm` section | `Model`, `SmallModel`, `Temperature`, `MaxTokens`, `ApiKey`, `BaseUrl` |
| `EmbeddingConfig` | Bound from the `Embedding` section | `EmbeddingDimension`, `ModelId`, `BatchSize`, `BatchConcurrency` |
| `GraphitiCacheOptions` | LLM response cache (HybridCache) | `LlmResponseExpiration`, `LlmResponseLocalCacheExpiration`, `LlmResponseTags` |
| `GraphitiResilienceOptions` | Polly retry/timeout/concurrency for provider calls | `MaxRetryAttempts` (default `3`), `RetryDelay`, `MaxRetryDelay`, `AttemptTimeout`, `ProviderConcurrencyLimit` |

Use `AddLadybugDbGraphDriver(...)` (from the `Graphiti.Core.Drivers.Ladybug` package) to point the
driver factory at LadybugDB; it sets `GraphitiOptions.GraphDriverFactory` for you.
`AddLadybugDbGraphDriver(configuration)` binds `LadybugDbOptions` from a configuration section. Because
`Graphiti.Core` no longer references the LadybugDB packages, this call is what makes
`GraphProvider.LadybugDb`/`Kuzu` resolvable.

## Drivers

A graph driver implements `IGraphDriver` (`Graphiti.Core.Drivers`). Backends are identified by the
`GraphProvider` enum.

| Driver | `GraphProvider` | Status | Notes |
|---|---|---|---|
| **InMemory** | `InMemory` | Deterministic reference/test driver | `new InMemoryGraphDriver(database)`. In-process, fully featured (persistence + search), ideal for tests, samples, and ephemeral graphs. Used by both samples. |
| **LadybugDB** | `LadybugDb` | Primary provider target (opt-in package) | The C# port's investment backend (a Kuzu-lineage embedded graph DB). Lives in the separate **`Graphiti.Core.Drivers.Ladybug`** package — add it, then build via `LadybugDbGraphDriverFactory.Create(databasePath)` / `.CreateInMemory()`, or wire through DI with `AddLadybugDbGraphDriver`. Selecting `LadybugDb`/`Kuzu` without that package throws a clear `InvalidOperationException`. See the [local-package caveat](#ladybugdb-local-package-caveat). |
| **Neo4j** | `Neo4j` | Legacy reference | `new Neo4jGraphDriver(uri, user, password, database)` (also built automatically if you pass a `uri` to the `Graphiti` constructor). Kept working as reference coverage; not the investment target. |
| FalkorDB / Neptune | `FalkorDb` / `Neptune` | Compatibility surface only | Present on the `GraphProvider` enum for wire compatibility with Python; **not** implemented as configured C# providers. |

The driver-facing provider value is `GraphProvider.LadybugDb`. `GraphProvider.Kuzu` remains as an
`[Obsolete]` alias (the Python-parity compatibility name) that still resolves to the LadybugDB driver.

### LadybugDB persistence

`LadybugDbGraphDriverFactory.Create(databasePath)` (and the DI `LadybugDbOptions.DatabasePath`) take a
file path for a persistent database. An empty string or the Kuzu `:memory:` sentinel selects an
in-memory database. Example:

```csharp
using Graphiti.Core.Drivers.Ladybug;

var driver = LadybugDbGraphDriverFactory.Create("graphiti.db"); // persisted to disk
await using var graphiti = new Graphiti(graphDriver: driver, /* clients... */);
```

## Search

There are two `SearchAsync` overloads plus `SearchAdvancedAsync` on `Graphiti`
(`src/Graphiti.Core/Graphiti.Search.cs`).

**Convenience overload** — returns the most relevant facts (`EntityEdge`s):

```csharp
public Task<IReadOnlyList<EntityEdge>> SearchAsync(
    string query,
    string? centerNodeUuid = null,
    IReadOnlyList<string>? groupIds = null,
    int numResults = SearchConfiguration.DefaultSearchLimit,
    SearchFilters? searchFilter = null,
    IGraphDriver? driver = null,
    CancellationToken cancellationToken = default);
```

Pass `groupIds` to scope the search to graph partitions and `numResults` to cap results. When
`centerNodeUuid` is supplied the results are reranked by graph distance to that node; otherwise
reciprocal rank fusion is used.

```csharp
var facts = await graphiti.SearchAsync(
    "What is the current Atlas rollout date?",
    groupIds: new[] { "sample-openai" },
    numResults: 5);
```

**Config-driven overload** — returns a combined `SearchResults` (edges, nodes, episodes,
communities) according to an explicit `SearchConfig`:

```csharp
using Graphiti.Core.Search;

var results = await graphiti.SearchAsync(
    "Who owns Atlas?",
    config: SearchConfigRecipes.CombinedHybridSearchCrossEncoder,
    groupIds: new[] { "sample-openai" });

// results.Edges, results.Nodes, results.Episodes, results.Communities
```

### Recipes

`SearchConfigRecipes` (`Graphiti.Core.Search`) provides ready-made presets matching the Python
implementation. Each is a fresh `SearchConfig` you can further tune (`Limit`, `RerankerMinScore`).

| Recipe family | What it searches | Reranker |
|---|---|---|
| `CombinedHybridSearchRrf` | edges + nodes + episodes + communities | RRF (reciprocal rank fusion) |
| `CombinedHybridSearchMmr` | edges + nodes + episodes + communities | MMR (diversity) |
| `CombinedHybridSearchCrossEncoder` | edges + nodes + episodes + communities (adds BFS) | cross-encoder (default for `SearchAdvancedAsync`) |
| `EdgeHybridSearch{Rrf,Mmr,NodeDistance,EpisodeMentions,CrossEncoder}` | facts only | as named |
| `NodeHybridSearch{Rrf,Mmr,NodeDistance,EpisodeMentions,CrossEncoder}` | entities only | as named |
| `CommunityHybridSearch{Rrf,Mmr,CrossEncoder}` | communities only | as named |

Rerankers at a glance: **RRF** fuses multiple ranked lists; **MMR** trades relevance for diversity;
**cross-encoder** reranks with the chat model (requires a real cross-encoder client); **node-distance**
biases toward a `centerNodeUuid`; **episode-mentions** favors facts mentioned in more episodes.

`SearchFilters` (`Graphiti.Core.Search`) further constrains candidates — by `NodeLabels`, `EdgeTypes`,
and temporal predicates (`ValidAt`, `InvalidAt`, `CreatedAt`, `ExpiredAt`), each an AND-of-OR-groups of
`DateFilter`s.

See [docs/search.md](docs/search.md) for more detail on methods, rerankers, and filters.

## Custom entity & edge types

Define an ontology with `EntityTypeDefinition` and `EntityAttributeDefinition` (`Graphiti.Core.Models`)
and pass it to `AddEpisodeAsync` via the `entityTypes` / `edgeTypes` parameters (both are
`IReadOnlyDictionary<string, EntityTypeDefinition>` keyed by type name). Extraction is then guided by
your types and the LLM fills the typed attributes you declared.

```csharp
using Graphiti.Core.Models;

var entityTypes = new Dictionary<string, EntityTypeDefinition>
{
    ["Person"] = new EntityTypeDefinition(
        name: "Person",
        description: "A named individual.",
        attributes: new Dictionary<string, EntityAttributeDefinition>
        {
            ["role"] = new EntityAttributeDefinition(
                description: "The person's job title or role."),
        }),
    ["Project"] = new EntityTypeDefinition(
        name: "Project",
        description: "A software or migration project."),
};

await graphiti.AddEpisodeAsync(
    name: "Intro",
    episodeBody: "User: Maya Patel manages the Atlas migration at Nimbus Health.",
    sourceDescription: "chat transcript",
    referenceTime: DateTime.UtcNow,
    source: EpisodeType.Message,
    groupId: "ontology-demo",
    entityTypes: entityTypes);
```

`AddEpisodeAsync` also accepts `excludedEntityTypes`, an `edgeTypeMap` (allowed edge types per
source/target type pair), and `customExtractionInstructions` (extra natural-language guidance appended
to the extraction prompt). The same `entityTypes` / `edgeTypes` parameters exist on
`AddEpisodeBulkAsync`. Extracted attributes land in `EntityNode.Attributes` /
`EntityEdge.Attributes`.

## The temporal model

Every fact is an `EntityEdge` (`Graphiti.Core.Models.Edges`) with three temporal markers:

- **`valid_at`** (`ValidAt`) — the event time from which the fact is considered true.
- **`invalid_at`** (`InvalidAt`) — the event time at which the fact stopped being true, if known.
- **`expired_at`** (`ExpiredAt`) — the transaction time at which the fact was superseded in the graph.

When a newly ingested episode states something that **contradicts** an existing fact, Graphiti does not
delete the old fact — it sets the old fact's `expired_at` (and, where applicable, `invalid_at`) and
adds the new one, preserving an auditable history. The OpenAI sample demonstrates this: it ingests a
March 15 rollout date, then a "rollout moved to March 29" episode, and the original date fact is
invalidated while the new one becomes current. You can inspect these markers directly:

```csharp
foreach (var edge in await EntityEdge.GetByGroupIdsAsync(graphiti.Driver, new[] { groupId }))
{
    Console.WriteLine($"{edge.Fact}");
    Console.WriteLine($"  valid_at={edge.ValidAt} invalid_at={edge.InvalidAt} expired_at={edge.ExpiredAt}");
}
```

`AddTripletAsync` adds a single `source → edge → target` fact directly (bypassing LLM extraction) and
runs the same deduplication and invalidation. `RemoveEpisodeAsync(episodeUuid)` removes an episode and
cleans up the graph elements only it produced (data still referenced by other episodes is retained).

## Samples & evaluation

Both samples use the in-memory driver and real OpenAI clients, and require `OPENAI_API_KEY`.

- **`samples/Graphiti.Sample.OpenAI`** — ingests a small fixture (the "Atlas rollout" story),
  prints the extracted entities and facts with their temporal markers, and runs a few searches.

  ```powershell
  $env:OPENAI_API_KEY = "..."
  dotnet run --project samples/Graphiti.Sample.OpenAI
  ```

- **`samples/Graphiti.Eval`** — an evaluation harness with two modes:
  - **default** — graph-building regression eval. Establishes a persisted baseline on first run, then
    judges (via an LLM) whether a later candidate extraction is *worse* than the baseline.
  - **`--qa`** — retrieval-QA eval. For each gold (question, answer) pair it retrieves facts, forms an
    answer from the top-1 fact, and scores it with an LLM judge. Includes a distractor question whose
    answer is absent from the fixture, to surface retrieval/judge leaks.

  ```powershell
  $env:OPENAI_API_KEY = "..."
  dotnet run --project samples/Graphiti.Eval          # graph-building regression
  dotnet run --project samples/Graphiti.Eval -- --qa  # retrieval QA
  ```

To run the full live-provider validation loop (restore, build, OpenAI integration tests, then the
OpenAI sample) in one command — it also loads a local, gitignored `.env` for `OPENAI_API_KEY`:

```powershell
$env:OPENAI_API_KEY = "..."
.\eng\Run-OpenAIProviderValidation.ps1
```

## Building & verifying

There is nothing to build for this README (markdown only), but to build and verify the library run the
full verifier from the `csharp` folder:

```powershell
.\eng\Verify-GraphitiCore.ps1
```

It runs restore, formatting checks, build, tests, package creation for both `Graphiti.Core` and
`Graphiti.Core.Drivers.Ladybug`, and a package-consumption smoke check. The smoke check creates
fresh temporary `net10.0` console projects with strict `NuGet.config` files (`<clear />`) and isolated
`NUGET_PACKAGES`: the core consumer restores, builds, and runs from the packed `Graphiti.Core` output
plus nuget.org, while the Ladybug consumer restores, builds, and runs from both packed Graphiti outputs
plus the local Ladybug feed and nuget.org. Use `-SkipPackageSmoke` only when iterating on non-packaging
changes. For a quick local test-only loop:

```powershell
dotnet test Graphiti.Core.CSharp.slnx
```

OpenAI provider integration tests live in `tests/Graphiti.Core.Tests` and skip unless `OPENAI_API_KEY`
is set. To run just those with real providers:

```powershell
$env:OPENAI_API_KEY = "..."
dotnet test Graphiti.Core.CSharp.slnx --filter "FullyQualifiedName~OpenAIProviderIntegrationTests"
```

> The `Graphiti.Core.Drivers.Ladybug` project and the LadybugDB tests restore `LadybugDB` /
> `LadybugDB.Native` from the local feed in [`NuGet.config`](NuGet.config). If those artifacts are
> missing, rebuild them from the sibling Ladybug checkout (see the
> [local-package caveat](#ladybugdb-local-package-caveat)). `Graphiti.Core` and the samples restore
> from nuget.org alone and are unaffected.

## Project layout

- `src/Graphiti.Core` — core library: models, the `Graphiti` orchestrator, the graph-driver contract
  with the InMemory and Neo4j drivers, search, maintenance helpers, and LLM/embedder/reranker
  contracts. LadybugDB-free (restores from nuget.org alone).
- `src/Graphiti.Core.Drivers.Ladybug` — opt-in LadybugDB graph driver: owns the `LadybugDB` /
  `LadybugDB.Native` package references, the driver/executor/statement implementation, and the
  `AddLadybugDbGraphDriver` DI helper.
- `tests/Graphiti.Core.Tests` — parity-oriented xUnit tests for ingestion, search/ranking, text
  utilities, provider infrastructure, serialization/cache behavior, and graph-driver contracts.
- `samples/Graphiti.Sample.OpenAI` — console host wiring the core to real OpenAI chat, embedding, and
  reranking providers via `Microsoft.Extensions.AI.OpenAI`.
- `samples/Graphiti.Eval` — graph-building and retrieval-QA evaluation harness.

### Namespace layout

Public types are organized into feature sub-namespaces under `Graphiti.Core`. The primary entry point
(`Graphiti`) and the exception hierarchy stay in the root `Graphiti.Core` namespace; everything else
lives under a matching sub-namespace:

| Sub-namespace | Contents |
|---|---|
| `Graphiti.Core` | `Graphiti` orchestrator, `GraphitiException` and the exception hierarchy |
| `Graphiti.Core.Models` | `EpisodeType`, `EntityTypeDefinition`, `EntityAttributeDefinition` |
| `Graphiti.Core.Models.Nodes` | `Node`, `EntityNode`, `EpisodicNode`, `CommunityNode`, `SagaNode` |
| `Graphiti.Core.Models.Edges` | `Edge`, `EntityEdge`, `EpisodicEdge`, `CommunityEdge`, `HasEpisodeEdge`, `NextEpisodeEdge` |
| `Graphiti.Core.Models.Results` | `AddEpisodeResults`, `AddBulkEpisodeResults`, `AddTripletResults`, `RawEpisode`, `GraphitiClients` |
| `Graphiti.Core.Drivers` | `IGraphDriver`, `GraphDriverBase`, `InMemoryGraphDriver`, `Neo4jGraphDriver`, `GraphProvider`, `SagaEpisodeContent` |
| `Graphiti.Core.Drivers.Ladybug` | `LadybugDbGraphDriverFactory` and the LadybugDB driver internals (ships in the separate `Graphiti.Core.Drivers.Ladybug` package) |
| `Graphiti.Core.Search` | search engine, configuration, filters, and reranking |
| `Graphiti.Core.LlmClients` | `ILlmClient`, `LlmClient`, `LlmConfig`, response caches, token usage |
| `Graphiti.Core.Embedding` | `IEmbedderClient`, `EmbedderClient`, `HashEmbedder` |
| `Graphiti.Core.CrossEncoder` | `ICrossEncoderClient`, `CrossEncoderClient` and rerankers |
| `Graphiti.Core.Maintenance` | dedup and community clustering |
| `Graphiti.Core.Prompts` | LLM prompt builders ported from Python `graphiti_core/prompts/` |
| `Graphiti.Core.Text` | content chunking, token counting, text helpers |
| `Graphiti.Core.Namespaces` | node/edge namespace facades |
| `Graphiti.Core.Telemetry` | `ActivitySource` and logging |
| `Graphiti.Core.Configuration` | options and DI registration |
| `Graphiti.Core.Serialization` | `System.Text.Json` context |

### Migrating from the flat `Graphiti.Core` namespace

Earlier prototypes exposed every type from the single flat `Graphiti.Core` namespace. In the `2.0.0`
line those types moved into the sub-namespaces above. This is a source-breaking change for external
consumers: add the sub-namespace `using` directives for the types you reference (for example
`using Graphiti.Core.Models.Nodes;` for `EntityNode`). LLM client types were also renamed from the
`LLM` prefix to `Llm` (`ILLMClient` → `ILlmClient`, `LLMConfig` → `LlmConfig`, `LLMResponseCache` →
`LlmResponseCache`, etc.). For consistency, the configuration section bound to `LlmConfig` is now `Llm`
(was `LLM`), and the chat telemetry span is now `Graphiti.Llm.GenerateResponse` (was
`Graphiti.LLM.GenerateResponse`).

## Relationship to Python Graphiti

This is a managed C# port of Python [`graphiti_core`](https://github.com/getzep/graphiti), not a native
binding. Python remains the **behavioral source of truth**: the C# port keeps the JSON/wire shape
(snake_case properties, enum wire values such as `fact_triple`), prompt and response-format identity,
ranking/search semantics (RRF, MMR, cross-encoder, node-distance, episode-mentions), and LLM cache-key
inputs compatible with Python, while staying idiomatic C# elsewhere. The C# port currently targets the
in-memory (reference/test) and LadybugDB (primary) drivers, keeps Neo4j as legacy reference coverage,
and treats FalkorDB/Neptune as compatibility surfaces only.

## License

Apache-2.0. See [`LICENSE`](LICENSE) and the `PackageLicenseExpression` in both shippable package
projects (`src/Graphiti.Core/Graphiti.Core.csproj` and
`src/Graphiti.Core.Drivers.Ladybug/Graphiti.Core.Drivers.Ladybug.csproj`), matching upstream
Graphiti.
