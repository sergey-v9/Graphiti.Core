using Graphiti.Core;

namespace Graphiti.Core.Tests.Drivers;

public class InMemorySearchGraphDriverTests
{
    [Fact]
    public async Task InMemorySearchDriver_RunsBoundedFulltextVectorAndReturnsClones()
    {
        var driver = new InMemoryGraphDriver();
        var alpha = new EntityNode
        {
            Name = "Alpha",
            Summary = "project lead",
            GroupId = "group",
            Labels = { "Entity" },
            NameEmbedding = new List<float> { 1f, 0f }
        };
        var otherGroup = new EntityNode
        {
            Name = "Alpha",
            Summary = "other group",
            GroupId = "other",
            Labels = { "Entity" },
            NameEmbedding = new List<float> { 1f, 0f }
        };

        await alpha.SaveAsync(driver);
        await otherGroup.SaveAsync(driver);

        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var fulltext = await searchDriver.SearchEntityNodesFulltextAsync(
            "alpha",
            new SearchFilters { NodeLabels = new List<string> { "Entity" } },
            new[] { "group" },
            limit: 10);
        var vector = await searchDriver.SearchEntityNodesByEmbeddingAsync(
            new[] { 1f, 0f },
            new SearchFilters { NodeLabels = new List<string> { "Entity" } },
            new[] { "group" },
            limit: 10,
            minScore: 0.6f);

        var fulltextNode = Assert.Single(fulltext).Item;
        Assert.Equal(alpha.Uuid, fulltextNode.Uuid);
        Assert.Equal(alpha.Uuid, Assert.Single(vector).Item.Uuid);

        fulltextNode.Summary = "mutated";
        var stored = await EntityNode.GetByUuidAsync(driver, alpha.Uuid);
        Assert.Equal("project lead", stored.Summary);
    }

    [Fact]
    public async Task InMemoryUuidReadsOmitEntityAndFactEmbeddingsUntilExplicitlyLoaded()
    {
        var driver = new InMemoryGraphDriver();
        var node = new EntityNode
        {
            Uuid = "entity",
            Name = "Alice",
            Summary = "project lead",
            GroupId = "group",
            Labels = { "Entity" },
            NameEmbedding = new List<float> { 1f, 0f }
        };
        var edge = new EntityEdge
        {
            Uuid = "edge",
            SourceNodeUuid = node.Uuid,
            TargetNodeUuid = "bob",
            GroupId = "group",
            Name = "KNOWS",
            Fact = "Alice knows Bob.",
            FactEmbedding = new List<float> { 0f, 1f }
        };
        await node.SaveAsync(driver);
        await new EntityNode { Uuid = "bob", Name = "Bob", GroupId = "group" }.SaveAsync(driver);
        await edge.SaveAsync(driver);

        var nodeByUuid = await EntityNode.GetByUuidAsync(driver, node.Uuid);
        var nodeByUuids = Assert.Single(await EntityNode.GetByUuidsAsync(driver, new[] { node.Uuid }));
        var edgeByUuid = await EntityEdge.GetByUuidAsync(driver, edge.Uuid);
        var edgeByUuids = Assert.Single(await EntityEdge.GetByUuidsAsync(driver, new[] { edge.Uuid }));

        Assert.Null(nodeByUuid.NameEmbedding);
        Assert.Null(nodeByUuids.NameEmbedding);
        Assert.Null(edgeByUuid.FactEmbedding);
        Assert.Null(edgeByUuids.FactEmbedding);

        await nodeByUuid.LoadNameEmbeddingAsync(driver);
        await edgeByUuid.LoadFactEmbeddingAsync(driver);

        Assert.Equal(node.NameEmbedding, nodeByUuid.NameEmbedding);
        Assert.Equal(edge.FactEmbedding, edgeByUuid.FactEmbedding);
    }

