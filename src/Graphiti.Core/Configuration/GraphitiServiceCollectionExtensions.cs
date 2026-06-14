using System.Threading.RateLimiting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Graphiti.Core.Configuration;

/// <summary>
/// Dependency-injection helpers for registering Graphiti Core in an <see cref="IServiceCollection"/>.
/// Wires up options/validators, caching, the LLM/embedder/cross-encoder clients (falling back to the
/// built-in no-op/deterministic implementations when no provider is registered), the graph driver,
/// and the <see cref="Graphiti"/> orchestrator.
/// </summary>
public static class GraphitiServiceCollectionExtensions
{
    /// <summary>
    /// Registers Graphiti Core services, optionally configuring <see cref="GraphitiOptions"/> in code.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configure">Optional delegate to configure options.</param>
    public static IServiceCollection AddGraphiti(
        this IServiceCollection services,
        Action<GraphitiOptions>? configure = null)
    {
        AddGraphitiCoreServices(services);
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services;
    }

    /// <summary>
    /// Registers Graphiti Core services and binds <see cref="GraphitiOptions"/> plus the
    /// <c>Llm</c>, <c>Embedding</c>, and <c>ContentChunking</c> sections from configuration. An
    /// optional <paramref name="configure"/> delegate runs last and can override bound values.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">Configuration root the options are bound from.</param>
    /// <param name="configure">Optional delegate to override options after binding.</param>
    public static IServiceCollection AddGraphiti(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<GraphitiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        AddGraphitiCoreServices(services);
        services.Configure<GraphitiOptions>(configuration);
        services.Configure<LlmConfig>(configuration.GetSection("Llm"));
        services.Configure<EmbeddingConfig>(configuration.GetSection("Embedding"));
        services.Configure<ContentChunkingOptions>(configuration.GetSection("ContentChunking"));
        services.Configure<GraphitiCacheOptions>(configuration.GetSection("Cache"));
        services.Configure<GraphitiResilienceOptions>(configuration.GetSection("Resilience"));
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services;
    }

    /// <summary>
    /// Obsolete alias for <see cref="AddGraphiti(IServiceCollection, Action{GraphitiOptions}?)"/>.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configure">Optional delegate to configure options.</param>
    [Obsolete("Use AddGraphiti", DiagnosticId = "GRPH0002")]
    public static IServiceCollection AddGraphitiCore(
        this IServiceCollection services,
        Action<GraphitiOptions>? configure = null) =>
        services.AddGraphiti(configure);

    /// <summary>
    /// Obsolete alias for
    /// <see cref="AddGraphiti(IServiceCollection, IConfiguration, Action{GraphitiOptions}?)"/>.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">Configuration root the options are bound from.</param>
    /// <param name="configure">Optional delegate to override options after binding.</param>
    [Obsolete("Use AddGraphiti", DiagnosticId = "GRPH0002")]
    public static IServiceCollection AddGraphitiCore(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<GraphitiOptions>? configure = null) =>
        services.AddGraphiti(configuration, configure);

