# Graphiti OpenAI Sample

This console sample runs the C# core library against real OpenAI chat and embedding providers
through `Microsoft.Extensions.AI.OpenAI`.

## Run

```powershell
$env:OPENAI_API_KEY = "..."
dotnet run --project samples\Graphiti.Sample.OpenAI\Graphiti.Sample.OpenAI.csproj
```

Optional environment variables:

- `OPENAI_CHAT_MODEL` (default `gpt-4.1-mini`)
- `OPENAI_SMALL_MODEL` (default matches `OPENAI_CHAT_MODEL`)
- `OPENAI_EMBEDDING_MODEL` (default `text-embedding-3-small`)
- `OPENAI_EMBEDDING_DIMENSIONS` (default `1536`)

The sample uses `InMemoryGraphDriver`, ingests a short temporal conversation about a project whose
rollout date changes, then prints extracted entities, facts, summaries, and hybrid search results.