    [Fact]
    public async Task InMemoryVectorSearch_ReturnsStableTopKAndClonesFinalHits()
    {
        var driver = new InMemoryGraphDriver();
        var first = new EntityNode
        {
            Name = "First",
            Summary = "first original",
            GroupId = "group",
            Labels = { "Entity" },
            NameEmbedding = new List<float> { 1f, 0f }
        };
        var second = new EntityNode
        {
            Name = "Second",
            Summary = "second original",
            GroupId = "group",
            Labels = { "Entity" },
            NameEmbedding = new List<float> { 1f, 0f }
        };

        await first.SaveAsync(driver);
        await second.SaveAsync(driver);
        for (var i = 0; i < 120; i++)
        {
            await new EntityNode
            {
                Name = $"Low {i}",
                Summary = "low score",
                GroupId = "group",
                Labels = { "Entity" },
                NameEmbedding = new List<float> { 0f, 1f }
            }.SaveAsync(driver);
        }

        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var hits = await searchDriver.SearchEntityNodesByEmbeddingAsync(
            new[] { 1f, 0f },
            new SearchFilters { NodeLabels = new List<string> { "Entity" } },
            new[] { "group" },
            limit: 2,
            minScore: 0.5f);

        Assert.Equal(new[] { first.Uuid, second.Uuid }, hits.Select(hit => hit.Item.Uuid));

        hits[0].Item.Summary = "mutated";
        hits[0].Item.NameEmbedding![0] = 99f;
        var stored = await EntityNode.GetByUuidAsync(driver, first.Uuid);
        Assert.Equal("first original", stored.Summary);
        await stored.LoadNameEmbeddingAsync(driver);
        Assert.Equal(new List<float> { 1f, 0f }, stored.NameEmbedding);
    }

