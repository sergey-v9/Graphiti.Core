using Graphiti.Core.Drivers.Ladybug;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Graphiti.Core.Tests.Drivers.Ladybug;

/// <summary>
/// Step B/C/E release-readiness coverage: the driver-facing <see cref="GraphProvider.LadybugDb"/>
/// value and the obsolete <see cref="GraphProvider.Kuzu"/> alias both resolve to a working
/// LadybugDB-backed driver through Core DI.
/// </summary>
public class GraphProviderLadybugNamingTests
{
#pragma warning disable GRPH0001
    [Theory]
    [InlineData(GraphProvider.LadybugDb)]
    [InlineData(GraphProvider.Kuzu)]
    public async Task ProviderResolvesWorkingLadybugDriverFromCoreOptions(GraphProvider provider)
    {
        var services = new ServiceCollection();
        services.AddGraphiti(options => options.Provider = provider);

        await using var serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        await using var scope = serviceProvider.CreateAsyncScope();

        var options = scope.ServiceProvider.GetRequiredService<IOptions<GraphitiOptions>>().Value;
        var driver = scope.ServiceProvider.GetRequiredService<IGraphDriver>();

        Assert.Equal(provider, options.Provider);
        Assert.Null(options.GraphDriverFactory);
        Assert.IsType<LadybugGraphDriver>(driver);
        Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        Assert.Equal(GraphProvider.LadybugDb, driver.Provider);

        // Round-trip a node to prove the resolved driver is functional, not just constructed.
        var createdAt = new DateTime(2026, 9, 21, 10, 11, 12, DateTimeKind.Utc);
        var node = new EntityNode
        {
            Uuid = "ladybug-naming-node",
            Name = "Ladybug Naming",
            GroupId = "tenant",
            Labels = ["Person"],
            NameEmbedding = [1.0f, 0.0f],
            CreatedAt = createdAt,
            Summary = $"persisted through GraphProvider.{provider}"
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(node);
        var fetched = await driver.GetNodeByUuidAsync<EntityNode>(node.Uuid);
        await driver.CloseAsync();

        Assert.Equal(node.Uuid, fetched.Uuid);
        Assert.Equal("Ladybug Naming", fetched.Name);
        Assert.Equal("tenant", fetched.GroupId);
        Assert.Equal(new[] { "Person", "Entity" }, fetched.Labels);
    }
#pragma warning restore GRPH0001
}
