using Graphiti.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace Graphiti.Core.Tests.Configuration;

public class GraphitiOptionsValidationTests
{
    public static TheoryData<Action<GraphitiOptions>> InvalidOptions =>
        new()
        {
            options => options.EmbeddingDimension = 0,
            options => options.EmbeddingDimension = -1,
            options => options.MaxCoroutines = 0,
            options => options.MaxCoroutines = -1,
            options => options.Database = "   ",
            options => options.Provider = GraphProvider.FalkorDb,
            options => options.Provider = GraphProvider.Neptune,
            options => options.Provider = (GraphProvider)999
        };

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void InvalidOptions_FailWhenResolvingOptions(Action<GraphitiOptions> configure)
    {
        using var provider = BuildProvider(configure);

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<GraphitiOptions>>().Value);
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void InvalidOptions_FailWhenResolvingGraphDriver(Action<GraphitiOptions> configure)
    {
        using var provider = BuildProvider(configure);
        using var scope = provider.CreateScope();

        Assert.Throws<OptionsValidationException>(
            () => scope.ServiceProvider.GetRequiredService<IGraphDriver>());
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void InvalidOptions_FailWhenResolvingGraphiti(Action<GraphitiOptions> configure)
    {
        using var provider = BuildProvider(configure);
        using var scope = provider.CreateScope();

        Assert.Throws<OptionsValidationException>(
            () => scope.ServiceProvider.GetRequiredService<Graphiti>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_FailsForInvalidMaxCoroutines(int maxCoroutines)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new Graphiti(graphDriver: new InMemoryGraphDriver(), maxCoroutines: maxCoroutines));

        Assert.Equal("maxCoroutines", exception.ParamName);
    }

    [Theory]
    [InlineData(GraphProvider.Neptune)]
    [InlineData((GraphProvider)999)]
    public async Task GraphDriverFactory_BypassesProviderValidation(GraphProvider provider)
    {
        var services = new ServiceCollection();
        services.AddGraphiti(options =>
        {
            options.Provider = provider;
            options.GraphDriverFactory = _ => new InMemoryGraphDriver();
        });

        await using var serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
        await using var scope = serviceProvider.CreateAsyncScope();

        var options = scope.ServiceProvider.GetRequiredService<IOptions<GraphitiOptions>>().Value;
        var driver = scope.ServiceProvider.GetRequiredService<IGraphDriver>();
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();

        Assert.Equal(provider, options.Provider);
        Assert.IsType<InMemoryGraphDriver>(driver);
        Assert.Same(driver, graphiti.Driver);
    }

    private static ServiceProvider BuildProvider(Action<GraphitiOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddGraphiti(configure);
        return services.BuildServiceProvider();
    }

    public static TheoryData<Action<ContentChunkingOptions>> InvalidContentChunkingOptions =>
        new()
        {
            options => options.ChunkTokenSize = 0,
            options => options.ChunkTokenSize = -1,
            options => options.ChunkOverlapTokens = -1,
            options => options.ChunkMinTokens = 0,
            options => options.ChunkMinTokens = -1,
            options => options.ChunkDensityThreshold = 0,
            options => options.ChunkDensityThreshold = -0.1,
            options => options.ChunkDensityThreshold = double.NaN,
            options => options.ChunkDensityThreshold = double.PositiveInfinity
        };

    [Theory]
    [MemberData(nameof(InvalidContentChunkingOptions))]
    public void InvalidContentChunkingOptions_FailWhenResolvingOptions(
        Action<ContentChunkingOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddGraphiti();
        services.Configure(configure);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ContentChunkingOptions>>().Value);
    }

    public static TheoryData<Action<LlmConfig>> InvalidLlmConfig =>
        new()
        {
            options => options.Model = "",
            options => options.Model = "   ",
            options => options.SmallModel = "",
            options => options.MaxTokens = 0,
            options => options.MaxTokens = -1,
            options => options.Temperature = -0.1,
            options => options.Temperature = double.NaN,
            options => options.Temperature = double.PositiveInfinity
        };

    [Theory]
    [MemberData(nameof(InvalidLlmConfig))]
    public void InvalidLlmConfig_FailWhenResolvingOptions(Action<LlmConfig> configure)
    {
        var services = new ServiceCollection();
        services.AddGraphiti();
        services.Configure(configure);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<LlmConfig>>().Value);
    }

    public static TheoryData<Action<EmbeddingConfig>> InvalidEmbeddingConfig =>
        new()
        {
            options => options.ModelId = "",
            options => options.ModelId = "   ",
            options => options.EmbeddingDimension = 0,
            options => options.EmbeddingDimension = -1
        };

    [Theory]
    [MemberData(nameof(InvalidEmbeddingConfig))]
    public void InvalidEmbeddingConfig_FailWhenResolvingOptions(Action<EmbeddingConfig> configure)
    {
        var services = new ServiceCollection();
        services.AddGraphiti();
        services.Configure(configure);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<EmbeddingConfig>>().Value);
    }

    public static TheoryData<Action<GraphitiCacheOptions>> InvalidCacheOptions =>
        new()
        {
            options => options.LlmResponseExpiration = TimeSpan.FromMilliseconds(-1),
            options => options.LlmResponseLocalCacheExpiration = TimeSpan.FromMilliseconds(-1),
            options => options.LlmResponseTags = ["graphiti", ""]
        };

    [Theory]
    [MemberData(nameof(InvalidCacheOptions))]
    public void InvalidCacheOptions_FailWhenResolvingOptions(Action<GraphitiCacheOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddGraphiti();
        services.Configure(configure);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<GraphitiCacheOptions>>().Value);
    }

    public static TheoryData<Action<GraphitiResilienceOptions>> InvalidResilienceOptions =>
        new()
        {
            options => options.MaxRetryAttempts = -1,
            options => options.RetryDelay = TimeSpan.FromMilliseconds(-1),
            options => options.MaxRetryDelay = TimeSpan.FromMilliseconds(-1),
            options => options.MaxRetryDelay = TimeSpan.FromSeconds(1),
            options => options.AttemptTimeout = TimeSpan.FromMilliseconds(-1),
            options => options.ProviderConcurrencyLimit = 0,
            options => options.ProviderConcurrencyLimit = -1,
            options => options.ProviderQueueLimit = -1,
            options => options.ProviderQueueProcessingOrder = (QueueProcessingOrder)999
        };

    [Fact]
    public void ResilienceDefaults_MatchPythonRetryShape()
    {
        var options = new GraphitiResilienceOptions();

        Assert.Equal(3, options.MaxRetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(5), options.RetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(120), options.MaxRetryDelay);
        Assert.True(options.UseJitter);
        Assert.Equal(TimeSpan.Zero, options.AttemptTimeout);
    }

    [Theory]
    [MemberData(nameof(InvalidResilienceOptions))]
    public void InvalidResilienceOptions_FailWhenResolvingOptions(
        Action<GraphitiResilienceOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddGraphiti();
        services.Configure(configure);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<GraphitiResilienceOptions>>().Value);
    }
}
