using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Internal.Helpers;
using Graphiti.Core.LlmClients;
using Microsoft.Extensions.AI;

namespace Graphiti.Core.Tests;

public class LlmBoundaryFuzzTests
{
    public static TheoryData<string> MalformedProviderPayloads =>
        new()
        {
            "",
            "   ",
            "{",
            "not-json",
            "prefix {\"ok\":true}",
            "```json\n{\"ok\":true}\n``` trailing",
            "[{\"ok\":true}]",
            "true"
        };

    [Theory]
    [MemberData(nameof(MalformedProviderPayloads))]
    public async Task GenerateResponseAsync_MalformedProviderPayloadsRetryThenFail(string payload)
    {
        var chatClient = new ConstantChatClient(payload);
        var client = new MicrosoftExtensionsAIChatClient(chatClient);

        await Assert.ThrowsAsync<JsonException>(() =>
            client.GenerateResponseAsync(
                [new Message("user", "extract")],
                responseModel: typeof(BoundaryOkResponse)));

        Assert.Equal(3, chatClient.Calls);
    }

    public static TheoryData<JsonObject> InvalidAttributeResponses =>
        new()
        {
            new JsonObject(),
            new JsonObject { ["attributes"] = new JsonObject() },
            new JsonObject
            {
                ["attributes"] = new JsonObject
                {
                    ["score"] = "high",
                    ["role"] = "lead"
                }
            },
            new JsonObject
            {
                ["attributes"] = new JsonObject
                {
                    ["role"] = "lead",
                    ["extra"] = "ignored?"
                }
            }
        };

    [Theory]
    [MemberData(nameof(InvalidAttributeResponses))]
    public void StructuredResponseValidator_RuntimeAttributeSchemaRejectsMalformedResponses(JsonObject response)
    {
        var entityType = new EntityTypeDefinition(
            "Person",
            attributes: new Dictionary<string, EntityAttributeDefinition>
            {
                ["role"] = new("Role", required: true),
                ["score"] = new("Score", "number")
            });
        var schema = ExtractionContextBuilder.BuildAttributeResponseSchema(entityType, "AttributeBoundary");

        Assert.Throws<JsonException>(() => StructuredResponseValidator.Validate(response, schema));
    }

    [Fact]
    public void ExtractEdges_MixedMalformedRowsKeepOnlyCoherentFacts()
    {
        var response = new JsonObject
        {
            ["edges"] = new JsonArray
            {
                null,
                JsonValue.Create("not-an-object"),
                new JsonObject { ["source"] = "Alice", ["target"] = "Bob", ["fact"] = "Alice knows Bob." },
                new JsonObject
                {
                    ["source_entity_name"] = "Alice",
                    ["target_entity_name"] = "Acme",
                    ["relation_type"] = "   ",
                    ["fact"] = "Alice works at Acme."
                },
                new JsonObject
                {
                    ["source"] = "Alice",
                    ["target"] = "Acme",
                    ["relation_type"] = "WORKS_AT",
                    ["fact"] = "Alice works at Acme.",
                    ["valid_at"] = "not-a-date",
                    ["episode_indices"] = new JsonArray { "1", null, "bad", 3 }
                }
            }
        };

        var edge = Assert.Single(Graphiti.ExtractEdges(response));

        Assert.Equal("Alice", edge.SourceName);
        Assert.Equal("Acme", edge.TargetName);
        Assert.Equal("WORKS_AT", edge.RelationType);
        Assert.Equal("Alice works at Acme.", edge.Fact);
        Assert.Null(edge.ValidAt);
        Assert.Equal(new[] { 1, 3 }, edge.EpisodeIndices);
    }

    [Fact]
    public void ExtractEntityNames_InvalidEntityTypeIdsFailBeforeGraphContentIsBuilt()
    {
        var response = new JsonObject
        {
            ["extracted_entities"] = new JsonArray
            {
                new JsonObject { ["name"] = "Alice", ["entity_type_id"] = "not-number" }
            }
        };

        Assert.Throws<JsonException>(() => Graphiti.ExtractEntityNames(response, entityTypes: null));
    }

    [Fact]
    public void EdgeMergeHelpers_ReadIntArrayKeepsOnlyIntegerLikeValues()
    {
        var response = new JsonObject
        {
            ["duplicate_facts"] = new JsonArray
            {
                0,
                "1",
                "-2",
                " 3 ",
                "1.5",
                4.25,
                null,
                new JsonObject { ["id"] = 5 }
            }
        };

        var indexes = EdgeMergeHelpers.ReadIntArray(response, "duplicate_facts");

        Assert.Equal(new[] { 0, 1, -2, 3 }, indexes);
    }

    [Fact]
    public async Task GenerateTypedResponseAsync_CustomClientResponseMaterializesLeniently()
    {
        var client = new DirectJsonClient(
            new JsonObject
            {
                ["name"] = new JsonObject { ["kind"] = "Alice" },
                ["count"] = "not-an-int"
            });

        var response = await client.GenerateTypedResponseAsync<LenientTypedResponse>(
            [new Message("user", "extract")]);

        Assert.Equal("{\"kind\":\"Alice\"}", response.Name);
        Assert.Equal(0, response.Count);
    }

    public static TheoryData<string> MalformedRerankerPayloads =>
        new()
        {
            "",
            "```json\n{\"is_relevant\":true,\"confidence\":0.5}\n```",
            "prefix {\"is_relevant\":true,\"confidence\":0.5}",
            "[{\"is_relevant\":true,\"confidence\":0.5}]",
            "{\"is_relevant\":true,\"confidence\":\"high\"}"
        };

    [Theory]
    [MemberData(nameof(MalformedRerankerPayloads))]
    public async Task CrossEncoder_MalformedProviderPayloadsFailLoudly(string payload)
    {
        var client = new MicrosoftExtensionsAICrossEncoderClient(
            new ConstantChatClient(payload),
            maxConcurrency: 1);

        await Assert.ThrowsAnyAsync<JsonException>(() =>
            client.RankIndexedAsync("query", ["passage"]));
    }

    private sealed class BoundaryOkResponse
    {
        [JsonRequired]
        public bool Ok { get; set; }
    }

    private sealed class LenientTypedResponse
    {
        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }
    }

    private sealed class DirectJsonClient(JsonObject response) : ILlmClient
    {
        public TokenUsageTracker TokenTracker { get; } = new();

        public Task<JsonObject> GenerateResponseAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel = null,
            StructuredResponseSchema? responseSchema = null,
            int? maxTokens = null,
            ModelSize modelSize = ModelSize.Medium,
            string? groupId = null,
            string? promptName = null,
            bool attributeExtraction = false,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult((JsonObject)response.DeepClone());
        }
    }

    private sealed class ConstantChatClient(string responseText) : IChatClient
    {
        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(
                new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(
                    ChatRole.Assistant,
                    responseText)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
