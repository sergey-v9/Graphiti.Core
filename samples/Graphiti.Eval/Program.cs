using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Drivers;
using Graphiti.Core.Embedding;
using Graphiti.Core.LlmClients;
using Graphiti.Core.Models;
using Graphiti.Core.Models.Results;
using Graphiti.Core.Prompts;
using Graphiti.Core.Serialization;
using Microsoft.Extensions.AI;

// Eval harness for the Graphiti C# port. Two modes:
//
//   (default)  GRAPH-BUILDING regression eval. Ingests the fixture episodes to
//              build a CANDIDATE graph, capturing each AddEpisodeResults. A persisted baseline
//              artifact (eval-artifacts/baseline_graph_results.json) provides the BASELINE: if it
//              exists it is loaded and the eval_add_episode_results LLM judge decides, per episode,
//              whether the candidate extraction is WORSE than the baseline; otherwise this run writes
//              the artifact and establishes the baseline (no judging). Score = fraction of episodes
//              whose candidate is NOT worse.
//
//   --qa       RETRIEVAL-QA eval. Ingests the same fixture, and for each gold (question, answer) pair
//              retrieves facts, forms a candidate answer from the TOP-1 retrieved fact only, and scores
//              it with the eval_prompt LLM judge. Includes a distractor question whose gold
//              answer is NOT in the fixture: a perfect pass there would signal a retrieval/judge leak.
//
// Both modes are gated on OPENAI_API_KEY (exit 2 if absent), use temperature 0, and bound the
// question/episode counts. The runner injects OPENAI_API_KEY; this program never reads .env itself.
const string GroupId = "eval-atlas";

var qaMode = args.Any(arg => string.Equals(arg, "--qa", StringComparison.OrdinalIgnoreCase))
    || string.Equals(
        Environment.GetEnvironmentVariable("GRAPHITI_EVAL_MODE"),
        "qa",
        StringComparison.OrdinalIgnoreCase);

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Set OPENAI_API_KEY before running the eval harness.");
    Console.Error.WriteLine("Optional: OPENAI_CHAT_MODEL, OPENAI_SMALL_MODEL, OPENAI_RERANKER_MODEL, OPENAI_EMBEDDING_MODEL, OPENAI_EMBEDDING_DIMENSIONS.");
    Console.Error.WriteLine("Modes: default = graph-building regression eval; --qa = retrieval-QA eval.");
    Console.Error.WriteLine("Usage: dotnet run --project samples/Graphiti.Eval [--qa]");
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

Console.WriteLine($"Using chat model {chatModel}, reranker model {rerankerModel}, embedding model {embeddingModel} ({embeddingDimensions.ToString(CultureInfo.InvariantCulture)} dimensions).");
Console.WriteLine($"Mode: {(qaMode ? "retrieval-QA (measures whether top-1 retrieved fact answers a gold question)" : "graph-building regression (measures whether the candidate extraction is worse than a persisted baseline)")}.");
Console.WriteLine("Ingesting fixture episodes...");

// Ingest every episode through the real pipeline, capturing each per-episode AddEpisodeResults.
var addResults = new List<AddEpisodeResults>(episodes.Length);
foreach (var episode in episodes)
{
    var result = await graphiti.AddEpisodeAsync(
        episode.Name,
        episode.Body,
        episode.SourceDescription,
        episode.ReferenceTime,
        episode.Source,
        groupId: GroupId);

    addResults.Add(result);
    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"  {episode.Name}: {result.Nodes.Count} node(s), {result.Edges.Count} edge(s)"));
}

return qaMode
    ? await RunRetrievalQaAsync()
    : await RunGraphBuildingAsync();

