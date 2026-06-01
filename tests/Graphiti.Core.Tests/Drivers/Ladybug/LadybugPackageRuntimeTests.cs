using Graphiti.Core.Drivers.Ladybug;
using LadybugDB;
using GraphitiSagaNode = Graphiti.Core.Models.Nodes.SagaNode;

namespace Graphiti.Core.Tests.Drivers.Ladybug;

public class LadybugPackageRuntimeTests
{
    [Fact]
    public async Task PackageRuntime_BuildsSchemaAndRoundTripsScalarSagaThroughInternalDriver()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var createdAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var summarizedAt = createdAt.AddHours(2);
        var summarizedValidAt = createdAt.AddHours(1);
        var saga = new GraphitiSagaNode
        {
            Uuid = "saga-1",
            Name = "checkout",
            GroupId = "tenant",
            CreatedAt = createdAt,
            Summary = "summary",
            FirstEpisodeUuid = "episode-1",
            LastEpisodeUuid = "episode-2",
            LastSummarizedAt = summarizedAt,
            LastSummarizedEpisodeValidAt = summarizedValidAt
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(saga);
        var fetched = await driver.GetNodeByUuidAsync<GraphitiSagaNode>("saga-1");
        Assert.Contains("uuid", executor.LastColumnNames);

        var vectorRows = await executor.QueryAsync(new LadybugStatement(
            "RETURN array_cosine_similarity([1.0, 0.0], [1.0, 0.0]) AS score",
            new Dictionary<string, object?>(StringComparer.Ordinal)));

        Assert.Equal(GraphProvider.Kuzu, driver.Provider);
        Assert.Equal("checkout", fetched.Name);
        Assert.Equal("tenant", fetched.GroupId);
        Assert.Equal("summary", fetched.Summary);
        Assert.Equal("episode-1", fetched.FirstEpisodeUuid);
        Assert.Equal("episode-2", fetched.LastEpisodeUuid);
        Assert.Equal(summarizedAt, fetched.LastSummarizedAt);
        Assert.Equal(summarizedValidAt, fetched.LastSummarizedEpisodeValidAt);
        Assert.Equal(1.0, Assert.IsType<double>(Assert.Single(vectorRows)["score"]), precision: 6);
        Assert.Equal(new[] { "score" }, executor.LastColumnNames);
    }

    [Fact]
    public async Task PackageRuntime_NormalizedStatementsRoundTripEntityEdgeListsAndNulls()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var referenceTime = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        var source = new EntityNode
        {
            Uuid = "entity-source",
            Name = "Alice",
            GroupId = "tenant",
            Labels = ["Person"],
            NameEmbedding = [0.1f, 0.2f],
            Summary = "source summary"
        };
        var target = new EntityNode
        {
            Uuid = "entity-target",
            Name = "Bob",
            GroupId = "tenant",
            Labels = ["Person"],
            NameEmbedding = [0.2f, 0.3f],
            Summary = "target summary"
        };
        var edge = new EntityEdge
        {
            Uuid = "edge-1",
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "tenant",
            Name = "KNOWS",
            Fact = "Alice knows Bob",
            FactEmbedding = [0.3f, 0.4f],
            Episodes = ["episode-1", "episode-2"],
            CreatedAt = referenceTime.AddMinutes(-1),
            ValidAt = referenceTime.AddHours(-1),
            ExpiredAt = null,
            InvalidAt = null,
            ReferenceTime = referenceTime
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(source);
        await driver.SaveNodeAsync(target);
        await driver.SaveEdgeAsync(edge);
        var fetched = Assert.Single(await driver.GetEdgesByGroupIdsAsync<EntityEdge>(
            ["tenant"],
            withEmbeddings: true));

        Assert.Equal(source.Uuid, fetched.SourceNodeUuid);
        Assert.Equal(target.Uuid, fetched.TargetNodeUuid);
        Assert.Equal(new[] { "episode-1", "episode-2" }, fetched.Episodes);
        Assert.Equal(new[] { 0.3f, 0.4f }, fetched.FactEmbedding);
        Assert.Null(fetched.ExpiredAt);
        Assert.Null(fetched.InvalidAt);
        Assert.Equal(referenceTime, fetched.ReferenceTime);
    }

