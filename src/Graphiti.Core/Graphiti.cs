using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Graphiti.Core;

/// <summary>
/// The main entry point for Graphiti Core. Orchestrates building and querying a temporal context
/// graph: ingesting episodes, extracting and deduplicating entities and facts, invalidating
/// superseded facts, running hybrid (semantic + keyword + graph) search, building communities, and
/// maintaining sagas. Construct it with a graph driver plus LLM, embedder, and cross-encoder clients
/// (sensible no-op/deterministic defaults are used when omitted). Dispose it to release any driver
/// it owns.
/// </summary>
public sealed partial class Graphiti : IAsyncDisposable
{
    private readonly IGraphDriver? _ownedDriver;
    private readonly IGraphDriver _rootDriver;
    private readonly AsyncLocal<IGraphDriver?> _operationDriver = new();
    private readonly bool _storeRawEpisodeContent;
    private readonly int? _maxCoroutines;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<Graphiti> _logger;
    private readonly AttributeExtractionService _attributeExtractionService;
    private readonly CommunityService _communityService;
    private readonly EdgeResolutionService _edgeResolutionService;
    private readonly EntitySummaryService _entitySummaryService;
    private readonly EpisodeGraphExtractor _episodeGraphExtractor;
    private readonly NodeResolutionService _nodeResolutionService;
    private readonly SagaService _sagaService;
    private int _closed;

    /// <summary>
    /// Initializes a Graphiti instance. The graph backend is selected by precedence: an explicit
    /// <paramref name="graphDriver"/> is used as-is; otherwise an in-process
    /// <see cref="InMemoryGraphDriver"/> (the deterministic reference driver) is created so
    /// <c>new Graphiti()</c> works out of the box. When clients are omitted, a no-op LLM, hash
    /// embedder, and identity cross-encoder are used so the instance is usable without external
    /// providers.
    /// </summary>
    /// <param name="llmClient">LLM client for extraction/summarization; defaults to a no-op client.</param>
    /// <param name="embedder">Embedder client; defaults to a deterministic hash embedder.</param>
    /// <param name="crossEncoder">Reranker client; defaults to an identity lexical reranker.</param>
    /// <param name="storeRawEpisodeContent">Whether to persist raw episode content.</param>
    /// <param name="graphDriver">An explicit graph driver. When omitted, an in-memory driver is used.</param>
    /// <param name="maxCoroutines">Optional cap on concurrent operations; null or zero uses the default.</param>
    /// <param name="timeProvider">Time source used for timestamps; defaults to the system clock.</param>
    /// <param name="logger">Logger; defaults to a null logger.</param>
    /// <param name="database">Database name (group/db label) for the default in-memory driver.</param>
    public Graphiti(
        ILlmClient? llmClient = null,
        IEmbedderClient? embedder = null,
        ICrossEncoderClient? crossEncoder = null,
        bool storeRawEpisodeContent = true,
        IGraphDriver? graphDriver = null,
        int? maxCoroutines = null,
        TimeProvider? timeProvider = null,
        ILogger<Graphiti>? logger = null,
        string database = "")
    {
        if (maxCoroutines is { } concurrency)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(concurrency, nameof(maxCoroutines));
        }

        var ownsDriver = false;
        if (graphDriver is null)
        {
            // No explicit driver: default to the in-process reference driver so the instance is
            // usable out of the box.
            graphDriver = new InMemoryGraphDriver(database);
            ownsDriver = true;
        }

