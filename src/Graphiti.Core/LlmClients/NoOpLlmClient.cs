using System.Text.Json.Nodes;

namespace Graphiti.Core.LlmClients;

/// <summary>
/// An <see cref="ILlmClient"/> that performs no inference and returns an empty JSON object. Used as
/// the default when no real LLM client is supplied, allowing the library to run without a provider.
/// </summary>
public sealed class NoOpLlmClient : ILlmClient
{
    /// <inheritdoc />
    public TokenUsageTracker TokenTracker { get; } = new();

    /// <inheritdoc />
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
        return Task.FromResult(new JsonObject());
    }
}
