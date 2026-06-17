using System.Text.Json.Nodes;

namespace Graphiti.Core.Prompts;

/// <summary>
/// Renders JSON values for embedding inside prompt text: minified output with non-ASCII characters
/// preserved so prompts stay readable in logs and for the model. The compact (separator-space-free)
/// JSON shape is an accepted prompt-rendering divergence covered by the prompt parity contract in
/// <c>.agents/notes/decisions.md</c>.
/// </summary>
internal static class PromptJson
{
    internal static string Serialize(JsonNode? node) =>
        node?.ToJsonString(GraphitiJsonSerializer.Options) ?? "null";
}
