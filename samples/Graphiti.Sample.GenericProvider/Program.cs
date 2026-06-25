using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Drivers;
using Graphiti.Core.Embedding;
using Graphiti.Core.LlmClients;
using Graphiti.Core.Models;
using Microsoft.Extensions.AI;

const string GroupId = "generic-provider";
const int EmbeddingDimensions = 8;

using var chatClient = new LocalChatClient();
using var embeddingGenerator = new LocalEmbeddingGenerator(EmbeddingDimensions);

var llmClient = new MicrosoftExtensionsAIChatClient(
    chatClient,
    new LlmConfig
    {
        Model = "local-fixture-chat",
        SmallModel = "local-fixture-chat",
        Temperature = 0
    });
var embedder = new MicrosoftExtensionsAIEmbedderClient(
    embeddingGenerator,
    EmbeddingDimensions,
    modelId: "local-fixture-embeddings");

await using var graphiti = new global::Graphiti.Core.Graphiti(
    llmClient: llmClient,
    embedder: embedder,
    crossEncoder: new IdentityCrossEncoderClient(),
    graphDriver: new InMemoryGraphDriver("generic-provider"),
    maxCoroutines: 2);

await graphiti.BuildIndicesAndConstraintsAsync(deleteExisting: true);
await graphiti.AddEpisodeAsync(
    name: "Local provider episode",
    episodeBody: "Maya Patel manages the Atlas migration project at Nimbus Health.",
    sourceDescription: "fixture transcript",
    referenceTime: new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc),
    source: EpisodeType.Message,
    groupId: GroupId);

var facts = await graphiti.SearchAsync(
    "Who manages Atlas?",
    groupIds: new[] { GroupId },
    numResults: 3);

Console.WriteLine("Generic provider facts");
foreach (var fact in facts)
{
    Console.WriteLine($"- {fact.Fact}");
}

sealed class LocalChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        _ = options;
        cancellationToken.ThrowIfCancellationRequested();
        var prompt = messages.LastOrDefault()?.Text ?? string.Empty;
        var response = prompt.Contains("<ENTITIES>", StringComparison.Ordinal)
            ? """
              {
                "edges": [
                  {
                    "source_entity_name": "Maya Patel",
                    "target_entity_name": "Atlas migration",
                    "relation_type": "MANAGES",
                    "fact": "Maya Patel manages the Atlas migration project.",
                    "valid_at": "2026-01-10T09:00:00Z"
                  }
                ]
              }
              """
            : prompt.Contains("<CURRENT MESSAGE>", StringComparison.Ordinal)
                ? """
                  {
                    "extracted_entities": [
                      { "name": "Maya Patel", "entity_type_id": 0 },
                      { "name": "Atlas migration", "entity_type_id": 0 }
                    ]
                  }
                  """
                : "{}";

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response))
        {
            Usage = new UsageDetails
            {
                InputTokenCount = CountTokens(messages),
                OutputTokenCount = response.Length / 4
            }
        });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = messages;
        _ = options;
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        _ = serviceType;
        _ = serviceKey;
        return null;
    }

    public void Dispose()
    {
    }

    private static long CountTokens(IEnumerable<ChatMessage> messages)
    {
        var total = 0L;
        foreach (var message in messages)
        {
            total += message.Text?.Length / 4 ?? 0;
        }

        return total;
    }
}

sealed class LocalEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly int _dimensions;

    public LocalEmbeddingGenerator(int dimensions) => _dimensions = dimensions;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dimensions = options?.Dimensions ?? _dimensions;
        var embeddings = values.Select(value => new Embedding<float>(Embed(value, dimensions)));
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        _ = serviceType;
        _ = serviceKey;
        return null;
    }

    public void Dispose()
    {
    }

    private static float[] Embed(string value, int dimensions)
    {
        var vector = new float[dimensions];
        for (var i = 0; i < value.Length; i++)
        {
            vector[i % dimensions] += char.ToUpperInvariant(value[i]) * (i + 1);
        }

        var norm = VectorNorm(vector);
        if (norm == 0)
        {
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= norm;
        }

        return vector;
    }

    private static float VectorNorm(float[] vector)
    {
        var sum = 0f;
        for (var i = 0; i < vector.Length; i++)
        {
            sum += vector[i] * vector[i];
        }

        return MathF.Sqrt(sum);
    }
}
