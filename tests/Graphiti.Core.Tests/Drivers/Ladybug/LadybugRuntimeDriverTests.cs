using System.Text.Json.Nodes;
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
        Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        Assert.Equal(source.Uuid, fetched.Uuid);
        Assert.Equal(new[] { "Person", "Entity" }, fetched.Labels);
        Assert.Equal(edge.Uuid, fetchedEdge.Uuid);
        Assert.Equal(edge.Episodes, fetchedEdge.Episodes);
        Assert.Equal(edge.ReferenceTime, fetchedEdge.ReferenceTime);
    }

    [Fact]
    public async Task LadybugGraphitiIngestsAndSearchesEpisodeEndToEnd()
    {
        await using var driver = LadybugDbGraphDriverFactory.CreateInMemory();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: CreateAliceAcmeLlmClient(),
            embedder: new HashEmbedder(8));
        var referenceTime = new DateTime(2026, 9, 10, 11, 12, 13, DateTimeKind.Utc);

        await graphiti.BuildIndicesAndConstraintsAsync();
        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme.",
            "message",
            referenceTime,
            groupId: "tenant");
        var searchResults = await graphiti.SearchAdvancedAsync(
            "Alice Acme",
            SearchConfigRecipes.CombinedHybridSearchRrf,
            groupIds: ["tenant"]);

        Assert.Equal(GraphProvider.Kuzu, graphiti.Driver.Provider);
        Assert.IsAssignableFrom<ISearchGraphDriver>(graphiti.Driver);
        Assert.Equal(2, result.Nodes.Count);
        Assert.Single(result.Edges);
        Assert.Equal(result.Episode.Uuid, Assert.Single(result.Edges[0].Episodes));
        Assert.Equal(result.Edges[0].Uuid, Assert.Single(searchResults.Edges).Uuid);
        Assert.Contains(searchResults.Nodes, node => string.Equals(node.Name, "Alice", StringComparison.Ordinal));
        Assert.Contains(searchResults.Nodes, node => string.Equals(node.Name, "Acme", StringComparison.Ordinal));
        Assert.Equal(result.Episode.Uuid, Assert.Single(searchResults.Episodes).Uuid);
    }

    [Fact]
    public async Task LadybugGraphitiRemovesIngestedEpisodeEndToEnd()
    {
        await using var driver = LadybugDbGraphDriverFactory.CreateInMemory();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: CreateAliceAcmeLlmClient(),
            embedder: new HashEmbedder(8));
        var referenceTime = new DateTime(2026, 9, 11, 12, 13, 14, DateTimeKind.Utc);

        await graphiti.BuildIndicesAndConstraintsAsync();
        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme.",
            "message",
            referenceTime,
            groupId: "tenant");
        var attributed = await graphiti.GetNodesAndEdgesByEpisodeAsync([result.Episode.Uuid]);

        await graphiti.RemoveEpisodeAsync(result.Episode.Uuid);
        var searchResults = await graphiti.SearchAdvancedAsync(
            "Alice Acme",
            SearchConfigRecipes.CombinedHybridSearchRrf,
            groupIds: ["tenant"]);

        Assert.Equal(result.Edges[0].Uuid, Assert.Single(attributed.Edges).Uuid);
        Assert.Equal(2, attributed.Nodes.Count);
        await Assert.ThrowsAsync<NodeNotFoundException>(
            () => driver.GetNodeByUuidAsync<EpisodicNode>(result.Episode.Uuid));
        await Assert.ThrowsAsync<EdgeNotFoundException>(
            () => driver.GetEdgeByUuidAsync<EntityEdge>(result.Edges[0].Uuid));
        await Assert.ThrowsAsync<NodeNotFoundException>(
            () => driver.GetNodeByUuidAsync<EntityNode>(result.Nodes[0].Uuid));
        await Assert.ThrowsAsync<NodeNotFoundException>(
            () => driver.GetNodeByUuidAsync<EntityNode>(result.Nodes[1].Uuid));
        Assert.Empty(searchResults.Edges);
        Assert.Empty(searchResults.Nodes);
        Assert.Empty(searchResults.Episodes);
    }

    [Fact]
    public async Task LadybugGraphitiAddsTripletAndSearchesFactEndToEnd()
    {
        await using var driver = LadybugDbGraphDriverFactory.CreateInMemory();
        var graphiti = new Graphiti(graphDriver: driver, embedder: new HashEmbedder(8));
        var referenceTime = new DateTime(2026, 9, 12, 13, 14, 15, DateTimeKind.Utc);
        var source = new EntityNode
        {
            Name = "Alice",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = referenceTime,
            Summary = "A person in the Ladybug triplet test"
        };
        var target = new EntityNode
        {
            Name = "Acme",
            GroupId = "tenant",
            Labels = ["Organization"],
            CreatedAt = referenceTime,
            Summary = "An organization in the Ladybug triplet test"
        };
        var edge = new EntityEdge
        {
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "tenant",
            CreatedAt = referenceTime,
            ValidAt = referenceTime,
            ReferenceTime = referenceTime,
            Name = "WORKS_AT",
            Fact = "Alice works at Acme."
        };

        await graphiti.BuildIndicesAndConstraintsAsync();
        var result = await graphiti.AddTripletAsync(source, edge, target);
        var searchResults = await graphiti.SearchAsync("Alice Acme", groupIds: ["tenant"]);
        var storedSource = await driver.GetNodeByUuidAsync<EntityNode>(result.Nodes[0].Uuid);
        var storedTarget = await driver.GetNodeByUuidAsync<EntityNode>(result.Nodes[1].Uuid);
        var storedEdge = await driver.GetEdgeByUuidAsync<EntityEdge>(result.Edges[0].Uuid);

        Assert.Equal(2, result.Nodes.Count);
        Assert.Single(result.Edges);
        Assert.Equal(storedSource.Uuid, storedEdge.SourceNodeUuid);
        Assert.Equal(storedTarget.Uuid, storedEdge.TargetNodeUuid);
        Assert.Equal(edge.Fact, storedEdge.Fact);
        Assert.Equal(edge.ValidAt, storedEdge.ValidAt);
        Assert.Empty(storedEdge.Episodes);
        Assert.Equal(storedEdge.Uuid, Assert.Single(searchResults).Uuid);
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
        Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
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

    private static StaticJsonLlmClient CreateAliceAcmeLlmClient() =>
        new(_ => new JsonObject
        {
            ["extracted_entities"] = new JsonArray
            {
                new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                new JsonObject { ["name"] = "Acme", ["entity_type"] = "Organization" }
            },
            ["edges"] = new JsonArray
            {
                new JsonObject
                {
                    ["source_entity_name"] = "Alice",
                    ["target_entity_name"] = "Acme",
                    ["relation_type"] = "WORKS_AT",
                    ["fact"] = "Alice works at Acme."
                }
            }
        });
}
