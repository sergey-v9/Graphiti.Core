using System.Collections.Frozen;
using System.Text.Json.Nodes;
using Graphiti.Core;
using Microsoft.Extensions.Logging;

namespace Graphiti.Core.Tests;

public class GraphitiWorkflowTests
{
    [Fact]
    public void EntityTypeDefinition_AttributesAreCaseInsensitiveImmutableSnapshot()
    {
        var attributes = new Dictionary<string, EntityAttributeDefinition>(StringComparer.Ordinal)
        {
            ["Role"] = new("Job title"),
            ["active"] = new("Whether the person is active", "boolean")
        };

        var typeDefinition = new EntityTypeDefinition("Person", attributes: attributes);
        attributes["location"] = new("Current location");

        Assert.IsAssignableFrom<FrozenDictionary<string, EntityAttributeDefinition>>(typeDefinition.Attributes);
        Assert.True(typeDefinition.Attributes.ContainsKey("role"));
        Assert.True(typeDefinition.Attributes.ContainsKey("ACTIVE"));
        Assert.False(typeDefinition.Attributes.ContainsKey("location"));
        Assert.Equal("Job title", typeDefinition.Attributes["role"].Description);
        Assert.Equal("boolean", typeDefinition.Attributes["ACTIVE"].Type);
    }

