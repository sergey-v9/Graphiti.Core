using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.Core.Internal.Helpers;
using Graphiti.Core.Internal.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Graphiti.Core.Tests;

public class GraphitiCommunityTests
{
    [Fact]
    public void CommunityClustering_UsesSynchronousSemantics()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var a = Entity("A", "group", now, "a");
        var b = Entity("B", "group", now, "b");
        var c = Entity("C", "group", now, "c");
        var d = Entity("D", "group", now, "d");
        var e = Entity("E", "group", now, "e");

        var clusters = CommunityClustering.BuildClusters(
            new[] { a, b, c, d, e },
            new[]
            {
                Relates(a, c, "group", now),
                Relates(b, c, "group", now),
                Relates(c, d, "group", now)
            });

        Assert.Equal(
            new[] { new[] { "a", "b", "c", "d" }, new[] { "e" } },
            ClusterUuids(clusters));
    }

    [Fact]
    public void CommunityClustering_MatchesSynchronousOracleForDeterministicGraph()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var nodes = Enumerable.Range(0, 8)
            .Select(index => Entity($"Node {index}", "group", now, $"node-{index}"))
            .ToArray();
        var edges = new[]
        {
            Relates(nodes[0], nodes[2], "group", now),
            Relates(nodes[1], nodes[2], "group", now),
            Relates(nodes[2], nodes[3], "group", now),
            Relates(nodes[4], nodes[5], "group", now),
            Relates(nodes[5], nodes[6], "group", now),
            Relates(nodes[6], nodes[4], "group", now),
            Relates(nodes[6], nodes[7], "group", now)
        };

        var actual = ClusterUuids(CommunityClustering.BuildClusters(nodes, edges));
        var expected = SynchronousLabelPropagationOracle(nodes, edges);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CommunityClustering_UsesInputOrderForInitialCommunityIds()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var a = Entity("A", "group", now, "a");
        var b = Entity("B", "group", now, "b");
        var c = Entity("C", "group", now, "c");
        var d = Entity("D", "group", now, "d");
        var e = Entity("E", "group", now, "e");
        var nodes = new[] { b, d, e, a, c };
        var edges = new[]
        {
            Relates(a, b, "group", now),
            Relates(a, d, "group", now),
            Relates(a, e, "group", now),
            Relates(b, c, "group", now)
        };

        var actual = ClusterUuids(CommunityClustering.BuildClusters(nodes, edges));

        Assert.Equal(new[] { new[] { "b", "c" }, new[] { "d", "e", "a" } }, actual);
        Assert.Equal(SynchronousLabelPropagationOracle(nodes, edges), actual);
    }

    [Fact]
    public void CommunityClustering_SortsGroupsAndKeepsFirstDuplicateNode()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var groupB = Entity("Aardvark", "group-b", now, "group-b-node");
        var duplicateFirst = Entity("Beta", "group-a", now, "duplicate");
        var other = Entity("Gamma", "group-a", now, "other");
        var duplicateSecond = Entity("Alpha", "group-a", now, "duplicate");

        var clusters = CommunityClustering.BuildClusters(
            new[] { groupB, duplicateFirst, other, duplicateSecond },
            Array.Empty<EntityEdge>());

        Assert.Equal(
            new[] { new[] { "duplicate" }, new[] { "other" }, new[] { "group-b-node" } },
            ClusterUuids(clusters));
        Assert.Same(duplicateFirst, clusters[0][0]);
    }

    [Fact]
    public void CommunityClustering_IgnoresEdgeGroupAndSkipsMissingEndpoints()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var laterUuid = Entity("Alpha", "group", now, "node-b");
        var earlierUuid = Entity("alpha", "group", now, "node-a");
        var missing = Entity("Missing", "group", now, "missing");
        var crossGroupEdge = Relates(laterUuid, earlierUuid, "different-edge-group", now);
        var missingEndpointEdge = Relates(laterUuid, missing, "group", now);

        var clusters = CommunityClustering.BuildClusters(
            new[] { laterUuid, earlierUuid },
            new[] { missingEndpointEdge, crossGroupEdge });

        Assert.Equal(new[] { new[] { "node-b", "node-a" } }, ClusterUuids(clusters));
    }

    [Fact]
    public async Task BuildCommunities_LabelPropagationUsesSynchronousSemantics()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var a = Entity("A", "group", now, "a");
        var b = Entity("B", "group", now, "b");
        var c = Entity("C", "group", now, "c");
        var d = Entity("D", "group", now, "d");
        var e = Entity("E", "group", now, "e");
        foreach (var node in new[] { a, b, c, d, e })
        {
            await node.SaveAsync(driver);
        }

        await Relates(a, c, "group", now).SaveAsync(driver);
        await Relates(b, c, "group", now).SaveAsync(driver);
        await Relates(c, d, "group", now).SaveAsync(driver);

        var (communities, communityEdges) = await graphiti.BuildCommunitiesAsync(new[] { "group" });

        Assert.Equal(2, communities.Count);
        Assert.Equal(5, communityEdges.Count);
        var communityByEntity = communityEdges.ToDictionary(edge => edge.TargetNodeUuid, edge => edge.SourceNodeUuid);
        Assert.Equal(communityByEntity[a.Uuid], communityByEntity[b.Uuid]);
        Assert.Equal(communityByEntity[a.Uuid], communityByEntity[c.Uuid]);
        Assert.Equal(communityByEntity[a.Uuid], communityByEntity[d.Uuid]);
        Assert.NotEqual(communityByEntity[a.Uuid], communityByEntity[e.Uuid]);
    }

    [Fact]
    public async Task BuildCommunities_CreatesGroupScopedCommunitiesAndIgnoresCrossGroupEdges()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = Entity("Alice", "group-a", now);
        var bob = Entity("Bob", "group-a", now);
        var carol = Entity("Carol", "group-b", now);
        var dana = Entity("Dana", "group-b", now);

        foreach (var node in new[] { alice, bob, carol, dana })
        {
            await node.SaveAsync(driver);
        }

        await Relates(alice, bob, "group-a", now).SaveAsync(driver);
        await Relates(carol, dana, "group-b", now).SaveAsync(driver);
        await Relates(bob, carol, "cross-group", now).SaveAsync(driver);

        var (communities, communityEdges) = await graphiti.BuildCommunitiesAsync();

        Assert.Equal(2, communities.Count);
        Assert.Equal(4, communityEdges.Count);

        var communityByEntity = communityEdges.ToDictionary(edge => edge.TargetNodeUuid, edge => edge.SourceNodeUuid);
        Assert.Equal(communityByEntity[alice.Uuid], communityByEntity[bob.Uuid]);
        Assert.Equal(communityByEntity[carol.Uuid], communityByEntity[dana.Uuid]);
        Assert.NotEqual(communityByEntity[alice.Uuid], communityByEntity[carol.Uuid]);

        Assert.Single(await CommunityNode.GetByGroupIdsAsync(driver, new[] { "group-a" }));
        Assert.Single(await CommunityNode.GetByGroupIdsAsync(driver, new[] { "group-b" }));
    }

    [Fact]
    public async Task BuildCommunities_PreservesRequestedGroupOrder()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var groupB = Entity("Zulu", "group-b", now, "group-b-node");
        var groupA = Entity("Alpha", "group-a", now, "group-a-node");

        foreach (var node in new[] { groupA, groupB })
        {
            await node.SaveAsync(driver);
        }

        var (communities, communityEdges) = await graphiti.BuildCommunitiesAsync(new[] { "group-b", "group-a" });

        Assert.Equal(new[] { "group-b", "group-a" }, communities.Select(community => community.GroupId));
        Assert.Equal(new[] { groupB.Uuid, groupA.Uuid }, communityEdges.Select(edge => edge.TargetNodeUuid));
    }

    [Fact]
    public async Task BuildCommunities_PreservesIntraGroupReadOrder()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var laterUuid = Entity("Zulu", "group", now, "z-uuid");
        var earlierUuid = Entity("Alpha", "group", now, "a-uuid");

        foreach (var node in new[] { earlierUuid, laterUuid })
        {
            await node.SaveAsync(driver);
        }

        var (_, communityEdges) = await graphiti.BuildCommunitiesAsync(new[] { "group" });

        Assert.Equal(new[] { laterUuid.Uuid, earlierUuid.Uuid }, communityEdges.Select(edge => edge.TargetNodeUuid));
    }

    [Fact]
    public async Task BuildCommunities_ClustersByEndpointGroupsNotEdgeGroup()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = Entity("Alice", "group", now, "alice");
        var bob = Entity("Bob", "group", now, "bob");
        await alice.SaveAsync(driver);
        await bob.SaveAsync(driver);
        await Relates(alice, bob, "edge-only", now).SaveAsync(driver);

        var (communities, communityEdges) = await graphiti.BuildCommunitiesAsync(new[] { "group" });

        var community = Assert.Single(communities);
        Assert.Equal(2, communityEdges.Count);
        Assert.All(communityEdges, edge => Assert.Equal(community.Uuid, edge.SourceNodeUuid));
        Assert.Equal(new[] { alice.Uuid, bob.Uuid }, communityEdges.Select(edge => edge.TargetNodeUuid).Order());
    }

    [Fact]
    public async Task BuildCommunities_CapturesTimestampPerCommunityBuild()
    {
        var driver = new InMemoryGraphDriver();
        var firstNow = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var graphiti = new Graphiti(
            graphDriver: driver,
            maxCoroutines: 1,
            timeProvider: new SteppingTimeProvider(firstNow, TimeSpan.FromMinutes(7)));
        var createdAt = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc);
        var alice = Entity("Alice", "group", createdAt, "alice");
        var bob = Entity("Bob", "group", createdAt, "bob");
        await alice.SaveAsync(driver);
        await bob.SaveAsync(driver);

        var (communities, communityEdges) = await graphiti.BuildCommunitiesAsync(new[] { "group" });

        Assert.Equal(2, communities.Count);
        Assert.Equal(
            new[] { firstNow.UtcDateTime, firstNow.AddMinutes(7).UtcDateTime },
            communities.Select(community => community.CreatedAt).Order());
        foreach (var community in communities)
        {
            var membership = Assert.Single(communityEdges, edge => edge.SourceNodeUuid == community.Uuid);
            Assert.Equal(community.CreatedAt, membership.CreatedAt);
        }
    }

    [Fact]
    public async Task BuildCommunities_RemovesExistingCommunitiesBeforeRebuild()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = Entity("Alice", "group", now);
        var bob = Entity("Bob", "group", now);
        await alice.SaveAsync(driver);
        await bob.SaveAsync(driver);
        await Relates(alice, bob, "group", now).SaveAsync(driver);

        await graphiti.BuildCommunitiesAsync(new[] { "group" });
        await graphiti.BuildCommunitiesAsync(new[] { "group" });

        var storedCommunities = await CommunityNode.GetByGroupIdsAsync(driver, new[] { "group" });
        var storedCommunityEdges = await CommunityEdge.GetByGroupIdsAsync(driver, new[] { "group" });

        Assert.Single(storedCommunities);
        Assert.Equal(2, storedCommunityEdges.Count);
        Assert.All(storedCommunityEdges, edge => Assert.Equal(storedCommunities[0].Uuid, edge.SourceNodeUuid));
    }

    [Fact]
    public async Task BuildCommunities_ExplicitEmptyGroupIdsClearsAndBuildsNone()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = Entity("Alice", "group", now);
        var bob = Entity("Bob", "group", now);
        await alice.SaveAsync(driver);
        await bob.SaveAsync(driver);
        await Relates(alice, bob, "group", now).SaveAsync(driver);

        await graphiti.BuildCommunitiesAsync(new[] { "group" });

        var (communities, communityEdges) = await graphiti.BuildCommunitiesAsync(Array.Empty<string>());

        Assert.Empty(communities);
        Assert.Empty(communityEdges);
        Assert.Empty(await CommunityNode.GetByGroupIdsAsync(driver, new[] { "group" }));
        Assert.Empty(await CommunityEdge.GetByGroupIdsAsync(driver, new[] { "group" }));
    }

    [Fact]
    public async Task BuildCommunities_NoGroupIdsDiscoversDefaultEmptyGroup()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = Entity("Alice", string.Empty, now);
        var bob = Entity("Bob", string.Empty, now);
        await alice.SaveAsync(driver);
        await bob.SaveAsync(driver);
        await Relates(alice, bob, string.Empty, now).SaveAsync(driver);

        var (communities, communityEdges) = await graphiti.BuildCommunitiesAsync();

        Assert.Equal(new[] { string.Empty }, await driver.GetEntityGroupIdsAsync());
        Assert.Equal(new[] { string.Empty }, await driver.GetCommunityGroupIdsAsync());
        Assert.Single(communities);
        Assert.Equal(2, communityEdges.Count);
        Assert.Equal(string.Empty, communities[0].GroupId);
        Assert.All(communityEdges, edge => Assert.Equal(string.Empty, edge.GroupId));
        Assert.Single(await CommunityNode.GetByGroupIdsAsync(driver, new[] { string.Empty }));
        Assert.Equal(2, (await CommunityEdge.GetByGroupIdsAsync(driver, new[] { string.Empty })).Count);
    }

    [Fact]
    public async Task BuildCommunities_UsesStructuredSummaryAndNameResponses()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new CapturingCommunityLlmClient();
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = Entity("Alice", "group", now);
        var bob = Entity("Bob", "group", now);
        await alice.SaveAsync(driver);
        await bob.SaveAsync(driver);
        await Relates(alice, bob, "group", now).SaveAsync(driver);

        var (communities, _) = await graphiti.BuildCommunitiesAsync(new[] { "group" });

        var community = Assert.Single(communities);
        Assert.Equal("Team community", community.Name);
        Assert.Equal("combined team", community.Summary);
        Assert.Equal(
            "CommunitySummaryResponse",
            llm.ResponseModelsByPrompt["summarize_nodes.summarize_pair"].Single()?.Name);
        Assert.Equal(
            "CommunityNameResponse",
            llm.ResponseModelsByPrompt["summarize_nodes.summary_description"].Single()?.Name);
    }

    [Fact]
    public async Task BuildCommunities_SingleNodeClusterPersistsRawEntitySummary()
    {
        // Community building seeds the pairwise reduction with the RAW entity summary, and for a
        // single-node cluster persists the sentence-truncated entity summary verbatim. The community
        // summary must therefore be the entity's own summary, NOT the name-prefixed
        // "{Name}: {Summary}" deterministic text.
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var solo = Entity("Solo", "group", now);
        await solo.SaveAsync(driver);

        var (communities, _) = await graphiti.BuildCommunitiesAsync(new[] { "group" });

        var community = Assert.Single(communities);
        Assert.Equal("Solo summary", community.Summary);
        Assert.DoesNotContain("Solo: Solo summary", community.Summary, StringComparison.Ordinal);

        var storedCommunities = await CommunityNode.GetByGroupIdsAsync(driver, new[] { "group" });
        var storedCommunity = Assert.Single(storedCommunities);
        Assert.Equal("Solo summary", storedCommunity.Summary);
    }

    [Fact]
    public async Task BuildCommunities_PreservesBlankEntitySummariesInPairReduction()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new CapturingCommunityLlmClient();
        var graphiti = new Graphiti(graphDriver: driver, llmClient: llm);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = Entity("Alice", "group", now, "alice");
        var bob = Entity("Bob", "group", now, "bob");
        alice.Summary = string.Empty;
        await alice.SaveAsync(driver);
        await bob.SaveAsync(driver);
        await Relates(alice, bob, "group", now).SaveAsync(driver);

        var (communities, _) = await graphiti.BuildCommunitiesAsync(new[] { "group" });

        Assert.Equal("combined team", Assert.Single(communities).Summary);
        var pairMessages = Assert.Single(llm.MessagesByPrompt["summarize_nodes.summarize_pair"]);
        var promptSummaries = ReadSummaryPairPayload(pairMessages[^1])
            .Select(item => item?["summary"]?.GetValue<string>())
            .ToArray();
        Assert.Equal(new[] { "Bob summary", string.Empty }, promptSummaries);
    }

    [Fact]
    public async Task BuildCommunities_RejectsEmptySummaryFromRealLlm()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticJsonLlmClient(_ => new JsonObject { ["summary"] = string.Empty }));
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = Entity("Alice", "group", now, "alice");
        var bob = Entity("Bob", "group", now, "bob");
        await alice.SaveAsync(driver);
        await bob.SaveAsync(driver);
        await Relates(alice, bob, "group", now).SaveAsync(driver);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            graphiti.BuildCommunitiesAsync(new[] { "group" }));

        Assert.Contains("community summary", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildCommunities_RejectsEmptyNameFromRealLlm()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(
            graphDriver: driver,
            llmClient: new StaticJsonLlmClient(_ => new JsonObject { ["description"] = string.Empty }));
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = Entity("Alice", "group", now, "alice");
        await alice.SaveAsync(driver);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            graphiti.BuildCommunitiesAsync(new[] { "group" }));

        Assert.Contains("community name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildCommunities_RebuildRemovesCommunitiesAcrossAllGroups()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = Entity("Alice", "group-a", now);
        var bob = Entity("Bob", "group-a", now);
        var carol = Entity("Carol", "group-b", now);
        var dana = Entity("Dana", "group-b", now);
        foreach (var node in new[] { alice, bob, carol, dana })
        {
            await node.SaveAsync(driver);
        }

        await Relates(alice, bob, "group-a", now).SaveAsync(driver);
        await Relates(carol, dana, "group-b", now).SaveAsync(driver);
        await graphiti.BuildCommunitiesAsync();

        await graphiti.BuildCommunitiesAsync(new[] { "group-a" });

        Assert.Single(await CommunityNode.GetByGroupIdsAsync(driver, new[] { "group-a" }));
        Assert.Empty(await CommunityNode.GetByGroupIdsAsync(driver, new[] { "group-b" }));
        Assert.Empty(await CommunityEdge.GetByGroupIdsAsync(driver, new[] { "group-b" }));
    }

    [Fact]
    public async Task BuildCommunities_NoGroupIdsUsesDriverDiscoveredEntityGroups()
    {
        var inner = new InMemoryGraphDriver();
        var driver = new DelegatingGraphDriver(inner);
        var graphiti = new Graphiti(graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = Entity("Alice", "group-a", now);
        var bob = Entity("Bob", "group-a", now);
        var carol = Entity("Carol", "group-b", now);
        var dana = Entity("Dana", "group-b", now);
        foreach (var node in new[] { alice, bob, carol, dana })
        {
            await node.SaveAsync(driver);
        }

        await Relates(alice, bob, "group-a", now).SaveAsync(driver);
        await Relates(carol, dana, "group-b", now).SaveAsync(driver);

        var (communities, communityEdges) = await graphiti.BuildCommunitiesAsync();

        Assert.Equal(2, communities.Count);
        Assert.Equal(4, communityEdges.Count);
        Assert.Single(await CommunityNode.GetByGroupIdsAsync(driver, new[] { "group-a" }));
        Assert.Single(await CommunityNode.GetByGroupIdsAsync(driver, new[] { "group-b" }));
    }

    [Fact]
    public async Task BuildCommunities_GeneratesIndependentClustersConcurrentlyAndPreservesOrder()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new DelayedCommunityLlmClient();
        var embedder = new DelayedCommunityEmbedder();
        var graphiti = new Graphiti(
            llmClient: llm,
            embedder: embedder,
            graphDriver: driver,
            maxCoroutines: 2);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alpha = Entity("Alpha", "group", now, "alpha");
        var beta = Entity("Beta", "group", now, "beta");
        var gamma = Entity("Gamma", "group", now, "gamma");
        foreach (var node in new[] { alpha, beta, gamma })
        {
            await node.SaveAsync(driver);
        }

        var (communities, communityEdges) = await graphiti.BuildCommunitiesAsync(new[] { "group" });

        Assert.Equal(new[] { "Community Gamma", "Community Beta", "Community Alpha" }, communities.Select(community => community.Name));
        Assert.Equal(new[] { "gamma", "beta", "alpha" }, communityEdges.Select(edge => edge.TargetNodeUuid));
        Assert.Equal(3, llm.TrackedPromptCalls);
        Assert.InRange(llm.MaxObservedConcurrency, 2, 2);
        Assert.InRange(embedder.MaxObservedConcurrency, 2, 2);
        Assert.Equal(3, llm.CompletedNames.Count);
        Assert.Equal("Beta", llm.CompletedNames[0]);
        Assert.All(llm.ResponseModels, responseModel => Assert.Equal("CommunityNameResponse", responseModel?.Name));
    }

    [Fact]
    public async Task BuildCommunities_GeneratesPairSummariesConcurrentlyWithinCluster()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new DelayedPairSummaryLlmClient();
        var graphiti = new Graphiti(
            llmClient: llm,
            graphDriver: driver,
            maxCoroutines: 2);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alpha = Entity("Alpha", "group", now, "alpha");
        var beta = Entity("Beta", "group", now, "beta");
        var gamma = Entity("Gamma", "group", now, "gamma");
        var delta = Entity("Delta", "group", now, "delta");
        foreach (var node in new[] { alpha, beta, gamma, delta })
        {
            await node.SaveAsync(driver);
        }

        await Relates(alpha, beta, "group", now).SaveAsync(driver);
        await Relates(beta, gamma, "group", now).SaveAsync(driver);
        await Relates(gamma, delta, "group", now).SaveAsync(driver);

        var (communities, _) = await graphiti.BuildCommunitiesAsync(new[] { "group" });

        Assert.Single(communities);
        Assert.Equal(3, llm.PairSummaryCalls);
        Assert.Equal(2, llm.MaxPairSummaryConcurrency);
        Assert.Equal("Community summary", Assert.Single(communities).Name);
    }

    [Fact]
    public async Task AddEpisode_WithUpdateCommunities_AttachesResolvedNodeToNeighborCommunity()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new StaticJsonLlmClient(messages =>
        {
            var user = messages.Count > 0 ? messages[^1].Content : string.Empty;
            var system = messages.Count > 0 ? messages[0].Content : string.Empty;
            if (system.Contains("combines summaries", StringComparison.Ordinal))
            {
                return new JsonObject { ["summary"] = "People who work together" };
            }

            if (system.Contains("describes provided contents", StringComparison.Ordinal))
            {
                return new JsonObject { ["description"] = "Work group" };
            }

            if (system.Contains("entity deduplication assistant", StringComparison.Ordinal))
            {
                return new JsonObject
                {
                    ["entity_resolutions"] = new JsonArray(
                        new JsonObject { ["id"] = 0, ["name"] = "Carol", ["duplicate_candidate_id"] = -1 },
                        new JsonObject { ["id"] = 1, ["name"] = "Alice", ["duplicate_candidate_id"] = 0 })
                };
            }

            if (system.Contains("fact deduplication assistant", StringComparison.Ordinal))
            {
                return new JsonObject
                {
                    ["duplicate_facts"] = new JsonArray(),
                    ["contradicted_facts"] = new JsonArray()
                };
            }

            if (system.Contains("entity extraction specialist", StringComparison.Ordinal)
                && user.Contains("Carol works with Alice", StringComparison.Ordinal))
            {
                return new JsonObject
                {
                    ["extracted_entities"] = new JsonArray(
                        new JsonObject { ["name"] = "Carol", ["entity_type_id"] = 0 },
                        new JsonObject { ["name"] = "Alice", ["entity_type_id"] = 0 })
                };
            }

            if (system.Contains("expert fact extractor", StringComparison.Ordinal)
                && user.Contains("Carol works with Alice", StringComparison.Ordinal))
            {
                return new JsonObject
                {
                    ["edges"] = new JsonArray(
                        new JsonObject
                        {
                            ["source_entity_name"] = "Carol",
                            ["target_entity_name"] = "Alice",
                            ["relation_type"] = "WORKS_WITH",
                            ["fact"] = "Carol works with Alice"
                        })
                };
            }

            return new JsonObject();
        });
        var graphiti = new Graphiti(llmClient: llm, graphDriver: driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var alice = Entity("Alice", "group", now);
        var bob = Entity("Bob", "group", now);
        await alice.GenerateNameEmbeddingAsync(graphiti.Embedder);
        await bob.GenerateNameEmbeddingAsync(graphiti.Embedder);
        await alice.SaveAsync(driver);
        await bob.SaveAsync(driver);
        await Relates(alice, bob, "group", now).SaveAsync(driver);
        await graphiti.BuildCommunitiesAsync(new[] { "group" });

        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Carol works with Alice",
            "message",
            now.AddMinutes(1),
            groupId: "group",
            updateCommunities: true);
        var carol = Assert.Single(result.Nodes, node => node.Name == "Carol");
        var aliceResult = Assert.Single(result.Nodes, node => node.Name == "Alice");

        Assert.Equal(alice.Uuid, aliceResult.Uuid);
        Assert.Single(result.CommunityEdges);
        Assert.Equal(carol.Uuid, result.CommunityEdges[0].TargetNodeUuid);
        Assert.NotEmpty(result.Communities);

        var carolCommunities = await driver.GetCommunitiesByNodesAsync(new[] { carol });
        Assert.Single(carolCommunities);
        var storedCommunityEdges = await CommunityEdge.GetByGroupIdsAsync(driver, new[] { "group" });
        Assert.Equal(3, storedCommunityEdges.Count);
    }

    [Fact]
    public async Task UpdateCommunitiesForNodes_DeduplicatesNodesByUuidInFirstSeenOrder()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var community = Community("community", "Existing", "group", now);
        var carol = Entity("Carol", "group", now, "carol");
        var duplicate = Entity("Carol duplicate", "group", now, "carol");
        await community.SaveAsync(driver);
        await carol.SaveAsync(driver);
        await Member(community, carol, now).SaveAsync(driver);

        var service = CreateCommunityService(driver);
        var (communities, communityEdges) = await service.UpdateCommunitiesForNodesAsync(
            new[] { carol, duplicate },
            driver,
            CancellationToken.None);

        var updated = Assert.Single(communities);
        Assert.Equal("community", updated.Uuid);
        Assert.Empty(communityEdges);
    }

    [Fact]
    public async Task UpdateCommunitiesForNodes_ChoosesMostFrequentNeighborCommunity()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var majority = Community("z-community", "Majority", "group", now);
        var minority = Community("a-community", "Minority", "group", now);
        var carol = Entity("Carol", "group", now, "carol");
        var alice = Entity("Alice", "group", now, "alice");
        var alex = Entity("Alex", "group", now, "alex");
        var bob = Entity("Bob", "group", now, "bob");
        foreach (var node in new Node[] { majority, minority, carol, alice, alex, bob })
        {
            await node.SaveAsync(driver);
        }

        await Member(majority, alice, now).SaveAsync(driver);
        await Member(majority, alex, now).SaveAsync(driver);
        await Member(minority, bob, now).SaveAsync(driver);
        await Relates(carol, bob, "group", now, "edge-0").SaveAsync(driver);
        await Relates(carol, alice, "group", now, "edge-1").SaveAsync(driver);
        await Relates(carol, alex, "group", now, "edge-2").SaveAsync(driver);

        var service = CreateCommunityService(driver);
        var (communities, communityEdges) = await service.UpdateCommunitiesForNodesAsync(
            new[] { carol },
            driver,
            CancellationToken.None);

        var updated = Assert.Single(communities);
        var membership = Assert.Single(communityEdges);
        Assert.Equal("z-community", updated.Uuid);
        Assert.Equal("z-community", membership.SourceNodeUuid);
        Assert.Equal("carol", membership.TargetNodeUuid);
    }

    [Fact]
    public async Task UpdateCommunitiesForNodes_TieUsesFirstNeighborCommunity()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var first = Community("z-community", "First", "group", now);
        var second = Community("a-community", "Second", "group", now);
        var carol = Entity("Carol", "group", now, "carol");
        var alice = Entity("Alice", "group", now, "alice");
        var bob = Entity("Bob", "group", now, "bob");
        foreach (var node in new Node[] { first, second, carol, alice, bob })
        {
            await node.SaveAsync(driver);
        }

        await Member(first, alice, now).SaveAsync(driver);
        await Member(second, bob, now).SaveAsync(driver);
        await Relates(carol, alice, "group", now, "edge-0").SaveAsync(driver);
        await Relates(carol, bob, "group", now, "edge-1").SaveAsync(driver);

        var service = CreateCommunityService(driver);
        var (communities, communityEdges) = await service.UpdateCommunitiesForNodesAsync(
            new[] { carol },
            driver,
            CancellationToken.None);

        var updated = Assert.Single(communities);
        var membership = Assert.Single(communityEdges);
        Assert.Equal("z-community", updated.Uuid);
        Assert.Equal("z-community", membership.SourceNodeUuid);
        Assert.Equal("carol", membership.TargetNodeUuid);
    }

    [Fact]
    public void DeterministicCommunityText_SkipsBlankSummariesAndPreservesOrder()
    {
        var summary = DeterministicCommunityText.BuildCommunitySummary(
            new[] { string.Empty, "First", " ", "Second", "Third" });

        Assert.Equal("First; Second; Third", summary);
    }

    [Fact]
    public void DeterministicCommunityText_UsesFirstThreeDistinctNamesWithOriginalCasing()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var name = DeterministicCommunityText.BuildCommunityName(
            new[]
            {
                Entity("Alice", "group", now),
                Entity("alice", "group", now),
                Entity(" ", "group", now),
                Entity("Bob", "group", now),
                Entity("Dana", "group", now),
                Entity("Erin", "group", now)
            });

        Assert.Equal("Community: Alice, Bob, Dana", name);
    }

    private static EntityNode Entity(string name, string groupId, DateTime createdAt) =>
        new()
        {
            Name = name,
            GroupId = groupId,
            Labels = new List<string> { "Entity" },
            CreatedAt = createdAt,
            Summary = $"{name} summary"
        };

    private static EntityNode Entity(string name, string groupId, DateTime createdAt, string uuid)
    {
        var node = Entity(name, groupId, createdAt);
        node.Uuid = uuid;
        return node;
    }

    private static CommunityNode Community(string uuid, string name, string groupId, DateTime createdAt) =>
        new()
        {
            Uuid = uuid,
            Name = name,
            GroupId = groupId,
            Labels = new List<string> { "Community" },
            CreatedAt = createdAt,
            Summary = $"{name} summary"
        };

    private static CommunityEdge Member(CommunityNode community, EntityNode entity, DateTime createdAt) =>
        new()
        {
            SourceNodeUuid = community.Uuid,
            TargetNodeUuid = entity.Uuid,
            GroupId = community.GroupId,
            CreatedAt = createdAt
        };

    private static EntityEdge Relates(EntityNode source, EntityNode target, string groupId, DateTime createdAt) =>
        new()
        {
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = groupId,
            CreatedAt = createdAt,
            Name = "RELATES_TO",
            Fact = $"{source.Name} relates to {target.Name}"
        };

    private static EntityEdge Relates(
        EntityNode source,
        EntityNode target,
        string groupId,
        DateTime createdAt,
        string uuid)
    {
        var edge = Relates(source, target, groupId, createdAt);
        edge.Uuid = uuid;
        return edge;
    }

    private static CommunityService CreateCommunityService(InMemoryGraphDriver driver) =>
        new(
            () => driver,
            new StaticJsonLlmClient(messages =>
            {
                var system = messages.Count > 0 ? messages[0].Content : string.Empty;
                if (system.Contains("combines summaries", StringComparison.Ordinal))
                {
                    return new JsonObject { ["summary"] = "updated summary" };
                }

                if (system.Contains("describes provided contents", StringComparison.Ordinal))
                {
                    return new JsonObject { ["description"] = "Updated community" };
                }

                return new JsonObject();
            }),
            new HashEmbedder(4),
            NullLogger<Graphiti>.Instance,
            TimeProvider.System,
            () => 1);

    private static string[][] ClusterUuids(IReadOnlyList<List<EntityNode>> clusters) =>
        clusters
            .Select(cluster => cluster.Select(node => node.Uuid).ToArray())
            .ToArray();

    private static JsonArray ReadSummaryPairPayload(Message message)
    {
        const string marker = "Summaries:\n";
        var markerIndex = message.Content.LastIndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, "Expected a Summaries section in the summarize-pair prompt.");
        var summariesLine = message.Content[(markerIndex + marker.Length)..]
            .Split(["\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .First(line => line.Length > 0 && line[0] == '[');
        return JsonNode.Parse(summariesLine)!.AsArray();
    }

    private static string[][] SynchronousLabelPropagationOracle(
        IReadOnlyList<EntityNode> nodes,
        IReadOnlyList<EntityEdge> edges)
    {
        var nodesByUuid = nodes
            .GroupBy(node => node.Uuid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var orderedUuids = nodes
            .GroupBy(node => node.Uuid, StringComparer.Ordinal)
            .Select(group => group.Key)
            .ToArray();
        var projection = orderedUuids.ToDictionary(
            uuid => uuid,
            _ => new Dictionary<string, int>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!projection.ContainsKey(edge.SourceNodeUuid) || !projection.ContainsKey(edge.TargetNodeUuid))
            {
                continue;
            }

            Increment(projection[edge.SourceNodeUuid], edge.TargetNodeUuid);
            Increment(projection[edge.TargetNodeUuid], edge.SourceNodeUuid);
        }

        var communityByUuid = orderedUuids
            .Select((uuid, index) => (uuid, index))
            .ToDictionary(item => item.uuid, item => item.index, StringComparer.Ordinal);
        for (var iteration = 0; iteration < Math.Max(100, orderedUuids.Length * orderedUuids.Length); iteration++)
        {
            var noChange = true;
            var newCommunityByUuid = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var uuid in orderedUuids)
            {
                var currentCommunity = communityByUuid[uuid];
                var candidates = new Dictionary<int, int>();
                foreach (var neighbor in projection[uuid])
                {
                    var community = communityByUuid[neighbor.Key];
                    candidates.TryGetValue(community, out var edgeCount);
                    candidates[community] = edgeCount + neighbor.Value;
                }

                var selected = currentCommunity;
                if (candidates.Count > 0)
                {
                    var best = candidates
                        .OrderByDescending(pair => pair.Value)
                        .ThenByDescending(pair => pair.Key)
                        .First();
                    selected = best.Value > 1 ? best.Key : Math.Max(best.Key, currentCommunity);
                }

                newCommunityByUuid[uuid] = selected;
                noChange &= selected == currentCommunity;
            }

            if (noChange)
            {
                break;
            }

            communityByUuid = newCommunityByUuid;
        }

        return communityByUuid
            .GroupBy(pair => pair.Value)
            .Select(group => group
                .Select(pair => nodesByUuid[pair.Key])
                .Select(node => node.Uuid)
                .ToArray())
            .ToArray();
    }

    private static void Increment(Dictionary<string, int> neighbors, string uuid)
    {
        neighbors.TryGetValue(uuid, out var count);
        neighbors[uuid] = count + 1;
    }

    private sealed class CapturingCommunityLlmClient : LlmClient
    {
        private readonly Lock _gate = new();

        public CapturingCommunityLlmClient()
            : base(config: null, cache: false)
        {
        }

        public Dictionary<string, List<Type?>> ResponseModelsByPrompt { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<IReadOnlyList<Message>>> MessagesByPrompt { get; } = new(StringComparer.Ordinal);

        protected override Task<JsonObject> GenerateResponseCoreAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel,
            StructuredResponseSchema? responseSchema,
            int maxTokens,
            ModelSize modelSize,
            string? promptName,
            CancellationToken cancellationToken)
        {
            if (promptName is not null)
            {
                lock (_gate)
                {
                    if (!ResponseModelsByPrompt.TryGetValue(promptName, out var responseModels))
                    {
                        responseModels = new List<Type?>();
                        ResponseModelsByPrompt[promptName] = responseModels;
                    }

                    responseModels.Add(responseModel);

                    if (!MessagesByPrompt.TryGetValue(promptName, out var promptMessages))
                    {
                        promptMessages = new List<IReadOnlyList<Message>>();
                        MessagesByPrompt[promptName] = promptMessages;
                    }

                    promptMessages.Add(messages.ToArray());
                }
            }

            return Task.FromResult(promptName switch
            {
                "summarize_nodes.summarize_pair" => new JsonObject { ["summary"] = "combined team" },
                "summarize_nodes.summary_description" => new JsonObject { ["description"] = "Team community" },
                _ => new JsonObject()
            });
        }
    }

    private sealed class DelayedPairSummaryLlmClient : LlmClient
    {
        private int _activePairSummaryCalls;
        private int _maxPairSummaryConcurrency;
        private int _pairSummaryCalls;

        public DelayedPairSummaryLlmClient()
            : base(config: null, cache: false)
        {
        }

        public int PairSummaryCalls => Volatile.Read(ref _pairSummaryCalls);
        public int MaxPairSummaryConcurrency => Volatile.Read(ref _maxPairSummaryConcurrency);

        protected override async Task<JsonObject> GenerateResponseCoreAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel,
            StructuredResponseSchema? responseSchema,
            int maxTokens,
            ModelSize modelSize,
            string? promptName,
            CancellationToken cancellationToken)
        {
            if (string.Equals(promptName, "summarize_nodes.summarize_pair", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _pairSummaryCalls);
                var active = Interlocked.Increment(ref _activePairSummaryCalls);
                UpdateMax(ref _maxPairSummaryConcurrency, active);
                try
                {
                    await Task.Delay(75, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Decrement(ref _activePairSummaryCalls);
                }

                return new JsonObject { ["summary"] = "combined summary" };
            }

            if (string.Equals(promptName, "summarize_nodes.summary_description", StringComparison.Ordinal))
            {
                return new JsonObject { ["description"] = "Community summary" };
            }

            return new JsonObject();
        }
    }

    private sealed class DelayedCommunityLlmClient : ILlmClient
    {
        private readonly Lock _gate = new();
        private int _activeCalls;
        private int _maxObservedConcurrency;
        private int _trackedPromptCalls;

        public TokenUsageTracker TokenTracker { get; } = new();
        public int TrackedPromptCalls => Volatile.Read(ref _trackedPromptCalls);
        public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);
        public List<string> CompletedNames { get; } = new();
        public List<Type?> ResponseModels { get; } = new();

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
            if (!string.Equals(promptName, "summarize_nodes.summary_description", StringComparison.Ordinal))
            {
                return new JsonObject();
            }

            var name = ReadCommunityName(messages);
            Interlocked.Increment(ref _trackedPromptCalls);
            var active = Interlocked.Increment(ref _activeCalls);
            UpdateMax(ref _maxObservedConcurrency, active);
            try
            {
                await Task.Delay(DelayFor(name), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }

            lock (_gate)
            {
                CompletedNames.Add(name);
                ResponseModels.Add(responseModel);
            }

            return new JsonObject { ["description"] = $"Community {name}" };
        }

        private static string ReadCommunityName(IReadOnlyList<Message> messages)
        {
            var content = messages.Count == 0 ? string.Empty : messages[^1].Content;
            const string marker = "Summary:\n";
            var markerIndex = content.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0)
            {
                var summaryJson = content[(markerIndex + marker.Length)..].Trim();
                var summary = JsonNode.Parse(summaryJson)?.GetValue<string>() ?? string.Empty;
                var summarySeparator = summary.IndexOf(':', StringComparison.Ordinal);
                return summarySeparator > 0
                    ? summary[..summarySeparator]
                    : summary.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Unknown";
            }

            var separator = content.IndexOf(':', StringComparison.Ordinal);
            return separator > 0
                ? content[..separator]
                : content.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Unknown";
        }

        private static TimeSpan DelayFor(string name) =>
            string.Equals(name, "Alpha", StringComparison.Ordinal)
                ? TimeSpan.FromMilliseconds(150)
                : string.Equals(name, "Gamma", StringComparison.Ordinal)
                    ? TimeSpan.FromMilliseconds(45)
                : TimeSpan.FromMilliseconds(15);
    }

    private sealed class DelayedCommunityEmbedder : EmbedderClient
    {
        private int _activeCalls;
        private int _maxObservedConcurrency;

        public DelayedCommunityEmbedder()
            : base(new EmbedderConfig(embeddingDimension: 2))
        {
        }

        public int MaxObservedConcurrency => Volatile.Read(ref _maxObservedConcurrency);

        public override async Task<IReadOnlyList<float>> CreateAsync(
            string input,
            CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref _activeCalls);
            UpdateMax(ref _maxObservedConcurrency, active);
            try
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }

            return new[] { 1f, 0f };
        }
    }

    private sealed class SteppingTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _first;
        private readonly TimeSpan _step;
        private int _calls = -1;

        public SteppingTimeProvider(DateTimeOffset first, TimeSpan step)
        {
            _first = first;
            _step = step;
        }

        public override DateTimeOffset GetUtcNow()
        {
            var index = Interlocked.Increment(ref _calls);
            return _first.AddTicks(_step.Ticks * index);
        }
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

    private sealed class DelegatingGraphDriver : GraphDriverBase
    {
        private readonly InMemoryGraphDriver _inner;

        public DelegatingGraphDriver(InMemoryGraphDriver inner) : base(GraphProvider.LadybugDb) => _inner = inner;

        public override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            _inner.BuildIndicesAndConstraintsAsync(deleteExisting, cancellationToken);

        public override Task CloseAsync(CancellationToken cancellationToken = default) =>
            _inner.CloseAsync(cancellationToken);

        public override IGraphDriver Clone(string database) => new DelegatingGraphDriver(_inner);

        public override Task<IReadOnlyList<string>> GetEntityGroupIdsAsync(CancellationToken cancellationToken = default) =>
            _inner.GetEntityGroupIdsAsync(cancellationToken);

        public override Task<IReadOnlyList<string>> GetCommunityGroupIdsAsync(CancellationToken cancellationToken = default) =>
            _inner.GetCommunityGroupIdsAsync(cancellationToken);

        public override Task SaveNodeAsync(Node node, CancellationToken cancellationToken = default) =>
            _inner.SaveNodeAsync(node, cancellationToken);

        public override Task SaveEdgeAsync(Edge edge, CancellationToken cancellationToken = default) =>
            _inner.SaveEdgeAsync(edge, cancellationToken);

        public override Task DeleteNodeAsync(string uuid, CancellationToken cancellationToken = default) =>
            _inner.DeleteNodeAsync(uuid, cancellationToken);

        public override Task DeleteNodesByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default) =>
            _inner.DeleteNodesByGroupIdAsync(groupId, batchSize, cancellationToken);

        public override Task DeleteNodesByUuidsAsync(IEnumerable<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default) =>
            _inner.DeleteNodesByUuidsAsync(uuids, batchSize, cancellationToken);

        public override Task DeleteEdgeAsync(string uuid, CancellationToken cancellationToken = default) =>
            _inner.DeleteEdgeAsync(uuid, cancellationToken);

        public override Task DeleteEdgesByUuidsAsync(IEnumerable<string> uuids, CancellationToken cancellationToken = default) =>
            _inner.DeleteEdgesByUuidsAsync(uuids, cancellationToken);

        public override Task ClearDataAsync(IReadOnlyList<string>? groupIds = null, CancellationToken cancellationToken = default) =>
            _inner.ClearDataAsync(groupIds, cancellationToken);

        public override Task<TNode> GetNodeByUuidAsync<TNode>(string uuid, CancellationToken cancellationToken = default) =>
            _inner.GetNodeByUuidAsync<TNode>(uuid, cancellationToken);

        public override Task<IReadOnlyList<TNode>> GetNodesByUuidsAsync<TNode>(
            IEnumerable<string> uuids,
            string? groupId = null,
            CancellationToken cancellationToken = default) =>
            _inner.GetNodesByUuidsAsync<TNode>(uuids, groupId, cancellationToken);

        public override Task<IReadOnlyList<TNode>> GetNodesByGroupIdsAsync<TNode>(
            IEnumerable<string> groupIds,
            int? limit = null,
            string? uuidCursor = null,
            bool withEmbeddings = false,
            CancellationToken cancellationToken = default) =>
            _inner.GetNodesByGroupIdsAsync<TNode>(groupIds, limit, uuidCursor, withEmbeddings, cancellationToken);

        public override Task<T> GetEdgeByUuidAsync<T>(string uuid, CancellationToken cancellationToken = default) =>
            _inner.GetEdgeByUuidAsync<T>(uuid, cancellationToken);

        public override Task<IReadOnlyList<T>> GetEdgesByUuidsAsync<T>(IEnumerable<string> uuids, CancellationToken cancellationToken = default) =>
            _inner.GetEdgesByUuidsAsync<T>(uuids, cancellationToken);

        public override Task<IReadOnlyList<T>> GetEdgesByGroupIdsAsync<T>(
            IEnumerable<string> groupIds,
            int? limit = null,
            string? uuidCursor = null,
            bool withEmbeddings = false,
            CancellationToken cancellationToken = default) =>
            _inner.GetEdgesByGroupIdsAsync<T>(groupIds, limit, uuidCursor, withEmbeddings, cancellationToken);

        public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesBetweenNodesAsync(
            string sourceNodeUuid,
            string targetNodeUuid,
            CancellationToken cancellationToken = default) =>
            _inner.GetEntityEdgesBetweenNodesAsync(sourceNodeUuid, targetNodeUuid, cancellationToken);

        public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesByNodeUuidAsync(
            string nodeUuid,
            CancellationToken cancellationToken = default) =>
            _inner.GetEntityEdgesByNodeUuidAsync(nodeUuid, cancellationToken);

        public override Task<IReadOnlyList<EpisodicNode>> GetEpisodesByEntityNodeUuidAsync(
            string entityNodeUuid,
            CancellationToken cancellationToken = default) =>
            _inner.GetEpisodesByEntityNodeUuidAsync(entityNodeUuid, cancellationToken);

        public override Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(
            DateTime referenceTime,
            int lastN,
            IReadOnlyList<string>? groupIds = null,
            EpisodeType? source = null,
            string? saga = null,
            CancellationToken cancellationToken = default) =>
            _inner.RetrieveEpisodesAsync(referenceTime, lastN, groupIds, source, saga, cancellationToken);

        public override Task<IReadOnlyList<EntityNode>> GetMentionedNodesAsync(
            IReadOnlyList<EpisodicNode> episodes,
            CancellationToken cancellationToken = default) =>
            _inner.GetMentionedNodesAsync(episodes, cancellationToken);

        public override Task<IReadOnlyList<CommunityNode>> GetCommunitiesByNodesAsync(
            IReadOnlyList<EntityNode> nodes,
            CancellationToken cancellationToken = default) =>
            _inner.GetCommunitiesByNodesAsync(nodes, cancellationToken);

        public override Task<SagaNode?> FindSagaByNameAsync(string name, string groupId, CancellationToken cancellationToken = default) =>
            _inner.FindSagaByNameAsync(name, groupId, cancellationToken);

        public override Task<string?> GetSagaPreviousEpisodeUuidAsync(
            string sagaUuid,
            string currentEpisodeUuid,
            CancellationToken cancellationToken = default) =>
            _inner.GetSagaPreviousEpisodeUuidAsync(sagaUuid, currentEpisodeUuid, cancellationToken);

        public override Task<IReadOnlyList<SagaEpisodeContent>> GetSagaEpisodeContentsAsync(
            string sagaUuid,
            DateTime? since = null,
            int limit = 200,
            CancellationToken cancellationToken = default) =>
            _inner.GetSagaEpisodeContentsAsync(sagaUuid, since, limit, cancellationToken);
    }
}
