# Search

Detail on Graphiti's hybrid search: the entry points, the `SearchConfig` shape, the ready-made
recipes, the rerankers, and the filters. See the [README](../README.md#search) for the quick version.

All types below live in `Graphiti.Core.Search` unless noted, and are implemented in
`src/Graphiti.Core/Search/`.

## Entry points

`Graphiti` (`src/Graphiti.Core/Graphiti.Search.cs`) exposes three search methods.

### `SearchAsync(query, ...)` — facts only

Returns the most relevant facts as `IReadOnlyList<EntityEdge>`.

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

Internally it picks `SearchConfigRecipes.EdgeHybridSearchRrf` when `centerNodeUuid` is null, and
`SearchConfigRecipes.EdgeHybridSearchNodeDistance` when a center node is supplied, then sets the recipe
`Limit` to `numResults`.

### `SearchAsync(query, config, ...)` / `SearchAdvancedAsync(query, config = null, ...)` — combined results

Returns a `SearchResults` containing `Edges`, `Nodes`, `Episodes`, and `Communities`, driven by an
explicit `SearchConfig`.

```csharp
public Task<SearchResults> SearchAdvancedAsync(
    string query,
    SearchConfig? config = null,
    IReadOnlyList<string>? groupIds = null,
    string? centerNodeUuid = null,
    IReadOnlyList<string>? bfsOriginNodeUuids = null,
    SearchFilters? searchFilter = null,
    IReadOnlyList<float>? queryVector = null,
    IGraphDriver? driver = null,
    CancellationToken cancellationToken = default);
```

When `config` is omitted, `SearchAdvancedAsync` uses `SearchConfigRecipes.CombinedHybridSearchCrossEncoder`
(the Python `search_` default: combined hybrid search with cross-encoder reranking). Pass a precomputed
`queryVector` to skip re-embedding the query.

### `GetNodesAndEdgesByEpisodeAsync(episodeUuids, ...)`

Returns the entities and facts attributed to the given episodes as a `SearchResults` — useful for
inspecting what a specific episode contributed (no ranking involved).

## Context formatting

`SearchHelpers` exposes the Python `graphiti_core.search.search_helpers` conveniences for callers
that pass search output directly to an LLM:

- `FormatEdgeDateRange(EntityEdge)` returns `date unknown - present` when both temporal endpoints are
  missing, otherwise formats the edge's `ValidAt` / `InvalidAt` window with Python-style labels.
- `SearchResultsToContextString(SearchResults)` renders the `<FACTS>`, `<ENTITIES>`, `<EPISODES>`,
  and `<COMMUNITIES>` sections used by Python's `search_results_to_context_string`.

The context JSON uses Graphiti Core's canonical compact prompt JSON serializer; that intentionally
matches the C# prompt stack's compact-output decision while preserving Python's field names and null
date labels (`None` for missing `valid_at`, `Present` for missing `invalid_at`).

## `SearchConfig`

`SearchConfig` (`src/Graphiti.Core/Search/SearchConfig.cs`) turns on each result type by supplying the
matching per-type config; leave one `null` to skip that result type.

| Property | Type | Meaning |
|---|---|---|
| `EdgeConfig` | `EdgeSearchConfig?` | fact (edge) search settings |
| `NodeConfig` | `NodeSearchConfig?` | entity (node) search settings |
| `EpisodeConfig` | `EpisodeSearchConfig?` | episode search settings |
| `CommunityConfig` | `CommunitySearchConfig?` | community search settings |
| `Limit` | `int` | max results per result type (default `SearchConfiguration.DefaultSearchLimit`) |
| `RerankerMinScore` | `double` | minimum reranker score a result must reach to be kept |

Each per-type config carries a list of `SearchMethods` (BM25, cosine similarity, BFS) and a `Reranker`.
The recipes below set sensible combinations; you can build a `SearchConfig` by hand for full control.

## Recipes

`SearchConfigRecipes` (`src/Graphiti.Core/Search/SearchConfigRecipes.cs`) returns a fresh `SearchConfig`
each time, so you can mutate `Limit` / `RerankerMinScore` without affecting other callers.

### Combined (edges + nodes + episodes + communities)

| Recipe | Search methods | Reranker |
|---|---|---|
| `CombinedHybridSearchRrf` | BM25 + cosine (BM25 only for episodes) | RRF |
| `CombinedHybridSearchMmr` | BM25 + cosine | MMR (`MmrLambda = 1`; episodes use RRF) |
| `CombinedHybridSearchCrossEncoder` | BM25 + cosine + **BFS** (edges/nodes) | cross-encoder |

### Edges only (`EdgeHybridSearch*`)

`Rrf`, `Mmr`, `NodeDistance`, `EpisodeMentions`, `CrossEncoder`. The cross-encoder variant adds BFS and
defaults `Limit = 10`.

### Nodes only (`NodeHybridSearch*`)

`Rrf`, `Mmr`, `NodeDistance`, `EpisodeMentions`, `CrossEncoder`. The cross-encoder variant adds BFS and
defaults `Limit = 10`.

### Communities only (`CommunityHybridSearch*`)

`Rrf`, `Mmr`, `CrossEncoder`. The cross-encoder variant defaults `Limit = 3`.

## Rerankers

| Reranker | What it does | Needs |
|---|---|---|
| RRF | Reciprocal rank fusion across the enabled search methods | — |
| MMR | Maximal marginal relevance — trades relevance for diversity (`MmrLambda`) | — |
| Cross-encoder | Reranks candidates with the chat model | a real `ICrossEncoderClient` (e.g. `MicrosoftExtensionsAICrossEncoderClient`) |
| Node-distance | Biases toward graph proximity to a node | `centerNodeUuid` |
| Episode-mentions | Favors items mentioned across more episodes | — |

With the default identity cross-encoder (no provider wired), the cross-encoder recipes still run but
fall back to the lexical/identity ordering.

## Filters

`SearchFilters` (`src/Graphiti.Core/Search/SearchFilters.cs`) constrains candidates before ranking:

- `NodeLabels` (`List<string>?`) — restrict to nodes carrying these labels; validated on assignment.
- `EdgeTypes` (`List<string>?`) — restrict to edges of these relationship types.
- `EdgeUuids` (`List<string>?`) — restrict to specific edge UUIDs.
- `ValidAt`, `InvalidAt`, `CreatedAt`, `ExpiredAt` (`List<List<DateFilter>>?`) — temporal predicates.
  Each inner list is combined with AND, and the outer list is combined with OR (matching Python filter
  semantics).

For `EdgeTypes` and `EdgeUuids`, `null` means no predicate; an explicitly empty list is an active
empty predicate and matches no edges, matching Python's filter constructors.

```csharp
var filters = new SearchFilters
{
    EdgeTypes = new List<string> { "OWNS" },
};

var results = await graphiti.SearchAsync(
    "Who owns Atlas?",
    config: SearchConfigRecipes.EdgeHybridSearchRrf,
    groupIds: new[] { "demo" },
    searchFilter: filters);
```