    private static void AddGraphitiCoreServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<GraphitiOptions>();
        services.AddOptions<LlmConfig>();
        services.AddOptions<EmbeddingConfig>();
        services.AddOptions<ContentChunkingOptions>();
        services.AddOptions<GraphitiCacheOptions>();
        services.AddOptions<GraphitiResilienceOptions>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<GraphitiOptions>, GraphitiOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<LlmConfig>, LlmConfigValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<EmbeddingConfig>, EmbeddingConfigValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<ContentChunkingOptions>, ContentChunkingOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<GraphitiCacheOptions>, GraphitiCacheOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<GraphitiResilienceOptions>, GraphitiResilienceOptionsValidator>());
        services.AddHybridCache();
        services.TryAddSingleton<ILlmResponseCache>(CreateLlmResponseCache);
        services.TryAddSingleton(CreateChatResiliencePipeline);
        services.TryAddSingleton(CreateEmbeddingResiliencePipeline);
        services.TryAddSingleton<RateLimiter>(CreateProviderRateLimiter);
        services.TryAddScoped<ILlmClient>(CreateLlmClient);
        services.TryAddScoped<IEmbedderClient>(CreateEmbedderClient);
        services.TryAddSingleton<ICrossEncoderClient, IdentityCrossEncoderClient>();
        services.TryAddSingleton<ITokenCounter>(_ => TiktokenTokenCounter.CreateDefault());
        services.TryAddSingleton<IContentChunker, DefaultContentChunker>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped(CreateGraphDriver);
        services.TryAddScoped(CreateGraphiti);
    }

    private static ILlmClient CreateLlmClient(IServiceProvider services)
    {
        var config = services.GetRequiredService<IOptions<LlmConfig>>().Value;
        var chatClient = services.GetService<IChatClient>();
        if (chatClient is null)
        {
            return new NoOpLlmClient();
        }

        return new MicrosoftExtensionsAIChatClient(
            chatClient,
            config,
            cache: services.GetService<ILlmResponseCache>(),
            pipeline: services.GetService<ResiliencePipeline<ChatResponse>>(),
            rateLimiter: services.GetService<RateLimiter>());
    }

    private static ILlmResponseCache CreateLlmResponseCache(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<GraphitiCacheOptions>>().Value;
        IReadOnlyList<string> tags = options.LlmResponseTags.Length == 0
            ? new[] { "graphiti", "llm" }
            : options.LlmResponseTags;
        return new HybridCacheLlmResponseCache(
            services.GetRequiredService<HybridCache>(),
            new HybridCacheEntryOptions
            {
                Expiration = options.LlmResponseExpiration,
                LocalCacheExpiration = options.LlmResponseLocalCacheExpiration
            },
            tags);
    }

    private static ResiliencePipeline<ChatResponse> CreateChatResiliencePipeline(IServiceProvider services) =>
        CreateAiProviderResiliencePipeline<ChatResponse>(services);

    private static ResiliencePipeline<GeneratedEmbeddings<Embedding<float>>> CreateEmbeddingResiliencePipeline(
        IServiceProvider services) =>
        CreateAiProviderResiliencePipeline<GeneratedEmbeddings<Embedding<float>>>(services);

    private static ResiliencePipeline<T> CreateAiProviderResiliencePipeline<T>(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<GraphitiResilienceOptions>>().Value;
        var builder = new ResiliencePipelineBuilder<T>();
        if (options.MaxRetryAttempts > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                Delay = options.RetryDelay,
                MaxDelay = options.MaxRetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = options.UseJitter
            });
        }

        if (options.AttemptTimeout > TimeSpan.Zero)
        {
            builder.AddTimeout(options.AttemptTimeout);
        }

        return builder.Build();
    }

    private static RateLimiter CreateProviderRateLimiter(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<GraphitiResilienceOptions>>().Value;
        return new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = options.ProviderConcurrencyLimit ?? int.MaxValue,
            QueueLimit = options.ProviderQueueLimit,
            QueueProcessingOrder = options.ProviderQueueProcessingOrder
        });
    }

    private static IEmbedderClient CreateEmbedderClient(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<GraphitiOptions>>().Value;
        var embeddingConfig = services.GetRequiredService<IOptions<EmbeddingConfig>>().Value;
        var embeddingDimension = embeddingConfig.EmbeddingDimension ?? options.EmbeddingDimension;
        var embeddingGenerator = services.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        if (embeddingGenerator is null)
        {
            return new HashEmbedder(embeddingDimension);
        }

        return new MicrosoftExtensionsAIEmbedderClient(
            embeddingGenerator,
            embeddingDimension,
            modelId: embeddingConfig.ModelId,
            pipeline: services.GetService<ResiliencePipeline<GeneratedEmbeddings<Embedding<float>>>>(),
            rateLimiter: services.GetService<RateLimiter>(),
            batchSize: embeddingConfig.BatchSize,
            batchConcurrency: embeddingConfig.BatchConcurrency);
    }

    private static IGraphDriver CreateGraphDriver(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<GraphitiOptions>>().Value;
        if (options.GraphDriverFactory is not null)
        {
            return options.GraphDriverFactory(services);
        }

        return options.Provider switch
        {
            GraphProvider.InMemory => new InMemoryGraphDriver(options.Database),
            GraphProvider.Neo4j => new Neo4jGraphDriver(
                options.Uri ?? throw new InvalidOperationException("GraphitiOptions.Uri is required for Neo4j."),
                options.User,
                options.Password,
                options.Database),
            // LadybugDb is the driver-facing value; Kuzu is the obsolete compatibility alias. Both
            // are backed by the LadybugDB-backed driver, which now lives in the separate
            // Graphiti.Core.Drivers.Ladybug package. That package's AddLadybugDbGraphDriver()
            // registers a GraphDriverFactory (handled above); without it, core cannot construct the
            // driver because it deliberately does not reference the LadybugDB package.
            GraphProvider.LadybugDb or GraphProvider.Kuzu => throw new InvalidOperationException(
                "GraphProvider.LadybugDb requires the Graphiti.Core.Drivers.Ladybug package — call AddLadybugDbGraphDriver()."),
            _ => throw new NotSupportedException($"{options.Provider} is not supported by the C# port yet.")
        };
    }

    private static Graphiti CreateGraphiti(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<GraphitiOptions>>().Value;
        return new Graphiti(
            llmClient: services.GetRequiredService<ILlmClient>(),
            embedder: services.GetRequiredService<IEmbedderClient>(),
            crossEncoder: services.GetRequiredService<ICrossEncoderClient>(),
            storeRawEpisodeContent: options.StoreRawEpisodeContent,
            graphDriver: services.GetRequiredService<IGraphDriver>(),
            maxCoroutines: options.MaxCoroutines,
            timeProvider: options.TimeProvider ?? services.GetRequiredService<TimeProvider>(),
            logger: services.GetService<ILogger<Graphiti>>());
    }
}