    [Fact]
    public async Task InMemoryVectorSearch_UsesStrictSimilarityMinimumScore()
    {
        var driver = new InMemoryGraphDriver();
        var boundary = new EntityNode
        {
            Name = "Boundary",
            Summary = "exact threshold",
            GroupId = "group",
            Labels = { "Entity" },
            NameEmbedding = new List<float> { 1f, 0f }
        };

        await boundary.SaveAsync(driver);

        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var hits = await searchDriver.SearchEntityNodesByEmbeddingAsync(
            new[] { 1f, 0f },
            new SearchFilters { NodeLabels = new List<string> { "Entity" } },
            new[] { "group" },
            limit: 10,
            minScore: 1f);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task InMemoryFulltextSearch_ReturnsStableTopKAndClonesFinalHits()
    {
        var driver = new InMemoryGraphDriver();
        var first = new EntityNode
        {
            Name = "Alpha",
            Summary = string.Empty,
            GroupId = "group",
            Labels = { "Entity" }
        };
        var second = new EntityNode
        {
            Name = "Alpha",
            Summary = string.Empty,
            GroupId = "group",
            Labels = { "Entity" }
        };

        await first.SaveAsync(driver);
        await second.SaveAsync(driver);
        for (var i = 0; i < 120; i++)
        {
            await new EntityNode
            {
                Name = $"Low {i}",
                Summary = "no match",
                GroupId = "group",
                Labels = { "Entity" }
            }.SaveAsync(driver);
        }

        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var hits = await searchDriver.SearchEntityNodesFulltextAsync(
            "alpha",
            new SearchFilters { NodeLabels = new List<string> { "Entity" } },
            new[] { "group" },
            limit: 2);

        Assert.Equal(new[] { first.Uuid, second.Uuid }, hits.Select(hit => hit.Item.Uuid));

        hits[0].Item.Summary = "mutated";
        var stored = await EntityNode.GetByUuidAsync(driver, first.Uuid);
        Assert.Equal(string.Empty, stored.Summary);
    }

    [Fact]
    public async Task InMemoryFulltextSearch_UsesBm25CorpusRankingAndReturnsClones()
    {
        var driver = new InMemoryGraphDriver();
        var bothTerms = new EntityNode
        {
            Name = "Alpha Beta",
            Summary = string.Empty,
            GroupId = "group",
            Labels = { "Entity" }
        };
        var alphaOnly = new EntityNode
        {
            Name = "Alpha",
            Summary = "alpha alpha alpha alpha",
            GroupId = "group",
            Labels = { "Entity" }
        };

        await alphaOnly.SaveAsync(driver);
        await bothTerms.SaveAsync(driver);

        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var hits = await searchDriver.SearchEntityNodesFulltextAsync(
            "alpha beta",
            new SearchFilters { NodeLabels = new List<string> { "Entity" } },
            new[] { "group" },
            limit: 2);

        Assert.Equal(new[] { bothTerms.Uuid, alphaOnly.Uuid }, hits.Select(hit => hit.Item.Uuid));
        Assert.True(hits[0].Score > hits[1].Score);

        hits[0].Item.Summary = "mutated";
        var stored = await EntityNode.GetByUuidAsync(driver, bothTerms.Uuid);
        Assert.Equal(string.Empty, stored.Summary);
    }

    [Fact]
    public async Task InMemoryFulltextSearch_UsesDatabaseIndexedTextFields()
    {
        var driver = new InMemoryGraphDriver();
        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var entity = new EntityNode
        {
            Name = "Entity",
            Summary = "ordinary summary",
            GroupId = "tenant-x",
            Labels = { "Entity" },
            CreatedAt = now
        };
        var community = new CommunityNode
        {
            Name = "Community",
            Summary = "summary-only-secret",
            GroupId = "tenant-x",
            CreatedAt = now
        };
        var episode = new EpisodicNode
        {
            Name = "Episode",
            Content = "ordinary body",
            Source = EpisodeType.Json,
            SourceDescription = "support email",
            GroupId = "tenant-x",
            CreatedAt = now,
            ValidAt = now
        };

        foreach (var node in new Node[] { entity, community, episode })
        {
            await node.SaveAsync(driver);
        }

        var edge = new EntityEdge
        {
            SourceNodeUuid = entity.Uuid,
            TargetNodeUuid = entity.Uuid,
            Name = "RELATES_TO",
            Fact = "ordinary fact",
            GroupId = "tenant-x",
            CreatedAt = now
        };
        await edge.SaveAsync(driver);

        Assert.Equal(entity.Uuid, Assert.Single(await searchDriver.SearchEntityNodesFulltextAsync(
            "tenant-x",
            new SearchFilters(),
            groupIds: null,
            limit: 10)).Item.Uuid);
        Assert.Equal(edge.Uuid, Assert.Single(await searchDriver.SearchEntityEdgesFulltextAsync(
            "tenant-x",
            new SearchFilters(),
            groupIds: null,
            limit: 10)).Item.Uuid);
        Assert.Equal(episode.Uuid, Assert.Single(await searchDriver.SearchEpisodesFulltextAsync(
            "json",
            new SearchFilters(),
            groupIds: null,
            limit: 10)).Item.Uuid);
        Assert.Equal(episode.Uuid, Assert.Single(await searchDriver.SearchEpisodesFulltextAsync(
            "email",
            new SearchFilters(),
            groupIds: null,
            limit: 10)).Item.Uuid);
        Assert.Equal(community.Uuid, Assert.Single(await searchDriver.SearchCommunitiesFulltextAsync(
            "tenant-x",
            groupIds: null,
            limit: 10)).Item.Uuid);
        Assert.Empty(await searchDriver.SearchCommunitiesFulltextAsync(
            "summary-only-secret",
            groupIds: null,
            limit: 10));
    }

    [Fact]
    public async Task InMemorySearchDriver_IgnoresPropertyFilters()
    {
        var driver = new InMemoryGraphDriver();
        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var source = Entity("source", now);
        var target = Entity("target", now);
        var edge = Edge(source, target, "apollo canonical fact", now);
        edge.Attributes["fact"] = "attribute fact";
        var otherEdge = Edge(source, target, "apollo alternative fact", now);
        otherEdge.Attributes["fact"] = "other attribute fact";

        await source.SaveAsync(driver);
        await target.SaveAsync(driver);
        await edge.SaveAsync(driver);
        await otherEdge.SaveAsync(driver);

        var hits = await searchDriver.SearchEntityEdgesFulltextAsync(
            "apollo",
            new SearchFilters
            {
                PropertyFilters = new List<PropertyFilter>
                {
                    new("fact", ComparisonOperator.Equals, "no matching attribute")
                }
            },
            new[] { "group" },
            limit: 10);

        Assert.Equal(
            new[] { edge.Uuid, otherEdge.Uuid }.Order(StringComparer.Ordinal),
            hits.Select(hit => hit.Item.Uuid).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task InMemorySearchDriver_ProjectsEdgeEpisodeAndCommunityHitsAsClones()
    {
        var driver = new InMemoryGraphDriver();
        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var source = Entity("source", now);
        var target = Entity("target", now);
        var otherTarget = Entity("other-target", now);
        var edge = Edge(source, target, "apollo vector fact", now);
        edge.FactEmbedding = new List<float> { 1f, 0f };
        var filteredEdge = Edge(source, otherTarget, "apollo vector fact", now);
        filteredEdge.FactEmbedding = new List<float> { 1f, 0f };
        var episode = new EpisodicNode
        {
            Uuid = "episode",
            Name = "episode",
            GroupId = "group",
            Source = EpisodeType.Message,
            SourceDescription = "message",
            Content = "apollo episode content",
            CreatedAt = now,
            ValidAt = now
        };
        var community = new CommunityNode
        {
            Uuid = "community",
            Name = "Apollo community",
            GroupId = "group",
            NameEmbedding = new List<float> { 0f, 1f },
            CreatedAt = now
        };

        foreach (var node in new Node[] { source, target, otherTarget, episode, community })
        {
            await node.SaveAsync(driver);
        }

        await edge.SaveAsync(driver);
        await filteredEdge.SaveAsync(driver);

        var edgeHit = Assert.Single(await searchDriver.SearchEntityEdgesByEmbeddingAsync(
            new[] { 1f, 0f },
            new SearchFilters { EdgeTypes = new List<string> { "RELATES_TO" } },
            new[] { "group" },
            limit: 10,
            minScore: 0.5f,
            sourceNodeUuid: source.Uuid,
            targetNodeUuid: target.Uuid)).Item;
        var episodeHit = Assert.Single(await searchDriver.SearchEpisodesFulltextAsync(
            "apollo",
            new SearchFilters(),
            new[] { "group" },
            limit: 10)).Item;
        var communityHit = Assert.Single(await searchDriver.SearchCommunitiesByEmbeddingAsync(
            new[] { 0f, 1f },
            new[] { "group" },
            limit: 10,
            minScore: 0.5f)).Item;

        Assert.Equal(edge.Uuid, edgeHit.Uuid);
        edgeHit.Fact = "mutated";
        edgeHit.FactEmbedding![0] = 99f;
        episodeHit.Content = "mutated";
        communityHit.Name = "mutated";
        communityHit.NameEmbedding![1] = 99f;

        var storedEdge = await EntityEdge.GetByUuidAsync(driver, edge.Uuid);
        var storedEpisode = await EpisodicNode.GetByUuidAsync(driver, episode.Uuid);
        var storedCommunity = await CommunityNode.GetByUuidAsync(driver, community.Uuid);
        Assert.Equal("apollo vector fact", storedEdge.Fact);
        await storedEdge.LoadFactEmbeddingAsync(driver);
        Assert.Equal(new List<float> { 1f, 0f }, storedEdge.FactEmbedding);
        Assert.Equal("apollo episode content", storedEpisode.Content);
        Assert.Equal("Apollo community", storedCommunity.Name);
        Assert.Equal(new List<float> { 0f, 1f }, storedCommunity.NameEmbedding);
    }

    [Fact]
    public async Task InMemoryVectorSearch_EdgeAndCommunityThresholdsTiesNullsAndDimensions()
    {
        var driver = new InMemoryGraphDriver();
        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var source = Entity("source", now);
        var target = Entity("target", now);
        await source.SaveAsync(driver);
        await target.SaveAsync(driver);

        var firstEdge = Edge(source, target, "first vector edge", now);
        firstEdge.FactEmbedding = new List<float> { 1f, 0f };
        var secondEdge = Edge(source, target, "second vector edge", now);
        secondEdge.FactEmbedding = new List<float> { 1f, 0f };
        var nullEmbeddingEdge = Edge(source, target, "null vector edge", now);
        var mismatchedEdge = Edge(source, target, "mismatched vector edge", now);
        mismatchedEdge.FactEmbedding = new List<float> { 1f, 0f, 0f };
        await firstEdge.SaveAsync(driver);
        await secondEdge.SaveAsync(driver);
        await nullEmbeddingEdge.SaveAsync(driver);

        var firstCommunity = new CommunityNode
        {
            Uuid = "community-a",
            Name = "Community A",
            GroupId = "group",
            NameEmbedding = new List<float> { 0f, 1f }
        };
        var secondCommunity = new CommunityNode
        {
            Uuid = "community-b",
            Name = "Community B",
            GroupId = "group",
            NameEmbedding = new List<float> { 0f, 1f }
        };
        var nullEmbeddingCommunity = new CommunityNode
        {
            Uuid = "community-null",
            Name = "Community Null",
            GroupId = "group"
        };
        var mismatchedCommunity = new CommunityNode
        {
            Uuid = "community-mismatch",
            Name = "Community Mismatch",
            GroupId = "group",
            NameEmbedding = new List<float> { 0f, 1f, 0f }
        };
        await firstCommunity.SaveAsync(driver);
        await secondCommunity.SaveAsync(driver);
        await nullEmbeddingCommunity.SaveAsync(driver);

        var edgeHits = await searchDriver.SearchEntityEdgesByEmbeddingAsync(
            new[] { 1f, 0f },
            new SearchFilters { EdgeTypes = new List<string> { "RELATES_TO" } },
            new[] { "group" },
            limit: 10,
            minScore: 0.5f);
        var communityHits = await searchDriver.SearchCommunitiesByEmbeddingAsync(
            new[] { 0f, 1f },
            new[] { "group" },
            limit: 10,
            minScore: 0.5f);
        var strictEdgeBoundary = await searchDriver.SearchEntityEdgesByEmbeddingAsync(
            new[] { 1f, 0f },
            new SearchFilters(),
            new[] { "group" },
            limit: 10,
            minScore: 1f);
        var strictCommunityBoundary = await searchDriver.SearchCommunitiesByEmbeddingAsync(
            new[] { 0f, 1f },
            new[] { "group" },
            limit: 10,
            minScore: 1f);

        Assert.Equal(new[] { firstEdge.Uuid, secondEdge.Uuid }, edgeHits.Select(hit => hit.Item.Uuid));
        Assert.Equal(new[] { firstCommunity.Uuid, secondCommunity.Uuid }, communityHits.Select(hit => hit.Item.Uuid));
        Assert.Empty(strictEdgeBoundary);
        Assert.Empty(strictCommunityBoundary);

        await mismatchedEdge.SaveAsync(driver);
        await Assert.ThrowsAsync<ArgumentException>(() => searchDriver.SearchEntityEdgesByEmbeddingAsync(
            new[] { 1f, 0f },
            new SearchFilters(),
            new[] { "group" },
            limit: 10,
            minScore: -1f));

        await mismatchedCommunity.SaveAsync(driver);
        await Assert.ThrowsAsync<ArgumentException>(() => searchDriver.SearchCommunitiesByEmbeddingAsync(
            new[] { 0f, 1f },
            new[] { "group" },
            limit: 10,
            minScore: -1f));
    }

    [Fact]
    public async Task InMemorySearchDriver_UsesGraphTraversalAndRankers()
    {
        var driver = new InMemoryGraphDriver();
        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var episode = new EpisodicNode
        {
            Name = "episode",
            GroupId = "group",
            Source = EpisodeType.Message,
            SourceDescription = "message",
            Content = "alpha beta",
            CreatedAt = now,
            ValidAt = now
        };
        var a = Entity("a", now);
        var b = Entity("b", now);
        var c = Entity("c", now);
        var e1 = Edge(a, b, "a to b", now);
        var e2 = Edge(b, c, "b to c", now);
        var mention = new EpisodicEdge
        {
            SourceNodeUuid = episode.Uuid,
            TargetNodeUuid = a.Uuid,
            GroupId = "group",
            CreatedAt = now
        };

        foreach (var node in new Node[] { episode, a, b, c })
        {
            await node.SaveAsync(driver);
        }

        foreach (var edge in new Edge[] { mention, e1, e2 })
        {
            await edge.SaveAsync(driver);
        }

        var bfsEdges = await searchDriver.SearchEntityEdgesBfsAsync(
            new[] { episode.Uuid },
            new SearchFilters { EdgeTypes = new List<string> { "RELATES_TO" } },
            maxDepth: 2,
            new[] { "group" },
            limit: 10);
        var distanceRanks = await searchDriver.RankNodeDistanceAsync(
            new[] { c.Uuid, b.Uuid, a.Uuid },
            a.Uuid);
        var mentionRanks = await searchDriver.RankNodeEpisodeMentionsAsync(
            new[] { b.Uuid, a.Uuid });

        Assert.Equal(new[] { e1.Uuid }, bfsEdges.Select(hit => hit.Item.Uuid));
        Assert.Equal(new[] { a.Uuid, b.Uuid, c.Uuid }, distanceRanks.Select(rank => rank.Uuid));
        Assert.Equal(new[] { 10f, 1f, 0f }, distanceRanks.Select(rank => rank.Score));
        Assert.Equal(new[] { a.Uuid, b.Uuid }, mentionRanks.Select(rank => rank.Uuid));
        Assert.Equal(1, mentionRanks[0].Score);
        Assert.True(float.IsPositiveInfinity(mentionRanks[1].Score));

        bfsEdges[0].Item.Fact = "mutated";
        var stored = await EntityEdge.GetByUuidAsync(driver, e1.Uuid);
        Assert.Equal("a to b", stored.Fact);
    }

    [Fact]
    public async Task InMemoryEdgeBfs_IgnoresEmptyOriginsAndTraversesSourceEdgesInUuidOrder()
    {
        var driver = new InMemoryGraphDriver();
        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var episode = new EpisodicNode
        {
            Uuid = "episode",
            Name = "episode",
            GroupId = "group",
            Source = EpisodeType.Message,
            SourceDescription = "message",
            Content = "alpha",
            CreatedAt = now,
            ValidAt = now
        };
        var alpha = Entity("alpha", now);
        alpha.Uuid = "alpha";
        var beta = Entity("beta", now);
        beta.Uuid = "beta";
        var gamma = Entity("gamma", now);
        gamma.Uuid = "gamma";

        foreach (var node in new Node[] { episode, alpha, beta, gamma })
        {
            await node.SaveAsync(driver);
        }

        await new EpisodicEdge
        {
            Uuid = "mention",
            SourceNodeUuid = episode.Uuid,
            TargetNodeUuid = alpha.Uuid,
            GroupId = "group",
            CreatedAt = now
        }.SaveAsync(driver);

        var laterUuidEdge = Edge(alpha, beta, "alpha to beta", now);
        laterUuidEdge.Uuid = "edge-b";
        var earlierUuidEdge = Edge(alpha, gamma, "alpha to gamma", now);
        earlierUuidEdge.Uuid = "edge-a";
        await laterUuidEdge.SaveAsync(driver);
        await earlierUuidEdge.SaveAsync(driver);

        var hits = await searchDriver.SearchEntityEdgesBfsAsync(
            new[] { string.Empty, "missing-origin", episode.Uuid },
            new SearchFilters { EdgeTypes = new List<string> { "RELATES_TO" } },
            maxDepth: 2,
            groupIds: null,
            limit: 10);

        Assert.Equal(
            new[] { earlierUuidEdge.Uuid, laterUuidEdge.Uuid },
            hits.Select(hit => hit.Item.Uuid));
    }

    [Fact]
    public async Task InMemoryNodeBfs_KeepsResultsInOriginGroupWhenGroupIdsNull()
    {
        var driver = new InMemoryGraphDriver();
        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var episode = new EpisodicNode
        {
            Name = "episode",
            GroupId = "group-a",
            Source = EpisodeType.Message,
            SourceDescription = "message",
            Content = "same other",
            CreatedAt = now,
            ValidAt = now
        };
        var sameGroup = Entity("same", now);
        sameGroup.GroupId = "group-a";
        var otherGroup = Entity("other", now);
        otherGroup.GroupId = "group-b";

        foreach (var node in new Node[] { episode, sameGroup, otherGroup })
        {
            await node.SaveAsync(driver);
        }

        foreach (var target in new[] { sameGroup, otherGroup })
        {
            await new EpisodicEdge
            {
                SourceNodeUuid = episode.Uuid,
                TargetNodeUuid = target.Uuid,
                GroupId = "group-a",
                CreatedAt = now
            }.SaveAsync(driver);
        }

        var bfsNodes = await searchDriver.SearchEntityNodesBfsAsync(
            new[] { episode.Uuid },
            new SearchFilters { NodeLabels = new List<string> { "Entity" } },
            maxDepth: 1,
            groupIds: null,
            limit: 10);

        Assert.Equal(new[] { sameGroup.Uuid }, bfsNodes.Select(hit => hit.Item.Uuid));
    }

    [Fact]
    public async Task InMemoryIndexes_UpdateEpisodeCommunityAndBfsLookupsAfterOverwrite()
    {
        var driver = new InMemoryGraphDriver();
        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var episode = new EpisodicNode
        {
            Uuid = "episode",
            Name = "episode",
            GroupId = "group",
            Content = "alpha beta",
            CreatedAt = now,
            ValidAt = now
        };
        var alpha = Entity("alpha", now);
        alpha.Uuid = "alpha";
        var beta = Entity("beta", now);
        beta.Uuid = "beta";
        var community = new CommunityNode
        {
            Uuid = "community",
            Name = "Community",
            GroupId = "group"
        };
        foreach (var node in new Node[] { episode, alpha, beta, community })
        {
            await node.SaveAsync(driver);
        }

        await new EpisodicEdge
        {
            Uuid = "mention",
            SourceNodeUuid = episode.Uuid,
            TargetNodeUuid = alpha.Uuid,
            GroupId = "group"
        }.SaveAsync(driver);
        await new CommunityEdge
        {
            Uuid = "membership",
            SourceNodeUuid = community.Uuid,
            TargetNodeUuid = alpha.Uuid,
            GroupId = "group"
        }.SaveAsync(driver);
        await Edge(alpha, beta, "alpha to beta", now).SaveAsync(driver);

        await new EpisodicEdge
        {
            Uuid = "mention",
            SourceNodeUuid = episode.Uuid,
            TargetNodeUuid = beta.Uuid,
            GroupId = "group"
        }.SaveAsync(driver);
        await new CommunityEdge
        {
            Uuid = "membership",
            SourceNodeUuid = community.Uuid,
            TargetNodeUuid = beta.Uuid,
            GroupId = "group"
        }.SaveAsync(driver);

        Assert.Empty(await driver.GetEpisodesByEntityNodeUuidAsync(alpha.Uuid));
        Assert.Equal(episode.Uuid, Assert.Single(await driver.GetEpisodesByEntityNodeUuidAsync(beta.Uuid)).Uuid);
        Assert.Equal(beta.Uuid, Assert.Single(await driver.GetMentionedNodesAsync(new[] { episode })).Uuid);
        Assert.Empty(await driver.GetCommunitiesByNodesAsync(new[] { alpha }));
        Assert.Equal(community.Uuid, Assert.Single(await driver.GetCommunitiesByNodesAsync(new[] { beta })).Uuid);

        var bfsNodes = await searchDriver.SearchEntityNodesBfsAsync(
            new[] { episode.Uuid },
            new SearchFilters { NodeLabels = new List<string> { "Entity" } },
            maxDepth: 1,
            new[] { "group" },
            limit: 10);

        Assert.Equal(new[] { beta.Uuid }, bfsNodes.Select(hit => hit.Item.Uuid));
    }

    [Fact]
    public async Task InMemorySearchDriver_SearchesAndRankersAreSafeDuringConcurrentWrites()
    {
        var driver = new InMemoryGraphDriver();
        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var episode = new EpisodicNode
        {
            Name = "episode",
            GroupId = "group",
            Source = EpisodeType.Message,
            SourceDescription = "message",
            Content = "alpha beta",
            CreatedAt = now,
            ValidAt = now
        };
        var a = Entity("a", now);
        var b = Entity("b", now);
        var c = Entity("c", now);

        foreach (var node in new Node[] { episode, a, b, c })
        {
            await node.SaveAsync(driver);
        }

        foreach (var edge in new Edge[]
                 {
                     new EpisodicEdge
                     {
                         SourceNodeUuid = episode.Uuid,
                         TargetNodeUuid = a.Uuid,
                         GroupId = "group",
                         CreatedAt = now
                     },
                     Edge(a, b, "a to b", now),
                     Edge(b, c, "b to c", now)
                 })
        {
            await edge.SaveAsync(driver);
        }

        var writers = Enumerable.Range(0, 4).Select(worker => Task.Run(async () =>
        {
            var source = Entity($"writer-source-{worker}", now);
            await source.SaveAsync(driver);
            for (var i = 0; i < 50; i++)
            {
                var target = Entity($"writer-target-{worker}-{i}", now);
                await target.SaveAsync(driver);
                await Edge(source, target, $"writer edge {worker}-{i}", now).SaveAsync(driver);
            }
        }));
        var searchers = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                await searchDriver.SearchEntityNodesBfsAsync(
                    new[] { episode.Uuid },
                    new SearchFilters { NodeLabels = new List<string> { "Entity" } },
                    maxDepth: 2,
                    new[] { "group" },
                    limit: 10);
                await searchDriver.SearchEntityEdgesBfsAsync(
                    new[] { episode.Uuid },
                    new SearchFilters { EdgeTypes = new List<string> { "RELATES_TO" } },
                    maxDepth: 2,
                    new[] { "group" },
                    limit: 10);
                await searchDriver.RankNodeDistanceAsync(new[] { c.Uuid, b.Uuid, a.Uuid }, a.Uuid);
                await searchDriver.RankNodeEpisodeMentionsAsync(new[] { b.Uuid, a.Uuid });
            }
        }));

