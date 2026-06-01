# Graphiti Core C# Port

This folder contains the C# port of `graphiti_core/` as a reusable .NET library.

## Projects

- `src/Graphiti.Core`: core library models, `Graphiti` orchestration, graph drivers, search, maintenance helpers, LLM/embedder/reranker contracts, and tested utility behavior.
- `src/Graphiti.Core`: LadybugDB driver that owns the LadybugDB
  package/native dependency boundary.
- `tests/Graphiti.Core.Tests`: parity-oriented xUnit tests for search config/filter defaults, text and content chunking, LLM base behavior, and in-memory graph workflows.

## Current Drivers

- `InMemoryGraphDriver`: executable deterministic driver for local embedding, ingestion, retrieval, search, triplets, saga links, and tests.
- `Neo4jGraphDriver`: Neo4j-backed semantic operation driver using `Neo4j.Driver`.
- `Graphiti.Core`: optional package exposing a LadybugDB-backed driver factory and DI
  helpers. Factory-backed Graphiti ingestion, attribution lookup, episode removal, and advanced
  search, plus direct triplet persistence/search, bulk duplicate-fact ingestion, and saga association
  plus summarization, community build/rebuild/search, and incremental community updates have initial
  end-to-end proof. Configured file-backed `DatabasePath` persistence is also package-proved, while
  the core DI provider switch supports LadybugDB.

## Namespace Layout

Public types are organized into feature sub-namespaces under `Graphiti.Core`. The primary
entry point (`Graphiti`) and the exception hierarchy stay in the root `Graphiti.Core` namespace;
everything else lives under a matching sub-namespace:

| Sub-namespace | Contents |
|---|---|
| `Graphiti.Core` | `Graphiti` orchestrator, `GraphitiException` and the error hierarchy |
| `Graphiti.Core.Models` | `EpisodeType`, `EntityTypeDefinition`, `EntityAttributeDefinition` |
| `Graphiti.Core.Models.Nodes` | `Node`, `EntityNode`, `EpisodicNode`, `CommunityNode`, `SagaNode` |
| `Graphiti.Core.Models.Edges` | `Edge`, `EntityEdge`, `EpisodicEdge`, `CommunityEdge`, `HasEpisodeEdge`, `NextEpisodeEdge` |
| `Graphiti.Core.Models.Results` | `AddEpisodeResults`, `AddBulkEpisodeResults`, `AddTripletResults`, `RawEpisode`, `GraphitiClients` |
| `Graphiti.Core.Drivers` | `IGraphDriver`, `GraphDriverBase`, `InMemoryGraphDriver`, `Neo4jGraphDriver`, `GraphProvider` |
| `Graphiti.Core.Search` | search engine, configuration, filters, and reranking |
| `Graphiti.Core.LlmClients` | `ILlmClient`, `LlmClient`, `LlmConfig`, response caches, token usage |
| `Graphiti.Core.Embedding` | `IEmbedderClient`, `EmbedderClient`, `HashEmbedder` |
| `Graphiti.Core.CrossEncoder` | `ICrossEncoderClient`, `CrossEncoderClient` and rerankers |
| `Graphiti.Core.Maintenance` | dedup and community clustering |
| `Graphiti.Core.Text` | content chunking, token counting, text helpers |
| `Graphiti.Core.Namespaces` | node/edge namespace facades |
| `Graphiti.Core.Telemetry` | `ActivitySource` and logging |
| `Graphiti.Core.Configuration` | options and DI registration |
| `Graphiti.Core.Serialization` | `System.Text.Json` context |

### Migrating from the flat `Graphiti.Core` namespace

Earlier prototypes exposed every type from the single flat `Graphiti.Core` namespace. As of
2.0.0 those types moved into the sub-namespaces above. This is a source-breaking change for
external consumers: add the sub-namespace `using` directives for the types you reference (for
example `using Graphiti.Core.Models.Nodes;` for `EntityNode`). LLM client types were also
renamed from the `LLM` prefix to `Llm` (`ILLMClient` -> `ILlmClient`, `LLMConfig` -> `LlmConfig`,
`LLMResponseCache` -> `LlmResponseCache`, etc.). For consistency with the renamed types, the
configuration section bound to `LlmConfig` is now `Llm` (was `LLM`), and the chat telemetry span
is now `Graphiti.Llm.GenerateResponse` (was `Graphiti.LLM.GenerateResponse`).

## Verify

```powershell
dotnet test csharp\Graphiti.Core.CSharp.slnx
```
