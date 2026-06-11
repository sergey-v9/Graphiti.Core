using System.Text.Json.Nodes;

namespace Graphiti.Core.Prompts;

/// <summary>
/// Renders JSON values for embedding inside prompt text, mirroring Python
/// <c>prompts/prompt_helpers.py::to_prompt_json</c>: minified output with non-ASCII characters
/// preserved so prompts stay readable in logs and for the model. Python's <c>json.dumps</c> adds
/// spaces after separators; C# renders compact JSON instead. That divergence is covered by the
/// prompt parity contract in <c>.agents/notes/decisions.md</c>.
/// </summary>
internal static class PromptJson
{
    internal static string Serialize(JsonNode? node) =>
        node?.ToJsonString(GraphitiJsonSerializer.Options) ?? "null";
}
