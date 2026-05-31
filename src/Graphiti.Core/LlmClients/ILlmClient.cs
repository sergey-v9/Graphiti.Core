using System.Text.Json.Nodes;

namespace Graphiti.Core.LlmClients;

/// <summary>
/// Abstraction over a large language model used by Graphiti for entity/edge extraction,
/// deduplication, and summarization. Implementations return a structured JSON object that conforms
/// to the requested response model or schema.
/// </summary>
public interface ILlmClient
{
    /// <summary>Tracks token usage accumulated by this client.</summary>
    TokenUsageTracker TokenTracker { get; }

    /// <summary>
    /// Sends the prompt messages to the model and returns the structured JSON response.
    /// </summary>
    /// <param name="messages">Ordered chat messages forming the prompt.</param>
    /// <param name="responseModel">Optional CLR type describing the expected response shape.</param>
    /// <param name="responseSchema">Optional explicit JSON schema for the response.</param>
    /// <param name="maxTokens">Optional cap on output tokens.</param>
    /// <param name="modelSize">Which model tier to use.</param>
    /// <param name="groupId">Optional graph partition context for the call.</param>
    /// <param name="promptName">Optional prompt name used for token attribution.</param>
    /// <param name="attributeExtraction">Whether the call is extracting entity attributes.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    Task<JsonObject> GenerateResponseAsync(
        IReadOnlyList<Message> messages,
        Type? responseModel = null,
        StructuredResponseSchema? responseSchema = null,
        int? maxTokens = null,
        ModelSize modelSize = ModelSize.Medium,
        string? groupId = null,
        string? promptName = null,
        bool attributeExtraction = false,
        CancellationToken cancellationToken = default);
}