    [Fact]
    public async Task AddEpisode_HeuristicallyBuildsAndSearchesTemporalGraph()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var referenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice likes Bob",
            "message",
            referenceTime,
            groupId: "group");

        Assert.Equal("conversation", result.Episode.Name);
        Assert.Equal(2, result.Nodes.Count);
        Assert.Single(result.Edges);
        Assert.Equal(result.Episode.Uuid, result.Edges[0].Episodes[0]);
        Assert.Equal(2, result.EpisodicEdges.Count);

        var searchResults = await graphiti.SearchAsync("Alice Bob", groupIds: new[] { "group" });

        Assert.Single(searchResults);
        Assert.Equal(result.Edges[0].Uuid, searchResults[0].Uuid);

        var graphResults = await graphiti.SearchAdvancedAsync("Alice Bob", groupIds: new[] { "group" });

        Assert.Equal(result.Edges[0].Uuid, Assert.Single(graphResults.Edges).Uuid);
        Assert.Contains(graphResults.Nodes, node => node.Name == "Alice");
        Assert.Contains(graphResults.Nodes, node => node.Name == "Bob");
        Assert.Equal(result.Episode.Uuid, Assert.Single(graphResults.Episodes).Uuid);
    }

    [Fact]
    public async Task AddEpisode_ExplicitMissingUuidRequiresExistingEpisode()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);

        await Assert.ThrowsAsync<NodeNotFoundException>(() => graphiti.AddEpisodeAsync(
            "conversation",
            "Alice likes Bob",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            uuid: "missing-episode"));
    }

    [Fact]
    public async Task AddTriplet_MergesAttributesSummaryAndLabels()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var existingSource = new EntityNode
        {
            Name = "Alice",
            GroupId = "group",
            Labels = new List<string> { "Person" },
            CreatedAt = now,
            Summary = "Old summary",
            Attributes = new Dictionary<string, object?> { ["age"] = 30, ["city"] = "New York" }
        };
        await existingSource.SaveAsync(driver);

        var source = new EntityNode
        {
            Uuid = existingSource.Uuid,
            Name = "Alice",
            GroupId = "group",
            Labels = new List<string> { "Employee" },
            CreatedAt = now,
            Summary = "Updated summary",
            Attributes = new Dictionary<string, object?> { ["age"] = 31, ["department"] = "Engineering" }
        };
        var target = new EntityNode
        {
            Name = "Bob",
            GroupId = "group",
            Labels = new List<string> { "Person" },
            CreatedAt = now
        };
        var edge = new EntityEdge
        {
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "group",
            CreatedAt = now,
            Name = "WORKS_WITH",
            Fact = "Alice works with Bob"
        };

        await graphiti.AddTripletAsync(source, edge, target);

        var storedSource = await EntityNode.GetByUuidAsync(driver, existingSource.Uuid);
        Assert.Equal(31, storedSource.Attributes["age"]);
        Assert.Equal("New York", storedSource.Attributes["city"]);
        Assert.Equal("Engineering", storedSource.Attributes["department"]);
        Assert.Equal("Updated summary", storedSource.Summary);
        Assert.Contains("Person", storedSource.Labels);
        Assert.Contains("Employee", storedSource.Labels);
    }

    [Fact]
    public async Task AddTriplet_ResolvesMissingUuidAgainstExistingNode()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var existing = new EntityNode
        {
            Name = "OpenAI",
            GroupId = "group",
            Labels = new List<string> { "Entity", "Company" },
            CreatedAt = now,
            Summary = "Existing organization",
            Attributes = new Dictionary<string, object?> { ["founded"] = 2015 }
        };
        await existing.SaveAsync(driver);

        var source = new EntityNode
        {
            Name = "Open AI",
            GroupId = "group",
            Labels = new List<string> { "Organization" },
            CreatedAt = now,
            Attributes = new Dictionary<string, object?> { ["industry"] = "AI" }
        };
        var target = new EntityNode
        {
            Name = "Bob",
            GroupId = "group",
            Labels = new List<string> { "Person" },
            CreatedAt = now
        };
        var edge = new EntityEdge
        {
            GroupId = "group",
            CreatedAt = now,
            Name = "HIRED",
            Fact = "Open AI hired Bob"
        };

        var result = await graphiti.AddTripletAsync(source, edge, target);

        Assert.Contains(result.Nodes, node => node.Uuid == existing.Uuid);
        Assert.Equal(existing.Uuid, Assert.Single(result.Edges).SourceNodeUuid);
        var storedSource = await EntityNode.GetByUuidAsync(driver, existing.Uuid);
        Assert.Equal(2015, storedSource.Attributes["founded"]);
        Assert.Equal("AI", storedSource.Attributes["industry"]);
        Assert.Contains("Company", storedSource.Labels);
        Assert.Contains("Organization", storedSource.Labels);
        var storedNodes = await driver.GetNodesByGroupIdsAsync<EntityNode>(new[] { "group" });
        Assert.Equal(2, storedNodes.Count);
    }

    [Fact]
    public async Task AddTriplet_ReusesExistingEdgeForDuplicateFactBetweenResolvedNodes()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = new EntityNode { Name = "Alice", GroupId = "group", CreatedAt = now };
        var bob = new EntityNode { Name = "Bob", GroupId = "group", CreatedAt = now };
        await alice.SaveAsync(driver);
        await bob.SaveAsync(driver);
        var existingEdge = new EntityEdge
        {
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "group",
            CreatedAt = now,
            Name = "KNOWS",
            Fact = "Alice knows Bob"
        };
        await existingEdge.SaveAsync(driver);

        var duplicateEdge = new EntityEdge
        {
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "group",
            CreatedAt = now.AddMinutes(1),
            Name = "KNOWS",
            Fact = " Alice   knows Bob "
        };

        var result = await graphiti.AddTripletAsync(alice, duplicateEdge, bob);

        Assert.Equal(existingEdge.Uuid, Assert.Single(result.Edges).Uuid);
        var storedEdges = await EntityEdge.GetByGroupIdsAsync(driver, new[] { "group" });
        Assert.Single(storedEdges);
        Assert.Equal("Alice knows Bob", storedEdges[0].Fact);
    }

    [Fact]
    public async Task AddTriplet_LlmDuplicateResolutionReusesExistingEdgeAndAppendsSyntheticEpisode()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["dedupe_edges.resolve_edge"] = new()
            {
                ["duplicate_facts"] = new JsonArray { 0 }
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = new EntityNode { Name = "Alice", GroupId = "group", CreatedAt = now };
        var bob = new EntityNode { Name = "Bob", GroupId = "group", CreatedAt = now };
        await alice.SaveAsync(driver);
        await bob.SaveAsync(driver);
        var existingEdge = new EntityEdge
        {
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "group",
            CreatedAt = now,
            Name = "WORKS_WITH",
            Fact = "Alice works with Bob"
        };
        await existingEdge.SaveAsync(driver);
        var duplicateEdge = new EntityEdge
        {
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "group",
            CreatedAt = now.AddMinutes(1),
            Name = "COLLABORATES_WITH",
            Fact = "Alice collaborates with Bob"
        };

        var result = await graphiti.AddTripletAsync(alice, duplicateEdge, bob);

        var edge = Assert.Single(result.Edges);
        Assert.Equal(existingEdge.Uuid, edge.Uuid);
        var storedEdge = await EntityEdge.GetByUuidAsync(driver, existingEdge.Uuid);
        var episodeUuid = Assert.Single(storedEdge.Episodes);
        Assert.False(string.IsNullOrWhiteSpace(episodeUuid));
        var call = Assert.Single(llm.Calls, call => call.PromptName == "dedupe_edges.resolve_edge");
        Assert.Equal("EdgeResolutionResponse", call.ResponseModel?.Name);
    }

    [Fact]
    public async Task AddTriplet_InvalidatesContradictedExistingFact()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["dedupe_edges.resolve_edge"] = new()
            {
                ["contradicted_facts"] = new JsonArray { 0 }
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);
        var oldValidAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var newValidAt = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = new EntityNode { Name = "Alice", GroupId = "group", CreatedAt = oldValidAt };
        var acme = new EntityNode { Name = "Acme", GroupId = "group", CreatedAt = oldValidAt };
        await alice.SaveAsync(driver);
        await acme.SaveAsync(driver);
        var oldEdge = new EntityEdge
        {
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = acme.Uuid,
            GroupId = "group",
            CreatedAt = oldValidAt,
            Name = "WORKS_AT",
            Fact = "Alice works at Acme",
            ValidAt = oldValidAt
        };
        await oldEdge.SaveAsync(driver);
        var newEdge = new EntityEdge
        {
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = acme.Uuid,
            GroupId = "group",
            CreatedAt = newValidAt,
            Name = "LEFT",
            Fact = "Alice left Acme",
            ValidAt = newValidAt
        };

        var result = await graphiti.AddTripletAsync(alice, newEdge, acme);

        Assert.Contains(result.Edges, edge => edge.Uuid == newEdge.Uuid);
        var invalidated = Assert.Single(result.Edges, edge => edge.Uuid == oldEdge.Uuid);
        Assert.Equal(newValidAt, invalidated.InvalidAt);
        Assert.NotNull(invalidated.ExpiredAt);
        var storedOld = await EntityEdge.GetByUuidAsync(driver, oldEdge.Uuid);
        Assert.Equal(newValidAt, storedOld.InvalidAt);
        Assert.NotNull(storedOld.ExpiredAt);
    }

    [Fact]
    public async Task GetNodesAndEdgesByEpisode_PreservesPerEpisodeEdgeMultiplicity()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var edge = new EntityEdge
        {
            SourceNodeUuid = "alice",
            TargetNodeUuid = "bob",
            GroupId = "group",
            Name = "KNOWS",
            Fact = "Alice knows Bob",
            CreatedAt = now
        };
        await edge.SaveAsync(driver);
        var firstEpisode = new EpisodicNode
        {
            Name = "first",
            GroupId = "group",
            CreatedAt = now,
            ValidAt = now,
            Content = "Alice knows Bob",
            EntityEdges = new List<string> { edge.Uuid }
        };
        var secondEpisode = new EpisodicNode
        {
            Name = "second",
            GroupId = "group",
            CreatedAt = now.AddMinutes(1),
            ValidAt = now.AddMinutes(1),
            Content = "Alice still knows Bob",
            EntityEdges = new List<string> { edge.Uuid }
        };
        await firstEpisode.SaveAsync(driver);
        await secondEpisode.SaveAsync(driver);

        var results = await graphiti.GetNodesAndEdgesByEpisodeAsync(
            new[] { firstEpisode.Uuid, secondEpisode.Uuid });

        Assert.Equal(2, results.Edges.Count);
        Assert.All(results.Edges, resultEdge => Assert.Equal(edge.Uuid, resultEdge.Uuid));
    }

    [Fact]
    public async Task RemoveEpisode_PrunesEpisodeFromSharedEntityEdge()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var source = new EntityNode { Uuid = "alice", Name = "Alice", GroupId = "group" };
        var target = new EntityNode { Uuid = "bob", Name = "Bob", GroupId = "group" };
        var edge = new EntityEdge
        {
            Uuid = "edge",
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "group",
            Name = "KNOWS",
            Fact = "Alice knows Bob",
            CreatedAt = now
        };
        var firstEpisode = new EpisodicNode
        {
            Uuid = "episode-first",
            Name = "first",
            GroupId = "group",
            CreatedAt = now,
            ValidAt = now,
            EntityEdges = new List<string> { edge.Uuid }
        };
        var secondEpisode = new EpisodicNode
        {
            Uuid = "episode-second",
            Name = "second",
            GroupId = "group",
            CreatedAt = now.AddMinutes(1),
            ValidAt = now.AddMinutes(1),
            EntityEdges = new List<string> { edge.Uuid }
        };
        edge.Episodes = new List<string> { firstEpisode.Uuid, secondEpisode.Uuid, firstEpisode.Uuid };

        await SaveEpisodeRemovalFixtureAsync(driver, source, target, edge, firstEpisode, secondEpisode);

        await graphiti.RemoveEpisodeAsync(firstEpisode.Uuid);

        var storedEdge = await EntityEdge.GetByUuidAsync(driver, edge.Uuid);
        Assert.Equal(new[] { secondEpisode.Uuid }, storedEdge.Episodes);
        Assert.Equal(source.Uuid, (await EntityNode.GetByUuidAsync(driver, source.Uuid)).Uuid);
        Assert.Equal(target.Uuid, (await EntityNode.GetByUuidAsync(driver, target.Uuid)).Uuid);
        await Assert.ThrowsAsync<NodeNotFoundException>(() =>
            EpisodicNode.GetByUuidAsync(driver, firstEpisode.Uuid));
    }

    [Fact]
    public async Task RemoveEpisode_DeletesEntityEdgeWhenNoEpisodesRemain()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var source = new EntityNode { Uuid = "alice", Name = "Alice", GroupId = "group" };
        var target = new EntityNode { Uuid = "bob", Name = "Bob", GroupId = "group" };
        var episode = new EpisodicNode
        {
            Uuid = "episode",
            Name = "only episode",
            GroupId = "group",
            CreatedAt = now,
            ValidAt = now,
            EntityEdges = new List<string> { "edge" }
        };
        var edge = new EntityEdge
        {
            Uuid = "edge",
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "group",
            Name = "KNOWS",
            Fact = "Alice knows Bob",
            CreatedAt = now,
            Episodes = new List<string> { episode.Uuid }
        };

        await SaveEpisodeRemovalFixtureAsync(driver, source, target, edge, episode);

        await graphiti.RemoveEpisodeAsync(episode.Uuid);

        await Assert.ThrowsAsync<EdgeNotFoundException>(() =>
            EntityEdge.GetByUuidAsync(driver, edge.Uuid));
        await Assert.ThrowsAsync<NodeNotFoundException>(() =>
            EntityNode.GetByUuidAsync(driver, source.Uuid));
        await Assert.ThrowsAsync<NodeNotFoundException>(() =>
            EntityNode.GetByUuidAsync(driver, target.Uuid));
        await Assert.ThrowsAsync<NodeNotFoundException>(() =>
            EpisodicNode.GetByUuidAsync(driver, episode.Uuid));
    }

    [Fact]
    public async Task RemoveEpisode_RepairsSagaNextEpisodeEdgesWhenRemovingMiddleEpisode()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var fixture = await SaveSagaRemovalFixtureAsync(driver, episodeCount: 3);
        var first = fixture.Episodes[0];
        var middle = fixture.Episodes[1];
        var last = fixture.Episodes[2];

        await graphiti.RemoveEpisodeAsync(middle.Uuid);

        var hasEpisodeTargets = (await HasEpisodeEdge.GetByGroupIdsAsync(driver, new[] { "group" }))
            .Select(edge => edge.TargetNodeUuid)
            .Order(StringComparer.Ordinal)
            .ToList();
        var nextEpisodeEdge = Assert.Single(await NextEpisodeEdge.GetByGroupIdsAsync(driver, new[] { "group" }));
        var saga = await SagaNode.GetByUuidAsync(driver, fixture.Saga.Uuid);

        Assert.Equal(new[] { first.Uuid, last.Uuid }.Order(StringComparer.Ordinal), hasEpisodeTargets);
        Assert.Equal(first.Uuid, nextEpisodeEdge.SourceNodeUuid);
        Assert.Equal(last.Uuid, nextEpisodeEdge.TargetNodeUuid);
        Assert.Equal(first.Uuid, saga.FirstEpisodeUuid);
        Assert.Equal(last.Uuid, saga.LastEpisodeUuid);
        await Assert.ThrowsAsync<NodeNotFoundException>(() =>
            EpisodicNode.GetByUuidAsync(driver, middle.Uuid));
    }

    [Fact]
    public async Task RemoveEpisode_ReusesExistingSagaBypassWhenRemovingMiddleEpisode()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var fixture = await SaveSagaRemovalFixtureAsync(driver, episodeCount: 3);
        var first = fixture.Episodes[0];
        var middle = fixture.Episodes[1];
        var last = fixture.Episodes[2];
        await new NextEpisodeEdge
        {
            Uuid = "next-existing-bypass",
            SourceNodeUuid = first.Uuid,
            TargetNodeUuid = last.Uuid,
            GroupId = "group",
            CreatedAt = last.CreatedAt
        }.SaveAsync(driver);

        await graphiti.RemoveEpisodeAsync(middle.Uuid);

        var nextEpisodeEdge = Assert.Single(await NextEpisodeEdge.GetByGroupIdsAsync(driver, new[] { "group" }));
        var saga = await SagaNode.GetByUuidAsync(driver, fixture.Saga.Uuid);

        Assert.Equal("next-existing-bypass", nextEpisodeEdge.Uuid);
        Assert.Equal(first.Uuid, nextEpisodeEdge.SourceNodeUuid);
        Assert.Equal(last.Uuid, nextEpisodeEdge.TargetNodeUuid);
        Assert.Equal(first.Uuid, saga.FirstEpisodeUuid);
        Assert.Equal(last.Uuid, saga.LastEpisodeUuid);
    }

    [Fact]
    public async Task RemoveEpisode_RepairsSagaBoundsByValidCreatedAndUuidOrder()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var validAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var createdAt = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        var saga = new SagaNode
        {
            Uuid = "saga",
            Name = "launch",
            GroupId = "group",
            CreatedAt = createdAt,
            FirstEpisodeUuid = "stale-first",
            LastEpisodeUuid = "stale-last"
        };
        var removed = new EpisodicNode
        {
            Uuid = "episode-remove",
            Name = "remove",
            GroupId = "group",
            CreatedAt = createdAt.AddMinutes(3),
            ValidAt = validAt.AddMinutes(1)
        };
        var uuidFirst = new EpisodicNode
        {
            Uuid = "episode-a",
            Name = "a",
            GroupId = "group",
            CreatedAt = createdAt,
            ValidAt = validAt
        };
        var uuidSecond = new EpisodicNode
        {
            Uuid = "episode-b",
            Name = "b",
            GroupId = "group",
            CreatedAt = createdAt,
            ValidAt = validAt
        };
        var validLast = new EpisodicNode
        {
            Uuid = "episode-last",
            Name = "last",
            GroupId = "group",
            CreatedAt = createdAt.AddMinutes(1),
            ValidAt = validAt.AddMinutes(2)
        };

        await saga.SaveAsync(driver);
        foreach (var episode in new[] { validLast, removed, uuidSecond, uuidFirst })
        {
            await episode.SaveAsync(driver);
            await new HasEpisodeEdge
            {
                Uuid = $"has-{episode.Uuid}",
                SourceNodeUuid = saga.Uuid,
                TargetNodeUuid = episode.Uuid,
                GroupId = "group",
                CreatedAt = episode.CreatedAt
            }.SaveAsync(driver);
        }

        await graphiti.RemoveEpisodeAsync(removed.Uuid);

        var repairedSaga = await SagaNode.GetByUuidAsync(driver, saga.Uuid);
        Assert.Equal(uuidFirst.Uuid, repairedSaga.FirstEpisodeUuid);
        Assert.Equal(validLast.Uuid, repairedSaga.LastEpisodeUuid);
        await Assert.ThrowsAsync<NodeNotFoundException>(() =>
            EpisodicNode.GetByUuidAsync(driver, removed.Uuid));
    }

    [Fact]
    public async Task RemoveEpisode_RepairsSagaBoundsWhenRemovingFirstOrLastEpisode()
    {
        var headDriver = new InMemoryGraphDriver();
        var headGraphiti = new Graphiti(graphDriver: headDriver);
        var headFixture = await SaveSagaRemovalFixtureAsync(headDriver, episodeCount: 3);

        await headGraphiti.RemoveEpisodeAsync(headFixture.Episodes[0].Uuid);

        var headSaga = await SagaNode.GetByUuidAsync(headDriver, headFixture.Saga.Uuid);
        var headNext = Assert.Single(await NextEpisodeEdge.GetByGroupIdsAsync(headDriver, new[] { "group" }));
        Assert.Equal(headFixture.Episodes[1].Uuid, headSaga.FirstEpisodeUuid);
        Assert.Equal(headFixture.Episodes[2].Uuid, headSaga.LastEpisodeUuid);
        Assert.Equal(headFixture.Episodes[1].Uuid, headNext.SourceNodeUuid);
        Assert.Equal(headFixture.Episodes[2].Uuid, headNext.TargetNodeUuid);

        var tailDriver = new InMemoryGraphDriver();
        var tailGraphiti = new Graphiti(graphDriver: tailDriver);
        var tailFixture = await SaveSagaRemovalFixtureAsync(tailDriver, episodeCount: 3);

        await tailGraphiti.RemoveEpisodeAsync(tailFixture.Episodes[2].Uuid);

        var tailSaga = await SagaNode.GetByUuidAsync(tailDriver, tailFixture.Saga.Uuid);
        var tailNext = Assert.Single(await NextEpisodeEdge.GetByGroupIdsAsync(tailDriver, new[] { "group" }));
        Assert.Equal(tailFixture.Episodes[0].Uuid, tailSaga.FirstEpisodeUuid);
        Assert.Equal(tailFixture.Episodes[1].Uuid, tailSaga.LastEpisodeUuid);
        Assert.Equal(tailFixture.Episodes[0].Uuid, tailNext.SourceNodeUuid);
        Assert.Equal(tailFixture.Episodes[1].Uuid, tailNext.TargetNodeUuid);
    }

    [Fact]
    public async Task RemoveEpisode_ClearsSagaBoundsWhenRemovingOnlyEpisode()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var fixture = await SaveSagaRemovalFixtureAsync(driver, episodeCount: 1);

        await graphiti.RemoveEpisodeAsync(fixture.Episodes[0].Uuid);

        var saga = await SagaNode.GetByUuidAsync(driver, fixture.Saga.Uuid);
        var hasEpisodeEdges = await HasEpisodeEdge.GetByGroupIdsAsync(driver, new[] { "group" });
        var nextEpisodeEdges = await NextEpisodeEdge.GetByGroupIdsAsync(driver, new[] { "group" });

        Assert.Null(saga.FirstEpisodeUuid);
        Assert.Null(saga.LastEpisodeUuid);
        Assert.Empty(hasEpisodeEdges);
        Assert.Empty(nextEpisodeEdges);
    }

    [Fact]
    public async Task AddEpisode_DeduplicatesFuzzyEntityNamesAgainstExistingNodes()
    {
        var driver = new InMemoryGraphDriver();
        var existing = new EntityNode
        {
            Name = "OpenAI",
            GroupId = "group",
            Labels = new List<string> { "Entity" },
            Summary = "Existing summary"
        };
        await existing.SaveAsync(driver);

        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new JsonObject
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Open AI", ["entity_type"] = "Organization" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Open AI",
                        ["target"] = "Bob",
                        ["relation_type"] = "HIRED",
                        ["fact"] = "Open AI hired Bob."
                    }
                }
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Open AI hired Bob.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        Assert.Equal(2, result.Nodes.Count);
        Assert.Contains(result.Nodes, node => node.Uuid == existing.Uuid);
        var edge = Assert.Single(result.Edges);
        Assert.Equal(existing.Uuid, edge.SourceNodeUuid);
        Assert.Equal("Open AI hired Bob.", edge.Fact);

        var storedExisting = await EntityNode.GetByUuidAsync(driver, existing.Uuid);
        Assert.Contains("Organization", storedExisting.Labels);
    }

    [Fact]
    public async Task AddEpisode_CollapsesExactDuplicateExtractedEntitiesBySpecificity()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new JsonObject
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Entity" },
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Bob",
                        ["relation_type"] = "KNOWS",
                        ["fact"] = "Alice knows Bob."
                    }
                }
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice knows Bob.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        Assert.Equal(2, result.Nodes.Count);
        var alice = Assert.Single(result.Nodes, node => node.Name.Trim() == "Alice");
        Assert.Contains("Person", alice.Labels);
        var edge = Assert.Single(result.Edges);
        Assert.Equal(alice.Uuid, edge.SourceNodeUuid);
    }

    [Fact]
    public async Task AddEpisode_MapsPythonStyleEntityTypeIdsToLabels()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new JsonObject
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type_id"] = 1 },
                    new JsonObject { ["name"] = "Acme", ["entity_type_id"] = 2 }
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
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new("Person"),
                ["Organization"] = new("Organization")
            });

        var alice = Assert.Single(result.Nodes, node => node.Name == "Alice");
        var acme = Assert.Single(result.Nodes, node => node.Name == "Acme");
        Assert.Contains("Person", alice.Labels);
        Assert.Contains("Organization", acme.Labels);
        Assert.Single(result.Edges);
    }

    [Fact]
    public async Task AddEpisode_HydratesDeclaredEntityAttributes()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Bob",
                        ["relation_type"] = "KNOWS",
                        ["fact"] = "Alice knows Bob."
                    }
                }
            },
            ["extract_nodes.extract_attributes"] = new()
            {
                ["Role"] = "engineer",
                ["ACTIVE"] = true
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice knows Bob.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new(
                    "Person",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["role"] = new("Job title"),
                        ["active"] = new("Whether the person is active", "boolean")
                    })
            });

        var alice = Assert.Single(result.Nodes, node => node.Name == "Alice");
        var storedAlice = await EntityNode.GetByUuidAsync(driver, alice.Uuid);
        Assert.Equal("engineer", storedAlice.Attributes["role"]);
        Assert.Equal(true, storedAlice.Attributes["active"]);
        Assert.Equal(2, llm.PromptNames.Count(prompt => prompt == "extract_nodes.extract_attributes"));
        var attributeCalls = llm.Calls
            .Where(call => call.PromptName == "extract_nodes.extract_attributes")
            .ToList();
        Assert.Same(attributeCalls[0].ResponseSchema, attributeCalls[1].ResponseSchema);
        Assert.All(
            attributeCalls,
            call =>
            {
                Assert.True(call.AttributeExtraction);
                Assert.Equal("NodeAttributeResponse", call.ResponseSchema?.Name);
                var schema = JsonNode.Parse(call.ResponseSchema!.SchemaElement.GetRawText())!.AsObject();
                var schemaAttributes = schema["properties"]!["attributes"]!["properties"]!.AsObject();
                Assert.Equal(new[] { "active", "role" }, schemaAttributes.Select(pair => pair.Key));
                var context = JsonNode.Parse(call.Messages[^1].Content)!.AsObject();
                var attributes = context["entity_type"]!["attributes"]!.AsObject();
                Assert.Equal(new[] { "active", "role" }, attributes.Select(pair => pair.Key));
                Assert.Contains(
                    context["connected_facts"]!.AsArray(),
                    fact => fact?.GetValue<string>() == "Alice knows Bob.");
            });
    }

    [Fact]
    public async Task AddEpisode_HydratesNestedStructuredAttributeResponses()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
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
                        ["source"] = "Alice",
                        ["target"] = "Acme",
                        ["relation_type"] = "WORKS_AT",
                        ["fact"] = "Alice works at Acme."
                    }
                }
            },
            ["extract_nodes.extract_attributes"] = new()
            {
                ["attributes"] = new JsonObject
                {
                    ["role"] = "engineer",
                    ["profile"] = new JsonObject
                    {
                        ["tags"] = new JsonArray("backend", "search"),
                        ["history"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["company"] = "Acme",
                                ["years"] = 2
                            }
                        }
                    }
                }
            },
            ["extract_edges.extract_attributes"] = new()
            {
                ["attributes"] = new JsonObject
                {
                    ["confidence"] = 0.91
                }
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new(
                    "Person",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["role"] = new("Job title"),
                        ["profile"] = new("Structured employment profile", "object")
                    })
            },
            edgeTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["WORKS_AT"] = new(
                    "WORKS_AT",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["confidence"] = new("Extraction confidence", "number")
                    })
            },
            edgeTypeMap: new Dictionary<(string SourceType, string TargetType), IReadOnlyList<string>>
            {
                [("Person", "Organization")] = new[] { "WORKS_AT" }
            });

        var alice = Assert.Single(result.Nodes, node => node.Name == "Alice");
        var edge = Assert.Single(result.Edges);
        Assert.Equal("engineer", alice.Attributes["role"]);
        var profile = Assert.IsType<Dictionary<string, object?>>(alice.Attributes["profile"]);
        Assert.Equal(new object?[] { "backend", "search" }, Assert.IsType<List<object?>>(profile["tags"]));
        var history = Assert.IsType<List<object?>>(profile["history"]);
        var priorRole = Assert.IsType<Dictionary<string, object?>>(Assert.Single(history));
        Assert.Equal("Acme", priorRole["company"]);
        Assert.Equal(2, priorRole["years"]);
        Assert.Equal(0.91, edge.Attributes["confidence"]);
    }

    [Fact]
    public async Task AddEpisode_NodeAttributeExtractionUsesBoundedConcurrency()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new ConcurrencyTrackingLlmClient(
            new Dictionary<string, JsonObject>
            {
                ["extract_nodes.extract_message"] = new()
                {
                    ["extracted_entities"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                        new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" },
                        new JsonObject { ["name"] = "Carol", ["entity_type"] = "Person" },
                        new JsonObject { ["name"] = "Dave", ["entity_type"] = "Person" }
                    },
                    ["edges"] = new JsonArray()
                },
                ["extract_nodes.extract_attributes"] = new()
                {
                    ["role"] = "engineer"
                }
            },
            trackedPromptName: "extract_nodes.extract_attributes",
            delay: TimeSpan.FromMilliseconds(75));
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm, maxCoroutines: 2);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice, Bob, Carol, and Dave are engineers.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new(
                    "Person",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["role"] = new("Job title")
                    })
            });

        Assert.Equal(4, result.Nodes.Count);
        Assert.Equal(4, llm.TrackedPromptCalls);
        Assert.Equal(2, llm.MaxObservedConcurrency);
        Assert.All(result.Nodes, node => Assert.Equal("engineer", node.Attributes["role"]));
    }

    [Fact]
    public async Task AddEpisode_NodeAttributePromptExcludesReusedDuplicateEdges()
    {
        var driver = new InMemoryGraphDriver();
        var alice = new EntityNode
        {
            Name = "Alice",
            GroupId = "group",
            Labels = new List<string> { "Entity", "Person" }
        };
        var bob = new EntityNode
        {
            Name = "Bob",
            GroupId = "group",
            Labels = new List<string> { "Entity", "Person" }
        };
        await alice.SaveAsync(driver);
        await bob.SaveAsync(driver);
        var existingEdge = new EntityEdge
        {
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "group",
            Name = "KNOWS",
            Fact = "Alice knows Bob.",
            Episodes = new List<string> { "previous-episode" }
        };
        await existingEdge.SaveAsync(driver);

        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Bob",
                        ["relation_type"] = "KNOWS",
                        ["fact"] = " alice   knows bob. "
                    }
                }
            },
            ["extract_nodes.extract_attributes"] = new()
            {
                ["role"] = "engineer"
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice knows Bob.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new(
                    "Person",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["role"] = new("Job title")
                    })
            });

        var edge = Assert.Single(result.Edges);
        Assert.Equal(existingEdge.Uuid, edge.Uuid);
        Assert.Contains("previous-episode", edge.Episodes);
        Assert.Contains(result.Episode.Uuid, edge.Episodes);
        var attributeCalls = llm.Calls
            .Where(call => call.PromptName == "extract_nodes.extract_attributes")
            .ToList();
        Assert.Equal(2, attributeCalls.Count);
        Assert.All(attributeCalls, call =>
        {
            var context = JsonNode.Parse(call.Messages[^1].Content)!.AsObject();
            Assert.Empty(context["connected_facts"]!.AsArray());
        });
    }

    [Fact]
    public async Task AddEpisode_AttributeHydrationOverlaysExistingResolvedNodeAttributes()
    {
        var driver = new InMemoryGraphDriver();
        var existing = new EntityNode
        {
            Name = "Alice",
            GroupId = "group",
            Labels = new List<string> { "Entity", "Person" },
            Attributes = new Dictionary<string, object?>
            {
                ["city"] = "Paris",
                ["department"] = "Research"
            }
        };
        await existing.SaveAsync(driver);
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Bob",
                        ["relation_type"] = "KNOWS",
                        ["fact"] = "Alice knows Bob."
                    }
                }
            },
            ["extract_nodes.extract_attributes"] = new()
            {
                ["role"] = "engineer"
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice knows Bob.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new(
                    "Person",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["role"] = new("Job title"),
                        ["city"] = new("Home city")
                    })
            });

        var storedAlice = await EntityNode.GetByUuidAsync(driver, existing.Uuid);
        Assert.Equal("engineer", storedAlice.Attributes["role"]);
        Assert.Equal("Paris", storedAlice.Attributes["city"]);
        Assert.Equal("Research", storedAlice.Attributes["department"]);
    }

    [Fact]
    public async Task AddEpisode_AttributeHydrationDropsOverlongNodeStringsWithoutClearingPriorValues()
    {
        var driver = new InMemoryGraphDriver();
        var existing = new EntityNode
        {
            Name = "Alice",
            GroupId = "group",
            Labels = new List<string> { "Entity", "Person" },
            Attributes = new Dictionary<string, object?>
            {
                ["city"] = "Paris"
            }
        };
        await existing.SaveAsync(driver);
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Bob",
                        ["relation_type"] = "KNOWS",
                        ["fact"] = "Alice knows Bob."
                    }
                }
            },
            ["extract_nodes.extract_attributes"] = new()
            {
                ["role"] = "engineer",
                ["city"] = new string('x', 251)
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice knows Bob.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new(
                    "Person",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["role"] = new("Job title"),
                        ["city"] = new("Home city")
                    })
            });

        var storedAlice = await EntityNode.GetByUuidAsync(driver, existing.Uuid);
        Assert.Equal("engineer", storedAlice.Attributes["role"]);
        Assert.Equal("Paris", storedAlice.Attributes["city"]);
    }

    [Fact]
    public async Task AddEpisode_SkipsAttributePromptWhenEntityTypeHasNoDeclaredAttributes()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Bob",
                        ["relation_type"] = "KNOWS",
                        ["fact"] = "Alice knows Bob."
                    }
                }
            },
            ["extract_nodes.extract_attributes"] = new()
            {
                ["role"] = "engineer"
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice knows Bob.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new("Person")
            });

        Assert.DoesNotContain("extract_nodes.extract_attributes", llm.PromptNames);
        Assert.All(result.Nodes, node => Assert.Empty(node.Attributes));
    }

    [Fact]
    public async Task AddEpisodeBulk_HydratesDeclaredEntityAttributes()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Bob",
                        ["relation_type"] = "KNOWS",
                        ["fact"] = "Alice knows Bob."
                    }
                }
            },
            ["extract_nodes.extract_attributes"] = new()
            {
                ["role"] = "engineer"
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var result = await graphiti.AddEpisodeBulkAsync(
            new[]
            {
                new RawEpisode
                {
                    Name = "conversation",
                    Content = "Alice knows Bob.",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                }
            },
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new(
                    "Person",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["role"] = new("Job title")
                    })
            });

        var alice = Assert.Single(result.Nodes, node => node.Name == "Alice");
        var storedAlice = await EntityNode.GetByUuidAsync(driver, alice.Uuid);
        Assert.Equal("engineer", storedAlice.Attributes["role"]);
        Assert.Equal(2, llm.PromptNames.Count(prompt => prompt == "extract_nodes.extract_attributes"));
    }

    [Fact]
    public async Task AddEpisodeBulk_UsesSavedBatchEpisodesForPreviousContext()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new JsonObject());
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        await graphiti.AddEpisodeBulkAsync(
            new[]
            {
                new RawEpisode
                {
                    Name = "first",
                    Content = "Alice joins the project.",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                },
                new RawEpisode
                {
                    Name = "second",
                    Content = "Bob asks Alice for help.",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc)
                }
            },
            groupId: "group");

        var extractionCalls = llm.Calls
            .Where(call => call.PromptName == "extract_nodes.extract_message")
            .ToList();
        var secondContext = JsonNode.Parse(extractionCalls[1].Messages[^1].Content)!.AsObject();
        var previousEpisodes = secondContext["previous_episodes"]!.AsArray();

        Assert.Contains(
            previousEpisodes,
            episode => episode?["content"]?.GetValue<string>() == "Alice joins the project.");
    }

    [Fact]
    public async Task AddEpisodeBulk_IncludesPreviousEpisodesAcrossSourceTypes()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new JsonObject());
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        await graphiti.AddEpisodeBulkAsync(
            new[]
            {
                new RawEpisode
                {
                    Name = "brief",
                    Content = "Alice owns the migration brief.",
                    SourceDescription = "doc",
                    Source = EpisodeType.Text,
                    ReferenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                },
                new RawEpisode
                {
                    Name = "message",
                    Content = "Bob asks Alice about the migration.",
                    SourceDescription = "message",
                    Source = EpisodeType.Message,
                    ReferenceTime = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc)
                }
            },
            groupId: "group");

        var messageExtractionCall = Assert.Single(
            llm.Calls,
            call => call.PromptName == "extract_nodes.extract_message");
        var context = JsonNode.Parse(messageExtractionCall.Messages[^1].Content)!.AsObject();
        var previousEpisodes = context["previous_episodes"]!.AsArray();

        Assert.Contains(
            previousEpisodes,
            episode => episode?["content"]?.GetValue<string>() == "Alice owns the migration brief.");
    }

    [Fact]
    public async Task AddEpisodeBulk_ReusesExistingEpisodeWhenUuidIsProvided()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var existing = new EpisodicNode
        {
            Uuid = "existing-episode",
            Name = "stored",
            GroupId = "group",
            Source = EpisodeType.Message,
            SourceDescription = "message",
            Content = "Stored episode content.",
            CreatedAt = now,
            ValidAt = now
        };
        await existing.SaveAsync(driver);
        var llm = new StaticLlmClient(new JsonObject());
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var result = await graphiti.AddEpisodeBulkAsync(
            new[]
            {
                new RawEpisode
                {
                    Uuid = existing.Uuid,
                    Name = "replacement",
                    Content = "Replacement content should not be used.",
                    SourceDescription = "message",
                    ReferenceTime = now.AddHours(1)
                }
            },
            groupId: "group");

        var context = JsonNode.Parse(llm.Calls[0].Messages[^1].Content)!.AsObject();

        Assert.Equal(existing.Uuid, Assert.Single(result.Episodes).Uuid);
        Assert.Equal("Stored episode content.", context["episode_content"]?.GetValue<string>());
    }

    [Fact]
    public async Task AddEpisodeBulk_ExplicitMissingUuidRequiresExistingEpisode()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);

        await Assert.ThrowsAsync<NodeNotFoundException>(() => graphiti.AddEpisodeBulkAsync(
            new[]
            {
                new RawEpisode
                {
                    Uuid = "missing-episode",
                    Name = "replacement",
                    Content = "Replacement content should not be created.",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc)
                }
            },
            groupId: "group"));
    }

    [Fact]
    public async Task AddEpisodeBulk_HonorsStoreRawEpisodeContentAfterExtraction()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new JsonObject());
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: llm,
            storeRawEpisodeContent: false);
        var firstReference = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var secondReference = firstReference.AddMinutes(5);

        var result = await graphiti.AddEpisodeBulkAsync(
            new[]
            {
                new RawEpisode
                {
                    Name = "first",
                    Content = "Alice joins the project.",
                    SourceDescription = "message",
                    ReferenceTime = firstReference
                },
                new RawEpisode
                {
                    Name = "second",
                    Content = "Bob asks Alice for help.",
                    SourceDescription = "message",
                    ReferenceTime = secondReference
                }
            },
            groupId: "group");

        var extractionCalls = llm.Calls
            .Where(call => call.PromptName == "extract_nodes.extract_message")
            .ToList();
        var secondContext = JsonNode.Parse(extractionCalls[1].Messages[^1].Content)!.AsObject();
        var previousEpisodes = secondContext["previous_episodes"]!.AsArray();
        var storedEpisodes = await EpisodicNode.GetByUuidsAsync(
            driver,
            result.Episodes.Select(episode => episode.Uuid));

        Assert.Contains(
            previousEpisodes,
            episode => episode?["content"]?.GetValue<string>() == "Alice joins the project.");
        Assert.All(storedEpisodes, episode => Assert.Empty(episode.Content));
    }

    [Fact]
    public async Task AddEpisodeBulk_ExtractsEpisodesConcurrentlyAndPreservesInputOrder()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new BulkExtractionTrackingLlmClient();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: llm,
            maxCoroutines: 2);

        var result = await graphiti.AddEpisodeBulkAsync(
            new[]
            {
                new RawEpisode
                {
                    Name = "first",
                    Content = "Alpha event",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                },
                new RawEpisode
                {
                    Name = "second",
                    Content = "Beta event",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 1, 1, 12, 1, 0, DateTimeKind.Utc)
                },
                new RawEpisode
                {
                    Name = "third",
                    Content = "Gamma event",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 1, 1, 12, 2, 0, DateTimeKind.Utc)
                }
            },
            groupId: "group");

        Assert.Equal(new[] { "first", "second", "third" }, result.Episodes.Select(episode => episode.Name));
        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, result.Nodes.Select(node => node.Name));
        Assert.Equal(3, llm.TrackedPromptCalls);
        Assert.InRange(llm.MaxObservedConcurrency, 2, 2);
        Assert.Equal(3, llm.CompletedContents.Count);
        Assert.Equal("Beta event", llm.CompletedContents[0]);
    }

    [Fact]
    public async Task AddEpisodeBulk_AssociatesSagaByValidAtWithoutReorderingResults()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new JsonObject()));
        var earliest = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var middle = earliest.AddHours(1);
        var latest = earliest.AddHours(2);

        var result = await graphiti.AddEpisodeBulkAsync(
            new[]
            {
                new RawEpisode
                {
                    Name = "latest",
                    Content = "Latest event.",
                    SourceDescription = "message",
                    ReferenceTime = latest
                },
                new RawEpisode
                {
                    Name = "earliest",
                    Content = "Earliest event.",
                    SourceDescription = "message",
                    ReferenceTime = earliest
                },
                new RawEpisode
                {
                    Name = "middle",
                    Content = "Middle event.",
                    SourceDescription = "message",
                    ReferenceTime = middle
                }
            },
            groupId: "group",
            saga: "launch");

        var saga = await driver.FindSagaByNameAsync("launch", "group");
        Assert.NotNull(saga);
        var nextEdges = await NextEpisodeEdge.GetByGroupIdsAsync(driver, new[] { "group" });
        var byName = result.Episodes.ToDictionary(episode => episode.Name, StringComparer.Ordinal);

        Assert.Equal(new[] { "latest", "earliest", "middle" }, result.Episodes.Select(episode => episode.Name));
        Assert.Equal(byName["earliest"].Uuid, saga.FirstEpisodeUuid);
        Assert.Equal(byName["latest"].Uuid, saga.LastEpisodeUuid);
        Assert.Contains(nextEdges, edge =>
            edge.SourceNodeUuid == byName["earliest"].Uuid && edge.TargetNodeUuid == byName["middle"].Uuid);
        Assert.Contains(nextEdges, edge =>
            edge.SourceNodeUuid == byName["middle"].Uuid && edge.TargetNodeUuid == byName["latest"].Uuid);
    }

    [Fact]
    public async Task AddEpisodeBulk_AppendsToExistingSagaChainByValidAt()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new JsonObject()));
        var existingTime = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var earliestNewTime = existingTime.AddHours(1);
        var latestNewTime = existingTime.AddHours(2);
        var saga = new SagaNode
        {
            Name = "launch",
            GroupId = "group",
            CreatedAt = existingTime
        };
        var existingEpisode = new EpisodicNode
        {
            Name = "existing",
            GroupId = "group",
            Source = EpisodeType.Message,
            SourceDescription = "message",
            Content = "Existing event.",
            CreatedAt = existingTime,
            ValidAt = existingTime
        };
        saga.FirstEpisodeUuid = existingEpisode.Uuid;
        saga.LastEpisodeUuid = existingEpisode.Uuid;
        await saga.SaveAsync(driver);
        await existingEpisode.SaveAsync(driver);
        await new HasEpisodeEdge
        {
            SourceNodeUuid = saga.Uuid,
            TargetNodeUuid = existingEpisode.Uuid,
            GroupId = "group",
            CreatedAt = existingTime
        }.SaveAsync(driver);

        var result = await graphiti.AddEpisodeBulkAsync(
            new[]
            {
                new RawEpisode
                {
                    Name = "latest-new",
                    Content = "Latest new event.",
                    SourceDescription = "message",
                    ReferenceTime = latestNewTime
                },
                new RawEpisode
                {
                    Name = "earliest-new",
                    Content = "Earliest new event.",
                    SourceDescription = "message",
                    ReferenceTime = earliestNewTime
                }
            },
            groupId: "group",
            saga: "launch");

        var storedSaga = await SagaNode.GetByUuidAsync(driver, saga.Uuid);
        var nextEdges = await NextEpisodeEdge.GetByGroupIdsAsync(driver, new[] { "group" });
        var hasEdges = await HasEpisodeEdge.GetByGroupIdsAsync(driver, new[] { "group" });
        var byName = result.Episodes.ToDictionary(episode => episode.Name, StringComparer.Ordinal);

        Assert.Equal(new[] { "latest-new", "earliest-new" }, result.Episodes.Select(episode => episode.Name));
        Assert.Equal(existingEpisode.Uuid, storedSaga.FirstEpisodeUuid);
        Assert.Equal(byName["latest-new"].Uuid, storedSaga.LastEpisodeUuid);
        Assert.Contains(nextEdges, edge =>
            edge.SourceNodeUuid == existingEpisode.Uuid && edge.TargetNodeUuid == byName["earliest-new"].Uuid);
        Assert.Contains(nextEdges, edge =>
            edge.SourceNodeUuid == byName["earliest-new"].Uuid && edge.TargetNodeUuid == byName["latest-new"].Uuid);
        Assert.Contains(hasEdges, edge => edge.TargetNodeUuid == existingEpisode.Uuid);
        Assert.Contains(hasEdges, edge => edge.TargetNodeUuid == byName["earliest-new"].Uuid);
        Assert.Contains(hasEdges, edge => edge.TargetNodeUuid == byName["latest-new"].Uuid);
    }

    [Fact]
    public async Task AddEpisodeBulk_DoesNotFinalBulkSaveAfterExtractionFailure()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new FailingBulkExtractionLlmClient(),
            maxCoroutines: 2);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            graphiti.AddEpisodeBulkAsync(
                new[]
                {
                    new RawEpisode
                    {
                        Name = "first",
                        Content = "Alpha event",
                        SourceDescription = "message",
                        ReferenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                    },
                    new RawEpisode
                    {
                        Name = "second",
                        Content = "fail event",
                        SourceDescription = "message",
                        ReferenceTime = new DateTime(2026, 1, 1, 12, 1, 0, DateTimeKind.Utc)
                    }
                },
                groupId: "group"));

        Assert.Equal("extraction failed", exception.Message);
        Assert.Equal(2, (await EpisodicNode.GetByGroupIdsAsync(driver, new[] { "group" })).Count);
        Assert.Empty(await EntityNode.GetByGroupIdsAsync(driver, new[] { "group" }));
        Assert.Empty(await EntityEdge.GetByGroupIdsAsync(driver, new[] { "group" }));
        Assert.Empty(await EpisodicEdge.GetByGroupIdsAsync(driver, new[] { "group" }));
    }

    [Fact]
    public async Task AddEpisode_PassesTypeMetadataAndCustomInstructionsToExtractionPrompt()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
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
                        ["source"] = "Alice",
                        ["target"] = "Acme",
                        ["relation_type"] = "WORKS_AT",
                        ["fact"] = "Alice works at Acme."
                    }
                }
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new("Person", "A human being"),
                ["Organization"] = new("Organization", "A company"),
                ["Location"] = new("Location", "A place")
            },
            excludedEntityTypes: new[] { "Location" },
            edgeTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["WORKS_AT"] = new("WORKS_AT", "Employment relationship")
            },
            edgeTypeMap: new Dictionary<(string SourceType, string TargetType), IReadOnlyList<string>>
            {
                [("Person", "Organization")] = new[] { "WORKS_AT" }
            },
            customExtractionInstructions: "Only extract durable workplace facts.");

        var nodeExtractionCall = Assert.Single(llm.Calls, call => call.PromptName == "extract_nodes.extract_message");
        var edgeExtractionCall = Assert.Single(llm.Calls, call => call.PromptName == "extract_edges.edge");
        var payload = Assert.IsType<JsonObject>(JsonNode.Parse(nodeExtractionCall.Messages[1].Content));
        Assert.Equal("Alice works at Acme.", payload["episode_content"]?.GetValue<string>());
        Assert.Equal("Only extract durable workplace facts.", payload["custom_extraction_instructions"]?.GetValue<string>());
        Assert.Equal("EpisodeNodeExtractionResponse", nodeExtractionCall.ResponseModel?.Name);

        var entityTypes = Assert.IsType<JsonArray>(payload["entity_types"]);
        Assert.Contains(entityTypes.OfType<JsonObject>(), item => item["name"]?.GetValue<string>() == "Person");
        var defaultEntityType = Assert.Single(
            entityTypes.OfType<JsonObject>(),
            item => item["entity_type_id"]?.GetValue<int>() == 0
                    && item["entity_type_name"]?.GetValue<string>() == "Entity");
        var defaultEntityTypeDescription = defaultEntityType["entity_type_description"]!.GetValue<string>();
        Assert.Contains("specific, identifiable entity", defaultEntityTypeDescription, StringComparison.Ordinal);
        Assert.Contains("When in doubt, do not extract the entity.", defaultEntityTypeDescription, StringComparison.Ordinal);
        Assert.Equal(defaultEntityTypeDescription, defaultEntityType["description"]?.GetValue<string>());
        Assert.Contains(
            entityTypes.OfType<JsonObject>(),
            item => item["entity_type_id"]?.GetValue<int>() == 1
                    && item["entity_type_name"]?.GetValue<string>() == "Person");
        var excludedTypes = Assert.IsType<JsonArray>(payload["excluded_entity_types"]);
        Assert.Contains(excludedTypes, item => item?.GetValue<string>() == "Location");

        var edgePayload = Assert.IsType<JsonObject>(JsonNode.Parse(edgeExtractionCall.Messages[1].Content));
        Assert.Equal("EpisodeEdgeExtractionResponse", edgeExtractionCall.ResponseModel?.Name);
        Assert.Contains(
            Assert.IsType<JsonArray>(edgePayload["nodes"]).OfType<JsonObject>(),
            item => item["name"]?.GetValue<string>() == "Alice");
        var edgeType = Assert.Single(Assert.IsType<JsonArray>(edgePayload["edge_types"]).OfType<JsonObject>());
        Assert.Equal("WORKS_AT", edgeType["name"]?.GetValue<string>());
        Assert.Equal("WORKS_AT", edgeType["fact_type_name"]?.GetValue<string>());
        var signature = Assert.Single(Assert.IsType<JsonArray>(edgeType["signatures"]).OfType<JsonObject>());
        Assert.Equal("Person", signature["source"]?.GetValue<string>());
        Assert.Equal("Organization", signature["target"]?.GetValue<string>());
        var factSignature = Assert.Single(Assert.IsType<JsonArray>(edgeType["fact_type_signatures"]).OfType<JsonObject>());
        Assert.Equal("Person", factSignature["source"]?.GetValue<string>());
        Assert.Equal("Organization", factSignature["target"]?.GetValue<string>());
    }

    [Fact]
    public async Task AddEpisode_FiltersExcludedExtractedEntityTypes()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Paris", ["entity_type"] = "Location" }
                }
            },
            ["extract_edges.edge"] = new()
            {
                ["edges"] = new JsonArray()
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice visited Paris.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new("Person"),
                ["Location"] = new("Location")
            },
            excludedEntityTypes: new[] { "Location" });

        var node = Assert.Single(result.Nodes);
        Assert.Equal("Alice", node.Name);
        Assert.Equal(new[] { "Entity", "Person" }, node.Labels);
        Assert.Empty(result.Edges);
    }

    [Theory]
    [InlineData(EpisodeType.Message, "extract_nodes.extract_message")]
    [InlineData(EpisodeType.Text, "extract_nodes.extract_text")]
    [InlineData(EpisodeType.Json, "extract_nodes.extract_json")]
    [InlineData(EpisodeType.FactTriple, "extract_nodes.extract_text")]
    public async Task AddEpisode_UsesSourceSpecificNodePromptThenSeparateEdgePrompt(
        EpisodeType source,
        string expectedNodePrompt)
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            [expectedNodePrompt] = new()
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Acme", ["entity_type"] = "Organization" }
                }
            },
            ["extract_edges.edge"] = new()
            {
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Acme",
                        ["relation_type"] = "WORKS_AT",
                        ["fact"] = "Alice works at Acme."
                    }
                }
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme.",
            source.ToWireValue(),
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            source,
            groupId: "group");

        var nodePromptIndex = llm.PromptNames.ToList().IndexOf(expectedNodePrompt);
        var edgePromptIndex = llm.PromptNames.ToList().IndexOf("extract_edges.edge");

        Assert.True(nodePromptIndex >= 0);
        Assert.True(edgePromptIndex > nodePromptIndex);
        Assert.DoesNotContain("extract_nodes_and_edges.extract_message", llm.PromptNames);
        Assert.Equal(new[] { "Alice", "Acme" }, result.Nodes.Select(node => node.Name));
        Assert.Equal("Alice works at Acme.", Assert.Single(result.Edges).Fact);
    }

    [Fact]
    public async Task AddEpisode_UsesStructuredGraphExtractionResponseAndKeepsHeuristicFallback()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new EmptyGraphExtractionLlmClient();
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice met Bob.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        Assert.Equal("EpisodeNodeExtractionResponse", llm.ExtractionResponseModel?.Name);
        Assert.Contains(result.Nodes, node => node.Name == "Alice");
        Assert.Contains(result.Nodes, node => node.Name == "Bob");
        Assert.Single(result.Edges);
    }

    [Fact]
    public async Task AddEpisode_RejectsUnknownExcludedEntityTypes()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new JsonObject());
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new("Person")
            },
            excludedEntityTypes: new[] { "Location" }));

        Assert.Contains("Location", exception.Message, StringComparison.Ordinal);
        Assert.Empty(llm.PromptNames);
    }

    [Fact]
    public async Task AddEpisodeBulk_RejectsUnknownExcludedEntityTypes()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new JsonObject());
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => graphiti.AddEpisodeBulkAsync(
            new[]
            {
                new RawEpisode
                {
                    Name = "conversation",
                    Content = "Alice works at Acme.",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                }
            },
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new("Person")
            },
            excludedEntityTypes: new[] { "Location" }));

        Assert.Contains("Location", exception.Message, StringComparison.Ordinal);
        Assert.Empty(llm.PromptNames);
    }

    [Fact]
    public async Task AddEpisode_RejectsEntityAttributesThatUseProtectedNames()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new JsonObject());
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var exception = await Assert.ThrowsAsync<EntityTypeValidationException>(() => graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new(
                    "Person",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["name"] = new("Protected field")
                    })
            }));

        Assert.Contains("name", exception.Message, StringComparison.Ordinal);
        Assert.Empty(llm.PromptNames);
    }

    [Fact]
    public async Task AddEpisodeBulk_RejectsEntityAttributesThatUseProtectedNames()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new JsonObject());
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var exception = await Assert.ThrowsAsync<EntityTypeValidationException>(() => graphiti.AddEpisodeBulkAsync(
            new[]
            {
                new RawEpisode
                {
                    Name = "conversation",
                    Content = "Alice works at Acme.",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                }
            },
            groupId: "group",
            entityTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new(
                    "Person",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["summary"] = new("Protected field")
                    })
            }));

        Assert.Contains("summary", exception.Message, StringComparison.Ordinal);
        Assert.Empty(llm.PromptNames);
    }

    [Fact]
    public async Task AddEpisode_HydratesDeclaredEdgeAttributes()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
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
                        ["source"] = "Alice",
                        ["target"] = "Acme",
                        ["relation_type"] = "WORKS_AT",
                        ["fact"] = "Alice works at Acme."
                    }
                }
            },
            ["extract_edges.extract_attributes"] = new()
            {
                ["role"] = "engineer",
                ["confidence"] = 0.87
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            edgeTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["WORKS_AT"] = new(
                    "WORKS_AT",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["role"] = new("Role at the organization"),
                        ["confidence"] = new("Extraction confidence", "number")
                    })
            },
            edgeTypeMap: new Dictionary<(string SourceType, string TargetType), IReadOnlyList<string>>
            {
                [("Person", "Organization")] = new[] { "WORKS_AT" }
            });

        var edge = Assert.Single(result.Edges);
        var storedEdge = await EntityEdge.GetByUuidAsync(driver, edge.Uuid);
        Assert.Equal("engineer", storedEdge.Attributes["role"]);
        Assert.Equal(0.87, storedEdge.Attributes["confidence"]);
        var call = Assert.Single(llm.Calls, call => call.PromptName == "extract_edges.extract_attributes");
        Assert.True(call.AttributeExtraction);
        Assert.Equal("EdgeAttributeResponse", call.ResponseSchema?.Name);
        var schema = JsonNode.Parse(call.ResponseSchema!.SchemaElement.GetRawText())!.AsObject();
        var schemaAttributes = schema["properties"]!["attributes"]!["properties"]!.AsObject();
        Assert.Equal(new[] { "confidence", "role" }, schemaAttributes.Select(pair => pair.Key));
        var context = JsonNode.Parse(call.Messages[^1].Content)!.AsObject();
        var attributes = context["edge_type"]!["attributes"]!.AsObject();
        Assert.Equal(new[] { "confidence", "role" }, attributes.Select(pair => pair.Key));
    }

    [Fact]
    public async Task AddEpisode_AttributeHydrationDropsOverlongEdgeStringsAndReplacesOmittedFields()
    {
        var driver = new InMemoryGraphDriver();
        var alice = new EntityNode { Name = "Alice", GroupId = "group" };
        var acme = new EntityNode { Name = "Acme", GroupId = "group" };
        await alice.SaveAsync(driver);
        await acme.SaveAsync(driver);
        var existingEdge = new EntityEdge
        {
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = acme.Uuid,
            GroupId = "group",
            Name = "WORKS_AT",
            Fact = "Alice works at Acme.",
            Episodes = new List<string> { "previous-episode" },
            Attributes = new Dictionary<string, object?>
            {
                ["role"] = "existing role",
                ["confidence"] = 0.41,
                ["stale"] = "remove me"
            }
        };
        await existingEdge.SaveAsync(driver);
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
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
                        ["source"] = "Alice",
                        ["target"] = "Acme",
                        ["relation_type"] = "WORKS_AT",
                        ["fact"] = "Alice works at Acme."
                    }
                }
            },
            ["extract_edges.extract_attributes"] = new()
            {
                ["role"] = new string('x', 251),
                ["confidence"] = 0.87
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            edgeTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["WORKS_AT"] = new(
                    "WORKS_AT",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["role"] = new("Role at the organization"),
                        ["confidence"] = new("Extraction confidence", "number")
                    })
            });

        var storedEdge = await EntityEdge.GetByUuidAsync(driver, existingEdge.Uuid);
        Assert.Equal("existing role", storedEdge.Attributes["role"]);
        Assert.Equal(0.87, storedEdge.Attributes["confidence"]);
        Assert.False(storedEdge.Attributes.ContainsKey("stale"));
    }

    [Fact]
    public async Task AddEpisode_ReusesEdgeAttributeSchemaForSameTypeBatch()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Acme", ["entity_type"] = "Organization" },
                    new JsonObject { ["name"] = "Contoso", ["entity_type"] = "Organization" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Acme",
                        ["relation_type"] = "WORKS_AT",
                        ["fact"] = "Alice works at Acme.",
                        ["valid_at"] = "2026-01-01T00:00:00Z"
                    },
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Contoso",
                        ["relation_type"] = "WORKS_AT",
                        ["fact"] = "Alice advises Contoso.",
                        ["valid_at"] = "2026-01-01T00:00:00Z"
                    }
                }
            },
            ["extract_edges.extract_attributes"] = new()
            {
                ["attributes"] = new JsonObject
                {
                    ["confidence"] = 0.75
                }
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme and advises Contoso.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            edgeTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["WORKS_AT"] = new(
                    "WORKS_AT",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["confidence"] = new("Extraction confidence", "number")
                    })
            },
            edgeTypeMap: new Dictionary<(string SourceType, string TargetType), IReadOnlyList<string>>
            {
                [("Person", "Organization")] = new[] { "WORKS_AT" }
            });

        var attributeCalls = llm.Calls
            .Where(call => call.PromptName == "extract_edges.extract_attributes")
            .ToList();
        Assert.Equal(2, attributeCalls.Count);
        Assert.Same(attributeCalls[0].ResponseSchema, attributeCalls[1].ResponseSchema);
    }

    [Fact]
    public async Task AddEpisode_EdgeAttributeExtractionUsesBoundedConcurrency()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new ConcurrencyTrackingLlmClient(
            new Dictionary<string, JsonObject>
            {
                ["extract_nodes.extract_message"] = new()
                {
                    ["extracted_entities"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                        new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" },
                        new JsonObject { ["name"] = "Carol", ["entity_type"] = "Person" },
                        new JsonObject { ["name"] = "Dave", ["entity_type"] = "Person" }
                    },
                    ["edges"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["source"] = "Alice",
                            ["target"] = "Bob",
                            ["relation_type"] = "KNOWS",
                            ["fact"] = "Alice knows Bob.",
                            ["valid_at"] = "2026-01-01T00:00:00Z"
                        },
                        new JsonObject
                        {
                            ["source"] = "Alice",
                            ["target"] = "Carol",
                            ["relation_type"] = "KNOWS",
                            ["fact"] = "Alice knows Carol.",
                            ["valid_at"] = "2026-01-01T00:00:00Z"
                        },
                        new JsonObject
                        {
                            ["source"] = "Alice",
                            ["target"] = "Dave",
                            ["relation_type"] = "KNOWS",
                            ["fact"] = "Alice knows Dave.",
                            ["valid_at"] = "2026-01-01T00:00:00Z"
                        }
                    }
                },
                ["extract_edges.extract_attributes"] = new()
                {
                    ["confidence"] = "high"
                }
            },
            trackedPromptName: "extract_edges.extract_attributes",
            delay: TimeSpan.FromMilliseconds(75));
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm, maxCoroutines: 2);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice knows Bob, Carol, and Dave.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            edgeTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["KNOWS"] = new(
                    "KNOWS",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["confidence"] = new("Extraction confidence")
                    })
            });

        Assert.Equal(3, result.Edges.Count);
        Assert.Equal(3, llm.TrackedPromptCalls);
        Assert.Equal(2, llm.MaxObservedConcurrency);
        Assert.All(result.Edges, edge => Assert.Equal("high", edge.Attributes["confidence"]));
    }

    [Fact]
    public async Task AddEpisode_SkipsEdgeAttributePromptWhenTypeMapDoesNotMatchEndpoints()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
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
                        ["source"] = "Alice",
                        ["target"] = "Acme",
                        ["relation_type"] = "WORKS_AT",
                        ["fact"] = "Alice works at Acme."
                    }
                }
            },
            ["extract_edges.extract_attributes"] = new()
            {
                ["role"] = "engineer"
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group",
            edgeTypes: new Dictionary<string, EntityTypeDefinition>
            {
                ["WORKS_AT"] = new(
                    "WORKS_AT",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["role"] = new("Role at the organization")
                    })
            },
            edgeTypeMap: new Dictionary<(string SourceType, string TargetType), IReadOnlyList<string>>
            {
                [("Organization", "Person")] = new[] { "WORKS_AT" }
            });

        Assert.DoesNotContain("extract_edges.extract_attributes", llm.PromptNames);
        Assert.Empty(Assert.Single(result.Edges).Attributes);
    }

    [Fact]
    public async Task AddEpisode_ExactNormalizedDedupResolvesExistingAndPreservesEdgeAlias()
    {
        var driver = new InMemoryGraphDriver();
        var existing = new EntityNode
        {
            Name = "Alice Smith",
            GroupId = "group",
            Labels = new List<string> { "Entity" }
        };
        await existing.SaveAsync(driver);
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new JsonObject
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = " alice   smith ", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = " alice   smith ",
                        ["target"] = "Bob",
                        ["relation_type"] = "KNOWS",
                        ["fact"] = "Alice Smith knows Bob."
                    }
                }
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice Smith knows Bob.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        Assert.Contains(result.Nodes, node => node.Uuid == existing.Uuid);
        var edge = Assert.Single(result.Edges);
        Assert.Equal(existing.Uuid, edge.SourceNodeUuid);
        var storedExisting = await EntityNode.GetByUuidAsync(driver, existing.Uuid);
        Assert.Contains("Person", storedExisting.Labels);
    }

    [Fact]
    public async Task AddEpisode_LowEntropyShortNameDoesNotFuzzyMerge()
    {
        var driver = new InMemoryGraphDriver();
        var existing = new EntityNode { Name = "AI", GroupId = "group" };
        await existing.SaveAsync(driver);
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new JsonObject
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "A I", ["entity_type"] = "Organization" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "A I",
                        ["target"] = "Bob",
                        ["relation_type"] = "MENTIONS",
                        ["fact"] = "A I mentioned Bob."
                    }
                }
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "A I mentioned Bob.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        Assert.DoesNotContain(result.Nodes, node => node.Uuid == existing.Uuid);
        var edge = Assert.Single(result.Edges);
        Assert.NotEqual(existing.Uuid, edge.SourceNodeUuid);
    }

    [Fact]
    public async Task AddEpisode_AmbiguousExactExistingNameDoesNotChooseArbitraryFirstMatch()
    {
        var driver = new InMemoryGraphDriver();
        var first = new EntityNode { Name = "Acme Corp", GroupId = "group" };
        var second = new EntityNode { Name = " acme   corp ", GroupId = "group" };
        await first.SaveAsync(driver);
        await second.SaveAsync(driver);
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new JsonObject
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "ACME corp", ["entity_type"] = "Organization" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "ACME corp",
                        ["target"] = "Bob",
                        ["relation_type"] = "MENTIONS",
                        ["fact"] = "ACME corp mentioned Bob."
                    }
                }
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "ACME corp mentioned Bob.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        var edge = Assert.Single(result.Edges);
        Assert.NotEqual(first.Uuid, edge.SourceNodeUuid);
        Assert.NotEqual(second.Uuid, edge.SourceNodeUuid);
        Assert.Contains(result.Nodes, node => node.Name == "ACME corp");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddEpisode_LlmNodeDedupeResolvesAmbiguousExactCandidate(bool stringifyResolutionIds)
    {
        var driver = new InMemoryGraphDriver();
        var first = new EntityNode { Name = "Acme Corp", GroupId = "group", Summary = "A vendor." };
        var second = new EntityNode { Name = " acme   corp ", GroupId = "group", Summary = "The ACME corp in this conversation." };
        await first.SaveAsync(driver);
        await second.SaveAsync(driver);
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "ACME corp", ["entity_type"] = "Organization" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "ACME corp",
                        ["target"] = "Bob",
                        ["relation_type"] = "MENTIONS",
                        ["fact"] = "ACME corp mentioned Bob."
                    }
                }
            },
            ["dedupe_nodes.nodes"] = new()
            {
                ["entity_resolutions"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = stringifyResolutionIds ? "0" : 0,
                        ["name"] = "ACME corp",
                        ["duplicate_candidate_id"] = stringifyResolutionIds ? "1" : 1
                    }
                }
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "ACME corp mentioned Bob.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        var nodeDedupeCall = Assert.Single(llm.Calls, call => call.PromptName == "dedupe_nodes.nodes");
        Assert.Equal("NodeResolutionsResponse", nodeDedupeCall.ResponseModel?.Name);
        Assert.Contains("candidate_id", nodeDedupeCall.Messages[^1].Content, StringComparison.Ordinal);
        Assert.Contains("\"entity_type_description\":\"Default Entity Type\"", nodeDedupeCall.Messages[^1].Content, StringComparison.Ordinal);
        Assert.Contains(result.Nodes, node => node.Uuid == second.Uuid);
        Assert.DoesNotContain(result.Nodes, node => node.Uuid == first.Uuid);
        var edge = Assert.Single(result.Edges);
        Assert.Equal(second.Uuid, edge.SourceNodeUuid);
    }

    [Fact]
    public async Task AddEpisodeBulk_UsesDeterministicDedupAcrossEpisodesAndExistingNodes()
    {
        var driver = new InMemoryGraphDriver();
        var existing = new EntityNode { Name = "OpenAI", GroupId = "group" };
        await existing.SaveAsync(driver);
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new JsonObject
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Open AI", ["entity_type"] = "Organization" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Open AI",
                        ["target"] = "Bob",
                        ["relation_type"] = "HIRED",
                        ["fact"] = "Open AI hired Bob."
                    }
                }
            }));

        var result = await graphiti.AddEpisodeBulkAsync(
            new[]
            {
                new RawEpisode
                {
                    Name = "first",
                    Content = "Open AI hired Bob.",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                },
                new RawEpisode
                {
                    Name = "second",
                    Content = "Open AI hired Bob again.",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 1, 1, 12, 5, 0, DateTimeKind.Utc)
                }
            },
            groupId: "group");

        Assert.Equal(2, result.Nodes.Count);
        Assert.Contains(result.Nodes, node => node.Uuid == existing.Uuid);
        var edge = Assert.Single(result.Edges);
        Assert.Equal(existing.Uuid, edge.SourceNodeUuid);
        Assert.Equal(2, edge.Episodes.Count);
        Assert.All(result.Episodes, episode => Assert.Contains(episode.Uuid, edge.Episodes));
    }

    [Fact]
    public async Task AddEpisodeBulk_CollapsesDuplicateFactsToSingleReturnedEdgeWithBothEpisodes()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new JsonObject
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Bob",
                        ["relation_type"] = "LIKES",
                        ["fact"] = "Alice likes Bob."
                    }
                }
            }));

        var result = await graphiti.AddEpisodeBulkAsync(
            new[]
            {
                new RawEpisode
                {
                    Name = "first",
                    Content = "Alice likes Bob.",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
                },
                new RawEpisode
                {
                    Name = "second",
                    Content = "Alice still likes Bob.",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 1, 1, 12, 5, 0, DateTimeKind.Utc)
                }
            },
            groupId: "group");

        var edge = Assert.Single(result.Edges);
        Assert.Equal(2, edge.Episodes.Count);
        Assert.All(result.Episodes, episode => Assert.Contains(episode.Uuid, edge.Episodes));
        Assert.All(result.Episodes, episode => Assert.Equal(new[] { edge.Uuid }, episode.EntityEdges));

        var storedEdges = await EntityEdge.GetByGroupIdsAsync(driver, new[] { "group" });
        var storedEdge = Assert.Single(storedEdges);
        Assert.Equal(edge.Uuid, storedEdge.Uuid);
        Assert.Equal(2, storedEdge.Episodes.Count);
    }

    [Fact]
    public async Task AddEpisodeBulk_DoesNotReinvalidateAlreadyInvalidatedSnapshotEdge()
    {
        var driver = new InMemoryGraphDriver();
        var fixedNow = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var alice = new EntityNode { Name = "Alice", GroupId = "group" };
        var acme = new EntityNode { Name = "Acme", GroupId = "group", Labels = new List<string> { "Entity", "Organization" } };
        await alice.SaveAsync(driver);
        await acme.SaveAsync(driver);
        var oldEdge = new EntityEdge
        {
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = acme.Uuid,
            GroupId = "group",
            Name = "WORKS_AT",
            Fact = "Alice works at Acme.",
            ValidAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        await oldEdge.SaveAsync(driver);
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new BulkReinvalidationLlmClient(),
            timeProvider: new FixedTimeProvider(fixedNow));

        var result = await graphiti.AddEpisodeBulkAsync(
            new[]
            {
                new RawEpisode
                {
                    Name = "first",
                    Content = "Alice left Acme.",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc)
                },
                new RawEpisode
                {
                    Name = "second",
                    Content = "Alice rejoined Acme.",
                    SourceDescription = "message",
                    ReferenceTime = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc)
                }
            },
            groupId: "group");

        var invalidated = Assert.Single(result.Edges, edge => edge.Uuid == oldEdge.Uuid);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), invalidated.InvalidAt);
        Assert.Equal(fixedNow.UtcDateTime, invalidated.ExpiredAt);

        var first = Assert.Single(result.Episodes, episode => episode.Name == "first");
        var second = Assert.Single(result.Episodes, episode => episode.Name == "second");
        Assert.Contains(oldEdge.Uuid, first.EntityEdges);
        Assert.DoesNotContain(oldEdge.Uuid, second.EntityEdges);
    }

    [Fact]
    public async Task AddEpisode_ReusesExistingExactDuplicateEdgeAndAppendsEpisode()
    {
        var driver = new InMemoryGraphDriver();
        var alice = new EntityNode { Name = "Alice", GroupId = "group" };
        var bob = new EntityNode { Name = "Bob", GroupId = "group" };
        await alice.SaveAsync(driver);
        await bob.SaveAsync(driver);
        var existingEdge = new EntityEdge
        {
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "group",
            Name = "LIKES",
            Fact = "Alice likes Bob.",
            Episodes = new List<string> { "previous-episode" }
        };
        await existingEdge.SaveAsync(driver);

        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new JsonObject
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Bob",
                        ["relation_type"] = "LIKES",
                        ["fact"] = " alice   likes bob. "
                    }
                }
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice likes Bob.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        var edge = Assert.Single(result.Edges);
        Assert.Equal(existingEdge.Uuid, edge.Uuid);
        Assert.Contains("previous-episode", edge.Episodes);
        Assert.Contains(result.Episode.Uuid, edge.Episodes);
        var storedEdges = await EntityEdge.GetByGroupIdsAsync(driver, new[] { "group" });
        Assert.Single(storedEdges);
    }

    [Fact]
    public async Task AddEpisode_LlmDuplicateResolutionReusesRelatedEdge()
    {
        var driver = new InMemoryGraphDriver();
        var alice = new EntityNode { Name = "Alice", GroupId = "group" };
        var acme = new EntityNode { Name = "Acme", GroupId = "group" };
        await alice.SaveAsync(driver);
        await acme.SaveAsync(driver);
        var existingEdge = new EntityEdge
        {
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = acme.Uuid,
            GroupId = "group",
            Name = "WORKS_AT",
            Fact = "Alice is employed by Acme.",
            Episodes = new List<string> { "previous-episode" }
        };
        await existingEdge.SaveAsync(driver);

        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new Dictionary<string, JsonObject>
            {
                ["extract_nodes.extract_message"] = new()
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
                            ["source"] = "Alice",
                            ["target"] = "Acme",
                            ["relation_type"] = "WORKS_AT",
                            ["fact"] = "Alice works for Acme."
                        }
                    }
                },
                ["dedupe_edges.resolve_edge"] = new()
                {
                    ["duplicate_facts"] = new JsonArray { -1, 99, 0 },
                    ["contradicted_facts"] = new JsonArray()
                }
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works for Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        var edge = Assert.Single(result.Edges);
        Assert.Equal(existingEdge.Uuid, edge.Uuid);
        Assert.Contains(result.Episode.Uuid, edge.Episodes);
        Assert.Equal("Alice is employed by Acme.", edge.Fact);
    }

    [Fact]
    public async Task AddEpisode_UsesBoundedInvalidationCandidateRetrieval()
    {
        var driver = new InMemoryGraphDriver();
        var alice = new EntityNode { Name = "Alice", GroupId = "group" };
        await alice.SaveAsync(driver);
        for (var i = 0; i < 25; i++)
        {
            var company = new EntityNode { Name = $"Company {i}", GroupId = "group" };
            await company.SaveAsync(driver);
            await new EntityEdge
            {
                SourceNodeUuid = alice.Uuid,
                TargetNodeUuid = company.Uuid,
                GroupId = "group",
                Name = "WORKS_AT",
                Fact = $"Alice worked at Acme division {i}.",
                ValidAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }.SaveAsync(driver);
        }

        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
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
                        ["source"] = "Alice",
                        ["target"] = "Acme",
                        ["relation_type"] = "WORKS_AT",
                        ["fact"] = "Alice now works at Acme.",
                        ["valid_at"] = "2026-01-01T00:00:00Z"
                    }
                }
            },
            ["dedupe_edges.resolve_edge"] = new()
            {
                ["duplicate_facts"] = new JsonArray(),
                ["contradicted_facts"] = new JsonArray()
            }
        });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice now works at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        var resolveCall = Assert.Single(llm.Calls, call => call.PromptName == "dedupe_edges.resolve_edge");
        Assert.Equal("EdgeResolutionResponse", resolveCall.ResponseModel?.Name);
        var context = JsonNode.Parse(resolveCall.Messages[^1].Content)!.AsObject();
        Assert.Empty(context["existing_edges"]!.AsArray());
        var candidates = context["edge_invalidation_candidates"]!.AsArray();
        Assert.Equal(SearchUtilities.RelevantSchemaLimit, candidates.Count);
        Assert.True(candidates.Count < 25);
    }

    [Fact]
    public async Task AddEpisode_LlmContradictionInvalidatesOlderEdge()
    {
        var driver = new InMemoryGraphDriver();
        var fixedNow = new DateTimeOffset(2026, 3, 2, 4, 5, 6, TimeSpan.Zero);
        var alice = new EntityNode { Name = "Alice", GroupId = "group" };
        var acme = new EntityNode { Name = "Acme", GroupId = "group" };
        await alice.SaveAsync(driver);
        await acme.SaveAsync(driver);
        var oldEdge = new EntityEdge
        {
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = acme.Uuid,
            GroupId = "group",
            Name = "WORKS_AT",
            Fact = "Alice works at Acme.",
            ValidAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        await oldEdge.SaveAsync(driver);

        var graphiti = new Graphiti(
            graphDriver: driver,
            timeProvider: new FixedTimeProvider(fixedNow),
            llmClient: new StaticLlmClient(new Dictionary<string, JsonObject>
            {
                ["extract_nodes.extract_message"] = new()
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
                            ["source"] = "Alice",
                            ["target"] = "Acme",
                            ["relation_type"] = "LEFT",
                            ["fact"] = "Alice left Acme.",
                            ["valid_at"] = "2026-02-01T00:00:00Z"
                        }
                    }
                },
                ["dedupe_edges.resolve_edge"] = new()
                {
                    ["duplicate_facts"] = new JsonArray(),
                    ["contradicted_facts"] = new JsonArray { 0 }
                }
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice left Acme.",
            "message",
            new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        Assert.Equal(2, result.Edges.Count);
        var invalidated = Assert.Single(result.Edges, edge => edge.Uuid == oldEdge.Uuid);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), invalidated.InvalidAt);
        Assert.Equal(fixedNow.UtcDateTime, invalidated.ExpiredAt);
        Assert.Contains(result.Edges, edge => edge.Uuid != oldEdge.Uuid && edge.ValidAt == invalidated.InvalidAt);
    }

    [Fact]
    public async Task AddEpisode_LlmContradictionExpiresNewEdgeWhenLaterFactExists()
    {
        var driver = new InMemoryGraphDriver();
        var fixedNow = new DateTimeOffset(2026, 3, 2, 4, 5, 6, TimeSpan.Zero);
        var alice = new EntityNode { Name = "Alice", GroupId = "group" };
        var acme = new EntityNode { Name = "Acme", GroupId = "group" };
        await alice.SaveAsync(driver);
        await acme.SaveAsync(driver);
        var laterEdge = new EntityEdge
        {
            Uuid = "later-edge-1",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = acme.Uuid,
            GroupId = "group",
            Name = "LEFT",
            Fact = "Alice left Acme.",
            ValidAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        await laterEdge.SaveAsync(driver);
        var earliestLaterEdge = new EntityEdge
        {
            Uuid = "later-edge-2",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = acme.Uuid,
            GroupId = "group",
            Name = "LEFT",
            Fact = "Alice had already left Acme.",
            ValidAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        await earliestLaterEdge.SaveAsync(driver);

        var graphiti = new Graphiti(
            graphDriver: driver,
            timeProvider: new FixedTimeProvider(fixedNow),
            llmClient: new StaticLlmClient(new Dictionary<string, JsonObject>
            {
                ["extract_nodes.extract_message"] = new()
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
                            ["source"] = "Alice",
                            ["target"] = "Acme",
                            ["relation_type"] = "WORKS_AT",
                            ["fact"] = "Alice worked at Acme.",
                            ["valid_at"] = "2026-01-01T00:00:00Z"
                        }
                    }
                },
                ["dedupe_edges.resolve_edge"] = new()
                {
                    ["duplicate_facts"] = new JsonArray(),
                    ["contradicted_facts"] = new JsonArray { 0, 1 }
                }
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice worked at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        var newEdge = Assert.Single(result.Edges, edge => edge.Fact == "Alice worked at Acme.");
        Assert.Equal(earliestLaterEdge.ValidAt, newEdge.InvalidAt);
        Assert.Equal(fixedNow.UtcDateTime, newEdge.ExpiredAt);
    }

    [Fact]
    public async Task AddEpisode_ExtractsMissingEdgeTimestampsWithLlm()
    {
        var driver = new InMemoryGraphDriver();
        var fixedNow = new DateTimeOffset(2026, 3, 2, 4, 5, 6, TimeSpan.Zero);
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
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
                        ["source"] = "Alice",
                        ["target"] = "Acme",
                        ["relation_type"] = "WORKS_AT",
                        ["fact"] = "Alice worked at Acme until February."
                    }
                }
            },
            ["extract_edges.extract_timestamps"] = new()
            {
                ["valid_at"] = "2026-01-01T00:00:00Z",
                ["invalid_at"] = "2026-02-01T00:00:00Z"
            }
        });
        var graphiti = new Graphiti(
            graphDriver: driver,
            timeProvider: new FixedTimeProvider(fixedNow),
            llmClient: llm);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice worked at Acme until February.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        var edge = Assert.Single(result.Edges);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), edge.ValidAt);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), edge.InvalidAt);
        Assert.Equal(fixedNow.UtcDateTime, edge.ExpiredAt);
        var timestampCall = Assert.Single(llm.Calls, call => call.PromptName == "extract_edges.extract_timestamps");
        Assert.Equal("EdgeTimestampResponse", timestampCall.ResponseModel?.Name);
    }

    [Fact]
    public async Task AddEpisode_IgnoresMalformedExtractedEdgeTimestamps()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new JsonObject
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
                        ["source"] = "Alice",
                        ["target"] = "Acme",
                        ["relation_type"] = "WORKS_AT",
                        ["fact"] = "Alice worked at Acme.",
                        ["valid_at"] = "early last winter",
                        ["invalid_at"] = "not a timestamp"
                    }
                }
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice worked at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        var edge = Assert.Single(result.Edges);
        Assert.Null(edge.ValidAt);
        Assert.Null(edge.InvalidAt);
        Assert.Null(edge.ExpiredAt);
    }

    [Fact]
    public async Task AddEpisode_IgnoresMalformedLlmEdgeTimestampFields()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes.extract_message"] = new()
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
                        ["source"] = "Alice",
                        ["target"] = "Acme",
                        ["relation_type"] = "WORKS_AT",
                        ["fact"] = "Alice started at Acme."
                    }
                }
            },
            ["extract_edges.extract_timestamps"] = new()
            {
                ["valid_at"] = "2026-01-01T00:00:00Z",
                ["invalid_at"] = "not a timestamp"
            }
        });
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: llm);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice started at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        var edge = Assert.Single(result.Edges);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), edge.ValidAt);
        Assert.Null(edge.InvalidAt);
        Assert.Null(edge.ExpiredAt);
        var timestampCall = Assert.Single(llm.Calls, call => call.PromptName == "extract_edges.extract_timestamps");
        Assert.Equal("EdgeTimestampResponse", timestampCall.ResponseModel?.Name);
    }

    [Fact]
    public async Task AddEpisode_SwallowsInvalidStructuredEdgeTimestampResponse()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new InvalidStructuredTimestampLlmClient();
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice started at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        var edge = Assert.Single(result.Edges);
        Assert.Null(edge.ValidAt);
        Assert.Null(edge.InvalidAt);
        Assert.Null(edge.ExpiredAt);
        Assert.Equal("EdgeTimestampResponse", llm.TimestampResponseModel?.Name);
    }

    [Fact]
    public async Task AddEpisode_DoesNotOverwriteProvidedEdgeTimestampWithLlm()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new Dictionary<string, JsonObject>
            {
                ["extract_nodes.extract_message"] = new()
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
                            ["source"] = "Alice",
                            ["target"] = "Acme",
                            ["relation_type"] = "WORKS_AT",
                            ["fact"] = "Alice started at Acme.",
                            ["valid_at"] = "2026-01-01T00:00:00Z"
                        }
                    }
                },
                ["extract_edges.extract_timestamps"] = new()
                {
                    ["valid_at"] = "2030-01-01T00:00:00Z"
                }
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice started at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        var edge = Assert.Single(result.Edges);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), edge.ValidAt);
    }

    [Fact]
    public async Task AddEpisode_DoesNotExtractTimestampsForReusedDuplicateEdge()
    {
        var driver = new InMemoryGraphDriver();
        var alice = new EntityNode { Name = "Alice", GroupId = "group" };
        var acme = new EntityNode { Name = "Acme", GroupId = "group" };
        await alice.SaveAsync(driver);
        await acme.SaveAsync(driver);
        var existingValidAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var existingEdge = new EntityEdge
        {
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = acme.Uuid,
            GroupId = "group",
            Name = "WORKS_AT",
            Fact = "Alice works at Acme.",
            ValidAt = existingValidAt
        };
        await existingEdge.SaveAsync(driver);

        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new Dictionary<string, JsonObject>
            {
                ["extract_nodes.extract_message"] = new()
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
                            ["source"] = "Alice",
                            ["target"] = "Acme",
                            ["relation_type"] = "WORKS_AT",
                            ["fact"] = " alice works at acme. "
                        }
                    }
                },
                ["extract_edges.extract_timestamps"] = new()
                {
                    ["valid_at"] = "2030-01-01T00:00:00Z"
                }
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice works at Acme.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        var edge = Assert.Single(result.Edges);
        Assert.Equal(existingEdge.Uuid, edge.Uuid);
        Assert.Equal(existingValidAt, edge.ValidAt);
    }

    [Fact]
    public async Task AddEpisode_CollapsesDuplicateExtractedEdgesWithinEpisode()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticLlmClient(new JsonObject
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Bob",
                        ["relation_type"] = "LIKES",
                        ["fact"] = "Alice likes Bob."
                    },
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Bob",
                        ["relation_type"] = "MENTIONS",
                        ["fact"] = " alice   likes bob. "
                    }
                }
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice likes Bob.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        var edge = Assert.Single(result.Edges);
        Assert.Equal("LIKES", edge.Name);
    }

    [Fact]
    public async Task AddEpisode_SetsExpiredAtWhenExtractedEdgeHasInvalidAt()
    {
        var driver = new InMemoryGraphDriver();
        var fixedNow = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var graphiti = new Graphiti(
            graphDriver: driver,
            timeProvider: new FixedTimeProvider(fixedNow),
            llmClient: new StaticLlmClient(new JsonObject
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                    new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source"] = "Alice",
                        ["target"] = "Bob",
                        ["relation_type"] = "LIKES",
                        ["fact"] = "Alice liked Bob until February.",
                        ["invalid_at"] = "2026-02-01T00:00:00Z"
                    }
                }
            }));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice liked Bob until February.",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        var edge = Assert.Single(result.Edges);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), edge.InvalidAt);
        Assert.Equal(fixedNow.UtcDateTime, edge.ExpiredAt);
    }

    [Fact]
    public async Task SagaAssociation_CreatesHasAndNextEpisodeEdges()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var firstTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var secondTime = firstTime.AddMinutes(5);

        var first = await graphiti.AddEpisodeAsync(
            "first",
            "Alice met Bob",
            "message",
            firstTime,
            groupId: "group",
            saga: "launch");
        var second = await graphiti.AddEpisodeAsync(
            "second",
            "Bob met Carol",
            "message",
            secondTime,
            groupId: "group",
            saga: "launch",
            sagaPreviousEpisodeUuid: first.Episode.Uuid);

        var hasEpisodeEdges = await HasEpisodeEdge.GetByGroupIdsAsync(driver, new[] { "group" });
        var nextEpisodeEdges = await NextEpisodeEdge.GetByGroupIdsAsync(driver, new[] { "group" });

        Assert.Equal(2, hasEpisodeEdges.Count);
        Assert.Single(nextEpisodeEdges);
        Assert.Equal(first.Episode.Uuid, nextEpisodeEdges[0].SourceNodeUuid);
        Assert.Equal(second.Episode.Uuid, nextEpisodeEdges[0].TargetNodeUuid);
    }

    [Fact]
    public async Task GetSagaEpisodeContents_UsesLatestEpisodesForInitialSummary()
    {
        var driver = new InMemoryGraphDriver();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var saga = new SagaNode { Name = "launch", GroupId = "group", CreatedAt = start };
        await saga.SaveAsync(driver);

        for (var i = 0; i <= 200; i++)
        {
            var timestamp = start.AddMinutes(i);
            var episode = new EpisodicNode
            {
                Name = $"episode-{i}",
                GroupId = "group",
                CreatedAt = timestamp,
                ValidAt = timestamp,
                Content = $"content-{i}"
            };
            await episode.SaveAsync(driver);
            await new HasEpisodeEdge
            {
                SourceNodeUuid = saga.Uuid,
                TargetNodeUuid = episode.Uuid,
                GroupId = "group",
                CreatedAt = timestamp
            }.SaveAsync(driver);
        }

        var contents = await driver.GetSagaEpisodeContentsAsync(saga.Uuid, since: null, limit: 200);

        Assert.Equal(200, contents.Count);
        Assert.Equal("content-1", contents[0].Content);
        Assert.Equal("content-200", contents[^1].Content);
        Assert.DoesNotContain(contents, content => content.Content == "content-0");
    }

    [Fact]
    public async Task GetSagaEpisodeContents_AppliesLimitBeforeDroppingEmptyContent()
    {
        var driver = new InMemoryGraphDriver();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var saga = new SagaNode { Name = "launch", GroupId = "group", CreatedAt = start };
        await saga.SaveAsync(driver);

        for (var i = 0; i <= 200; i++)
        {
            var timestamp = start.AddMinutes(i);
            var episode = new EpisodicNode
            {
                Name = $"episode-{i}",
                GroupId = "group",
                CreatedAt = timestamp,
                ValidAt = timestamp,
                Content = i == 200 ? string.Empty : $"content-{i}"
            };
            await episode.SaveAsync(driver);
            await new HasEpisodeEdge
            {
                SourceNodeUuid = saga.Uuid,
                TargetNodeUuid = episode.Uuid,
                GroupId = "group",
                CreatedAt = timestamp
            }.SaveAsync(driver);
        }

        var contents = await driver.GetSagaEpisodeContentsAsync(saga.Uuid, since: null, limit: 200);

        Assert.Equal(199, contents.Count);
        Assert.Equal("content-1", contents[0].Content);
        Assert.Equal("content-199", contents[^1].Content);
        Assert.DoesNotContain(contents, content => content.Content == "content-0");
        Assert.DoesNotContain(contents, content => content.Content == "content-200");
    }

    [Fact]
    public async Task SummarizeSaga_IncludesExistingSummaryAndSagaNameInPrompt()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticLlmClient(new JsonObject { ["summary"] = "merged launch summary" });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);
        var lastSummary = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var episodeTime = lastSummary.AddHours(1);
        var saga = new SagaNode
        {
            Name = "launch",
            GroupId = "group",
            CreatedAt = lastSummary,
            Summary = "Prior launch decision",
            LastSummarizedAt = lastSummary,
            LastSummarizedEpisodeValidAt = lastSummary
        };
        var episode = new EpisodicNode
        {
            Name = "update",
            GroupId = "group",
            CreatedAt = episodeTime,
            ValidAt = episodeTime,
            Content = "Launch moved to March 15."
        };
        var followUpEpisode = new EpisodicNode
        {
            Name = "owner",
            GroupId = "group",
            CreatedAt = episodeTime.AddMinutes(1),
            ValidAt = episodeTime.AddMinutes(1),
            Content = "Marketing owns the launch checklist."
        };
        await saga.SaveAsync(driver);
        await episode.SaveAsync(driver);
        await followUpEpisode.SaveAsync(driver);
        foreach (var item in new[] { episode, followUpEpisode })
        {
            await new HasEpisodeEdge
            {
                SourceNodeUuid = saga.Uuid,
                TargetNodeUuid = item.Uuid,
                GroupId = "group",
                CreatedAt = item.CreatedAt
            }.SaveAsync(driver);
        }

        var summarized = await graphiti.SummarizeSagaAsync(saga.Uuid);

        var call = Assert.Single(llm.Calls);
        var userMessage = Assert.Single(call.Messages, message => message.Role == "user");
        Assert.Equal("summarize_sagas.summarize_saga", call.PromptName);
        Assert.Equal("SagaSummaryResponse", call.ResponseModel?.Name);
        Assert.Contains("Prior launch decision", userMessage.Content, StringComparison.Ordinal);
        Assert.Contains("topic \"launch\"", userMessage.Content, StringComparison.Ordinal);
        Assert.Contains(
            "Launch moved to March 15.\n---\nMarketing owns the launch checklist.",
            userMessage.Content,
            StringComparison.Ordinal);
        Assert.Equal("merged launch summary", summarized.Summary);
        Assert.Equal(followUpEpisode.ValidAt, summarized.LastSummarizedEpisodeValidAt);
    }

    [Fact]
    public async Task SummarizeSaga_HardTruncatesLongSummaryLikePython()
    {
        var driver = new InMemoryGraphDriver();
        var longSummary = "Complete sentence. " + new string('x', TextUtilities.MaxSummaryChars + 50);
        var llm = new StaticLlmClient(new JsonObject { ["summary"] = longSummary });
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);
        var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var saga = new SagaNode
        {
            Name = "launch",
            GroupId = "group",
            CreatedAt = timestamp
        };
        var episode = new EpisodicNode
        {
            Name = "update",
            GroupId = "group",
            CreatedAt = timestamp,
            ValidAt = timestamp,
            Content = "Launch moved to March 15."
        };
        await saga.SaveAsync(driver);
        await episode.SaveAsync(driver);
        await new HasEpisodeEdge
        {
            SourceNodeUuid = saga.Uuid,
            TargetNodeUuid = episode.Uuid,
            GroupId = "group",
            CreatedAt = timestamp
        }.SaveAsync(driver);

        var summarized = await graphiti.SummarizeSagaAsync(saga.Uuid);

        Assert.Equal(TextUtilities.MaxSummaryChars, summarized.Summary.Length);
        Assert.Equal(longSummary[..TextUtilities.MaxSummaryChars], summarized.Summary);
        Assert.NotEqual("Complete sentence.", summarized.Summary);
    }

    [Fact]
    public async Task SummarizeSaga_UsesDeterministicFallbackWhenTypedLlmReturnsNoSummary()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var saga = new SagaNode
        {
            Name = "launch",
            GroupId = "group",
            CreatedAt = timestamp
        };
        var firstEpisode = new EpisodicNode
        {
            Name = "first",
            GroupId = "group",
            CreatedAt = timestamp,
            ValidAt = timestamp,
            Content = "Launch moved to March 15."
        };
        var secondEpisode = new EpisodicNode
        {
            Name = "second",
            GroupId = "group",
            CreatedAt = timestamp.AddMinutes(1),
            ValidAt = timestamp.AddMinutes(1),
            Content = "Marketing owns the launch checklist."
        };
        await saga.SaveAsync(driver);
        await firstEpisode.SaveAsync(driver);
        await secondEpisode.SaveAsync(driver);
        foreach (var episode in new[] { firstEpisode, secondEpisode })
        {
            await new HasEpisodeEdge
            {
                SourceNodeUuid = saga.Uuid,
                TargetNodeUuid = episode.Uuid,
                GroupId = "group",
                CreatedAt = episode.CreatedAt
            }.SaveAsync(driver);
        }

        var summarized = await graphiti.SummarizeSagaAsync(saga.Uuid);

        Assert.Equal(
            "Launch moved to March 15.\nMarketing owns the launch checklist.",
            summarized.Summary);
        Assert.Equal(secondEpisode.ValidAt, summarized.LastSummarizedEpisodeValidAt);
    }

    [Fact]
    public async Task Graphiti_UsesInjectedTimeProviderForIngestionTimestamps()
    {
        var driver = new InMemoryGraphDriver();
        var fixedNow = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var graphiti = new Graphiti(
            graphDriver: driver,
            timeProvider: new FixedTimeProvider(fixedNow));

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice likes Bob",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");

        Assert.Equal(fixedNow.UtcDateTime, result.Episode.CreatedAt);
        Assert.All(result.Nodes, node => Assert.Equal(fixedNow.UtcDateTime, node.CreatedAt));
        Assert.All(result.Edges, edge => Assert.Equal(fixedNow.UtcDateTime, edge.CreatedAt));
        Assert.All(result.EpisodicEdges, edge => Assert.Equal(fixedNow.UtcDateTime, edge.CreatedAt));
    }

    [Fact]
    public async Task Graphiti_EmitsStandardLogsForIngestionAndSearch()
    {
        var driver = new InMemoryGraphDriver();
        var logger = new ListLogger<Graphiti>();
        var graphiti = new Graphiti(graphDriver: driver, logger: logger);

        await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice likes Bob",
            "message",
            new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            groupId: "group");
        await graphiti.SearchAsync("Alice Bob", groupIds: new[] { "group" });

        Assert.Contains(logger.Entries, entry => entry.EventId == 1000 && entry.Level == LogLevel.Information);
        Assert.Contains(logger.Entries, entry => entry.EventId == 1001 && entry.Message.Contains("Added episode", StringComparison.Ordinal));
        Assert.Contains(logger.Entries, entry => entry.EventId == 1020 && entry.Level == LogLevel.Debug);
        Assert.Contains(logger.Entries, entry => entry.EventId == 1021 && entry.Message.Contains("Edge search completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildCommunities_UsesInjectedTimeProviderForCommunityTimestamps()
    {
        var driver = new InMemoryGraphDriver();
        var fixedNow = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var graphiti = new Graphiti(
            graphDriver: driver,
            timeProvider: new FixedTimeProvider(fixedNow));
        var alice = new EntityNode { Name = "Alice", GroupId = "group", Summary = "Alice summary" };
        var bob = new EntityNode { Name = "Bob", GroupId = "group", Summary = "Bob summary" };
        await alice.SaveAsync(driver);
        await bob.SaveAsync(driver);
        await new EntityEdge
        {
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "group",
            Name = "RELATES_TO",
            Fact = "Alice likes Bob"
        }.SaveAsync(driver);

        var (communities, communityEdges) = await graphiti.BuildCommunitiesAsync(new[] { "group" });

        Assert.Single(communities);
        Assert.Equal(2, communityEdges.Count);
        Assert.All(communities, community => Assert.Equal(fixedNow.UtcDateTime, community.CreatedAt));
        Assert.All(communityEdges, edge => Assert.Equal(fixedNow.UtcDateTime, edge.CreatedAt));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(eventId.Id, logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(int EventId, LogLevel Level, string Message);

    private static async Task SaveEpisodeRemovalFixtureAsync(
        IGraphDriver driver,
        EntityNode source,
        EntityNode target,
        EntityEdge edge,
        params EpisodicNode[] episodes)
    {
        await source.SaveAsync(driver);
        await target.SaveAsync(driver);
        await edge.SaveAsync(driver);
        foreach (var episode in episodes)
        {
            await episode.SaveAsync(driver);
            await new EpisodicEdge
            {
                GroupId = episode.GroupId,
                SourceNodeUuid = episode.Uuid,
                TargetNodeUuid = source.Uuid,
                CreatedAt = episode.CreatedAt
            }.SaveAsync(driver);
            await new EpisodicEdge
            {
                GroupId = episode.GroupId,
                SourceNodeUuid = episode.Uuid,
                TargetNodeUuid = target.Uuid,
                CreatedAt = episode.CreatedAt
            }.SaveAsync(driver);
        }
    }

    private static async Task<SagaRemovalFixture> SaveSagaRemovalFixtureAsync(
        IGraphDriver driver,
        int episodeCount)
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var episodes = Enumerable.Range(0, episodeCount)
            .Select(index =>
            {
                var timestamp = start.AddMinutes(index);
                return new EpisodicNode
                {
                    Uuid = $"episode-{index}",
                    Name = $"episode-{index}",
                    GroupId = "group",
                    CreatedAt = timestamp,
                    ValidAt = timestamp,
                    Content = $"content-{index}"
                };
            })
            .ToList();
        var saga = new SagaNode
        {
            Uuid = "saga",
            Name = "launch",
            GroupId = "group",
            CreatedAt = start,
            FirstEpisodeUuid = episodes.FirstOrDefault()?.Uuid,
            LastEpisodeUuid = episodes.LastOrDefault()?.Uuid
        };

        await saga.SaveAsync(driver);
        foreach (var episode in episodes)
        {
            await episode.SaveAsync(driver);
            await new HasEpisodeEdge
            {
                Uuid = $"has-{episode.Uuid}",
                SourceNodeUuid = saga.Uuid,
                TargetNodeUuid = episode.Uuid,
                GroupId = "group",
                CreatedAt = episode.CreatedAt
            }.SaveAsync(driver);
        }

        for (var index = 1; index < episodes.Count; index++)
        {
            await new NextEpisodeEdge
            {
                Uuid = $"next-{index}",
                SourceNodeUuid = episodes[index - 1].Uuid,
                TargetNodeUuid = episodes[index].Uuid,
                GroupId = "group",
                CreatedAt = episodes[index].CreatedAt
            }.SaveAsync(driver);
        }

        return new SagaRemovalFixture(saga, episodes);
    }

    private sealed record SagaRemovalFixture(SagaNode Saga, IReadOnlyList<EpisodicNode> Episodes);

    private sealed class EmptyGraphExtractionLlmClient : LlmClient
    {
        public EmptyGraphExtractionLlmClient()
            : base(config: null, cache: false)
        {
        }

        public Type? ExtractionResponseModel { get; private set; }

        protected override Task<JsonObject> GenerateResponseCoreAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel,
            StructuredResponseSchema? responseSchema,
            int maxTokens,
            ModelSize modelSize,
            string? promptName,
            CancellationToken cancellationToken)
        {
            if (IsNodeExtractionPrompt(promptName))
            {
                ExtractionResponseModel = responseModel;
            }

            return Task.FromResult(new JsonObject());
        }
    }

    private sealed class InvalidStructuredTimestampLlmClient : LlmClient
    {
        public InvalidStructuredTimestampLlmClient()
            : base(config: null, cache: false)
        {
        }

        public Type? TimestampResponseModel { get; private set; }

        protected override Task<JsonObject> GenerateResponseCoreAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel,
            StructuredResponseSchema? responseSchema,
            int maxTokens,
            ModelSize modelSize,
            string? promptName,
            CancellationToken cancellationToken)
        {
            if (IsNodeExtractionPrompt(promptName))
            {
                return Task.FromResult(new JsonObject
                {
                    ["extracted_entities"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                        new JsonObject { ["name"] = "Acme", ["entity_type"] = "Organization" }
                    },
                });
            }

            if (string.Equals(promptName, "extract_edges.edge", StringComparison.Ordinal))
            {
                return Task.FromResult(new JsonObject
                {
                    ["edges"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["source_entity_name"] = "Alice",
                            ["target_entity_name"] = "Acme",
                            ["relation_type"] = "WORKS_AT",
                            ["fact"] = "Alice started at Acme."
                        }
                    }
                });
            }

            if (string.Equals(promptName, "extract_edges.extract_timestamps", StringComparison.Ordinal))
            {
                TimestampResponseModel = responseModel;
                return Task.FromResult(new JsonObject
                {
                    ["valid_at"] = 42,
                    ["invalid_at"] = "not a timestamp"
                });
            }

            return Task.FromResult(new JsonObject());
        }
    }

    private sealed class StaticLlmClient : ILlmClient
    {
        private readonly JsonObject? _response;
        private readonly Dictionary<string, JsonObject>? _responsesByPromptName;
        private readonly Lock _gate = new();

        public StaticLlmClient(JsonObject response) => _response = response;

        public StaticLlmClient(IReadOnlyDictionary<string, JsonObject> responsesByPromptName) =>
            _responsesByPromptName = NormalizeExtractionResponses(responsesByPromptName);

        public TokenUsageTracker TokenTracker { get; } = new();
        public List<LlmCall> Calls { get; } = new();
        public IReadOnlyList<string?> PromptNames
        {
            get
            {
                lock (_gate)
                {
                    return Calls.Select(call => call.PromptName).ToList();
                }
            }
        }

        public Task<JsonObject> GenerateResponseAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel = null,
            StructuredResponseSchema? responseSchema = null,
            int? maxTokens = null,
            ModelSize modelSize = ModelSize.Medium,
            string? groupId = null,
            string? promptName = null,
            bool attributeExtraction = false,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Calls.Add(new LlmCall(promptName, attributeExtraction, responseModel, responseSchema, messages.ToList()));
            }

            if (TryGetResponse(promptName, out var response))
            {
                return Task.FromResult((JsonObject)response.DeepClone());
            }

            return Task.FromResult(_response is null
                ? new JsonObject()
                : ExtractionResponseForPrompt(_response, promptName));
        }

        private bool TryGetResponse(string? promptName, out JsonObject response)
        {
            response = null!;
            if (promptName is null || _responsesByPromptName is null)
            {
                return false;
            }

            return _responsesByPromptName.TryGetValue(promptName, out response!);
        }
    }

    private sealed class ConcurrencyTrackingLlmClient : ILlmClient
    {
        private readonly Dictionary<string, JsonObject> _responsesByPromptName;
        private readonly string _trackedPromptName;
        private readonly TimeSpan _delay;
        private int _activePromptCalls;
        private int _maxObservedConcurrency;
        private int _trackedPromptCalls;

        public ConcurrencyTrackingLlmClient(
            IReadOnlyDictionary<string, JsonObject> responsesByPromptName,
            string trackedPromptName,
            TimeSpan delay)
        {
            _responsesByPromptName = NormalizeExtractionResponses(responsesByPromptName);
            _trackedPromptName = trackedPromptName;
            _delay = delay;
        }

        public TokenUsageTracker TokenTracker { get; } = new();
        public int TrackedPromptCalls => Volatile.Read(ref _trackedPromptCalls);
        public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);

        public async Task<JsonObject> GenerateResponseAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel = null,
            StructuredResponseSchema? responseSchema = null,
            int? maxTokens = null,
            ModelSize modelSize = ModelSize.Medium,
            string? groupId = null,
            string? promptName = null,
            bool attributeExtraction = false,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(promptName, _trackedPromptName, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _trackedPromptCalls);
                var active = Interlocked.Increment(ref _activePromptCalls);
                UpdateMax(ref _maxObservedConcurrency, active);
                try
                {
                    await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref _activePromptCalls);
                }
            }

            return promptName is not null && TryGetResponse(promptName, out var response)
                ? (JsonObject)response.DeepClone()
                : new JsonObject();
        }

        private bool TryGetResponse(string promptName, out JsonObject response) =>
            _responsesByPromptName.TryGetValue(promptName, out response!);
    }

    private sealed class BulkExtractionTrackingLlmClient : ILlmClient
    {
        private readonly Lock _gate = new();
        private int _activePromptCalls;
        private int _maxObservedConcurrency;
        private int _trackedPromptCalls;

        public TokenUsageTracker TokenTracker { get; } = new();
        public int TrackedPromptCalls => Volatile.Read(ref _trackedPromptCalls);
        public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);
        public List<string> CompletedContents { get; } = new();

        public async Task<JsonObject> GenerateResponseAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel = null,
            StructuredResponseSchema? responseSchema = null,
            int? maxTokens = null,
            ModelSize modelSize = ModelSize.Medium,
            string? groupId = null,
            string? promptName = null,
            bool attributeExtraction = false,
            CancellationToken cancellationToken = default)
        {
            if (!IsNodeExtractionPrompt(promptName))
            {
                return new JsonObject();
            }

            var content = ReadEpisodeContent(messages);
            Interlocked.Increment(ref _trackedPromptCalls);
            var active = Interlocked.Increment(ref _activePromptCalls);
            UpdateMax(ref _maxObservedConcurrency, active);
            try
            {
                await Task.Delay(DelayFor(content), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _activePromptCalls);
            }

            lock (_gate)
            {
                CompletedContents.Add(content);
            }

            var entityName = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).First();
            return new JsonObject
            {
                ["extracted_entities"] = new JsonArray(
                    new JsonObject
                    {
                        ["name"] = entityName,
                        ["entity_type"] = "Entity",
                        ["episode_indices"] = new JsonArray { 0 }
                    })
            };
        }

        private static TimeSpan DelayFor(string content) =>
            content.StartsWith("Alpha", StringComparison.Ordinal)
                ? TimeSpan.FromMilliseconds(150)
                : TimeSpan.FromMilliseconds(15);
    }

    private sealed class FailingBulkExtractionLlmClient : ILlmClient
    {
        public TokenUsageTracker TokenTracker { get; } = new();

        public async Task<JsonObject> GenerateResponseAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel = null,
            StructuredResponseSchema? responseSchema = null,
            int? maxTokens = null,
            ModelSize modelSize = ModelSize.Medium,
            string? groupId = null,
            string? promptName = null,
            bool attributeExtraction = false,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(promptName, "extract_edges.edge", StringComparison.Ordinal))
            {
                return new JsonObject { ["edges"] = new JsonArray() };
            }

            if (!IsNodeExtractionPrompt(promptName))
            {
                return new JsonObject();
            }

            var content = ReadEpisodeContent(messages);
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            if (content.Contains("fail", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("extraction failed");
            }

            return new JsonObject
            {
                ["extracted_entities"] = new JsonArray(
                    new JsonObject
                    {
                        ["name"] = "Alpha",
                        ["entity_type"] = "Entity",
                        ["episode_indices"] = new JsonArray { 0 }
                    })
            };
        }
    }

    private sealed class BulkReinvalidationLlmClient : ILlmClient
    {
        public TokenUsageTracker TokenTracker { get; } = new();

        public Task<JsonObject> GenerateResponseAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel = null,
            StructuredResponseSchema? responseSchema = null,
            int? maxTokens = null,
            ModelSize modelSize = ModelSize.Medium,
            string? groupId = null,
            string? promptName = null,
            bool attributeExtraction = false,
            CancellationToken cancellationToken = default)
        {
            if (IsNodeExtractionPrompt(promptName))
            {
                return Task.FromResult(new JsonObject
                {
                    ["extracted_entities"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                        new JsonObject { ["name"] = "Acme", ["entity_type"] = "Organization" }
                    }
                });
            }

            if (string.Equals(promptName, "extract_edges.edge", StringComparison.Ordinal))
            {
                var content = ReadEpisodeContent(messages);
                var left = content.Contains("left", StringComparison.OrdinalIgnoreCase);
                return Task.FromResult(new JsonObject
                {
                    ["edges"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["source"] = "Alice",
                            ["target"] = "Acme",
                            ["relation_type"] = left ? "LEFT" : "REJOINED",
                            ["fact"] = left ? "Alice left Acme." : "Alice rejoined Acme.",
                            ["valid_at"] = left ? "2026-02-01T00:00:00Z" : "2026-03-01T00:00:00Z"
                        }
                    }
                });
            }

            if (string.Equals(promptName, "dedupe_edges.resolve_edge", StringComparison.Ordinal))
            {
                return Task.FromResult(new JsonObject
                {
                    ["duplicate_facts"] = new JsonArray(),
                    ["contradicted_facts"] = new JsonArray { 0 }
                });
            }

            return Task.FromResult(new JsonObject());
        }
    }

    private sealed record LlmCall(
        string? PromptName,
        bool AttributeExtraction,
        Type? ResponseModel,
        StructuredResponseSchema? ResponseSchema,
        IReadOnlyList<Message> Messages);

    private static string ReadEpisodeContent(IReadOnlyList<Message> messages)
    {
        var context = JsonNode.Parse(messages[^1].Content)!.AsObject();
        return context["episode_content"]!.GetValue<string>();
    }

    private static bool IsNodeExtractionPrompt(string? promptName) =>
        string.Equals(promptName, "extract_nodes.extract_message", StringComparison.Ordinal)
        || string.Equals(promptName, "extract_nodes.extract_text", StringComparison.Ordinal)
        || string.Equals(promptName, "extract_nodes.extract_json", StringComparison.Ordinal);

    private static Dictionary<string, JsonObject> NormalizeExtractionResponses(
        IReadOnlyDictionary<string, JsonObject> responsesByPromptName)
    {
        var normalized = responsesByPromptName.ToDictionary(
            pair => pair.Key,
            pair => (JsonObject)pair.Value.DeepClone(),
            StringComparer.Ordinal);

        foreach (var promptName in new[]
                 {
                     "extract_nodes.extract_message",
                     "extract_nodes.extract_text",
                     "extract_nodes.extract_json"
                 })
        {
            if (!normalized.TryGetValue(promptName, out var response))
            {
                continue;
            }

            if (!normalized.ContainsKey("extract_edges.edge")
                && response.TryGetPropertyValue("edges", out var edges)
                && edges is not null)
            {
                normalized["extract_edges.edge"] = new JsonObject
                {
                    ["edges"] = edges.DeepClone()
                };
            }

            if (response.TryGetPropertyValue("edges", out _))
            {
                response.Remove("edges");
            }
        }

        if (normalized.TryGetValue("extract_edges.edge", out var edgeResponse))
        {
            edgeResponse.Remove("extracted_entities");
            edgeResponse.Remove("entities");
        }

        return normalized;
    }

    private static JsonObject ExtractionResponseForPrompt(JsonObject response, string? promptName)
    {
        if (IsNodeExtractionPrompt(promptName))
        {
            var nodeResponse = (JsonObject)response.DeepClone();
            nodeResponse.Remove("edges");
            return nodeResponse;
        }

        if (string.Equals(promptName, "extract_edges.edge", StringComparison.Ordinal))
        {
            return response.TryGetPropertyValue("edges", out var edges) && edges is not null
                ? new JsonObject { ["edges"] = edges.DeepClone() }
                : new JsonObject { ["edges"] = new JsonArray() };
        }

        return (JsonObject)response.DeepClone();
    }

    private static void UpdateMax(ref int target, int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (value <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }
}