        _ownedDriver = ownsDriver ? graphDriver : null;
        _rootDriver = graphDriver;
        LlmClient = llmClient ?? new NoOpLlmClient();
        Embedder = embedder ?? new HashEmbedder();
        CrossEncoder = crossEncoder ?? new IdentityCrossEncoderClient();
        _storeRawEpisodeContent = storeRawEpisodeContent;
        _maxCoroutines = maxCoroutines;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<Graphiti>.Instance;
        _attributeExtractionService = new AttributeExtractionService(LlmClient, GetMaxDegreeOfParallelism);
        _entitySummaryService = new EntitySummaryService(LlmClient, _logger, GetMaxDegreeOfParallelism);
        _episodeGraphExtractor = new EpisodeGraphExtractor(LlmClient, UtcNow);
        _nodeResolutionService = new NodeResolutionService(() => Driver, LlmClient, Embedder, _logger);
        _communityService = new CommunityService(
            () => Driver,
            LlmClient,
            Embedder,
            _logger,
            _timeProvider,
            GetMaxDegreeOfParallelism);
        _sagaService = new SagaService(() => Driver, LlmClient, _timeProvider);

        Clients = new GraphitiClients(_rootDriver, LlmClient, Embedder, CrossEncoder);
        _edgeResolutionService = new EdgeResolutionService(
            () => Driver,
            Clients,
            LlmClient,
            _logger,
            GetMaxDegreeOfParallelism,
            UtcNow);
        Nodes = new NodeNamespace(_rootDriver, Embedder);
        Edges = new EdgeNamespace(_rootDriver, Embedder);
    }

    /// <summary>The active graph driver for the current operation scope (group-scoped when applicable).</summary>
    public IGraphDriver Driver => _operationDriver.Value ?? _rootDriver;

    /// <summary>The LLM client used for extraction and summarization.</summary>
    public ILlmClient LlmClient { get; }

    /// <summary>The embedder client used to generate vectors.</summary>
    public IEmbedderClient Embedder { get; }

    /// <summary>The cross-encoder client used to rerank search results.</summary>
    public ICrossEncoderClient CrossEncoder { get; }

    /// <summary>The bundled clients passed through the ingestion and search pipelines.</summary>
    public GraphitiClients Clients { get; }

    /// <summary>Facade for node-level save/delete/query operations.</summary>
    public NodeNamespace Nodes { get; }

    /// <summary>Facade for edge-level save/delete/query operations.</summary>
    public EdgeNamespace Edges { get; }

    /// <summary>Accumulated LLM token usage for this instance.</summary>
    public TokenUsageTracker TokenTracker => LlmClient.TokenTracker;

    /// <summary>Closes the instance, releasing the driver if it is owned by this instance.</summary>
    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Closes the underlying driver if this instance created (owns) it; otherwise a no-op.</summary>
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_ownedDriver is null)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Interlocked.Exchange(ref _closed, 1) == 0
            ? _ownedDriver.CloseAsync(cancellationToken)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Creates the indices and constraints the graph requires. Set <paramref name="deleteExisting"/>
    /// to drop and recreate them. Run this once before ingesting data into a fresh database.
    /// </summary>
    public async Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default)
    {
        using var activity = GraphitiTelemetry.StartActivity("BuildIndicesAndConstraints");
        activity?.SetTag("graphiti.delete_existing", deleteExisting);

        try
        {
            await Driver.BuildIndicesAndConstraintsAsync(deleteExisting, cancellationToken).ConfigureAwait(false);
            GraphitiTelemetry.SetOk(activity);
        }
        catch (Exception exception)
        {
            GraphitiTelemetry.RecordException(activity, exception);
            throw;
        }
    }

    /// <summary>
    /// Retrieves the most recent episodes before <paramref name="referenceTime"/>, optionally filtered
    /// by group, source type, or saga. Used to assemble prior context for ingestion and retrieval.
    /// </summary>
    public Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(
        DateTime referenceTime,
        int lastN = MaintenanceUtilities.EpisodeWindowLength,
        IReadOnlyList<string>? groupIds = null,
        EpisodeType? source = null,
        IGraphDriver? driver = null,
        string? saga = null,
        CancellationToken cancellationToken = default) =>
        (driver ?? Driver).RetrieveEpisodesAsync(referenceTime, lastN, groupIds, source, saga, cancellationToken);

}