    [Fact]
    public async Task PackageRuntime_NormalizedStatementsRoundTripEpisodeMentionsAndGroupFilters()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var createdAt = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var referenceTime = createdAt.AddHours(1);
        var entity = new EntityNode
        {
            Uuid = "entity-mentioned",
            Name = "Alice",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            NameEmbedding = [0.7f, 0.8f],
            Summary = "mentioned entity"
        };
        var otherEntity = new EntityNode
        {
            Uuid = "entity-other",
            Name = "Mallory",
            GroupId = "other",
            Labels = ["Person"],
            CreatedAt = createdAt,
            NameEmbedding = [0.1f, 0.2f],
            Summary = "other tenant entity"
        };
        var olderEpisode = new EpisodicNode
        {
            Uuid = "episode-older",
            Name = "older",
            GroupId = "tenant",
            CreatedAt = createdAt,
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Alice opened the case.",
            ValidAt = createdAt.AddMinutes(10),
            EntityEdges = ["edge-older"]
        };
        var newerEpisode = new EpisodicNode
        {
            Uuid = "episode-newer",
            Name = "newer",
            GroupId = "tenant",
            CreatedAt = createdAt.AddMinutes(1),
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Alice closed the case.",
            ValidAt = createdAt.AddMinutes(20),
            EntityEdges = ["edge-newer"]
        };
        var filteredOutEpisode = new EpisodicNode
        {
            Uuid = "episode-other",
            Name = "other",
            GroupId = "other",
            CreatedAt = createdAt,
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Other tenant episode.",
            ValidAt = createdAt.AddMinutes(15),
            EntityEdges = ["edge-other"]
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(entity);
        await driver.SaveNodeAsync(otherEntity);
        await driver.SaveNodeAsync(olderEpisode);
        await driver.SaveNodeAsync(newerEpisode);
        await driver.SaveNodeAsync(filteredOutEpisode);
        await driver.SaveEdgeAsync(new EpisodicEdge
        {
            Uuid = "mention-older",
            GroupId = "tenant",
            SourceNodeUuid = olderEpisode.Uuid,
            TargetNodeUuid = entity.Uuid,
            CreatedAt = createdAt
        });
        await driver.SaveEdgeAsync(new EpisodicEdge
        {
            Uuid = "mention-newer",
            GroupId = "tenant",
            SourceNodeUuid = newerEpisode.Uuid,
            TargetNodeUuid = entity.Uuid,
            CreatedAt = createdAt.AddMinutes(1)
        });

        var tenantEntities = await driver.GetNodesByGroupIdsAsync<EntityNode>(
            ["tenant"],
            withEmbeddings: true);
        var retrieved = await driver.RetrieveEpisodesAsync(
            referenceTime,
            2,
            ["tenant"],
            EpisodeType.Message);
        var episodesByEntity = await driver.GetEpisodesByEntityNodeUuidAsync(entity.Uuid);
        var mentionedNodes = await driver.GetMentionedNodesAsync(retrieved);

        var tenantEntity = Assert.Single(tenantEntities);
        Assert.Equal(entity.Uuid, tenantEntity.Uuid);
        Assert.Equal(new[] { "Person", "Entity" }, tenantEntity.Labels);
        Assert.Equal(new[] { 0.7f, 0.8f }, tenantEntity.NameEmbedding);
        Assert.Equal(new[] { olderEpisode.Uuid, newerEpisode.Uuid }, retrieved.Select(episode => episode.Uuid));
        Assert.Equal(new[] { "edge-older" }, retrieved[0].EntityEdges);
        Assert.Equal(new[] { "edge-newer" }, retrieved[1].EntityEdges);
        Assert.Contains(episodesByEntity, episode => episode.Uuid == olderEpisode.Uuid);
        Assert.Contains(episodesByEntity, episode => episode.Uuid == newerEpisode.Uuid);
        Assert.DoesNotContain(episodesByEntity, episode => episode.Uuid == filteredOutEpisode.Uuid);
        Assert.Equal(entity.Uuid, Assert.Single(mentionedNodes).Uuid);
    }

