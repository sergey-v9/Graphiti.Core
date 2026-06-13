using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Drivers;
using Graphiti.Core.Embedding;
using Graphiti.Core.LlmClients;
using Graphiti.Core.Models;
using Graphiti.Core.Models.Edges;
using Graphiti.Core.Prompts;
using Microsoft.Extensions.AI;

// Eval harness for the Graphiti C# port. Ingests a small, self-contained fixture with known
// ground-truth facts, then for each gold (question, expected-answer) pair retrieves facts from the
// graph, forms a deterministic candidate answer, and scores it with the ported eval_prompt LLM judge
// (graphiti_core/prompts/eval.py). Prints per-question detail, an aggregate score, and a final
// machine-readable JSON line. Bounded and cheap: temperature 0, a handful of questions, no fan-out.
const string GroupId = "eval-atlas";

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set OPENAI_API_KEY before running the eval harness.");
    Console.Error.WriteLine("Optional: OPENAI_CHAT_MODEL, OPENAI_SMALL_MODEL, OPENAI_RERANKER_MODEL, OPENAI_EMBEDDING_MODEL, OPENAI_EMBEDDING_DIMENSIONS.");
    Console.Error.WriteLine("Usage: dotnet run --project samples/Graphiti.Eval");
    return 2;
}

var chatModel = GetEnvironmentValue("OPENAI_CHAT_MODEL", "gpt-4.1-mini");
var smallModel = GetEnvironmentValue("OPENAI_SMALL_MODEL", chatModel);
var rerankerModel = GetEnvironmentValue(
    "OPENAI_RERANKER_MODEL",
    MicrosoftExtensionsAICrossEncoderClient.DefaultModel);
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
var crossEncoder = new MicrosoftExtensionsAICrossEncoderClient(
    chatClient,
    new LlmConfig
    {
        Model = rerankerModel,
        SmallModel = rerankerModel,
        Temperature = 0,
        MaxTokens = 64
    });

await using var graphiti = new global::Graphiti.Core.Graphiti(
    llmClient: llmClient,
    embedder: embedder,
    crossEncoder: crossEncoder,
    graphDriver: new InMemoryGraphDriver("eval-atlas"),
    maxCoroutines: 2);

await graphiti.BuildIndicesAndConstraintsAsync(deleteExisting: true);

// --- Self-contained fixture with known ground truth -------------------------------------------
// Atlas rollout at Nimbus Health: Maya Patel manages the project, Leo Chen owns the deployment, and
// the rollout slipped from March 15 to March 29, 2026 after a QA-found authentication regression
// that QA later cleared on March 22, 2026.
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

// --- Gold (question, expected-answer) pairs ----------------------------------------------------
var goldPairs = new[]
{
    new GoldPair("Who owns the Atlas rollout?", "Leo Chen"),
    new GoldPair("What company is Atlas at?", "Nimbus Health"),
    new GoldPair("Who manages the Atlas migration project?", "Maya Patel"),
    new GoldPair("What is the current Atlas rollout date?", "March 29, 2026"),
    new GoldPair("Why was the Atlas rollout delayed?", "An authentication regression QA found"),
    new GoldPair("When did QA clear the authentication regression?", "March 22, 2026")
};

Console.WriteLine($"Using chat model {chatModel}, reranker model {rerankerModel}, embedding model {embeddingModel} ({embeddingDimensions.ToString(CultureInfo.InvariantCulture)} dimensions).");
Console.WriteLine("Ingesting fixture episodes...");
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

// --- Score each gold question against the LLM judge --------------------------------------------
var queryExpansionSchema = BuildSchema("QueryExpansion", ("query", "string"));
var evalSchema = BuildSchema("EvalResponse", ("is_correct", "boolean"), ("reasoning", "string"));

const int TopFacts = 5;
var passed = 0;