// ===============================================================================================
// GRAPH-BUILDING regression eval (default).
// ===============================================================================================
async Task<int> RunGraphBuildingAsync()
{
    // Persisted-artifact baseline: stable, gitignored path. The first run establishes it; later runs
    // load it and judge the current (candidate) extraction against it for cross-run regression
    // detection, writing baseline_graph_results.json / candidate_graph_results.json.
    var artifactDir = Path.Combine(AppContext.BaseDirectory, "eval-artifacts");
    Directory.CreateDirectory(artifactDir);
    var baselinePath = Path.Combine(artifactDir, "baseline_graph_results.json");
    var candidatePath = Path.Combine(artifactDir, "candidate_graph_results.json");

    // Serialize the candidate per-episode extraction results with stable snake_case Graphiti JSON,
    // nulling embeddings first (name_embedding/fact_embedding are cleared before serializing).
    var candidateJsonByEpisode = episodes
        .Select((episode, index) => SerializeResult(addResults[index]))
        .ToList();

    var candidateArtifact = new JsonArray();
    for (var i = 0; i < candidateJsonByEpisode.Count; i++)
    {
        candidateArtifact.Add(JsonNode.Parse(candidateJsonByEpisode[i]));
    }

    await File.WriteAllTextAsync(
        candidatePath,
        candidateArtifact.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    if (!File.Exists(baselinePath))
    {
        await File.WriteAllTextAsync(
            baselinePath,
            candidateArtifact.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine();
        Console.WriteLine($"baseline established: wrote {episodes.Length} episode result(s) to {baselinePath}");
        Console.WriteLine("Re-run to judge a future candidate against this baseline. No judging this run.");
        var established = new JsonObject
        {
            ["mode"] = "graph_building",
            ["baseline_established"] = true,
            ["episodes"] = episodes.Length
        };
        Console.WriteLine(established.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        return 0;
    }

    var baselineRaw = await File.ReadAllTextAsync(baselinePath);
    var baselineArtifact = JsonNode.Parse(baselineRaw) as JsonArray
        ?? throw new InvalidOperationException($"Baseline artifact {baselinePath} is not a JSON array.");
    var baselineJsonByEpisode = baselineArtifact
        .Select(node => node?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "null")
        .ToList();

    Console.WriteLine();
    Console.WriteLine($"Loaded baseline from {baselinePath} ({baselineJsonByEpisode.Count} episode result(s)).");
    Console.WriteLine("Judging candidate extraction against baseline (eval_add_episode_results)...");

    var episodeCount = Math.Min(baselineJsonByEpisode.Count, candidateJsonByEpisode.Count);
    var notWorse = 0;
    for (var i = 0; i < episodeCount; i++)
    {
        // Context is the current MESSAGE plus the PREVIOUS messages, with the BASELINE and CANDIDATE
        // graph extractions to compare. Pass the real message body and the joined prior bodies.
        var message = episodes[i].Body;
        var previousMessages = i == 0
            ? "[]"
            : "[" + string.Join(", ", episodes.Take(i).Select(e => JsonSerializer.Serialize(e.Body))) + "]";

        var verdict = await JudgeCandidateWorseAsync(
            previousMessages,
            message,
            baselineJsonByEpisode[i],
            candidateJsonByEpisode[i]);

        if (!verdict.CandidateIsWorse)
        {
            notWorse++;
        }

        Console.WriteLine();
        Console.WriteLine($"Episode {(i + 1).ToString(CultureInfo.InvariantCulture)}: {episodes[i].Name}");
        Console.WriteLine($"  candidate worse than baseline: {(verdict.CandidateIsWorse ? "YES (regression)" : "no")}");
        Console.WriteLine($"  reasoning: {verdict.Reasoning}");
    }

    var score = episodeCount == 0 ? 1d : (double)notWorse / episodeCount;
    Console.WriteLine();
    Console.WriteLine(string.Create(
        CultureInfo.InvariantCulture,
        $"Graph-building score (fraction not worse than baseline): {notWorse}/{episodeCount}."));
    Console.WriteLine($"Candidate artifact written to {candidatePath}.");

    var summary = new JsonObject
    {
        ["mode"] = "graph_building",
        ["episodes"] = episodeCount,
        ["not_worse"] = notWorse,
        ["score"] = Math.Round(score, 2)
    };
    Console.WriteLine(summary.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    return 0;
}

// ===============================================================================================
// RETRIEVAL-QA eval (--qa). Secondary mode. Measures whether the TOP-1 retrieved fact answers each
// gold question; includes a distractor whose answer is absent from the fixture.
// ===============================================================================================
async Task<int> RunRetrievalQaAsync()
{
    // Gold (question, expected-answer) pairs. The final pair is a DISTRACTOR: its answer ("Helsinki")
    // is nowhere in the fixture, so it should FAIL. A PASS there signals a retrieval or judge leak.
    var goldPairs = new[]
    {
        new GoldPair("Who owns the Atlas rollout?", "Leo Chen", InFixture: true),
        new GoldPair("What company is Atlas at?", "Nimbus Health", InFixture: true),
        new GoldPair("Who manages the Atlas migration project?", "Maya Patel", InFixture: true),
        new GoldPair("What is the current Atlas rollout date?", "March 29, 2026", InFixture: true),
        new GoldPair("Why was the Atlas rollout delayed?", "An authentication regression QA found", InFixture: true),
        new GoldPair("When did QA clear the authentication regression?", "March 22, 2026", InFixture: true),
        // Distractor: the fixture never mentions a city; the only correct judge verdict is FAIL.
        new GoldPair("In which city is the Atlas data center located?", "Helsinki", InFixture: false)
    };

    var queryExpansionSchema = BuildSchema("QueryExpansion", ("query", "string"));
    var evalSchema = BuildSchema("EvalResponse", ("is_correct", "boolean"), ("reasoning", "string"));

    var passed = 0;
    var leakDetected = false;

    Console.WriteLine();
    Console.WriteLine("Evaluating gold questions (candidate answer = top-1 retrieved fact only)...");
    foreach (var gold in goldPairs)
    {
        var expandedQuery = await ExpandQueryAsync(gold.Question, queryExpansionSchema);

        var hits = await graphiti.SearchAsync(
            expandedQuery,
            groupIds: new[] { GroupId },
            numResults: 5);

        // Build the candidate answer from ONLY the single top-ranked fact, not a concatenation of all
        // retrieved facts. Concatenating every fact inflates pass rates because the gold topic is far
        // more likely to appear somewhere in the union.
        var topFact = hits.Count == 0 ? null : hits[0].Fact;
        var candidateAnswer = topFact ?? "(no facts retrieved)";

        var verdict = await JudgeQaAsync(gold.Question, gold.ExpectedAnswer, candidateAnswer, evalSchema);
        if (verdict.IsCorrect)
        {
            passed++;
            if (!gold.InFixture)
            {
                // A distractor judged correct means a retrieval/judge leak, not real quality.
                leakDetected = true;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Q: {gold.Question}{(gold.InFixture ? string.Empty : "  [DISTRACTOR - expected FAIL]")}");
        if (!string.Equals(expandedQuery, gold.Question, StringComparison.Ordinal))
        {
            Console.WriteLine($"  expanded query: {expandedQuery}");
        }

        Console.WriteLine($"  top-1 fact:       {topFact ?? "(none)"}");
        Console.WriteLine($"  candidate answer: {candidateAnswer}");
        Console.WriteLine($"  gold answer:      {gold.ExpectedAnswer}");
        Console.WriteLine($"  verdict:          {(verdict.IsCorrect ? "PASS" : "FAIL")}");
        Console.WriteLine($"  reasoning:        {verdict.Reasoning}");
    }

    var total = goldPairs.Length;
    var score = total == 0 ? 0d : (double)passed / total;

    Console.WriteLine();
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Retrieval-QA score: {passed}/{total} passed."));
    if (leakDetected)
    {
        Console.WriteLine("WARNING: a distractor question PASSED - this indicates a retrieval or judge leak.");
    }

    var summary = new JsonObject
    {
        ["mode"] = "retrieval_qa",
        ["passed"] = passed,
        ["total"] = total,
        ["score"] = Math.Round(score, 2),
        ["distractor_leak"] = leakDetected
    };
    Console.WriteLine(summary.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    return 0;
}

// --- Helpers -----------------------------------------------------------------------------------

string SerializeResult(AddEpisodeResults result)
{
    // Clear embeddings so the artifact is stable and free of float noise (name_embedding/
    // fact_embedding are nulled before serializing). Only the result graph shape is compared.
    foreach (var node in result.Nodes)
    {
        node.NameEmbedding = null;
    }

    foreach (var edge in result.Edges)
    {
        edge.FactEmbedding = null;
    }

    return JsonSerializer.Serialize(result, GraphitiJsonSerializer.Options);
}

async Task<CandidateVerdict> JudgeCandidateWorseAsync(
    string previousMessages,
    string message,
    string baselineJson,
    string candidateJson)
{
    var schema = BuildSchema(
        "EvalAddEpisodeResults",
        ("candidate_is_worse", "boolean"),
        ("reasoning", "string"));
    var messages = EvalPrompts.BuildEvalAddEpisodeResults(
        previousMessages,
        message,
        baselineJson,
        candidateJson);
    var response = await llmClient.GenerateResponseAsync(
        messages,
        responseSchema: schema,
        promptName: "eval.eval_add_episode_results");
    var candidateIsWorse = response["candidate_is_worse"]?.GetValue<bool>() ?? false;
    var reasoning = response["reasoning"]?.GetValue<string>() ?? string.Empty;
    return new CandidateVerdict(candidateIsWorse, reasoning);
}

async Task<string> ExpandQueryAsync(string question, StructuredResponseSchema schema)
{
    try
    {
        var messages = EvalPrompts.BuildQueryExpansion(question);
        var response = await llmClient.GenerateResponseAsync(
            messages,
            responseSchema: schema,
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

async Task<Verdict> JudgeQaAsync(
    string question,
    string goldAnswer,
    string candidateAnswer,
    StructuredResponseSchema schema)
{
    var messages = EvalPrompts.BuildEval(question, goldAnswer, candidateAnswer);
    var response = await llmClient.GenerateResponseAsync(
        messages,
        responseSchema: schema,
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

internal readonly record struct GoldPair(string Question, string ExpectedAnswer, bool InFixture);

internal readonly record struct Verdict(bool IsCorrect, string Reasoning);

internal readonly record struct CandidateVerdict(bool CandidateIsWorse, string Reasoning);
