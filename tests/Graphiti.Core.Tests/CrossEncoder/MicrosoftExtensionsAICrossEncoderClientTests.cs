using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Graphiti.Core.Tests.CrossEncoder;

public class MicrosoftExtensionsAICrossEncoderClientTests
{
    [Fact]
    public async Task RankIndexedAsync_UsesStructuredBooleanScoresAndStableOrdering()
    {
        var chatClient = new RelevanceChatClient(
            static userText =>
            {
                if (userText.Contains("definitely relevant", StringComparison.Ordinal))
                {
                    return "{\"is_relevant\":true,\"confidence\":0.9}";
                }

                if (userText.Contains("maybe relevant", StringComparison.Ordinal))
                {
                    return "{\"is_relevant\":true,\"confidence\":0.4}";
                }

                return "{\"is_relevant\":false,\"confidence\":0.8}";
            });
        var client = new MicrosoftExtensionsAICrossEncoderClient(
            chatClient,
            new LlmConfig
            {
                Model = "large-model",
                SmallModel = "small-model",
                Temperature = 1,
                MaxTokens = 1_000
            },
            maxConcurrency: 1);

        var ranked = await client.RankIndexedAsync(
            "Which passage is relevant?",
            new[]
            {
                "definitely relevant passage",
                "irrelevant passage",
                "maybe relevant passage"
            });

        Assert.Equal(new[] { 0, 2, 1 }, ranked.Select(rank => rank.Index));
        Assert.Collection(
            ranked.Select(rank => rank.Score),
            score => Assert.Equal(0.9f, score, precision: 6),
            score => Assert.Equal(0.4f, score, precision: 6),
            score => Assert.Equal(0.2f, score, precision: 6));
        Assert.Equal(3, chatClient.Calls.Count);
        Assert.All(chatClient.Options, option =>
        {
            // Mirrors openai_reranker_client.py:87 `model=self.config.model or DEFAULT_MODEL`: the
            // reranker uses the PRIMARY model, never the configured small model.
            Assert.Equal("large-model", option.ModelId);
            Assert.Equal(64, option.MaxOutputTokens);
            Assert.Equal(0f, option.Temperature);
            var responseFormat = Assert.IsType<ChatResponseFormatJson>(option.ResponseFormat);
            Assert.Equal("RerankerRelevanceResponse", responseFormat.SchemaName);
        });
        Assert.All(chatClient.UserMessages, userMessage =>
        {
            Assert.Contains("<PASSAGE>", userMessage, StringComparison.Ordinal);
            Assert.Contains("<QUERY>", userMessage, StringComparison.Ordinal);
            Assert.Contains("Which passage is relevant?", userMessage, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task RankAsync_ProjectsRankedPassages()
    {
        var chatClient = new RelevanceChatClient(
            static userText => userText.Contains("first", StringComparison.Ordinal)
                ? "{\"is_relevant\":true,\"confidence\":0.7}"
                : "{\"is_relevant\":false,\"confidence\":0.6}");
        var client = new MicrosoftExtensionsAICrossEncoderClient(chatClient, maxConcurrency: 1);

        var ranked = await client.RankAsync("query", new[] { "first", "second" });

        Assert.Equal(new[] { "first", "second" }, ranked.Select(item => item.Passage));
        Assert.Collection(
            ranked.Select(item => item.Score),
            score => Assert.Equal(0.7f, score, precision: 6),
            score => Assert.Equal(0.4f, score, precision: 6));
    }

    [Fact]
    public async Task RankIndexedAsync_DefaultsToPrimaryRerankerModelWhenNoConfig()
    {
        // Mirrors openai_reranker_client.py:87 with config.model unset: `config.model or DEFAULT_MODEL`
        // falls back to gpt-4.1-nano (DEFAULT_MODEL, line 31). The no-config constructor sets the
        // primary model to DefaultModel, so requests must target gpt-4.1-nano.
        var chatClient = new RelevanceChatClient(static _ => "{\"is_relevant\":true,\"confidence\":0.5}");
        var client = new MicrosoftExtensionsAICrossEncoderClient(chatClient, maxConcurrency: 1);

        await client.RankIndexedAsync("query", new[] { "passage" });

        Assert.Equal(
            MicrosoftExtensionsAICrossEncoderClient.DefaultModel,
            Assert.Single(chatClient.Options).ModelId);
    }

    [Fact]
    public async Task RankIndexedAsync_PreservesInputOrderForEqualScoresAndDuplicatePassages()
    {
        var chatClient = new RelevanceChatClient(
            static _ => "{\"is_relevant\":true,\"confidence\":0.5}");
        var client = new MicrosoftExtensionsAICrossEncoderClient(chatClient, maxConcurrency: 1);

        var ranked = await client.RankIndexedAsync("query", new[] { "same", "different", "same" });

        Assert.Equal(new[] { 0, 1, 2 }, ranked.Select(rank => rank.Index));
        Assert.Equal(new[] { "same", "different", "same" }, ranked.Select(rank => rank.Passage));
    }

    [Fact]
    public async Task RankIndexedAsync_ReturnsEmptyWithoutProviderCalls()
    {
        var chatClient = new RelevanceChatClient(
            static _ => throw new InvalidOperationException("Provider should not be called."));
        var client = new MicrosoftExtensionsAICrossEncoderClient(chatClient);

        var ranked = await client.RankIndexedAsync("query", Array.Empty<string>());

        Assert.Empty(ranked);
        Assert.Empty(chatClient.Calls);
    }

    [Fact]
    public async Task RankIndexedAsync_RejectsInvalidStructuredResponse()
    {
        var chatClient = new RelevanceChatClient(
            static _ => "{\"is_relevant\":true,\"confidence\":\"high\"}");
        var client = new MicrosoftExtensionsAICrossEncoderClient(chatClient, maxConcurrency: 1);

        await Assert.ThrowsAsync<JsonException>(() =>
            client.RankIndexedAsync("query", new[] { "passage" }));
    }

    [Fact]
    public void Constructor_RejectsInvalidMaxConcurrency()
    {
        var chatClient = new RelevanceChatClient(static _ => "{\"is_relevant\":true,\"confidence\":1}");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MicrosoftExtensionsAICrossEncoderClient(chatClient, maxConcurrency: 0));
    }

    private sealed class RelevanceChatClient(Func<string, string> responseFactory) : IChatClient
    {
        public List<IReadOnlyList<Microsoft.Extensions.AI.ChatMessage>> Calls { get; } = new();

        public List<ChatOptions> Options { get; } = new();

        public List<string> UserMessages { get; } = new();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = messages.ToArray();
            var userMessage = snapshot.Last(message => message.Role == ChatRole.User).Text;
            Calls.Add(snapshot);
            Options.Add(options ?? new ChatOptions());
            UserMessages.Add(userMessage);
            return Task.FromResult(
                new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(
                    ChatRole.Assistant,
                    responseFactory(userMessage))));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
