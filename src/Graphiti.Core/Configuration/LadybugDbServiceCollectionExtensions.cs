using System.Diagnostics.CodeAnalysis;
using Graphiti.Core.Drivers.Ladybug;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Graphiti.Core.Configuration;

/// <summary>
/// Dependency-injection helpers for configuring the LadybugDB graph driver.
/// </summary>
public static class LadybugDbServiceCollectionExtensions
{
    /// <summary>
    /// Configures Graphiti Core to build graph drivers through LadybugDB.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">Optional LadybugDB option configuration.</param>
    public static IServiceCollection AddLadybugDbGraphDriver(
        this IServiceCollection services,
        Action<LadybugDbOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        AddLadybugDbServices(services);
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services;
    }

    /// <summary>
    /// Binds LadybugDB options from configuration and configures Graphiti Core to use LadybugDB.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">Configuration section containing <see cref="LadybugDbOptions"/>.</param>
    /// <param name="configure">Optional delegate that runs after configuration binding.</param>
    [RequiresUnreferencedCode(
        "Binds LadybugDbOptions from configuration via reflection; its members may be trimmed. Use the "
        + "Action<LadybugDbOptions> overload, or preserve LadybugDbOptions, in a trimmed host.")]
    public static IServiceCollection AddLadybugDbGraphDriver(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<LadybugDbOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        AddLadybugDbGraphDriver(services);
        services.Configure<LadybugDbOptions>(configuration);
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services;
    }

    internal static void AddLadybugDbOptions(IServiceCollection services)
    {
        services.AddOptions<LadybugDbOptions>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<LadybugDbOptions>, LadybugDbOptionsValidator>());
    }

    private static void AddLadybugDbServices(IServiceCollection services)
    {
        AddLadybugDbOptions(services);
        services.Configure<GraphitiOptions>(static options =>
            options.GraphDriverFactory = static services =>
            {
                var ladybugOptions = services.GetRequiredService<IOptions<LadybugDbOptions>>().Value;
                return LadybugDbGraphDriverFactory.Create(ladybugOptions.DatabasePath);
            });
    }
}
