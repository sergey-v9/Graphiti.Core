using Graphiti.Core.Configuration;
using Graphiti.Core.Drivers.Ladybug;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Graphiti.Core.Tests.Drivers.Ladybug;

public class LadybugRuntimeDriverTests
{
    [Fact]
    public async Task FactoryCreatesLadybugDriver()
    {
        var driver = LadybugDbGraphDriverFactory.CreateInMemory();
        var createdAt = new DateTime(2026, 8, 9, 10, 11, 12, DateTimeKind.Utc);
        var source = new EntityNode
        {
            Uuid = "ladybug-source",
            Name = "Ladybug Source",
            GroupId = "tenant",
            Labels = ["Person"],
            NameEmbedding = [1.0f, 0.0f],
            CreatedAt = createdAt,
            Summary = "created through the optional package source"
        };
        var target = new EntityNode
        {
            Uuid = "ladybug-target",
            Name = "Ladybug Target",
            GroupId = "tenant",
            Labels = ["Person"],
            NameEmbedding = [0.0f, 1.0f],
            CreatedAt = createdAt,
            Summary = "created through the optional package target"
        };
        var edge = new EntityEdge
        {
            Uuid = "ladybug-edge",
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "tenant",
            Name = "KNOWS",
            Fact = "Ladybug source knows target",
            FactEmbedding = [0.5f, 0.5f],
            Episodes = ["ladybug-episode"],
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(source);
        await driver.SaveNodeAsync(target);
        await driver.SaveEdgeAsync(edge);
        var fetched = await driver.GetNodeByUuidAsync<EntityNode>(source.Uuid);
        var fetchedEdge = await driver.GetEdgeByUuidAsync<EntityEdge>(edge.Uuid);
        await driver.CloseAsync();
        await driver.CloseAsync();

        Assert.Equal(GraphProvider.Kuzu, driver.Provider);
        Assert.Equal(source.Uuid, fetched.Uuid);
        Assert.Equal(new[] { "Person", "Entity" }, fetched.Labels);
        Assert.Equal(edge.Uuid, fetchedEdge.Uuid);
        Assert.Equal(edge.Episodes, fetchedEdge.Episodes);
        Assert.Equal(edge.ReferenceTime, fetchedEdge.ReferenceTime);
    }

    [Fact]
    public async Task ServiceCollectionExtensionRegistersLadybugDriverThroughGraphDriverFactory()
    {
        var services = new ServiceCollection();
        services.AddGraphitiCore();
        services.AddLadybugDbGraphDriver();

        await using var serviceProvider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        await using var scope = serviceProvider.CreateAsyncScope();

        var options = scope.ServiceProvider.GetRequiredService<IOptions<GraphitiOptions>>().Value;
        var driver = scope.ServiceProvider.GetRequiredService<IGraphDriver>();
        var graphiti = scope.ServiceProvider.GetRequiredService<Graphiti>();
        await driver.CloseAsync();

        Assert.Equal(GraphProvider.InMemory, options.Provider);
        Assert.NotNull(options.GraphDriverFactory);
        Assert.Equal(GraphProvider.Kuzu, driver.Provider);
        Assert.Same(driver, graphiti.Driver);
    }

    [Fact]
    public void ServiceCollectionExtensionRejectsBlankDatabasePath()
    {
        var services = new ServiceCollection();
        services.AddGraphitiCore();
        services.AddLadybugDbGraphDriver(options => options.DatabasePath = "   ");

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        Assert.Throws<OptionsValidationException>(
            () => scope.ServiceProvider.GetRequiredService<IOptions<LadybugDbOptions>>().Value);
        Assert.Throws<OptionsValidationException>(
            () => scope.ServiceProvider.GetRequiredService<IGraphDriver>());
    }

    [Fact]
    public async Task ServiceCollectionExtensionBindsConfigurationAndAllowsPostConfigureOverride()
    {
        var configuration = new ConfigurationManager
        {
            ["DatabasePath"] = "   "
        };
        var services = new ServiceCollection();
        services.AddLadybugDbGraphDriver(
            configuration,
            options => options.DatabasePath = string.Empty);
        services.AddGraphitiCore();

        await using var serviceProvider = services.BuildServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();

        var ladybugOptions = scope.ServiceProvider.GetRequiredService<IOptions<LadybugDbOptions>>().Value;
        var graphitiOptions = scope.ServiceProvider.GetRequiredService<IOptions<GraphitiOptions>>().Value;
        var driver = scope.ServiceProvider.GetRequiredService<IGraphDriver>();
        await driver.CloseAsync();

        Assert.Equal(string.Empty, ladybugOptions.DatabasePath);
        Assert.Equal(GraphProvider.InMemory, graphitiOptions.Provider);
        Assert.NotNull(graphitiOptions.GraphDriverFactory);
        Assert.Equal(GraphProvider.Kuzu, driver.Provider);
    }
}