Console.WriteLine();
Console.WriteLine("Evaluating gold questions...");
foreach (var gold in goldPairs)
{
    // Optionally expand the question into a third-person retrieval query (eval.py::query_expansion).
    var expandedQuery = await ExpandQueryAsync(gold.Question);

    var hits = await graphiti.SearchAsync(
        expandedQuery,
        groupIds: new[] { GroupId },
        numResults: TopFacts);
    var topFacts = hits.Select(edge => edge.Fact).ToList();

    // Deterministic candidate answer: the retrieved facts joined into one response. eval_prompt marks
    // a response correct as long as it references the gold answer's topic, so concatenated facts are
    // the intended cheap candidate (no RAG chain).
    var candidateAnswer = topFacts.Count == 0
        ? "(no facts retrieved)"
        : string.Join(" ", topFacts);

    var verdict = await JudgeAsync(gold.Question, gold.ExpectedAnswer, candidateAnswer);
    if (verdict.IsCorrect)
    {
        passed++;
    }

    Console.WriteLine();
    Console.WriteLine($"Q: {gold.Question}");
    if (!string.Equals(expandedQuery, gold.Question, StringComparison.Ordinal))
    {
        Console.WriteLine($"  expanded query: {expandedQuery}");
    }

    Console.WriteLine("  retrieved facts:");
    if (topFacts.Count == 0)
    {
        Console.WriteLine("    (none)");
    }
    else
    {
        foreach (var fact in topFacts)
        {
            Console.WriteLine($"    - {fact}");
        }
    }

    Console.WriteLine($"  candidate answer: {candidateAnswer}");
    Console.WriteLine($"  gold answer:      {gold.ExpectedAnswer}");
    Console.WriteLine($"  verdict:          {(verdict.IsCorrect ? "PASS" : "FAIL")}");
    Console.WriteLine($"  reasoning:        {verdict.Reasoning}");
}

var total = goldPairs.Length;
var score = total == 0 ? 0d : (double)passed / total;

Console.WriteLine();
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Score: {passed}/{total} passed."));

// Machine-readable final line for the runner to parse.
var summary = new JsonObject
{
    ["passed"] = passed,
    ["total"] = total,
    ["score"] = Math.Round(score, 2)
};
Console.WriteLine(summary.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));

return 0;

async Task<string> ExpandQueryAsync(string question)
{
    try
    {
        var messages = EvalPrompts.BuildQueryExpansion(question);
        var response = await llmClient.GenerateResponseAsync(
            messages,
            responseSchema: queryExpansionSchema,
            modelSize: ModelSize.Small,
            promptName: "eval.query_expansion");
        var expanded = response["query"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(expanded) ? question : expanded;
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        // Query expansion is a best-effort optimization; fall back to the raw question.
        Console.Error.WriteLine($"  (query expansion failed, using raw question: {exception.Message})");
        return question;
    }
}

async Task<Verdict> JudgeAsync(string question, string goldAnswer, string candidateAnswer)
{
    var messages = EvalPrompts.BuildEval(question, goldAnswer, candidateAnswer);
    var response = await llmClient.GenerateResponseAsync(
        messages,
        responseSchema: evalSchema,
        promptName: "eval.eval_prompt");
    var isCorrect = response["is_correct"]?.GetValue<bool>() ?? false;
    var reasoning = response["reasoning"]?.GetValue<string>() ?? string.Empty;
    return new Verdict(isCorrect, reasoning);
}

static StructuredResponseSchema BuildSchema(string name, params (string Name, string Type)[] properties)
{
    var props = new JsonObject();
    var required = new JsonArray();
    foreach (var (propName, propType) in properties)
    {
        props[propName] = new JsonObject { ["type"] = propType };
        required.Add(propName);
    }

    var schema = new JsonObject
    {
        ["type"] = "object",
        ["properties"] = props,
        ["required"] = required,
        ["additionalProperties"] = false
    };
    return new StructuredResponseSchema(name, schema);
}

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

internal readonly record struct EpisodeInput(
    string Name,
    string Body,
    EpisodeType Source,
    string SourceDescription,
    DateTime ReferenceTime);

internal readonly record struct GoldPair(string Question, string ExpectedAnswer);

internal readonly record struct Verdict(bool IsCorrect, string Reasoning);