    [Fact]
    public async Task PackageRuntime_SearchExecutorRunsFtsAndVectorStatements()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var search = new LadybugSearchExecutor(executor);
        var createdAt = new DateTime(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc);
        var source = new EntityNode
        {
            Uuid = "entity-search-source",
            Name = "Alice",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            NameEmbedding = [1.0f, 0.0f],
            Summary = "Alice likes graph search"
        };
        var target = new EntityNode
        {
            Uuid = "entity-search-target",
            Name = "Bob",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            NameEmbedding = [0.0f, 1.0f],
            Summary = "Bob manages databases"
        };
        var episode = new EpisodicNode
        {
            Uuid = "episode-search",
            Name = "search episode",
            GroupId = "tenant",
            CreatedAt = createdAt,
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Alice likes graph search.",
            ValidAt = createdAt,
            EntityEdges = ["edge-search"]
        };
        var community = new CommunityNode
        {
            Uuid = "community-search",
            Name = "Graph search community",
            GroupId = "tenant",
            CreatedAt = createdAt,
            NameEmbedding = [0.9f, 0.1f],
            Summary = "People working on graph search"
        };
        var edge = new EntityEdge
        {
            Uuid = "edge-search",
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "tenant",
            Name = "LIKES",
            Fact = "Alice likes graph search",
            FactEmbedding = [1.0f, 0.0f],
            Episodes = [episode.Uuid],
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(source);
        await driver.SaveNodeAsync(target);
        await driver.SaveNodeAsync(episode);
        await driver.SaveNodeAsync(community);
        await driver.SaveEdgeAsync(edge);
        await InstallFtsAndCreateSearchIndexesAsync(executor);

        var nodeVector = await search.SearchEntityNodesByEmbeddingAsync(
            [1.0f, 0.0f],
            new SearchFilters(),
            ["tenant"],
            limit: 5,
            minScore: 0.8f);
        var edgeVector = await search.SearchEntityEdgesByEmbeddingAsync(
            [1.0f, 0.0f],
            new SearchFilters(),
            ["tenant"],
            limit: 5,
            minScore: 0.8f);
        var communityVector = await search.SearchCommunitiesByEmbeddingAsync(
            [1.0f, 0.0f],
            ["tenant"],
            limit: 5,
            minScore: 0.8f);
        var nodeFulltext = await search.SearchEntityNodesFulltextAsync(
            "Alice",
            new SearchFilters(),
            ["tenant"],
            limit: 5);
        var edgeFulltext = await search.SearchEntityEdgesFulltextAsync(
            "graph",
            new SearchFilters(),
            ["tenant"],
            limit: 5);
        var episodeFulltext = await search.SearchEpisodesFulltextAsync(
            "graph",
            new SearchFilters(),
            ["tenant"],
            limit: 5);
        var communityFulltext = await search.SearchCommunitiesFulltextAsync(
            "graph",
            ["tenant"],
            limit: 5);

        var nodeVectorHit = Assert.Single(nodeVector);
        Assert.Equal(source.Uuid, nodeVectorHit.Item.Uuid);
        Assert.True(nodeVectorHit.Score > 0.99f);
        var edgeVectorHit = Assert.Single(edgeVector);
        Assert.Equal(edge.Uuid, edgeVectorHit.Item.Uuid);
        Assert.Equal(createdAt, edgeVectorHit.Item.ReferenceTime);
        Assert.True(edgeVectorHit.Score > 0.99f);
        Assert.Equal(community.Uuid, Assert.Single(communityVector).Item.Uuid);
        Assert.Equal(source.Uuid, Assert.Single(nodeFulltext).Item.Uuid);
        Assert.Equal(edge.Uuid, Assert.Single(edgeFulltext).Item.Uuid);
        Assert.Equal(episode.Uuid, Assert.Single(episodeFulltext).Item.Uuid);
        Assert.Equal(community.Uuid, Assert.Single(communityFulltext).Item.Uuid);
    }

