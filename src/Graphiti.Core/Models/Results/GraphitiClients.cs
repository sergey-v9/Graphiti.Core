namespace Graphiti.Core.Models.Results;

/// <summary>
/// Bundle of the collaborating clients (graph driver, LLM, embedder, cross-encoder) passed through
/// the ingestion and search pipelines so they share a single configured set of dependencies.
/// </summary>
public sealed class GraphitiClients
{
    /// <summary>Initializes the client bundle with its four dependencies.</summary>
    public GraphitiClients(
        IGraphDriver driver,
        ILlmClient llmClient,
        IEmbedderClient embedder,
        ICrossEncoderClient crossEncoder)
    {
        Driver = driver;
        LlmClient = llmClient;
        Embedder = embedder;
        CrossEncoder = crossEncoder;
    }

    /// <summary>Graph storage driver.</summary>
    public IGraphDriver Driver { get; }

    /// <summary>Client used for LLM extraction/summarization calls.</summary>
    public ILlmClient LlmClient { get; }

    /// <summary>Client used to generate embedding vectors.</summary>
    public IEmbedderClient Embedder { get; }

    /// <summary>Client used to rerank search candidates.</summary>
    public ICrossEncoderClient CrossEncoder { get; }
}
