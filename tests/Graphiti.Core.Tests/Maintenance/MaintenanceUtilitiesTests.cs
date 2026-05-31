namespace Graphiti.Core.Tests.Maintenance;

public class MaintenanceUtilitiesTests
{
    [Fact]
    public void BuildEpisodicEdges_MapsNodesToAttributedEpisodesInInputOrder()
    {
        var createdAt = new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc);
        var alice = new EntityNode { Uuid = "alice", GroupId = "group-a" };
        var bob = new EntityNode { Uuid = "bob", GroupId = "group-b" };

        var edges = MaintenanceUtilities.BuildEpisodicEdges(
            new[] { alice, bob },
            new[] { "episode-0", "episode-1", "episode-2" },
            createdAt,
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["alice"] = new[] { 2, 0, -1, 99 }
            });

        Assert.Equal(
            new[]
            {
                "episode-2->alice:group-a",
                "episode-0->alice:group-a",
                "episode-0->bob:group-b",
                "episode-1->bob:group-b",
                "episode-2->bob:group-b"
            },
            edges.Select(EdgeKey));
        Assert.All(edges, edge => Assert.Equal(createdAt, edge.CreatedAt));
    }

    [Fact]
    public void BuildEpisodicEdges_EmptyMappedIndicesSuppressDefaultEpisodeLinks()
    {
        var createdAt = new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc);
        var node = new EntityNode { Uuid = "node", GroupId = "group" };

        var edges = MaintenanceUtilities.BuildEpisodicEdges(
            new[] { node },
            new[] { "episode-0", "episode-1" },
            createdAt,
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["node"] = Array.Empty<int>()
            });

        Assert.Empty(edges);
    }

    [Fact]
    public void BuildEpisodicEdges_AllInvalidMappedIndicesSuppressDefaultEpisodeLinks()
    {
        var createdAt = new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc);
        var node = new EntityNode { Uuid = "node", GroupId = "group" };

        var edges = MaintenanceUtilities.BuildEpisodicEdges(
            new[] { node },
            new[] { "episode-0", "episode-1" },
            createdAt,
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["node"] = new[] { -1, 99 }
            });

        Assert.Empty(edges);
    }

    [Fact]
    public void BuildEpisodicEdges_SingleEpisodeOverloadUsesEpisodeSourceAndEntityGroup()
    {
        var createdAt = new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc);

        var edge = Assert.Single(MaintenanceUtilities.BuildEpisodicEdges(
            new[] { new EntityNode { Uuid = "entity", GroupId = "entity-group" } },
            "episode",
            createdAt));

        Assert.Equal("episode", edge.SourceNodeUuid);
        Assert.Equal("entity", edge.TargetNodeUuid);
        Assert.Equal("entity-group", edge.GroupId);
        Assert.Equal(createdAt, edge.CreatedAt);
    }

    [Fact]
    public void BuildCommunityEdges_UsesCommunitySourceGroupAndInputOrder()
    {
        var createdAt = new DateTime(2026, 5, 31, 12, 0, 0, DateTimeKind.Utc);
        var community = new CommunityNode { Uuid = "community", GroupId = "community-group" };

        var edges = MaintenanceUtilities.BuildCommunityEdges(
            new[]
            {
                new EntityNode { Uuid = "alice", GroupId = "entity-group-a" },
                new EntityNode { Uuid = "bob", GroupId = "entity-group-b" }
            },
            community,
            createdAt);

        Assert.Equal(
            new[]
            {
                "community->alice:community-group",
                "community->bob:community-group"
            },
            edges.Select(EdgeKey));
        Assert.All(edges, edge => Assert.Equal(createdAt, edge.CreatedAt));
    }

    [Fact]
    public void ResolveEdgePointers_MutatesExistingEntityEdgesWithOneHopUuidMap()
    {
        var first = new EntityEdge
        {
            Uuid = "edge-1",
            SourceNodeUuid = "source-old",
            TargetNodeUuid = "target-old"
        };
        var second = new EntityEdge
        {
            Uuid = "edge-2",
            SourceNodeUuid = "source-unchanged",
            TargetNodeUuid = "target-chain"
        };
        var edges = new List<EntityEdge> { first, second };

        var resolved = MaintenanceUtilities.ResolveEdgePointers(
            edges,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source-old"] = "source-new",
                ["target-old"] = "target-new",
                ["target-chain"] = "target-middle",
                ["target-middle"] = "target-final"
            });

        Assert.Same(edges, resolved);
        Assert.Same(first, resolved[0]);
        Assert.Same(second, resolved[1]);
        Assert.Equal("source-new", first.SourceNodeUuid);
        Assert.Equal("target-new", first.TargetNodeUuid);
        Assert.Equal("source-unchanged", second.SourceNodeUuid);
        Assert.Equal("target-middle", second.TargetNodeUuid);
    }

    [Fact]
    public void ResolveEdgePointers_RewritesEpisodicAndCommunityEdges()
    {
        var mention = new EpisodicEdge
        {
            Uuid = "mention",
            SourceNodeUuid = "episode",
            TargetNodeUuid = "entity-old",
            GroupId = "mention-group"
        };
        var member = new CommunityEdge
        {
            Uuid = "member",
            SourceNodeUuid = "community-old",
            TargetNodeUuid = "entity-old",
            GroupId = "community-group"
        };

        var resolvedMentions = MaintenanceUtilities.ResolveEdgePointers(
            new[] { mention },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["entity-old"] = "entity-new"
            });
        var resolvedMembers = MaintenanceUtilities.ResolveEdgePointers(
            new[] { member },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["community-old"] = "community-new",
                ["entity-old"] = "entity-new"
            });

        Assert.Same(mention, Assert.Single(resolvedMentions));
        Assert.Equal("episode", mention.SourceNodeUuid);
        Assert.Equal("entity-new", mention.TargetNodeUuid);
        Assert.Equal("mention-group", mention.GroupId);
        Assert.Same(member, Assert.Single(resolvedMembers));
        Assert.Equal("community-new", member.SourceNodeUuid);
        Assert.Equal("entity-new", member.TargetNodeUuid);
        Assert.Equal("community-group", member.GroupId);
    }

    private static string EdgeKey(Edge edge) =>
        $"{edge.SourceNodeUuid}->{edge.TargetNodeUuid}:{edge.GroupId}";
}