    [Fact]
    public async Task PackageRuntime_SearchExecutorRunsBfsAndRankerStatements()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var search = new LadybugSearchExecutor(executor);
        var createdAt = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var center = new EntityNode
        {
            Uuid = "entity-center",
            Name = "Center",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            NameEmbedding = [1.0f, 0.0f],
            Summary = "center entity"
        };
        var near = new EntityNode
        {
            Uuid = "entity-near",
            Name = "Near",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            NameEmbedding = [0.8f, 0.2f],
            Summary = "near entity"
        };
        var far = new EntityNode
        {
            Uuid = "entity-far",
            Name = "Far",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            NameEmbedding = [0.0f, 1.0f],
            Summary = "far entity"
        };
        var episode = new EpisodicNode
        {
            Uuid = "episode-bfs",
            Name = "bfs episode",
            GroupId = "tenant",
            CreatedAt = createdAt,
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Center starts a traversal.",
            ValidAt = createdAt,
            EntityEdges = ["edge-near", "edge-far"]
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(center);
        await driver.SaveNodeAsync(near);
        await driver.SaveNodeAsync(far);
        await driver.SaveNodeAsync(episode);
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-near",
            SourceNodeUuid = center.Uuid,
            TargetNodeUuid = near.Uuid,
            GroupId = "tenant",
            Name = "LINKS",
            Fact = "Center links to Near",
            FactEmbedding = [1.0f, 0.0f],
            Episodes = [episode.Uuid],
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt
        });
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-far",
            SourceNodeUuid = near.Uuid,
            TargetNodeUuid = far.Uuid,
            GroupId = "tenant",
            Name = "LINKS",
            Fact = "Near links to Far",
            FactEmbedding = [0.5f, 0.5f],
            Episodes = [episode.Uuid],
            CreatedAt = createdAt.AddMinutes(1),
            ValidAt = createdAt.AddMinutes(1),
            ReferenceTime = createdAt.AddMinutes(1)
        });
        await driver.SaveEdgeAsync(new EpisodicEdge
        {
            Uuid = "mention-center",
            SourceNodeUuid = episode.Uuid,
            TargetNodeUuid = center.Uuid,
            GroupId = "tenant",
            CreatedAt = createdAt
        });

        var nodeBfs = await search.SearchEntityNodesBfsAsync(
            [center.Uuid],
            new SearchFilters(),
            maxDepth: 2,
            ["tenant"],
            limit: 5);
        var episodeNodeBfs = await search.SearchEntityNodesBfsAsync(
            [episode.Uuid],
            new SearchFilters(),
            maxDepth: 2,
            ["tenant"],
            limit: 5);
        var edgeBfs = await search.SearchEntityEdgesBfsAsync(
            [center.Uuid],
            new SearchFilters(),
            maxDepth: 2,
            ["tenant"],
            limit: 5);
        var episodeEdgeBfs = await search.SearchEntityEdgesBfsAsync(
            [episode.Uuid],
            new SearchFilters(),
            maxDepth: 2,
            ["tenant"],
            limit: 5);
        var distanceRanks = await search.RankNodeDistanceAsync(
            [far.Uuid, center.Uuid, near.Uuid],
            center.Uuid,
            minScore: 0);
        var mentionRanks = await search.RankNodeEpisodeMentionsAsync(
            [far.Uuid, center.Uuid, near.Uuid],
            minScore: 0);

        Assert.Contains(nodeBfs, hit => hit.Item.Uuid == near.Uuid);
        Assert.Contains(nodeBfs, hit => hit.Item.Uuid == far.Uuid);
        Assert.Contains(episodeNodeBfs, hit => hit.Item.Uuid == center.Uuid);
        Assert.Contains(episodeNodeBfs, hit => hit.Item.Uuid == near.Uuid);
        Assert.Equal("edge-far", Assert.Single(edgeBfs).Item.Uuid);
        Assert.Equal("edge-near", Assert.Single(episodeEdgeBfs).Item.Uuid);
        Assert.Equal(new[] { center.Uuid, near.Uuid, far.Uuid }, distanceRanks.Select(rank => rank.Uuid));
        Assert.Equal(new[] { 10f, 1f, 0f }, distanceRanks.Select(rank => rank.Score));
        Assert.Equal(center.Uuid, mentionRanks[0].Uuid);
        Assert.Equal(1f, mentionRanks[0].Score);
        Assert.Contains(mentionRanks.Skip(1), rank => rank.Uuid == near.Uuid && float.IsPositiveInfinity(rank.Score));
        Assert.Contains(mentionRanks.Skip(1), rank => rank.Uuid == far.Uuid && float.IsPositiveInfinity(rank.Score));
    }

    [Fact]
    public async Task PackageRuntime_GraphDriverRunsUnionDeleteAndClearStatements()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var createdAt = new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        var source = new EntityNode
        {
            Uuid = "entity-delete-source",
            Name = "Delete Source",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            NameEmbedding = [1.0f, 0.0f],
            Summary = "source"
        };
        var target = new EntityNode
        {
            Uuid = "entity-delete-target",
            Name = "Delete Target",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            NameEmbedding = [0.0f, 1.0f],
            Summary = "target"
        };
        var other = new EntityNode
        {
            Uuid = "entity-other-group",
            Name = "Other Group",
            GroupId = "other",
            Labels = ["Person"],
            CreatedAt = createdAt,
            NameEmbedding = [0.4f, 0.6f],
            Summary = "other"
        };
        var episode = new EpisodicNode
        {
            Uuid = "episode-delete",
            Name = "delete episode",
            GroupId = "tenant",
            CreatedAt = createdAt,
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Delete maintenance episode.",
            ValidAt = createdAt,
            EntityEdges = ["edge-delete"]
        };
        var nextEpisode = new EpisodicNode
        {
            Uuid = "episode-next",
            Name = "next episode",
            GroupId = "tenant",
            CreatedAt = createdAt.AddMinutes(1),
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Next episode.",
            ValidAt = createdAt.AddMinutes(1),
            EntityEdges = []
        };
        var community = new CommunityNode
        {
            Uuid = "community-delete",
            Name = "Delete Community",
            GroupId = "tenant",
            CreatedAt = createdAt,
            NameEmbedding = [0.3f, 0.7f],
            Summary = "community"
        };
        var childCommunity = new CommunityNode
        {
            Uuid = "community-child",
            Name = "Child Community",
            GroupId = "tenant",
            CreatedAt = createdAt,
            NameEmbedding = [0.2f, 0.8f],
            Summary = "child"
        };
        var saga = new GraphitiSagaNode
        {
            Uuid = "saga-delete",
            Name = "delete saga",
            GroupId = "tenant",
            CreatedAt = createdAt,
            Summary = "saga",
            FirstEpisodeUuid = episode.Uuid,
            LastEpisodeUuid = nextEpisode.Uuid,
            LastSummarizedAt = createdAt,
            LastSummarizedEpisodeValidAt = createdAt
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(source);
        await driver.SaveNodeAsync(target);
        await driver.SaveNodeAsync(other);
        await driver.SaveNodeAsync(episode);
        await driver.SaveNodeAsync(nextEpisode);
        await driver.SaveNodeAsync(community);
        await driver.SaveNodeAsync(childCommunity);
        await driver.SaveNodeAsync(saga);
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-delete",
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "tenant",
            Name = "KNOWS",
            Fact = "Delete Source knows Delete Target",
            FactEmbedding = [0.5f, 0.5f],
            Episodes = [episode.Uuid],
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt
        });
        await driver.SaveEdgeAsync(new EpisodicEdge
        {
            Uuid = "mention-delete",
            SourceNodeUuid = episode.Uuid,
            TargetNodeUuid = source.Uuid,
            GroupId = "tenant",
            CreatedAt = createdAt
        });
        await driver.SaveEdgeAsync(new CommunityEdge
        {
            Uuid = "member-entity",
            SourceNodeUuid = community.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "tenant",
            CreatedAt = createdAt
        });
        await driver.SaveEdgeAsync(new CommunityEdge
        {
            Uuid = "member-community",
            SourceNodeUuid = community.Uuid,
            TargetNodeUuid = childCommunity.Uuid,
            GroupId = "tenant",
            CreatedAt = createdAt
        });
        await driver.SaveEdgeAsync(new HasEpisodeEdge
        {
            Uuid = "has-episode-delete",
            SourceNodeUuid = saga.Uuid,
            TargetNodeUuid = episode.Uuid,
            GroupId = "tenant",
            CreatedAt = createdAt
        });
        await driver.SaveEdgeAsync(new NextEpisodeEdge
        {
            Uuid = "next-delete",
            SourceNodeUuid = episode.Uuid,
            TargetNodeUuid = nextEpisode.Uuid,
            GroupId = "tenant",
            CreatedAt = createdAt
        });

        var memberships = await driver.GetEdgesByUuidsAsync<CommunityEdge>(
            ["member-entity", "member-community"]);
        var membershipTargets = memberships.Select(edge => edge.TargetNodeUuid).ToHashSet(StringComparer.Ordinal);

        await driver.DeleteEdgesByUuidsAsync([
            "edge-delete",
            "mention-delete",
            "member-entity",
            "member-community",
            "has-episode-delete",
            "next-delete"
        ]);
        var deletedEntityEdges = await driver.GetEdgesByUuidsAsync<EntityEdge>(["edge-delete"]);
        var deletedMentionEdges = await driver.GetEdgesByUuidsAsync<EpisodicEdge>(["mention-delete"]);
        var deletedCommunityEdges = await driver.GetEdgesByUuidsAsync<CommunityEdge>(["member-entity", "member-community"]);
        var deletedHasEpisodeEdges = await driver.GetEdgesByUuidsAsync<HasEpisodeEdge>(["has-episode-delete"]);
        var deletedNextEpisodeEdges = await driver.GetEdgesByUuidsAsync<NextEpisodeEdge>(["next-delete"]);
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-node-delete",
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "tenant",
            Name = "LINKS",
            Fact = "Source links to target before node delete",
            FactEmbedding = [0.6f, 0.4f],
            Episodes = [episode.Uuid],
            CreatedAt = createdAt.AddMinutes(2),
            ValidAt = createdAt.AddMinutes(2),
            ReferenceTime = createdAt.AddMinutes(2)
        });
        await driver.DeleteNodesByUuidsAsync(
            [source.Uuid, episode.Uuid, childCommunity.Uuid, saga.Uuid],
            batchSize: 2);
        var deletedNodeEdge = await driver.GetEdgesByUuidsAsync<EntityEdge>(["edge-node-delete"]);

        await driver.ClearDataAsync(["tenant"]);
        var otherAfterGroupClear = await driver.GetNodeByUuidAsync<EntityNode>(other.Uuid);
        await Assert.ThrowsAsync<NodeNotFoundException>(() => driver.GetNodeByUuidAsync<EntityNode>(target.Uuid));
        await Assert.ThrowsAsync<NodeNotFoundException>(() => driver.GetNodeByUuidAsync<CommunityNode>(community.Uuid));
        await driver.ClearDataAsync();

        Assert.Equal(new[] { childCommunity.Uuid, target.Uuid }, membershipTargets.Order(StringComparer.Ordinal));
        Assert.Empty(deletedEntityEdges);
        Assert.Empty(deletedMentionEdges);
        Assert.Empty(deletedCommunityEdges);
        Assert.Empty(deletedHasEpisodeEdges);
        Assert.Empty(deletedNextEpisodeEdges);
        Assert.Empty(deletedNodeEdge);
        await Assert.ThrowsAsync<NodeNotFoundException>(() => driver.GetNodeByUuidAsync<EntityNode>(source.Uuid));
        await Assert.ThrowsAsync<NodeNotFoundException>(() => driver.GetNodeByUuidAsync<EpisodicNode>(episode.Uuid));
        await Assert.ThrowsAsync<NodeNotFoundException>(() => driver.GetNodeByUuidAsync<CommunityNode>(childCommunity.Uuid));
        await Assert.ThrowsAsync<NodeNotFoundException>(() => driver.GetNodeByUuidAsync<GraphitiSagaNode>(saga.Uuid));
        Assert.Equal(other.Uuid, otherAfterGroupClear.Uuid);
        await Assert.ThrowsAsync<NodeNotFoundException>(() => driver.GetNodeByUuidAsync<EntityNode>(other.Uuid));
    }

    [Fact]
    public void PackageRuntime_DoesNotBindGraphitiListArrayOrNullParametersDirectlyYet()
    {
        using var database = new Database("");
        using var connection = new Connection(database);

        Assert.Throws<NotSupportedException>(() => ExecuteParameterQuery(
            connection,
            new List<string> { "tenant" }));
        Assert.Throws<NotSupportedException>(() => ExecuteParameterQuery(
            connection,
            new[] { "tenant" }));
        Assert.Throws<NotSupportedException>(() => ExecuteParameterQuery(
            connection,
            new[] { 0.1f, 0.2f }));
        Assert.Throws<NotSupportedException>(() => ExecuteParameterQuery(
            connection,
            new object[] { "tenant" }));
        Assert.Throws<ArgumentNullException>(() => ExecuteParameterQuery(
            connection,
            null!));
    }

    [Fact]
    public void PackageRuntime_FtsExtensionLoadingEnablesIndexAndSearchProof()
    {
        using var database = new Database("");
        using var connection = new Connection(database);
        connection.Query("CREATE NODE TABLE IF NOT EXISTS FtNode(uuid STRING PRIMARY KEY, name STRING);").Dispose();
        connection.Query("CREATE (:FtNode {uuid: 'n1', name: 'Alice likes graph search'});").Dispose();

        Assert.ThrowsAny<LadybugException>(() => connection
            .Query("CALL CREATE_FTS_INDEX('FtNode', 'ft_node_name', ['name']);")
            .Dispose());

        connection.Query("INSTALL FTS;").Dispose();
        connection.Query("LOAD EXTENSION FTS;").Dispose();
        connection.Query("CALL CREATE_FTS_INDEX('FtNode', 'ft_node_name', ['name']);").Dispose();
        using var result = connection.Execute(
            """
            CALL QUERY_FTS_INDEX('FtNode', 'ft_node_name', $query, TOP := $limit)
            RETURN node.uuid AS uuid, score AS score;
            """,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["query"] = "Alice",
                ["limit"] = 5
            });

        var row = Assert.Single(result.Rows());
        Assert.Equal("n1", Assert.IsType<string>(row[0]));
        Assert.True(Assert.IsType<double>(row[1]) > 0);
    }

    [Fact]
    public void CoreProject_DoesNotReferenceLadybugPackageBeforeProviderWiring()
    {
        var project = File.ReadAllText(Path.Combine(
            FindCSharpRoot(),
            "src",
            "Graphiti.Core",
            "Graphiti.Core.csproj"));

        Assert.DoesNotContain("LadybugDB", project, StringComparison.Ordinal);
    }

    private static void ExecuteParameterQuery(Connection connection, object value)
    {
        using var result = connection.Execute(
            "RETURN $value AS value",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["value"] = value
            });
    }

    private static async Task InstallFtsAndCreateSearchIndexesAsync(ILadybugQueryExecutor executor)
    {
        await executor.ExecuteAsync(new LadybugStatement(
            "INSTALL FTS;",
            new Dictionary<string, object?>(StringComparer.Ordinal)));
        await executor.ExecuteAsync(new LadybugStatement(
            "LOAD EXTENSION FTS;",
            new Dictionary<string, object?>(StringComparer.Ordinal)));
        foreach (var statement in LadybugSearchStatementBuilder.BuildFulltextIndexStatements())
        {
            await executor.ExecuteAsync(statement);
        }
    }

    private static string FindCSharpRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Graphiti.Core.CSharp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the csharp solution root.");
    }

    private sealed class PackageLadybugExecutor : ILadybugQueryExecutor
    {
        private readonly Database _database = new("");
        private readonly Connection _connection;
        private bool _disposed;

        internal PackageLadybugExecutor()
        {
            _connection = new Connection(_database);
        }

        internal IReadOnlyList<string> LastColumnNames { get; private set; } = Array.Empty<string>();

        public Task ExecuteAsync(LadybugStatement statement, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var result = ExecuteStatement(statement);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryAsync(
            LadybugStatement statement,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var result = ExecuteStatement(statement);
            var columns = result.ColumnNames;
            LastColumnNames = columns;
            var records = new List<IReadOnlyDictionary<string, object?>>((int)result.RowCount);
            foreach (var row in result.Rows())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var record = new Dictionary<string, object?>(columns.Count, StringComparer.Ordinal);
                for (var i = 0; i < columns.Count; i++)
                {
                    record[columns[i]] = row[i];
                }

                records.Add(record);
            }

            return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(records);
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _connection.Dispose();
            _database.Dispose();
            _disposed = true;
            return ValueTask.CompletedTask;
        }

        private QueryResult ExecuteStatement(LadybugStatement statement)
        {
            var normalized = LadybugStatementNormalizer.NormalizeForPackageExecution(statement);
            return normalized.Parameters.Count == 0
                ? _connection.Query(normalized.Query)
                : _connection.Execute(normalized.Query, normalized.Parameters);
        }
    }
}
