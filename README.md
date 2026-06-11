# Graphiti Core C# Port

This folder contains the C# port of `graphiti_core/` as a reusable .NET library.

## Projects

- `src/Graphiti.Core`: core library models, `Graphiti` orchestration, graph drivers, search, maintenance helpers, LLM/embedder/reranker contracts, LadybugDB integration, and tested utility behavior.
- `tests/Graphiti.Core.Tests`: parity-oriented xUnit tests for ingestion workflows, search and ranking behavior, text utilities, provider infrastructure, serialization/cache behavior, and graph-driver contracts.
- `samples/Graphiti.Sample.OpenAI`: console host that wires the C# core to real OpenAI chat and embedding providers through `Microsoft.Extensions.AI.OpenAI`.

## Current Drivers

- `InMemoryGraphDriver`: executable deterministic driver for local embedding, ingestion, retrieval, search, triplets, saga links, and tests.
- `Neo4jGraphDriver`: existing Neo4j-backed reference driver using `Neo4j.Driver`; kept working while
  present, but not a current provider investment target.
- LadybugDB: primary provider target with a core LadybugDB-backed driver, factory, and DI helpers.
  Graphiti ingestion, attribution
  lookup, episode removal, advanced search, direct triplet persistence/search, bulk duplicate-fact
  ingestion, saga association plus summarization, community build/rebuild/search, incremental
  community updates, and configured file-backed `DatabasePath` persistence have end-to-end proof.

## Namespace Layout

Public types are organized into feature sub-namespaces under `Graphiti.Core`. The primary
entry point (`Graphiti`) and the exception hierarchy stay in the root `Graphiti.Core` namespace;
everything else lives under a matching sub-namespace:

| Sub-namespace | Contents |
|---|---|
| `Graphiti.Core` | `Graphiti` orchestrator, `GraphitiException` and the exception hierarchy |
| `Graphiti.Core.Models` | `EpisodeType`, `EntityTypeDefinition`, `EntityAttributeDefinition` |
| `Graphiti.Core.Models.Nodes` | `Node`, `EntityNode`, `EpisodicNode`, `CommunityNode`, `SagaNode` |
| `Graphiti.Core.Models.Edges` | `Edge`, `EntityEdge`, `EpisodicEdge`, `CommunityEdge`, `HasEpisodeEdge`, `NextEpisodeEdge` |
| `Graphiti.Core.Models.Results` | `AddEpisodeResults`, `AddBulkEpisodeResults`, `AddTripletResults`, `RawEpisode`, `GraphitiClients` |
| `Graphiti.Core.Drivers` | `IGraphDriver`, `GraphDriverBase`, `InMemoryGraphDriver`, `Neo4jGraphDriver`, `GraphProvider`, `SagaEpisodeContent` |
| `Graphiti.Core.Drivers.Ladybug` | LadybugDB driver factory and provider internals |
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

Earlier prototypes exposed every type from the single flat `Graphiti.Core` namespace. In the
2.0.0 line those types moved into the sub-namespaces above. This is a source-breaking change for
external consumers: add the sub-namespace `using` directives for the types you reference (for
example `using Graphiti.Core.Models.Nodes;` for `EntityNode`). LLM client types were also
renamed from the `LLM` prefix to `Llm` (`ILLMClient` -> `ILlmClient`, `LLMConfig` -> `LlmConfig`,
`LLMResponseCache` -> `LlmResponseCache`, etc.). For consistency with the renamed types, the
configuration section bound to `LlmConfig` is now `Llm` (was `LLM`), and the chat telemetry span
is now `Graphiti.Llm.GenerateResponse` (was `Graphiti.LLM.GenerateResponse`).

## Verify

```powershell
.\eng\Verify-GraphitiCore.ps1
```

The verifier runs restore, formatting checks, build, tests, and package creation. For a quick local
test-only loop from this folder, use `dotnet test Graphiti.Core.CSharp.slnx`.
