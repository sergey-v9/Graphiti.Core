namespace Graphiti.Core.LlmClients;

/// <summary>
/// Deterministic payload used to derive LLM response cache keys. Property names and order are part
/// of the cache-key contract; update hash tests if this shape is deliberately changed.
/// </summary>
internal sealed record LlmCacheKeyPayload(
    string Model,
    string? SmallModel,
    string ResolvedModel,
    string ModelSize,
    double Temperature,
    int MaxTokens,
    string? ResponseModel,
    string? ResponseSchemaFingerprint,
    IReadOnlyList<Message> Messages);
