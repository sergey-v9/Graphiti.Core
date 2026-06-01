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
    public async Task LadybugGraphitiBulkIngestsDuplicateFactsEndToEnd()
    {
        await using var driver = LadybugDbGraphDriverFactory.CreateInMemory();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: CreateAliceAcmeLlmClient(),
            embedder: new HashEmbedder(8));
        var firstTime = new DateTime(2026, 9, 13, 14, 15, 16, DateTimeKind.Utc);
        var secondTime = firstTime.AddMinutes(5);

        await graphiti.BuildIndicesAndConstraintsAsync();
        var result = await graphiti.AddEpisodeBulkAsync(
            [
                new RawEpisode
                {
                    Name = "first",
                    Content = "Alice works at Acme.",
                    SourceDescription = "message",
                    ReferenceTime = firstTime
                },
                new RawEpisode
                {
                    Name = "second",
                    Content = "Alice still works at Acme.",
                    SourceDescription = "message",
                    ReferenceTime = secondTime
                }
            ],
            groupId: "tenant");
        var edge = Assert.Single(result.Edges);
        var storedEdge = Assert.Single(await driver.GetEdgesByGroupIdsAsync<EntityEdge>(["tenant"]));
        var attributed = await graphiti.GetNodesAndEdgesByEpisodeAsync(
            result.Episodes.Select(episode => episode.Uuid).ToArray());
        var searchResults = await graphiti.SearchAdvancedAsync(
            "Alice Acme",
            SearchConfigRecipes.CombinedHybridSearchRrf,
            groupIds: ["tenant"]);

        Assert.Equal(2, result.Episodes.Count);
        Assert.Equal(2, result.Nodes.Count);
        Assert.Equal(4, result.EpisodicEdges.Count);
        Assert.Equal(edge.Uuid, storedEdge.Uuid);
        Assert.Equal(2, storedEdge.Episodes.Count);
        Assert.All(result.Episodes, episode => Assert.Contains(episode.Uuid, storedEdge.Episodes));
        Assert.All(result.Episodes, episode => Assert.Equal(new[] { edge.Uuid }, episode.EntityEdges));
        Assert.Equal(2, attributed.Edges.Count);
        Assert.All(attributed.Edges, attributedEdge => Assert.Equal(edge.Uuid, attributedEdge.Uuid));
        Assert.Equal(edge.Uuid, Assert.Single(searchResults.Edges).Uuid);
        Assert.Equal(2, searchResults.Episodes.Count);
    }

    [Fact]
    public async Task LadybugGraphitiAssociatesSagaEpisodesEndToEnd()
    {
        await using var driver = LadybugDbGraphDriverFactory.CreateInMemory();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: CreateAliceAcmeLlmClient(),
            embedder: new HashEmbedder(8));
        var firstTime = new DateTime(2026, 9, 14, 15, 16, 17, DateTimeKind.Utc);
        var secondTime = firstTime.AddMinutes(5);

        await graphiti.BuildIndicesAndConstraintsAsync();
        var first = await graphiti.AddEpisodeAsync(
            "first",
            "Alice works at Acme.",
            "message",
            firstTime,
            groupId: "tenant",
            saga: "launch");
        var second = await graphiti.AddEpisodeAsync(
            "second",
            "Alice still works at Acme.",
            "message",
            secondTime,
            groupId: "tenant",
            saga: "launch",
            sagaPreviousEpisodeUuid: first.Episode.Uuid);
        var saga = await driver.FindSagaByNameAsync("launch", "tenant");
        Assert.NotNull(saga);

        var hasEpisodeEdges = await driver.GetEdgesByGroupIdsAsync<HasEpisodeEdge>(["tenant"]);
        var nextEpisodeEdge = Assert.Single(await driver.GetEdgesByGroupIdsAsync<NextEpisodeEdge>(["tenant"]));
        var contents = await driver.GetSagaEpisodeContentsAsync(saga.Uuid);

        Assert.Equal(first.Episode.Uuid, saga.FirstEpisodeUuid);
        Assert.Equal(second.Episode.Uuid, saga.LastEpisodeUuid);
        Assert.Equal(2, hasEpisodeEdges.Count);
        Assert.Contains(hasEpisodeEdges, edge =>
            edge.SourceNodeUuid == saga.Uuid && edge.TargetNodeUuid == first.Episode.Uuid);
        Assert.Contains(hasEpisodeEdges, edge =>
            edge.SourceNodeUuid == saga.Uuid && edge.TargetNodeUuid == second.Episode.Uuid);
        Assert.Equal(first.Episode.Uuid, nextEpisodeEdge.SourceNodeUuid);
        Assert.Equal(second.Episode.Uuid, nextEpisodeEdge.TargetNodeUuid);
        Assert.Equal(new[] { first.Episode.Content, second.Episode.Content }, contents.Select(content => content.Content));
    }

    [Fact]
    public async Task LadybugGraphitiSummarizesSagaEndToEnd()
    {
        await using var driver = LadybugDbGraphDriverFactory.CreateInMemory();
        var fixedNow = new DateTimeOffset(2026, 9, 15, 16, 17, 18, TimeSpan.Zero);
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: CreateAliceAcmeSummaryLlmClient("Alice owns the Acme launch summary."),
            embedder: new HashEmbedder(8),
            timeProvider: new FixedTimeProvider(fixedNow));
        var firstTime = new DateTime(2026, 9, 15, 10, 11, 12, DateTimeKind.Utc);
        var secondTime = firstTime.AddMinutes(5);

        await graphiti.BuildIndicesAndConstraintsAsync();
        var first = await graphiti.AddEpisodeAsync(
            "first",
            "Alice works at Acme.",
            "message",
            firstTime,
            groupId: "tenant",
            saga: "launch");
        await graphiti.AddEpisodeAsync(
            "second",
            "Alice owns the Acme launch checklist.",
            "message",
            secondTime,
            groupId: "tenant",
            saga: "launch",
            sagaPreviousEpisodeUuid: first.Episode.Uuid);
        var saga = await driver.FindSagaByNameAsync("launch", "tenant");
        Assert.NotNull(saga);

        var summarized = await graphiti.SummarizeSagaAsync(saga.Uuid);
        var storedSaga = await driver.GetNodeByUuidAsync<SagaNode>(saga.Uuid);

        Assert.Equal("Alice owns the Acme launch summary.", summarized.Summary);
        Assert.Equal(summarized.Summary, storedSaga.Summary);
        Assert.Equal(fixedNow.UtcDateTime, summarized.LastSummarizedAt);
        Assert.Equal(fixedNow.UtcDateTime, storedSaga.LastSummarizedAt);
        Assert.Equal(secondTime, summarized.LastSummarizedEpisodeValidAt);
        Assert.Equal(secondTime, storedSaga.LastSummarizedEpisodeValidAt);
        Assert.Equal(saga.FirstEpisodeUuid, storedSaga.FirstEpisodeUuid);
        Assert.Equal(saga.LastEpisodeUuid, storedSaga.LastEpisodeUuid);
    }

    [Fact]
    public async Task LadybugGraphitiBuildsCommunitiesEndToEnd()
    {
        await using var driver = LadybugDbGraphDriverFactory.CreateInMemory();
        var fixedNow = new DateTimeOffset(2026, 9, 16, 17, 18, 19, TimeSpan.Zero);
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: CreateCommunityLlmClient("Alice and Bob run the Acme launch.", "Acme Team"),
            embedder: new HashEmbedder(8),
            timeProvider: new FixedTimeProvider(fixedNow));
        var createdAt = new DateTime(2026, 9, 16, 10, 11, 12, DateTimeKind.Utc);
        var alice = new EntityNode
        {
            Uuid = "community-alice",
            Name = "Alice",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            Summary = "Alice owns the Acme launch."
        };
        var bob = new EntityNode
        {
            Uuid = "community-bob",
            Name = "Bob",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            Summary = "Bob coordinates launch operations."
        };
        var edge = new EntityEdge
        {
            Uuid = "community-alice-bob",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "tenant",
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt,
            Name = "WORKS_WITH",
            Fact = "Alice works with Bob on the Acme launch.",
            FactEmbedding = [0.5f, 0.5f],
            Episodes = []
        };

        await graphiti.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(alice);
        await driver.SaveNodeAsync(bob);
        await driver.SaveEdgeAsync(edge);
        var (communities, communityEdges) = await graphiti.BuildCommunitiesAsync(["tenant"]);
        var community = Assert.Single(communities);
        var storedCommunity = await driver.GetNodeByUuidAsync<CommunityNode>(community.Uuid);
        var storedCommunityEdges = await driver.GetEdgesByGroupIdsAsync<CommunityEdge>(["tenant"]);
        var communitiesByNodes = await driver.GetCommunitiesByNodesAsync([alice, bob]);

        Assert.Equal("Acme Team", community.Name);
        Assert.Equal("Alice and Bob run the Acme launch.", community.Summary);
        Assert.Equal(fixedNow.UtcDateTime, community.CreatedAt);
        Assert.NotNull(community.NameEmbedding);
        Assert.NotEmpty(community.NameEmbedding);
        Assert.Equal(community.Name, storedCommunity.Name);
        Assert.Equal(community.Summary, storedCommunity.Summary);
        Assert.Equal(2, communityEdges.Count);
        Assert.Equal(2, storedCommunityEdges.Count);
        Assert.All(
            storedCommunityEdges,
            membership => Assert.Equal(community.Uuid, membership.SourceNodeUuid));
        Assert.Equal(
            new[] { alice.Uuid, bob.Uuid },
            storedCommunityEdges.Select(membership => membership.TargetNodeUuid).Order(StringComparer.Ordinal));
        Assert.Equal(community.Uuid, Assert.Single(communitiesByNodes).Uuid);

        var (rebuiltCommunities, rebuiltCommunityEdges) = await graphiti.BuildCommunitiesAsync(["tenant"]);
        var rebuiltCommunity = Assert.Single(rebuiltCommunities);
        var rebuiltStoredCommunity = await driver.GetNodeByUuidAsync<CommunityNode>(rebuiltCommunity.Uuid);
        var rebuiltStoredCommunityEdges = await driver.GetEdgesByGroupIdsAsync<CommunityEdge>(["tenant"]);
        var rebuiltCommunitiesByNodes = await driver.GetCommunitiesByNodesAsync([alice, bob]);
        var searchResults = await graphiti.SearchAdvancedAsync(
            "Acme Team",
            SearchConfigRecipes.CommunityHybridSearchRrf,
            groupIds: ["tenant"]);

        Assert.NotEqual(community.Uuid, rebuiltCommunity.Uuid);
        await Assert.ThrowsAsync<NodeNotFoundException>(
            () => driver.GetNodeByUuidAsync<CommunityNode>(community.Uuid));
        Assert.Equal("Acme Team", rebuiltCommunity.Name);
        Assert.Equal("Alice and Bob run the Acme launch.", rebuiltCommunity.Summary);
        Assert.Equal(rebuiltCommunity.Name, rebuiltStoredCommunity.Name);
        Assert.Equal(rebuiltCommunity.Summary, rebuiltStoredCommunity.Summary);
        Assert.Equal(2, rebuiltCommunityEdges.Count);
        Assert.Equal(2, rebuiltStoredCommunityEdges.Count);
        Assert.All(
            rebuiltStoredCommunityEdges,
            membership => Assert.Equal(rebuiltCommunity.Uuid, membership.SourceNodeUuid));
        Assert.Equal(rebuiltCommunity.Uuid, Assert.Single(rebuiltCommunitiesByNodes).Uuid);
        Assert.Equal(rebuiltCommunity.Uuid, Assert.Single(searchResults.Communities).Uuid);
    }

    [Fact]
    public async Task LadybugGraphitiUpdatesCommunitiesDuringEpisodeIngestionEndToEnd()
    {
        await using var driver = LadybugDbGraphDriverFactory.CreateInMemory();
        var fixedNow = new DateTimeOffset(2026, 9, 17, 18, 19, 20, TimeSpan.Zero);
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: CreateCommunityUpdateLlmClient(
                "Acme collaborators work together.",
                "Acme Collaborators"),
            embedder: new HashEmbedder(8),
            timeProvider: new FixedTimeProvider(fixedNow));
        var createdAt = new DateTime(2026, 9, 17, 10, 11, 12, DateTimeKind.Utc);
        var alice = new EntityNode
        {
            Uuid = "community-update-alice",
            Name = "Alice",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            Summary = "Alice owns the Acme launch."
        };
        var bob = new EntityNode
        {
            Uuid = "community-update-bob",
            Name = "Bob",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            Summary = "Bob coordinates launch operations."
        };

        await graphiti.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(alice);
        await driver.SaveNodeAsync(bob);
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "community-update-alice-bob",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "tenant",
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt,
            Name = "WORKS_WITH",
            Fact = "Alice works with Bob on the Acme launch.",
            FactEmbedding = [0.5f, 0.5f, 0, 0, 0, 0, 0, 0],
            Episodes = []
        });
        var (initialCommunities, initialCommunityEdges) = await graphiti.BuildCommunitiesAsync(["tenant"]);
        var initialCommunity = Assert.Single(initialCommunities);
        Assert.Equal(2, initialCommunityEdges.Count);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Carol works with Alice",
            "message",
            createdAt.AddMinutes(1),
            groupId: "tenant",
            updateCommunities: true);
        var carol = Assert.Single(result.Nodes, node => node.Name == "Carol");
        var aliceResult = Assert.Single(result.Nodes, node => node.Name == "Alice");
        var newCommunityEdge = Assert.Single(result.CommunityEdges);
        var storedCommunity = await driver.GetNodeByUuidAsync<CommunityNode>(initialCommunity.Uuid);
        var carolCommunities = await driver.GetCommunitiesByNodesAsync([carol]);
        var storedCommunityEdges = await driver.GetEdgesByGroupIdsAsync<CommunityEdge>(["tenant"]);
        var searchResults = await graphiti.SearchAdvancedAsync(
            "Acme Collaborators",
            SearchConfigRecipes.CommunityHybridSearchRrf,
            groupIds: ["tenant"]);

        Assert.Equal(alice.Uuid, aliceResult.Uuid);
        Assert.Equal(carol.Uuid, newCommunityEdge.TargetNodeUuid);
        Assert.Equal(initialCommunity.Uuid, newCommunityEdge.SourceNodeUuid);
        Assert.NotEmpty(result.Communities);
        Assert.Contains(result.Communities, community => community.Uuid == initialCommunity.Uuid);
        Assert.Equal("Acme Collaborators", storedCommunity.Name);
        Assert.Equal("Acme collaborators work together.", storedCommunity.Summary);
        Assert.Equal(initialCommunity.Uuid, Assert.Single(carolCommunities).Uuid);
        Assert.Equal(3, storedCommunityEdges.Count);
        Assert.Contains(storedCommunityEdges, edge => edge.TargetNodeUuid == carol.Uuid);
        Assert.Equal(initialCommunity.Uuid, Assert.Single(searchResults.Communities).Uuid);
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

    [Fact]
    public async Task ServiceCollectionExtensionUsesConfiguredDatabasePathAcrossScopes()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "graphiti-ladybugdb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var services = new ServiceCollection();
            services.AddGraphitiCore();
            services.AddLadybugDbGraphDriver(options => options.DatabasePath = databasePath);

            await using (var serviceProvider = services.BuildServiceProvider())
            {
                await using (var firstScope = serviceProvider.CreateAsyncScope())
                {
                    var firstDriver = firstScope.ServiceProvider.GetRequiredService<IGraphDriver>();
                    await firstDriver.BuildIndicesAndConstraintsAsync();
                    await firstDriver.SaveNodeAsync(new EntityNode
                    {
                        Uuid = "persistent-ladybug-node",
                        Name = "Persistent Alice",
                        GroupId = "tenant",
                        Labels = ["Person"],
                        NameEmbedding = [1, 0, 0, 0, 0, 0, 0, 0],
                        CreatedAt = new DateTime(2026, 9, 18, 10, 11, 12, DateTimeKind.Utc),
                        Summary = "Persisted through configured LadybugDB path"
                    });
                    await firstDriver.CloseAsync();
                }

                await using (var secondScope = serviceProvider.CreateAsyncScope())
                {
                    var secondDriver = secondScope.ServiceProvider.GetRequiredService<IGraphDriver>();
                    var fetched = await secondDriver.GetNodeByUuidAsync<EntityNode>("persistent-ladybug-node");
                    await secondDriver.CloseAsync();

                    Assert.Equal(GraphProvider.Kuzu, secondDriver.Provider);
                    Assert.Equal("Persistent Alice", fetched.Name);
                    Assert.Equal("tenant", fetched.GroupId);
                    Assert.Equal("Persisted through configured LadybugDB path", fetched.Summary);
                    Assert.Equal(new[] { "Person", "Entity" }, fetched.Labels);
                }
            }

            Assert.True(
                Directory.Exists(databasePath) || File.Exists(databasePath),
                "The configured LadybugDB path should create persistent package storage.");
        }
        finally
        {
            if (Directory.Exists(databasePath))
            {
                Directory.Delete(databasePath, recursive: true);
            }
            else if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static StaticJsonLlmClient CreateAliceAcmeLlmClient() =>
        new(_ => CreateAliceAcmeResponse());

    private static StaticJsonLlmClient CreateAliceAcmeSummaryLlmClient(string summary) =>
        new(_ =>
        {
            var response = CreateAliceAcmeResponse();
            response["summary"] = summary;
            return response;
        });

    private static StaticJsonLlmClient CreateCommunityLlmClient(string summary, string description) =>
        new(_ => new JsonObject
        {
            ["summary"] = summary,
            ["description"] = description
        });

    private static StaticJsonLlmClient CreateCommunityUpdateLlmClient(string summary, string description) =>
        new(_ => new JsonObject
        {
            ["extracted_entities"] = new JsonArray
            {
                new JsonObject { ["name"] = "Carol", ["entity_type"] = "Person" },
                new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" }
            },
            ["edges"] = new JsonArray
            {
                new JsonObject
                {
                    ["source_entity_name"] = "Carol",
                    ["target_entity_name"] = "Alice",
                    ["relation_type"] = "WORKS_WITH",
                    ["fact"] = "Carol works with Alice"
                }
            },
            ["summary"] = summary,
            ["description"] = description
        });

    private static JsonObject CreateAliceAcmeResponse() =>
        new()
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
        };

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
