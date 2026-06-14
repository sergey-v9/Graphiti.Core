namespace Graphiti.Core.LlmClients;

/// <summary>
/// Raised when the model refuses to produce a response (e.g. the provider returns a content-filter
/// finish reason). Mirrors Python's <c>RefusalError</c> (graphiti_core/llm_client/errors.py): like
/// Python (openai_base_client.py:133-134,263-266), this is surfaced to the caller and is NOT retried
/// by <see cref="LlmClient"/>'s validation-retry loop, which only re-prompts on
/// <see cref="System.Text.Json.JsonException"/>.
/// </summary>
public sealed class LlmRefusalException : GraphitiException
{
    /// <summary>Creates the exception with a message describing the refusal.</summary>
    public LlmRefusalException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner cause.</summary>
    public LlmRefusalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
