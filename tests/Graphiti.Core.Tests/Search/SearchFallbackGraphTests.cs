namespace Graphiti.Core.Tests.Search;

public class SearchFallbackGraphTests
{
    [Fact]
    public async Task InMemorySnapshotProjection_FiltersTypesAndProjectsEmbeddings()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var entity = new EntityNode
        {
            Uuid = "entity",
            Name = "Entity",
            GroupId = "group",
            Labels = { "Entity" },
            NameEmbedding = new List<float> { 1f, 0f },
            CreatedAt = now
        };
        var episode = new EpisodicNode
        {
            Uuid = "episode",
            Name = "Episode",
            GroupId = "group",
            CreatedAt = now,
            ValidAt = now
        };
        var community = new CommunityNode
        {
            Uuid = "community",
            Name = "Community",
            GroupId = "group",
            NameEmbedding = new List<float> { 0f, 1f },
            CreatedAt = now
        };
        var entityEdge = new EntityEdge
        {
            Uuid = "entity-edge",
            GroupId = "group",
            SourceNodeUuid = entity.Uuid,
            TargetNodeUuid = entity.Uuid,
            Name = "RELATES_TO",
            Fact = "entity relates to entity",
            FactEmbedding = new List<float> { 1f, 1f },
            CreatedAt = now
        };
        var episodicEdge = new EpisodicEdge
        {
            Uuid = "episodic-edge",
            GroupId = "group",
            SourceNodeUuid = episode.Uuid,
            TargetNodeUuid = entity.Uuid,
            CreatedAt = now
        };

        await driver.SaveNodeAsync(entity);
        await driver.SaveNodeAsync(episode);
        await driver.SaveNodeAsync(community);
        await driver.SaveEdgeAsync(entityEdge);
        await driver.SaveEdgeAsync(episodicEdge);

        var entityWithoutEmbedding = Assert.Single(
            await SearchFallbackGraph.GetAllEntityNodesAsync(
                driver,
                groupIds: null,
                withEmbeddings: false,
                CancellationToken.None));
        var entityWithEmbedding = Assert.Single(
            await SearchFallbackGraph.GetAllEntityNodesAsync(
                driver,
                groupIds: null,
                withEmbeddings: true,
                CancellationToken.None));
        var communityWithoutEmbedding = Assert.Single(
            await SearchFallbackGraph.GetAllCommunityNodesAsync(
                driver,
                groupIds: null,
                withEmbeddings: false,
                CancellationToken.None));
        var communityWithEmbedding = Assert.Single(
            await SearchFallbackGraph.GetAllCommunityNodesAsync(
                driver,
                groupIds: null,
                withEmbeddings: true,
                CancellationToken.None));
        var edgeWithoutEmbedding = Assert.Single(
            await SearchFallbackGraph.GetAllEntityEdgesAsync(
                driver,
                groupIds: null,
                withEmbeddings: false,
                CancellationToken.None));
        var edgeWithEmbedding = Assert.Single(
            await SearchFallbackGraph.GetAllEntityEdgesAsync(
                driver,
                groupIds: null,
                withEmbeddings: true,
                CancellationToken.None));
        var fallbackEpisode = Assert.Single(
            await SearchFallbackGraph.GetAllEpisodesAsync(driver, groupIds: null, CancellationToken.None));
        var fallbackEpisodicEdge = Assert.Single(
            await SearchFallbackGraph.GetAllEpisodicEdgesAsync(driver, groupIds: null, CancellationToken.None));

        Assert.Equal(entity.Uuid, entityWithoutEmbedding.Uuid);
        Assert.Null(entityWithoutEmbedding.NameEmbedding);
        Assert.Equal(new[] { 1f, 0f }, entityWithEmbedding.NameEmbedding);
        Assert.Equal(community.Uuid, communityWithoutEmbedding.Uuid);
        Assert.Null(communityWithoutEmbedding.NameEmbedding);
        Assert.Equal(new[] { 0f, 1f }, communityWithEmbedding.NameEmbedding);
        Assert.Equal(entityEdge.Uuid, edgeWithoutEmbedding.Uuid);
        Assert.Null(edgeWithoutEmbedding.FactEmbedding);
        Assert.Equal(new[] { 1f, 1f }, edgeWithEmbedding.FactEmbedding);
        Assert.Equal(episode.Uuid, fallbackEpisode.Uuid);
        Assert.Equal(episodicEdge.Uuid, fallbackEpisodicEdge.Uuid);

        entityWithEmbedding.Name = "mutated";
        entityWithEmbedding.NameEmbedding![0] = 99f;
        communityWithEmbedding.Name = "mutated";
        communityWithEmbedding.NameEmbedding![0] = 99f;
        edgeWithEmbedding.Fact = "mutated";
        edgeWithEmbedding.FactEmbedding![0] = 99f;

        var storedEntity = await EntityNode.GetByUuidAsync(driver, entity.Uuid);
        var storedCommunity = await CommunityNode.GetByUuidAsync(driver, community.Uuid);
        var storedEdge = await EntityEdge.GetByUuidAsync(driver, entityEdge.Uuid);
        Assert.Equal("Entity", storedEntity.Name);
        await storedEntity.LoadNameEmbeddingAsync(driver);
        Assert.Equal(new[] { 1f, 0f }, storedEntity.NameEmbedding);
        Assert.Equal("Community", storedCommunity.Name);
        Assert.Equal(new[] { 0f, 1f }, storedCommunity.NameEmbedding);
        Assert.Equal("entity relates to entity", storedEdge.Fact);
        await storedEdge.LoadFactEmbeddingAsync(driver);
        Assert.Equal(new[] { 1f, 1f }, storedEdge.FactEmbedding);
    }

    [Fact]
    public async Task EdgeEndpointLookup_AcceptsReadOnlyEdgeLists()
    {
        var driver = new InMemoryGraphDriver();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var source = new EntityNode
        {
            Uuid = "source",
            Name = "Source",
            GroupId = "group",
            Labels = { "Entity", "Person" },
            CreatedAt = now
        };
        var target = new EntityNode
        {
            Uuid = "target",
            Name = "Target",
            GroupId = "group",
            Labels = { "Entity", "Person" },
            CreatedAt = now
        };
        var edge = new EntityEdge
        {
            Uuid = "edge",
            GroupId = "group",
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            Name = "RELATES_TO",
            Fact = "source relates to target",
            CreatedAt = now
        };

        await driver.SaveNodeAsync(source);
        await driver.SaveNodeAsync(target);

        var nodesByUuid = await SearchFallbackGraph.LoadEdgeEndpointNodeLookupAsync(
            driver,
            new[] { edge },
            CompiledSearchFilter.Compile(new SearchFilters { NodeLabels = new List<string> { "Person" } }),
            CancellationToken.None);

        Assert.Equal(new[] { source.Uuid, target.Uuid }, nodesByUuid.Keys);
    }
}