        await Task.WhenAll(writers.Concat(searchers));

        var ranks = await searchDriver.RankNodeDistanceAsync(new[] { c.Uuid, b.Uuid, a.Uuid }, a.Uuid);
        Assert.Equal(new[] { a.Uuid, b.Uuid, c.Uuid }, ranks.Select(rank => rank.Uuid));
    }

    [Fact]
    public async Task InMemoryEdgeSearch_NodeLabelFilterRequiresEveryLabelOnBothEndpoints()
    {
        var driver = new InMemoryGraphDriver();
        var searchDriver = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var person = Entity("Alice", now);
        person.Labels.Add("Person");
        person.Labels.Add("Company");
        var company = Entity("Acme", now);
        company.Labels.Add("Person");
        company.Labels.Add("Company");
        var project = Entity("Project", now);
        project.Labels.Add("Person");
        project.Labels.Add("Project");

        foreach (var node in new[] { person, company, project })
        {
            await node.SaveAsync(driver);
        }

        var matching = Edge(person, company, "shared label target", now);
        var filtered = Edge(person, project, "shared label target", now);
        await matching.SaveAsync(driver);
        await filtered.SaveAsync(driver);

        var hits = await searchDriver.SearchEntityEdgesFulltextAsync(
            "shared label target",
            new SearchFilters { NodeLabels = new List<string> { "Person", "Company" } },
            new[] { "group" },
            limit: 10);

        Assert.Equal(new[] { matching.Uuid }, hits.Select(hit => hit.Item.Uuid));
    }

    private static EntityNode Entity(string name, DateTime now) =>
        new()
        {
            Name = name,
            Summary = name,
            GroupId = "group",
            Labels = { "Entity" },
            CreatedAt = now
        };

    private static EntityEdge Edge(EntityNode source, EntityNode target, string fact, DateTime now) =>
        new()
        {
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "group",
            CreatedAt = now,
            Name = "RELATES_TO",
            Fact = fact
        };
}
