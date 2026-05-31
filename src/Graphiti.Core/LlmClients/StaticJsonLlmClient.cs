using System.Text.Json.Nodes;

namespace Graphiti.Core.LlmClients;

/// <summary>
/// A deterministic <see cref="LlmClient"/> for tests and offline use that returns JSON from a supplied
/// factory (or an empty object) instead of calling a real model.
/// </summary>
public sealed class StaticJsonLlmClient : LlmClient
{
    private readonly Func<IReadOnlyList<Message>, JsonObject> _factory;

    /// <summary>Creates the client with an optional factory mapping messages to a JSON response.</summary>
    public StaticJsonLlmClient(Func<IReadOnlyList<Message>, JsonObject>? factory = null)
        : base(cache: false)
    {
        _factory = factory ?? (_ => new JsonObject());
    }

    /// <inheritdoc />
    protected override Task<JsonObject> GenerateResponseCoreAsync(
        IReadOnlyList<Message> messages,
        Type? responseModel,
        StructuredResponseSchema? responseSchema,
        int maxTokens,
        ModelSize modelSize,
        string? promptName,
        CancellationToken cancellationToken) =>
        Task.FromResult(_factory(messages));
}
