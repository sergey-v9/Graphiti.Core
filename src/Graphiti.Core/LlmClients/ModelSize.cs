namespace Graphiti.Core.LlmClients;

/// <summary>Selects which configured model tier an LLM call should use.</summary>
public enum ModelSize
{
    /// <summary>The smaller/faster model, used for lightweight tasks.</summary>
    Small,

    /// <summary>The standard model, used for most extraction tasks.</summary>
    Medium
}
