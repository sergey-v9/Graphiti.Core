using System.Globalization;
using Graphiti.Core;
using Graphiti.Core.Drivers;
using Graphiti.Core.Embedding;
using Graphiti.Core.LlmClients;
using Graphiti.Core.Models;
using Graphiti.Core.Models.Edges;
using Graphiti.Core.Models.Nodes;
using Microsoft.Extensions.AI;

const string GroupId = "sample-openai";

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set OPENAI_API_KEY before running this sample.");
    Console.Error.WriteLine("Optional: OPENAI_CHAT_MODEL, OPENAI_SMALL_MODEL, OPENAI_EMBEDDING_MODEL, OPENAI_EMBEDDING_DIMENSIONS.");
    return 2;
}

var chatModel = GetEnvironmentValue("OPENAI_CHAT_MODEL", "gpt-4.1-mini");
var smallModel = GetEnvironmentValue("OPENAI_SMALL_MODEL", chatModel);
var embeddingModel = GetEnvironmentValue("OPENAI_EMBEDDING_MODEL", "text-embedding-3-small");
var embeddingDimensions = GetEnvironmentInt("OPENAI_EMBEDDING_DIMENSIONS", 1536);

var chatClient = new OpenAI.Chat.ChatClient(chatModel, apiKey).AsIChatClient();
var embeddingGenerator = new OpenAI.Embeddings.EmbeddingClient(embeddingModel, apiKey)
    .AsIEmbeddingGenerator(embeddingDimensions);

var llmClient = new MicrosoftExtensionsAIChatClient(
    chatClient,
    new LlmConfig
    {
        Model = chatModel,
        SmallModel = smallModel,
        Temperature = 0
    });
var embedder = new MicrosoftExtensionsAIEmbedderClient(
    embeddingGenerator,
    embeddingDimensions,
    modelId: embeddingModel,
    batchSize: 16,
    batchConcurrency: 2);

await using var graphiti = new global::Graphiti.Core.Graphiti(
    llmClient: llmClient,
    embedder: embedder,
    graphDriver: new InMemoryGraphDriver("sample-openai"),
    maxCoroutines: 2);

await graphiti.BuildIndicesAndConstraintsAsync(deleteExisting: true);

var start = new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc);
var episodes = new[]
{
    new EpisodeInput(
        "Intro",
        "User: My name is Maya Patel. I manage the Atlas migration project at Nimbus Health.",
        EpisodeType.Message,
        "chat transcript",
        start),
    new EpisodeInput(
        "Atlas owner",
        "User: Leo Chen from Operations is the deployment owner for Atlas. The first rollout window is March 15, 2026.",
        EpisodeType.Message,
        "chat transcript",
        start.AddMinutes(5)),
    new EpisodeInput(
        "Rollout change",
        "User: Change of plan: the Atlas rollout moved from March 15, 2026 to March 29, 2026 because QA found authentication issues.",
        EpisodeType.Message,
        "chat transcript",
        start.AddMinutes(10)),
    new EpisodeInput(
        "Project status",
        """
        {
          "project": "Atlas migration",
          "company": "Nimbus Health",
          "status": "blocked",
          "blocker": "authentication regression",
          "owner": "Leo Chen",
          "review_date": "2026-03-22"
        }
        """,
        EpisodeType.Json,
        "project status JSON",
        start.AddMinutes(15)),
    new EpisodeInput(
        "QA cleared",
        "User: QA cleared the Atlas authentication regression on March 22, 2026. Maya asked Leo to prepare the March 29 launch checklist.",
        EpisodeType.Message,
        "chat transcript",
        start.AddMinutes(20)),
    new EpisodeInput(
        "Memory check",
        "Assistant: For future Atlas questions, remember Maya Patel, Leo Chen, Nimbus Health, the March 29 rollout, and that QA cleared the auth issue.",
        EpisodeType.Message,
        "assistant memory note",
        start.AddMinutes(25))
};

Console.WriteLine($"Using chat model {chatModel}, embedding model {embeddingModel} ({embeddingDimensions.ToString(CultureInfo.InvariantCulture)} dimensions).");
Console.WriteLine("Ingesting episodes...");
foreach (var episode in episodes)
{
    var result = await graphiti.AddEpisodeAsync(
        episode.Name,
        episode.Body,
        episode.SourceDescription,
        episode.ReferenceTime,
        episode.Source,
        groupId: GroupId);

    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"  {episode.Name}: {result.Nodes.Count} node(s), {result.Edges.Count} edge(s)"));
}

var nodes = await EntityNode.GetByGroupIdsAsync(graphiti.Driver, new[] { GroupId });
var edges = await EntityEdge.GetByGroupIdsAsync(graphiti.Driver, new[] { GroupId });

Console.WriteLine();
Console.WriteLine("Entities");
foreach (var node in nodes.OrderBy(node => node.Name, StringComparer.Ordinal))
{
    Console.WriteLine($"- {node.Name} [{string.Join(", ", node.Labels)}]");
    if (!string.IsNullOrWhiteSpace(node.Summary))
    {
        Console.WriteLine($"  {node.Summary}");
    }
}

Console.WriteLine();
Console.WriteLine("Facts");
foreach (var edge in edges.OrderBy(edge => edge.Fact, StringComparer.Ordinal))
{
    Console.WriteLine($"- {edge.Fact}");
    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"  valid_at={FormatDate(edge.ValidAt)} invalid_at={FormatDate(edge.InvalidAt)} expired_at={FormatDate(edge.ExpiredAt)}"));
}

var queries = new[]
{
    "Who owns the Atlas rollout?",
    "What is the current Atlas rollout date?",
    "Why was Atlas blocked?"
};

Console.WriteLine();
Console.WriteLine("Search");
foreach (var query in queries)
{
    var results = await graphiti.SearchAsync(query, groupIds: new[] { GroupId }, numResults: 5);
    Console.WriteLine($"Query: {query}");
    foreach (var edge in results)
    {
        Console.WriteLine($"- {edge.Fact}");
    }
}

return 0;

static string GetEnvironmentValue(string name, string fallback)
{
    var value = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(value) ? fallback : value;
}

static int GetEnvironmentInt(string name, int fallback)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
    {
        return fallback;
    }

    return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
        ? parsed
        : throw new InvalidOperationException($"{name} must be a positive integer.");
}

static string FormatDate(DateTime? value) =>
    value is null ? "null" : value.Value.ToString("O", CultureInfo.InvariantCulture);

internal readonly record struct EpisodeInput(
    string Name,
    string Body,
    EpisodeType Source,
    string SourceDescription,
    DateTime ReferenceTime);
