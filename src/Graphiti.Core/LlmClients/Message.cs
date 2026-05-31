namespace Graphiti.Core.LlmClients;

/// <summary>A single chat message with a role (for example <c>system</c>, <c>user</c>) and content.</summary>
public sealed record Message(string Role, string Content);
