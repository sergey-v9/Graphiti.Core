using Graphiti.Core;

namespace Graphiti.Core.Tests.Drivers;

public class InMemoryGraphDriverDeleteTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task DeleteNodesByGroupIdAsync_InvalidBatchSizeDoesNotMutateState(int batchSize)
    {
        var driver = new InMemoryGraphDriver();
        var node = new EntityNode { Name = "Alice", GroupId = "tenant" };
        await driver.SaveNodeAsync(node);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            driver.DeleteNodesByGroupIdAsync("tenant", batchSize));

        var stored = await driver.GetNodeByUuidAsync<EntityNode>(node.Uuid);
        Assert.Equal(node.Uuid, stored.Uuid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task DeleteNodesByUuidsAsync_InvalidBatchSizeDoesNotMutateState(int batchSize)
    {
        var driver = new InMemoryGraphDriver();
        var node = new EntityNode { Name = "Alice", GroupId = "tenant" };
        await driver.SaveNodeAsync(node);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            driver.DeleteNodesByUuidsAsync(new[] { node.Uuid }, batchSize));

        var stored = await driver.GetNodeByUuidAsync<EntityNode>(node.Uuid);
        Assert.Equal(node.Uuid, stored.Uuid);
    }

    [Fact]
    public async Task DeleteNodesByUuidsAsync_RejectsNullUuids()
    {
        var driver = new InMemoryGraphDriver();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            driver.DeleteNodesByUuidsAsync(null!));
    }

    [Fact]
    public async Task DeleteEdgesByUuidsAsync_RejectsNullUuids()
    {
        var driver = new InMemoryGraphDriver();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            driver.DeleteEdgesByUuidsAsync(null!));
    }

    [Fact]
    public async Task DeleteEdgesByUuidsAsync_EmptyInputDoesNotMutateState()
    {
        var driver = new InMemoryGraphDriver();
        await driver.SaveNodeAsync(new EntityNode { Uuid = "source", Name = "Source", GroupId = "tenant" });
        await driver.SaveNodeAsync(new EntityNode { Uuid = "target", Name = "Target", GroupId = "tenant" });
        var edge = new EntityEdge
        {
            Uuid = "edge",
            GroupId = "tenant",
            SourceNodeUuid = "source",
            TargetNodeUuid = "target",
            Name = "RELATES_TO",
            Fact = "source relates to target"
        };
        await driver.SaveEdgeAsync(edge);

        await driver.DeleteEdgesByUuidsAsync(Array.Empty<string>());

        var stored = await driver.GetEdgeByUuidAsync<EntityEdge>(edge.Uuid);
        Assert.Equal(edge.Uuid, stored.Uuid);
    }

    [Theory]
    [MemberData(nameof(DanglingTypedEdges))]
    public async Task SaveEdgeAsync_SkipsEdgesWhenTypedEndpointsAreMissing(Edge edge)
    {
        var driver = new InMemoryGraphDriver();

        await driver.SaveEdgeAsync(edge);

        await AssertTypedEdgeMissingAsync(driver, edge);
    }

    [Fact]
    public async Task SaveEdgeAsync_MissingEndpointOverwriteLeavesExistingEdge()
    {
        var driver = new InMemoryGraphDriver();
        await driver.SaveNodeAsync(new EntityNode { Uuid = "source", Name = "Source", GroupId = "tenant" });
        await driver.SaveNodeAsync(new EntityNode { Uuid = "target", Name = "Target", GroupId = "tenant" });
        var existing = new EntityEdge
        {
            Uuid = "edge",
            GroupId = "tenant",
            SourceNodeUuid = "source",
            TargetNodeUuid = "target",
            Name = "RELATES_TO",
            Fact = "original"
        };
        await driver.SaveEdgeAsync(existing);

        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = existing.Uuid,
            GroupId = "tenant",
            SourceNodeUuid = "source",
            TargetNodeUuid = "missing",
            Name = "RELATES_TO",
            Fact = "replacement"
        });

        var stored = await driver.GetEdgeByUuidAsync<EntityEdge>(existing.Uuid);
        Assert.Equal("original", stored.Fact);
        Assert.Equal("target", stored.TargetNodeUuid);
    }

    [Fact]
    public async Task SaveEdgeAsync_SkipsEdgesWhenEndpointTypesDoNotMatch()
    {
        var driver = new InMemoryGraphDriver();
        await driver.SaveNodeAsync(new EntityNode { Uuid = "entity", Name = "Entity", GroupId = "group" });
        await driver.SaveNodeAsync(new CommunityNode { Uuid = "community", Name = "Community", GroupId = "group" });
        var edge = new EntityEdge
        {
            Uuid = "edge",
            GroupId = "group",
            SourceNodeUuid = "entity",
            TargetNodeUuid = "community",
            Name = "RELATES_TO",
            Fact = "entity relates to community"
        };

        await driver.SaveEdgeAsync(edge);

        await AssertTypedEdgeMissingAsync(driver, edge);
    }

    [Fact]
    public async Task SaveNodeAsync_OverwriteUpdatesGroupAndTypeIndexes()
    {
        var driver = new InMemoryGraphDriver();
        var node = new EntityNode
        {
            Uuid = "shared",
            Name = "Alice",
            GroupId = "tenant-a"
        };
        await driver.SaveNodeAsync(node);

        await driver.SaveNodeAsync(new EntityNode
        {
            Uuid = node.Uuid,
            Name = "Alice",
            GroupId = "tenant-b"
        });

        Assert.Empty(await driver.GetNodesByGroupIdsAsync<EntityNode>(new[] { "tenant-a" }));
        var entities = await driver.GetNodesByGroupIdsAsync<EntityNode>(new[] { "tenant-b" });
        Assert.Equal(node.Uuid, Assert.Single(entities).Uuid);
        Assert.Equal(new[] { "tenant-b" }, await driver.GetEntityGroupIdsAsync());
    }

    [Fact]
    public async Task SaveNodeAsync_RepeatedSaveKeepsSingleIndexEntry()
    {
        var driver = new InMemoryGraphDriver();
        await driver.SaveNodeAsync(new EntityNode
        {
            Uuid = "shared",
            Name = "Alice",
            GroupId = "tenant",
            Summary = "first"
        });
        await driver.SaveNodeAsync(new EntityNode
        {
            Uuid = "shared",
            Name = "Alice",
            GroupId = "tenant",
            Summary = "second"
        });

        var nodes = await driver.GetNodesByGroupIdsAsync<EntityNode>(new[] { "tenant" });

        var node = Assert.Single(nodes);
        Assert.Equal("shared", node.Uuid);
        Assert.Equal("second", node.Summary);
    }

    [Fact]
    public async Task SaveEdgeAsync_OverwriteUpdatesIncidentIndexes()
    {
        var driver = new InMemoryGraphDriver();
        var alpha = new EntityNode { Uuid = "alpha", Name = "Alpha", GroupId = "tenant" };
        var beta = new EntityNode { Uuid = "beta", Name = "Beta", GroupId = "tenant" };
        var gamma = new EntityNode { Uuid = "gamma", Name = "Gamma", GroupId = "tenant" };
        var delta = new EntityNode { Uuid = "delta", Name = "Delta", GroupId = "tenant" };
        foreach (var node in new[] { alpha, beta, gamma, delta })
        {
            await driver.SaveNodeAsync(node);
        }

        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge",
            GroupId = "tenant",
            SourceNodeUuid = alpha.Uuid,
            TargetNodeUuid = beta.Uuid,
            Name = "RELATES_TO",
            Fact = "alpha to beta"
        });
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge",
            GroupId = "tenant",
            SourceNodeUuid = gamma.Uuid,
            TargetNodeUuid = delta.Uuid,
            Name = "RELATES_TO",
            Fact = "gamma to delta"
        });

        Assert.Empty(await driver.GetEntityEdgesByNodeUuidAsync(alpha.Uuid));
        Assert.Empty(await driver.GetEntityEdgesBetweenNodesAsync(alpha.Uuid, beta.Uuid));
        Assert.Equal("edge", Assert.Single(await driver.GetEntityEdgesByNodeUuidAsync(gamma.Uuid)).Uuid);
        Assert.Equal("edge", Assert.Single(await driver.GetEntityEdgesBetweenNodesAsync(gamma.Uuid, delta.Uuid)).Uuid);

        await driver.DeleteNodeAsync(gamma.Uuid);

        await Assert.ThrowsAsync<EdgeNotFoundException>(() =>
            driver.GetEdgeByUuidAsync<EntityEdge>("edge"));
        Assert.Empty(await driver.GetEdgesByGroupIdsAsync<EntityEdge>(new[] { "tenant" }));
    }

    [Fact]
    public async Task SaveEdgeAsync_RepeatedSaveKeepsSingleIndexEntry()
    {
        var driver = new InMemoryGraphDriver();
        await driver.SaveNodeAsync(new EntityNode { Uuid = "source", Name = "Source", GroupId = "tenant" });
        await driver.SaveNodeAsync(new EntityNode { Uuid = "target", Name = "Target", GroupId = "tenant" });
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge",
            GroupId = "tenant",
            SourceNodeUuid = "source",
            TargetNodeUuid = "target",
            Name = "RELATES_TO",
            Fact = "first"
        });
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge",
            GroupId = "tenant",
            SourceNodeUuid = "source",
            TargetNodeUuid = "target",
            Name = "RELATES_TO",
            Fact = "second"
        });

        var edgesByGroup = await driver.GetEdgesByGroupIdsAsync<EntityEdge>(new[] { "tenant" });
        var incidentEdges = await driver.GetEntityEdgesByNodeUuidAsync("source");
        var endpointEdges = await driver.GetEntityEdgesBetweenNodesAsync("source", "target");

        var edge = Assert.Single(edgesByGroup);
        Assert.Equal("edge", edge.Uuid);
        Assert.Equal("second", edge.Fact);
        Assert.Equal("edge", Assert.Single(incidentEdges).Uuid);
        Assert.Equal("edge", Assert.Single(endpointEdges).Uuid);
    }

    [Fact]
    public async Task EntityEdgeLookups_ReturnUuidOrderedEdges()
    {
        var driver = new InMemoryGraphDriver();
        await driver.SaveNodeAsync(new EntityNode { Uuid = "source", Name = "Source", GroupId = "tenant" });
        await driver.SaveNodeAsync(new EntityNode { Uuid = "target", Name = "Target", GroupId = "tenant" });
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-b",
            GroupId = "tenant",
            SourceNodeUuid = "source",
            TargetNodeUuid = "target",
            Name = "RELATES_TO",
            Fact = "second"
        });
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-a",
            GroupId = "tenant",
            SourceNodeUuid = "source",
            TargetNodeUuid = "target",
            Name = "RELATES_TO",
            Fact = "first"
        });

        var incidentEdges = await driver.GetEntityEdgesByNodeUuidAsync("source");
        var endpointEdges = await driver.GetEntityEdgesBetweenNodesAsync("source", "target");

        Assert.Equal(new[] { "edge-a", "edge-b" }, incidentEdges.Select(edge => edge.Uuid));
        Assert.Equal(new[] { "edge-a", "edge-b" }, endpointEdges.Select(edge => edge.Uuid));
    }

    public static TheoryData<Edge> DanglingTypedEdges => new()
    {
        new EntityEdge
        {
            Uuid = "entity-edge",
            GroupId = "group",
            SourceNodeUuid = "entity-a",
            TargetNodeUuid = "entity-b",
            Name = "RELATES_TO",
            Fact = "Alice knows Bob."
        },
        new EpisodicEdge
        {
            Uuid = "mention-edge",
            GroupId = "group",
            SourceNodeUuid = "episode",
            TargetNodeUuid = "entity"
        },
        new CommunityEdge
        {
            Uuid = "community-edge",
            GroupId = "group",
            SourceNodeUuid = "community",
            TargetNodeUuid = "entity"
        },
        new HasEpisodeEdge
        {
            Uuid = "has-episode-edge",
            GroupId = "group",
            SourceNodeUuid = "saga",
            TargetNodeUuid = "episode"
        },
        new NextEpisodeEdge
        {
            Uuid = "next-episode-edge",
            GroupId = "group",
            SourceNodeUuid = "episode-a",
            TargetNodeUuid = "episode-b"
        }
    };

    private static async Task AssertTypedEdgeMissingAsync(InMemoryGraphDriver driver, Edge edge)
    {
        switch (edge)
        {
            case EntityEdge:
                await Assert.ThrowsAsync<EdgeNotFoundException>(() =>
                    driver.GetEdgeByUuidAsync<EntityEdge>(edge.Uuid));
                break;
            case EpisodicEdge:
                await Assert.ThrowsAsync<EdgeNotFoundException>(() =>
                    driver.GetEdgeByUuidAsync<EpisodicEdge>(edge.Uuid));
                break;
            case CommunityEdge:
                await Assert.ThrowsAsync<EdgeNotFoundException>(() =>
                    driver.GetEdgeByUuidAsync<CommunityEdge>(edge.Uuid));
                break;
            case HasEpisodeEdge:
                await Assert.ThrowsAsync<EdgeNotFoundException>(() =>
                    driver.GetEdgeByUuidAsync<HasEpisodeEdge>(edge.Uuid));
                break;
            case NextEpisodeEdge:
                await Assert.ThrowsAsync<EdgeNotFoundException>(() =>
                    driver.GetEdgeByUuidAsync<NextEpisodeEdge>(edge.Uuid));
                break;
        }
    }

    [Fact]
    public async Task DeleteNodesByUuidsAsync_DeletesAllBatchesAndIncidentEdges()
    {
        var driver = new InMemoryGraphDriver();
        var nodes = Enumerable.Range(0, 5)
            .Select(index => new EntityNode
            {
                Uuid = $"node-{index}",
                Name = $"Node {index}",
                GroupId = "tenant"
            })
            .ToList();
        foreach (var node in nodes)
        {
            await driver.SaveNodeAsync(node);
        }

        var edge = new EntityEdge
        {
            Uuid = "edge",
            GroupId = "tenant",
            SourceNodeUuid = nodes[0].Uuid,
            TargetNodeUuid = nodes[1].Uuid,
            Name = "RELATES_TO",
            Fact = "Node 0 relates to Node 1"
        };
        await driver.SaveEdgeAsync(edge);

        await driver.DeleteNodesByUuidsAsync(nodes.Select(node => node.Uuid), batchSize: 2);

        var remainingNodes = await driver.GetNodesByGroupIdsAsync<EntityNode>(new[] { "tenant" });
        var remainingEdges = await driver.GetEdgesByGroupIdsAsync<EntityEdge>(new[] { "tenant" });
        Assert.Empty(remainingNodes);
        Assert.Empty(remainingEdges);
    }
}
