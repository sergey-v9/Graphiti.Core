using System.Globalization;
using Graphiti.Core.Internal.Helpers;
using Microsoft.Extensions.AI;

namespace Graphiti.Core.Tests.Integration;

[Collection(OpenAIProviderIntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OpenAIProviderIntegrationTests
{
    private const string GroupIdPrefix = "openai-provider-integration";

    [Fact]
    public async Task AddEpisodeAsync_WithOpenAIProvider_IngestsResolvedTemporalGraph()
    {
        var settings = RequireOpenAISettings();
        var clients = CreateClients(settings);
        var groupId = $"{GroupIdPrefix}-{Guid.NewGuid():N}";
        await using var graphiti = new global::Graphiti.Core.Graphiti(
            llmClient: clients.LlmClient,
            embedder: clients.Embedder,
            graphDriver: new InMemoryGraphDriver(groupId),
            maxCoroutines: 1);

        await graphiti.BuildIndicesAndConstraintsAsync(deleteExisting: true);

        var firstReferenceTime = new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc);
        await graphiti.AddEpisodeAsync(
            "Atlas planning",
            "Maya Patel manages Project Atlas at Nimbus Health. Leo Chen owns the rollout.",
            "planning transcript",
            firstReferenceTime,
            EpisodeType.Message,
            groupId: groupId);
        await graphiti.AddEpisodeAsync(
            "Atlas rollout moved",
            "Maya Patel said Project Atlas moved from March 15, 2026 to March 29, 2026 after authentication testing.",
            "planning transcript",
            firstReferenceTime.AddMinutes(10),
            EpisodeType.Message,
            groupId: groupId);

        var nodes = await EntityNode.GetByGroupIdsAsync(graphiti.Driver, new[] { groupId });
        var edges = await EntityEdge.GetByGroupIdsAsync(graphiti.Driver, new[] { groupId });

        Assert.NotEmpty(nodes);
        Assert.NotEmpty(edges);
        Assert.Single(nodes, node => ContainsNormalized(node.Name, "maya"));
        Assert.Contains(edges, edge => edge.ValidAt is not null);
    }

    [Fact]
    public async Task StructuredResponseSchemas_WithOpenAIProvider_AreAccepted()
    {
        var settings = RequireOpenAISettings();
        var clients = CreateClients(settings);
        var cases = new (string Name, Type ResponseModel)[]
        {
            ("episode node extraction", typeof(global::Graphiti.Core.Graphiti.EpisodeNodeExtractionResponse)),
            ("episode edge extraction", typeof(global::Graphiti.Core.Graphiti.EpisodeEdgeExtractionResponse)),
            ("node resolutions", typeof(global::Graphiti.Core.Graphiti.NodeResolutionsResponse)),
            ("edge resolution", typeof(global::Graphiti.Core.Graphiti.EdgeResolutionResponse)),
            ("edge timestamp", typeof(global::Graphiti.Core.Graphiti.EdgeTimestampResponse)),
            ("batch edge timestamps", typeof(global::Graphiti.Core.Graphiti.BatchEdgeTimestampsResponse)),
            ("entity summaries", typeof(global::Graphiti.Core.Graphiti.SummarizedEntitiesResponse)),
            ("community summary", typeof(global::Graphiti.Core.Graphiti.CommunitySummaryResponse)),
            ("community name", typeof(global::Graphiti.Core.Graphiti.CommunityNameResponse)),
            ("saga summary", typeof(global::Graphiti.Core.Graphiti.SagaSummaryResponse)),
            ("combined extraction", typeof(global::Graphiti.Core.Graphiti.CombinedExtractionResponse))
        };

        foreach (var testCase in cases)
        {
            var response = await clients.LlmClient.GenerateResponseAsync(
                BuildSchemaProbeMessages(testCase.Name),
                responseModel: testCase.ResponseModel,
                modelSize: ModelSize.Small,
                maxTokens: 1_000,
                promptName: $"provider.schema.{testCase.Name.Replace(' ', '_')}");

            Assert.NotNull(response);
        }

        var projectType = new EntityTypeDefinition(
            "Project",
            attributes: new Dictionary<string, EntityAttributeDefinition>
            {
                ["owner"] = new("The person responsible for the project.", "string"),
                ["confidence"] = new("A confidence score from 0 to 1.", "number")
            });
        var attributeSchema = ExtractionContextBuilder.BuildAttributeResponseSchema(
            projectType,
            "ProviderAttributeResponse");
        var attributeResponse = await clients.LlmClient.GenerateResponseAsync(
            new[]
            {
                new Message("system", "Return only JSON that satisfies the provided schema."),
                new Message("user", "Return attributes with owner set to Maya Patel and confidence set to 0.9.")
            },
            responseSchema: attributeSchema,
            modelSize: ModelSize.Small,
            maxTokens: 500,
            promptName: "provider.schema.attributes");

        Assert.NotNull(attributeResponse);
    }

    private static Message[] BuildSchemaProbeMessages(string schemaName) =>
        new[]
        {
            new Message("system", "Return only a compact JSON object that satisfies the provided schema. Do not include markdown."),
            new Message(
                "user",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Create a minimal but non-empty valid response for the Graphiti {schemaName} schema. Use short placeholder values and empty arrays only when that is the most natural valid value."))
        };

    private static bool ContainsNormalized(string value, string expectedTerm)
    {
        var normalized = GraphitiHelpers.NormalizeEntityKey(value);
        return normalized.Contains(expectedTerm, StringComparison.Ordinal);
    }

    private static OpenAIIntegrationClients CreateClients(OpenAIIntegrationSettings settings)
    {
        var chatClient = new OpenAI.Chat.ChatClient(settings.ChatModel, settings.ApiKey)
            .AsIChatClient();
        var embeddingGenerator = new OpenAI.Embeddings.EmbeddingClient(
                settings.EmbeddingModel,
                settings.ApiKey)
            .AsIEmbeddingGenerator(settings.EmbeddingDimensions);

        return new OpenAIIntegrationClients(
            new MicrosoftExtensionsAIChatClient(
                chatClient,
                new LlmConfig
                {
                    Model = settings.ChatModel,
                    SmallModel = settings.SmallModel,
                    Temperature = 0
                }),
            new MicrosoftExtensionsAIEmbedderClient(
                embeddingGenerator,
                settings.EmbeddingDimensions,
                modelId: settings.EmbeddingModel,
                batchSize: 16,
                batchConcurrency: 1));
    }

    private static OpenAIIntegrationSettings RequireOpenAISettings()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Assert.Skip("Set OPENAI_API_KEY to run OpenAI provider integration tests.");
            throw new InvalidOperationException("Unreachable after Assert.Skip.");
        }

        return new OpenAIIntegrationSettings(
            apiKey,
            GetEnvironmentValue("OPENAI_CHAT_MODEL", "gpt-4.1-mini"),
            GetEnvironmentValue("OPENAI_SMALL_MODEL", GetEnvironmentValue("OPENAI_CHAT_MODEL", "gpt-4.1-mini")),
            GetEnvironmentValue("OPENAI_EMBEDDING_MODEL", "text-embedding-3-small"),
            GetEnvironmentInt("OPENAI_EMBEDDING_DIMENSIONS", 1536));
    }

    private static string GetEnvironmentValue(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static int GetEnvironmentInt(string name, int fallback)
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

    private sealed record OpenAIIntegrationSettings(
        string ApiKey,
        string ChatModel,
        string SmallModel,
        string EmbeddingModel,
        int EmbeddingDimensions);

    private sealed record OpenAIIntegrationClients(
        ILlmClient LlmClient,
        IEmbedderClient Embedder);
}

internal static class OpenAIProviderIntegrationCollection
{
    public const string Name = "OpenAI provider integration";
}

[CollectionDefinition(OpenAIProviderIntegrationCollection.Name, DisableParallelization = true)]
public sealed class OpenAIProviderIntegrationCollectionDefinition;
