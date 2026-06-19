using System.Collections;
using Graphiti.Core;

namespace Graphiti.Core.Tests.Namespaces;

public class NamespaceTests
{
    [Fact]
    public async Task EntityNodeNamespace_ProvidesBulkLookupEmbeddingAndTypedDeletion()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver, embedder: new HashEmbedder(8));
        var createdAt = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        var episode = new EpisodicNode
        {
            Uuid = "episode-1",
            Name = "episode",
            GroupId = "group",
            SourceDescription = "source",
            Content = "Alice met Bob.",
            CreatedAt = createdAt,
            ValidAt = createdAt
        };
        await graphiti.Nodes.Episode.SaveAsync(episode);

        var alice = new EntityNode
        {
            Uuid = "entity-alice",
            Name = "Alice",
            GroupId = "group",
            Labels = new List<string> { "Person" },
            CreatedAt = createdAt,
            NameEmbedding = new List<float> { 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f }
        };
        var bob = new EntityNode
        {
            Uuid = "entity-bob",
            Name = "Bob",
            GroupId = "other",
            Labels = new List<string> { "Person" },
            CreatedAt = createdAt,
            NameEmbedding = new List<float> { 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f }
        };

        await graphiti.Nodes.Entity.SaveBulkAsync(new[] { alice, bob });

        Assert.NotNull(alice.NameEmbedding);
        Assert.NotNull(bob.NameEmbedding);
        var fetched = await graphiti.Nodes.Entity.GetByUuidsAsync(new[] { alice.Uuid, bob.Uuid });
        Assert.Equal(new[] { alice.Uuid, bob.Uuid }, fetched.Select(node => node.Uuid).OrderBy(uuid => uuid));

        var byGroup = await graphiti.Nodes.Entity.GetByGroupIdsAsync(new[] { "group" });
        var grouped = Assert.Single(byGroup);
        Assert.Equal(alice.Uuid, grouped.Uuid);

        var shell = new EntityNode { Uuid = alice.Uuid, Name = "shell", GroupId = "group" };
        await graphiti.Nodes.Entity.LoadEmbeddingsBulkAsync(new[] { shell });
        Assert.Equal(alice.NameEmbedding, shell.NameEmbedding);
        shell.NameEmbedding![0] = 99f;
        var storedAlice = await graphiti.Nodes.Entity.GetByUuidAsync(alice.Uuid);
        Assert.Null(storedAlice.NameEmbedding);
        await graphiti.Nodes.Entity.LoadEmbeddingsAsync(storedAlice);
        Assert.Equal(alice.NameEmbedding, storedAlice.NameEmbedding);

        await graphiti.Nodes.Entity.DeleteByGroupIdAsync("group");

        await Assert.ThrowsAsync<NodeNotFoundException>(() => graphiti.Nodes.Entity.GetByUuidAsync(alice.Uuid));
        Assert.Equal(episode.Uuid, (await graphiti.Nodes.Episode.GetByUuidAsync(episode.Uuid)).Uuid);
        Assert.Equal(bob.Uuid, (await graphiti.Nodes.Entity.GetByUuidAsync(bob.Uuid)).Uuid);
    }

    [Fact]
    public async Task EmbeddingNamespaces_SaveRegeneratesPrefilledEmbeddings()
    {
        var expected = new[] { 0.25f, 0.75f };
        var graphiti = new Graphiti(
            graphDriver: new InMemoryGraphDriver(),
            embedder: new FixedBatchEmbedder(2, new[] { (IReadOnlyList<float>)expected }));
        var entity = new EntityNode
        {
            Uuid = "entity",
            Name = "Alice",
            GroupId = "group",
            NameEmbedding = new List<float> { 9f, 9f }
        };
        var community = new CommunityNode
        {
            Uuid = "community",
            Name = "Community",
            GroupId = "group",
            NameEmbedding = new List<float> { 8f, 8f }
        };
        var edge = new EntityEdge
        {
            Uuid = "edge",
            SourceNodeUuid = "entity",
            TargetNodeUuid = "entity",
            GroupId = "group",
            Name = "KNOWS",
            Fact = "Alice knows Bob.",
            FactEmbedding = new List<float> { 7f, 7f }
        };

        await graphiti.Nodes.Entity.SaveAsync(entity);
        await graphiti.Nodes.Community.SaveAsync(community);
        await graphiti.Edges.Entity.SaveAsync(edge);

        Assert.Equal(expected, entity.NameEmbedding);
        Assert.Equal(expected, community.NameEmbedding);
        Assert.Equal(expected, edge.FactEmbedding);
    }

    [Fact]
    public async Task EmbeddingNamespaces_SaveBulkPreservesSuppliedEmbeddingsWithoutCallingEmbedder()
    {
        var graphiti = new Graphiti(
            graphDriver: new InMemoryGraphDriver(),
            embedder: new ThrowingEmbedder(2));
        var entityWithEmbedding = new EntityNode
        {
            Uuid = "entity-with",
            Name = "Alice",
            GroupId = "group",
            NameEmbedding = new List<float> { 1f, 0f }
        };
        var entityMissingEmbedding = new EntityNode
        {
            Uuid = "entity-missing",
            Name = "Bob",
            GroupId = "group"
        };
        var communityWithEmbedding = new CommunityNode
        {
            Uuid = "community-with",
            Name = "Community A",
            GroupId = "group",
            NameEmbedding = new List<float> { 0f, 1f }
        };
        var communityMissingEmbedding = new CommunityNode
        {
            Uuid = "community-missing",
            Name = "Community B",
            GroupId = "group"
        };
        var edgeWithEmbedding = new EntityEdge
        {
            Uuid = "edge-with",
            SourceNodeUuid = entityWithEmbedding.Uuid,
            TargetNodeUuid = entityMissingEmbedding.Uuid,
            GroupId = "group",
            Name = "KNOWS",
            Fact = "Alice knows Bob.",
            FactEmbedding = new List<float> { 0.5f, 0.5f }
        };
        var edgeMissingEmbedding = new EntityEdge
        {
            Uuid = "edge-missing",
            SourceNodeUuid = entityMissingEmbedding.Uuid,
            TargetNodeUuid = entityWithEmbedding.Uuid,
            GroupId = "group",
            Name = "KNOWS",
            Fact = "Bob knows Alice."
        };

        await graphiti.Nodes.Entity.SaveBulkAsync(new[] { entityWithEmbedding, entityMissingEmbedding });
        await graphiti.Nodes.Community.SaveBulkAsync(new[] { communityWithEmbedding, communityMissingEmbedding });
        await graphiti.Edges.Entity.SaveBulkAsync(new[] { edgeWithEmbedding, edgeMissingEmbedding });

        var storedEntityWithEmbedding = await graphiti.Nodes.Entity.GetByUuidAsync(entityWithEmbedding.Uuid);
        Assert.Null(storedEntityWithEmbedding.NameEmbedding);
        await graphiti.Nodes.Entity.LoadEmbeddingsAsync(storedEntityWithEmbedding);
        Assert.Equal(new List<float> { 1f, 0f }, storedEntityWithEmbedding.NameEmbedding);
        Assert.Null((await graphiti.Nodes.Entity.GetByUuidAsync(entityMissingEmbedding.Uuid)).NameEmbedding);
        Assert.Equal(new List<float> { 0f, 1f }, (await graphiti.Nodes.Community.GetByUuidAsync(communityWithEmbedding.Uuid)).NameEmbedding);
        Assert.Null((await graphiti.Nodes.Community.GetByUuidAsync(communityMissingEmbedding.Uuid)).NameEmbedding);
        var storedEdgeWithEmbedding = await graphiti.Edges.Entity.GetByUuidAsync(edgeWithEmbedding.Uuid);
        Assert.Null(storedEdgeWithEmbedding.FactEmbedding);
        await graphiti.Edges.Entity.LoadEmbeddingsAsync(storedEdgeWithEmbedding);
        Assert.Equal(new List<float> { 0.5f, 0.5f }, storedEdgeWithEmbedding.FactEmbedding);
        Assert.Null((await graphiti.Edges.Entity.GetByUuidAsync(edgeMissingEmbedding.Uuid)).FactEmbedding);
    }

    [Fact]
    public async Task NodeNamespaces_AllowNonPositiveDeleteBatchSizes()
    {
        var graphiti = new Graphiti(graphDriver: new InMemoryGraphDriver());
        var entity = new EntityNode { Name = "Alice", GroupId = "tenant" };
        var episode = new EpisodicNode
        {
            Name = "episode",
            GroupId = "tenant",
            Source = EpisodeType.Message,
            Content = "Alice",
            ValidAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        await graphiti.Nodes.Entity.SaveAsync(entity);
        await graphiti.Nodes.Episode.SaveAsync(episode);

        await graphiti.Nodes.Entity.DeleteByGroupIdAsync("missing", batchSize: 0);
        await graphiti.Nodes.Entity.DeleteByGroupIdAsync("tenant", batchSize: 0);
        await graphiti.Nodes.Episode.DeleteByUuidsAsync(new[] { episode.Uuid }, batchSize: -1);

        await Assert.ThrowsAsync<NodeNotFoundException>(() =>
            graphiti.Nodes.Entity.GetByUuidAsync(entity.Uuid));
        await Assert.ThrowsAsync<NodeNotFoundException>(() =>
            graphiti.Nodes.Episode.GetByUuidAsync(episode.Uuid));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            graphiti.Nodes.Community.DeleteByUuidsAsync(null!));
    }

    [Fact]
    public async Task NodeNamespaces_DeleteAsyncDoesNotCrossSagaBoundary()
    {
        var graphiti = new Graphiti(graphDriver: new InMemoryGraphDriver());
        var entity = new EntityNode { Uuid = "shared-entity", Name = "Alice", GroupId = "group" };
        await graphiti.Nodes.Entity.SaveAsync(entity);

        await graphiti.Nodes.Saga.DeleteAsync(new SagaNode { Uuid = entity.Uuid, Name = "wrong", GroupId = "group" });

        Assert.Equal(entity.Uuid, (await graphiti.Nodes.Entity.GetByUuidAsync(entity.Uuid)).Uuid);
        await Assert.ThrowsAsync<NodeNotFoundException>(() => graphiti.Nodes.Saga.GetByUuidAsync(entity.Uuid));

        var saga = new SagaNode { Uuid = "shared-saga", Name = "launch", GroupId = "group" };
        await graphiti.Nodes.Saga.SaveAsync(saga);

        await graphiti.Nodes.Entity.DeleteAsync(new EntityNode { Uuid = saga.Uuid, Name = "wrong", GroupId = "group" });

        Assert.Equal(saga.Uuid, (await graphiti.Nodes.Saga.GetByUuidAsync(saga.Uuid)).Uuid);
        await Assert.ThrowsAsync<NodeNotFoundException>(() => graphiti.Nodes.Entity.GetByUuidAsync(saga.Uuid));
    }

    [Fact]
    public async Task NodeModel_DeleteAsyncDoesNotCrossSagaBoundary()
    {
        var driver = new InMemoryGraphDriver();
        var entity = new EntityNode { Uuid = "model-entity", Name = "Alice", GroupId = "group" };
        await entity.SaveAsync(driver);

        await new SagaNode { Uuid = entity.Uuid, Name = "wrong", GroupId = "group" }.DeleteAsync(driver);

        Assert.Equal(entity.Uuid, (await EntityNode.GetByUuidAsync(driver, entity.Uuid)).Uuid);
        await Assert.ThrowsAsync<NodeNotFoundException>(() => SagaNode.GetByUuidAsync(driver, entity.Uuid));

        var saga = new SagaNode { Uuid = "model-saga", Name = "launch", GroupId = "group" };
        await saga.SaveAsync(driver);

        await new EntityNode { Uuid = saga.Uuid, Name = "wrong", GroupId = "group" }.DeleteAsync(driver);

        Assert.Equal(saga.Uuid, (await SagaNode.GetByUuidAsync(driver, saga.Uuid)).Uuid);
        await Assert.ThrowsAsync<NodeNotFoundException>(() => EntityNode.GetByUuidAsync(driver, saga.Uuid));
    }

    [Fact]
    public async Task InMemoryNodeStorage_PreservesCrossTypeUuidBoundaries()
    {
        var driver = new InMemoryGraphDriver();
        const string uuid = "shared-node";
        var entity = new EntityNode { Uuid = uuid, Name = "Alice", GroupId = "group" };
        var episode = new EpisodicNode { Uuid = uuid, Name = "episode", GroupId = "group" };
        var community = new CommunityNode { Uuid = uuid, Name = "community", GroupId = "group" };
        var saga = new SagaNode { Uuid = uuid, Name = "saga", GroupId = "group" };

        await entity.SaveAsync(driver);
        await episode.SaveAsync(driver);
        await community.SaveAsync(driver);
        await saga.SaveAsync(driver);

        Assert.Equal("Alice", (await EntityNode.GetByUuidAsync(driver, uuid)).Name);
        Assert.Equal("episode", (await EpisodicNode.GetByUuidAsync(driver, uuid)).Name);
        Assert.Equal("community", (await CommunityNode.GetByUuidAsync(driver, uuid)).Name);
        Assert.Equal("saga", (await SagaNode.GetByUuidAsync(driver, uuid)).Name);
        Assert.Single(await EntityNode.GetByGroupIdsAsync(driver, ["group"]));
        Assert.Single(await EpisodicNode.GetByGroupIdsAsync(driver, ["group"]));
        Assert.Single(await CommunityNode.GetByGroupIdsAsync(driver, ["group"]));
        Assert.Single(await SagaNode.GetByGroupIdsAsync(driver, ["group"]));
    }

    [Fact]
    public async Task NamespaceSaveBulk_AllowsNonPositiveBatchSizes()
    {
        var driver = new DelayedNamespaceSaveDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var entity = new EntityNode { Uuid = "entity", Name = "Entity", GroupId = "group" };
        var community = new CommunityNode { Uuid = "community", Name = "Community", GroupId = "group" };
        var saga = new SagaNode { Uuid = "saga", Name = "Saga", GroupId = "group" };
        var episodes = Enumerable.Range(0, 6)
            .Select(index => new EpisodicNode
            {
                Uuid = $"episode-{index}",
                Name = $"Episode {index}",
                GroupId = "group"
            })
            .ToList();

        await graphiti.Nodes.Entity.SaveBulkAsync(new[] { entity }, batchSize: 0);
        await graphiti.Nodes.Community.SaveBulkAsync(new[] { community }, batchSize: -1);
        await graphiti.Nodes.Saga.SaveBulkAsync(new[] { saga }, batchSize: 0);
        await graphiti.Nodes.Episode.SaveBulkAsync(episodes, batchSize: -1);

        Assert.Equal(9, driver.SavedNodeCount);
        Assert.InRange(driver.MaxConcurrentSaves, 2, episodes.Count);

        driver.ResetMaxConcurrentSaves();
        var entityEdge = new EntityEdge
        {
            Uuid = "entity-edge",
            SourceNodeUuid = entity.Uuid,
            TargetNodeUuid = entity.Uuid,
            GroupId = "group",
            Name = "RELATED_TO",
            Fact = "Entity relates to itself."
        };
        var communityEdge = new CommunityEdge
        {
            Uuid = "community-edge",
            SourceNodeUuid = community.Uuid,
            TargetNodeUuid = entity.Uuid,
            GroupId = "group"
        };
        var hasEpisodeEdge = new HasEpisodeEdge
        {
            Uuid = "has-episode",
            SourceNodeUuid = saga.Uuid,
            TargetNodeUuid = episodes[0].Uuid,
            GroupId = "group"
        };
        var nextEpisodeEdge = new NextEpisodeEdge
        {
            Uuid = "next-episode",
            SourceNodeUuid = episodes[0].Uuid,
            TargetNodeUuid = episodes[1].Uuid,
            GroupId = "group"
        };
        var episodicEdges = Enumerable.Range(0, 5)
            .Select(index => new EpisodicEdge
            {
                Uuid = $"episodic-edge-{index}",
                SourceNodeUuid = episodes[index].Uuid,
                TargetNodeUuid = entity.Uuid,
                GroupId = "group"
            })
            .ToList();

        await graphiti.Edges.Entity.SaveBulkAsync(new[] { entityEdge }, batchSize: 0);
        await graphiti.Edges.Community.SaveBulkAsync(new[] { communityEdge }, batchSize: -1);
        await graphiti.Edges.HasEpisode.SaveBulkAsync(new[] { hasEpisodeEdge }, batchSize: 0);
        await graphiti.Edges.NextEpisode.SaveBulkAsync(new[] { nextEpisodeEdge }, batchSize: -1);
        await graphiti.Edges.Episode.SaveBulkAsync(episodicEdges, batchSize: 0);

        Assert.Equal(9, driver.SavedEdgeCount);
        Assert.InRange(driver.MaxConcurrentSaves, 2, episodicEdges.Count);
    }

    [Fact]
    public async Task CommunityNodeSaveBulk_PreCanceledTokenDoesNotEnumerateNodes()
    {
        var graphiti = new Graphiti(graphDriver: new InMemoryGraphDriver());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            graphiti.Nodes.Community.SaveBulkAsync(
                new ThrowingEnumerable<CommunityNode>(),
                cancellationToken: cts.Token));
    }

    [Fact]
    public async Task NamespaceSaveBulk_UsesBoundedConcurrentSavesForSequentialNamespaces()
    {
        var driver = new DelayedNamespaceSaveDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var episodes = Enumerable.Range(0, 6)
            .Select(index => new EpisodicNode
            {
                Uuid = $"episode-{index}",
                Name = $"episode-{index}",
                GroupId = "group"
            })
            .ToList();
        var edges = Enumerable.Range(0, 6)
            .Select(index => new EpisodicEdge
            {
                Uuid = $"edge-{index}",
                SourceNodeUuid = episodes[0].Uuid,
                TargetNodeUuid = $"entity-{index}",
                GroupId = "group"
            })
            .ToList();

        await graphiti.Nodes.Episode.SaveBulkAsync(episodes, batchSize: 3);

        Assert.Equal(episodes.Count, driver.SavedNodeCount);
        Assert.InRange(driver.MaxConcurrentSaves, 2, 3);

        driver.ResetMaxConcurrentSaves();
        await graphiti.Edges.Episode.SaveBulkAsync(edges, batchSize: 2);

        Assert.Equal(edges.Count, driver.SavedEdgeCount);
        Assert.Equal(2, driver.MaxConcurrentSaves);

        driver.ResetMaxConcurrentSaves();
        var cappedEpisodes = Enumerable.Range(0, 12)
            .Select(index => new EpisodicNode
            {
                Uuid = $"capped-episode-{index}",
                Name = $"capped-episode-{index}",
                GroupId = "group"
            })
            .ToList();
        await graphiti.Nodes.Episode.SaveBulkAsync(cappedEpisodes, batchSize: 12);

        Assert.Equal(8, driver.MaxConcurrentSaves);
    }

    [Fact]
    public async Task EntityNamespaces_SaveBulkUsesPerItemBatchesWithoutEmbeddingBackfill()
    {
        var driver = new DelayedNamespaceSaveDriver();
        var graphiti = new Graphiti(graphDriver: driver, embedder: new HashEmbedder(4));
        var nodes = Enumerable.Range(0, 5)
            .Select(index => new EntityNode
            {
                Uuid = $"entity-{index}",
                Name = $"Entity {index}",
                GroupId = "group"
            })
            .ToList();
        var edges = Enumerable.Range(0, 5)
            .Select(index => new EntityEdge
            {
                Uuid = $"edge-{index}",
                SourceNodeUuid = nodes[index % nodes.Count].Uuid,
                TargetNodeUuid = nodes[(index + 1) % nodes.Count].Uuid,
                GroupId = "group",
                Name = "RELATED_TO",
                Fact = $"Entity {index} relates to Entity {(index + 1) % nodes.Count}"
            })
            .ToList();

        await graphiti.Nodes.Entity.SaveBulkAsync(nodes, batchSize: 2);
        await graphiti.Edges.Entity.SaveBulkAsync(edges, batchSize: 2);

        Assert.Equal(nodes.Count, driver.SavedNodeCount);
        Assert.Equal(edges.Count, driver.SavedEdgeCount);
        Assert.Equal(2, driver.MaxConcurrentSaves);
        Assert.Empty(driver.EntityNodeBulkBatchSizes);
        Assert.Empty(driver.EntityEdgeBulkBatchSizes);
        Assert.All(nodes, node => Assert.Null(node.NameEmbedding));
        Assert.All(edges, edge => Assert.Null(edge.FactEmbedding));
    }

    [Fact]
    public async Task EdgeNamespaces_ValidateDeleteUuidsBeforeLookup()
    {
        var graphiti = new Graphiti(graphDriver: new InMemoryGraphDriver());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            graphiti.Edges.Entity.DeleteByUuidsAsync(null!));
    }

    [Fact]
    public async Task EdgeModel_DeleteByUuidsDoesNotDeleteSagaEdges()
    {
        var driver = new InMemoryGraphDriver();
        var entity = new EntityEdge
        {
            Uuid = "entity-edge",
            SourceNodeUuid = "entity-a",
            TargetNodeUuid = "entity-b",
            GroupId = "group",
            Name = "KNOWS",
            Fact = "Alice knows Bob."
        };
        var mention = new EpisodicEdge
        {
            Uuid = "mention-edge",
            SourceNodeUuid = "episode",
            TargetNodeUuid = "entity-a",
            GroupId = "group"
        };
        var membership = new CommunityEdge
        {
            Uuid = "community-edge",
            SourceNodeUuid = "community",
            TargetNodeUuid = "entity-a",
            GroupId = "group"
        };
        var hasEpisode = new HasEpisodeEdge
        {
            Uuid = "has-episode-edge",
            SourceNodeUuid = "saga",
            TargetNodeUuid = "episode",
            GroupId = "group"
        };
        var nextEpisode = new NextEpisodeEdge
        {
            Uuid = "next-episode-edge",
            SourceNodeUuid = "episode-a",
            TargetNodeUuid = "episode-b",
            GroupId = "group"
        };

        await SaveSharedEdgeEndpointNodesAsync(driver);
        await entity.SaveAsync(driver);
        await mention.SaveAsync(driver);
        await membership.SaveAsync(driver);
        await hasEpisode.SaveAsync(driver);
        await nextEpisode.SaveAsync(driver);

        await Edge.DeleteByUuidsAsync(
            driver,
            new[] { entity.Uuid, mention.Uuid, membership.Uuid, hasEpisode.Uuid, nextEpisode.Uuid });

        await Assert.ThrowsAsync<EdgeNotFoundException>(() => EntityEdge.GetByUuidAsync(driver, entity.Uuid));
        await Assert.ThrowsAsync<EdgeNotFoundException>(() => EpisodicEdge.GetByUuidAsync(driver, mention.Uuid));
        await Assert.ThrowsAsync<EdgeNotFoundException>(() => CommunityEdge.GetByUuidAsync(driver, membership.Uuid));
        Assert.Equal(hasEpisode.Uuid, (await HasEpisodeEdge.GetByUuidAsync(driver, hasEpisode.Uuid)).Uuid);
        Assert.Equal(nextEpisode.Uuid, (await NextEpisodeEdge.GetByUuidAsync(driver, nextEpisode.Uuid)).Uuid);

        await hasEpisode.DeleteAsync(driver);
        await nextEpisode.DeleteAsync(driver);

        await Assert.ThrowsAsync<EdgeNotFoundException>(() => HasEpisodeEdge.GetByUuidAsync(driver, hasEpisode.Uuid));
        await Assert.ThrowsAsync<EdgeNotFoundException>(() => NextEpisodeEdge.GetByUuidAsync(driver, nextEpisode.Uuid));
    }

    [Fact]
    public async Task InMemoryEdgeStorage_PreservesCrossTypeUuidBoundaries()
    {
        var driver = new InMemoryGraphDriver();
        const string uuid = "shared-edge";
        var entity = new EntityEdge
        {
            Uuid = uuid,
            SourceNodeUuid = "entity-a",
            TargetNodeUuid = "entity-b",
            GroupId = "group",
            Name = "KNOWS",
            Fact = "Alice knows Bob."
        };
        var mention = new EpisodicEdge
        {
            Uuid = uuid,
            SourceNodeUuid = "episode",
            TargetNodeUuid = "entity-a",
            GroupId = "group"
        };
        var membership = new CommunityEdge
        {
            Uuid = uuid,
            SourceNodeUuid = "community",
            TargetNodeUuid = "entity-a",
            GroupId = "group"
        };
        var hasEpisode = new HasEpisodeEdge
        {
            Uuid = uuid,
            SourceNodeUuid = "saga",
            TargetNodeUuid = "episode-a",
            GroupId = "group"
        };
        var nextEpisode = new NextEpisodeEdge
        {
            Uuid = uuid,
            SourceNodeUuid = "episode-a",
            TargetNodeUuid = "episode-b",
            GroupId = "group"
        };

        await SaveSharedEdgeEndpointNodesAsync(driver);
        await entity.SaveAsync(driver);
        await mention.SaveAsync(driver);
        await membership.SaveAsync(driver);
        await hasEpisode.SaveAsync(driver);
        await nextEpisode.SaveAsync(driver);

        Assert.Equal("KNOWS", (await EntityEdge.GetByUuidAsync(driver, uuid)).Name);
        Assert.Equal("episode", (await EpisodicEdge.GetByUuidAsync(driver, uuid)).SourceNodeUuid);
        Assert.Equal("community", (await CommunityEdge.GetByUuidAsync(driver, uuid)).SourceNodeUuid);
        Assert.Equal("saga", (await HasEpisodeEdge.GetByUuidAsync(driver, uuid)).SourceNodeUuid);
        Assert.Equal("episode-a", (await NextEpisodeEdge.GetByUuidAsync(driver, uuid)).SourceNodeUuid);
        Assert.Single(await EntityEdge.GetByGroupIdsAsync(driver, ["group"]));
        Assert.Single(await EpisodicEdge.GetByGroupIdsAsync(driver, ["group"]));
        Assert.Single(await CommunityEdge.GetByGroupIdsAsync(driver, ["group"]));
        Assert.Single(await HasEpisodeEdge.GetByGroupIdsAsync(driver, ["group"]));
        Assert.Single(await NextEpisodeEdge.GetByGroupIdsAsync(driver, ["group"]));
    }

    [Fact]
    public async Task EpisodeNamespace_ProvidesMentionLookupAndTemporalRetrieval()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var validAt = new DateTime(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc);
        var episode = new EpisodicNode
        {
            Uuid = "episode-1",
            Name = "episode",
            GroupId = "group",
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Alice met Bob.",
            ValidAt = validAt
        };
        var alice = new EntityNode { Uuid = "entity-alice", Name = "Alice", GroupId = "group" };
        var mention = new EpisodicEdge
        {
            Uuid = "mention-1",
            SourceNodeUuid = episode.Uuid,
            TargetNodeUuid = alice.Uuid,
            GroupId = "group"
        };

        await graphiti.Nodes.Episode.SaveBulkAsync(new[] { episode });
        await graphiti.Nodes.Entity.SaveAsync(alice);
        await graphiti.Edges.Episode.SaveBulkAsync(new[] { mention });

        var mentionedEpisodes = await graphiti.Nodes.Episode.GetByEntityNodeUuidAsync(alice.Uuid);
        Assert.Equal(episode.Uuid, Assert.Single(mentionedEpisodes).Uuid);

        var retrieved = await graphiti.Nodes.Episode.RetrieveEpisodesAsync(
            validAt.AddMinutes(1),
            lastN: 3,
            groupIds: new[] { "group" },
            source: EpisodeType.Message);
        Assert.Equal(episode.Uuid, Assert.Single(retrieved).Uuid);
    }

    [Fact]
    public async Task EdgeNamespaces_ReturnEmptyForAllMissingPluralReads()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver);
        var mention = new EpisodicEdge
        {
            Uuid = "mention-1",
            SourceNodeUuid = "episode-1",
            TargetNodeUuid = "entity-alice",
            GroupId = "group"
        };

        await driver.SaveNodeAsync(new EpisodicNode { Uuid = "episode-1", Name = "episode", GroupId = "group" });
        await driver.SaveNodeAsync(new EntityNode { Uuid = "entity-alice", Name = "Alice", GroupId = "group" });
        await graphiti.Edges.Episode.SaveAsync(mention);

        var modelException = await Assert.ThrowsAsync<EdgeNotFoundException>(() =>
            EpisodicEdge.GetByUuidsAsync(driver, new[] { "missing-1", "missing-2" }));
        Assert.Contains("missing-1", modelException.Message, StringComparison.Ordinal);

        Assert.Empty(await graphiti.Edges.Episode.GetByUuidsAsync(new[] { "missing-1", "missing-2" }));
        var mixed = await graphiti.Edges.Episode.GetByUuidsAsync(new[] { "missing-1", mention.Uuid });
        Assert.Equal(mention.Uuid, Assert.Single(mixed).Uuid);
        Assert.Empty(await graphiti.Edges.Episode.GetByUuidsAsync(Array.Empty<string>()));
        Assert.Empty(await graphiti.Edges.Entity.GetByUuidsAsync(new[] { "missing-1" }));
        Assert.Empty(await graphiti.Edges.Entity.GetByGroupIdsAsync(new[] { "missing-group" }));
        Assert.Empty(await graphiti.Edges.Episode.GetByGroupIdsAsync(new[] { "missing-group" }));
    }

    [Fact]
    public async Task EntityEdgeNamespace_ProvidesRelationshipLookupEmbeddingAndTypedDeletion()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(graphDriver: driver, embedder: new HashEmbedder(8));
        var alice = new EntityNode { Uuid = "entity-alice", Name = "Alice", GroupId = "group" };
        var bob = new EntityNode { Uuid = "entity-bob", Name = "Bob", GroupId = "group" };
        var carol = new EntityNode { Uuid = "entity-carol", Name = "Carol", GroupId = "group" };
        await graphiti.Nodes.Entity.SaveBulkAsync(new[] { alice, bob, carol });

        var knows = new EntityEdge
        {
            Uuid = "rel-1",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "group",
            Name = "KNOWS",
            Fact = "Alice knows Bob.",
            FactEmbedding = new List<float> { 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f }
        };
        var worksWith = new EntityEdge
        {
            Uuid = "rel-2",
            SourceNodeUuid = carol.Uuid,
            TargetNodeUuid = alice.Uuid,
            GroupId = "group",
            Name = "WORKS_WITH",
            Fact = "Carol works with Alice.",
            FactEmbedding = new List<float> { 0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f }
        };
        var mention = new EpisodicEdge
        {
            Uuid = "mention-1",
            SourceNodeUuid = "episode-1",
            TargetNodeUuid = alice.Uuid,
            GroupId = "group"
        };
        await graphiti.Nodes.Episode.SaveAsync(new EpisodicNode
        {
            Uuid = "episode-1",
            Name = "episode",
            GroupId = "group"
        });
        await graphiti.Edges.Episode.SaveAsync(mention);

        await graphiti.Edges.Entity.SaveBulkAsync(new[] { knows, worksWith });

        Assert.NotNull(knows.FactEmbedding);
        var between = await graphiti.Edges.Entity.GetBetweenNodesAsync(alice.Uuid, bob.Uuid);
        Assert.Equal(knows.Uuid, Assert.Single(between).Uuid);
        var byNode = await graphiti.Edges.Entity.GetByNodeUuidAsync(alice.Uuid);
        Assert.Equal(new[] { knows.Uuid, worksWith.Uuid }, byNode.Select(edge => edge.Uuid).OrderBy(uuid => uuid));

        var shell = new EntityEdge { Uuid = knows.Uuid };
        await graphiti.Edges.Entity.LoadEmbeddingsBulkAsync(new[] { shell });
        Assert.Equal(knows.FactEmbedding, shell.FactEmbedding);
        shell.FactEmbedding![0] = 99f;
        var storedKnows = await graphiti.Edges.Entity.GetByUuidAsync(knows.Uuid);
        Assert.Null(storedKnows.FactEmbedding);
        await graphiti.Edges.Entity.LoadEmbeddingsAsync(storedKnows);
        Assert.Equal(knows.FactEmbedding, storedKnows.FactEmbedding);

        await graphiti.Edges.Entity.DeleteByUuidsAsync(new[] { knows.Uuid, mention.Uuid });

        await Assert.ThrowsAsync<EdgeNotFoundException>(() => graphiti.Edges.Entity.GetByUuidAsync(knows.Uuid));
        Assert.Equal(mention.Uuid, (await graphiti.Edges.Episode.GetByUuidAsync(mention.Uuid)).Uuid);
        Assert.Equal(worksWith.Uuid, (await graphiti.Edges.Entity.GetByUuidAsync(worksWith.Uuid)).Uuid);
    }

    private static async Task SaveSharedEdgeEndpointNodesAsync(InMemoryGraphDriver driver)
    {
        await driver.SaveNodeAsync(new EntityNode { Uuid = "entity-a", Name = "Entity A", GroupId = "group" });
        await driver.SaveNodeAsync(new EntityNode { Uuid = "entity-b", Name = "Entity B", GroupId = "group" });
        await driver.SaveNodeAsync(new EpisodicNode { Uuid = "episode", Name = "Episode", GroupId = "group" });
        await driver.SaveNodeAsync(new EpisodicNode { Uuid = "episode-a", Name = "Episode A", GroupId = "group" });
        await driver.SaveNodeAsync(new EpisodicNode { Uuid = "episode-b", Name = "Episode B", GroupId = "group" });
        await driver.SaveNodeAsync(new CommunityNode { Uuid = "community", Name = "Community", GroupId = "group" });
        await driver.SaveNodeAsync(new SagaNode { Uuid = "saga", Name = "Saga", GroupId = "group" });
    }

    [Fact]
    public async Task CommunityNamespace_SaveRejectsInvalidGeneratedEmbeddingBeforeSave()
    {
        var driver = new InMemoryGraphDriver();
        var graphiti = new Graphiti(
            graphDriver: driver,
            embedder: new FixedBatchEmbedder(embeddingDimension: 2, new[] { (IReadOnlyList<float>)new[] { 1f } }));
        var community = new CommunityNode { Uuid = "community", Name = "Community", GroupId = "group" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => graphiti.Nodes.Community.SaveAsync(community));

        Assert.Contains("community node", exception.Message, StringComparison.Ordinal);
        Assert.Null(community.NameEmbedding);
        await Assert.ThrowsAsync<NodeNotFoundException>(() => graphiti.Nodes.Community.GetByUuidAsync(community.Uuid));
    }

    private sealed class FixedBatchEmbedder : EmbedderClient
    {
        private readonly IReadOnlyList<IReadOnlyList<float>> _embeddings;

        public FixedBatchEmbedder(int embeddingDimension, IReadOnlyList<IReadOnlyList<float>> embeddings)
            : base(new EmbedderConfig(embeddingDimension))
        {
            _embeddings = embeddings;
        }

        public override Task<IReadOnlyList<float>> CreateAsync(
            string input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_embeddings[0]);

        public override Task<IReadOnlyList<IReadOnlyList<float>>> CreateBatchAsync(
            IReadOnlyList<string> input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_embeddings);
    }

    private sealed class ThrowingEmbedder : EmbedderClient
    {
        public ThrowingEmbedder(int embeddingDimension)
            : base(new EmbedderConfig(embeddingDimension))
        {
        }

        public override Task<IReadOnlyList<float>> CreateAsync(
            string input,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The namespace bulk path should not call the embedder.");
    }

    private sealed class ThrowingEnumerable<T> : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator() =>
            throw new InvalidOperationException("The sequence should not be enumerated.");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class DelayedNamespaceSaveDriver : GraphDriverBase
    {
        private int _activeSaves;
        private int _maxConcurrentSaves;
        private int _savedNodeCount;
        private int _savedEdgeCount;

        public DelayedNamespaceSaveDriver()
            : base(GraphProvider.InMemory)
        {
        }

        public int MaxConcurrentSaves => Volatile.Read(ref _maxConcurrentSaves);
        public int SavedNodeCount => Volatile.Read(ref _savedNodeCount);
        public int SavedEdgeCount => Volatile.Read(ref _savedEdgeCount);
        public List<int> EntityNodeBulkBatchSizes { get; } = new();
        public List<int> EntityEdgeBulkBatchSizes { get; } = new();

        public void ResetMaxConcurrentSaves() =>
            Interlocked.Exchange(ref _maxConcurrentSaves, 0);

        public override Task SaveBulkAsync(
            IEnumerable<EpisodicNode> episodicNodes,
            IEnumerable<EpisodicEdge> episodicEdges,
            IEnumerable<EntityNode> entityNodes,
            IEnumerable<EntityEdge> entityEdges,
            IEmbedderClient embedder,
            CancellationToken cancellationToken = default)
        {
            var entityNodeList = entityNodes.ToList();
            var entityEdgeList = entityEdges.ToList();
            if (entityNodeList.Count > 0)
            {
                EntityNodeBulkBatchSizes.Add(entityNodeList.Count);
            }

            if (entityEdgeList.Count > 0)
            {
                EntityEdgeBulkBatchSizes.Add(entityEdgeList.Count);
            }

            return base.SaveBulkAsync(
                episodicNodes,
                episodicEdges,
                entityNodeList,
                entityEdgeList,
                embedder,
                cancellationToken);
        }

        public override async Task SaveNodeAsync(Node node, CancellationToken cancellationToken = default)
        {
            await DelaySaveAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _savedNodeCount);
        }

        public override async Task SaveEdgeAsync(Edge edge, CancellationToken cancellationToken = default)
        {
            await DelaySaveAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _savedEdgeCount);
        }

        private async Task DelaySaveAsync(CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _activeSaves);
            UpdateMax(ref _maxConcurrentSaves, active);
            try
            {
                await Task.Delay(40, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _activeSaves);
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

        public override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override IGraphDriver Clone(string database) => throw new NotSupportedException();
        public override Task DeleteNodeAsync(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteNodesByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteNodesByUuidsAsync(IEnumerable<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteEdgeAsync(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task DeleteEdgesByUuidsAsync(IEnumerable<string> uuids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task ClearDataAsync(IReadOnlyList<string>? groupIds = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<TNode> GetNodeByUuidAsync<TNode>(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<TNode>> GetNodesByUuidsAsync<TNode>(IEnumerable<string> uuids, string? groupId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<TNode>> GetNodesByGroupIdsAsync<TNode>(IEnumerable<string> groupIds, int? limit = null, string? uuidCursor = null, bool withEmbeddings = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<T> GetEdgeByUuidAsync<T>(string uuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<T>> GetEdgesByUuidsAsync<T>(IEnumerable<string> uuids, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<T>> GetEdgesByGroupIdsAsync<T>(IEnumerable<string> groupIds, int? limit = null, string? uuidCursor = null, bool withEmbeddings = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesBetweenNodesAsync(string sourceNodeUuid, string targetNodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EntityEdge>> GetEntityEdgesByNodeUuidAsync(string nodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EpisodicNode>> GetEpisodesByEntityNodeUuidAsync(string entityNodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(DateTime referenceTime, int lastN, IReadOnlyList<string>? groupIds = null, EpisodeType? source = null, string? saga = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<EntityNode>> GetMentionedNodesAsync(IReadOnlyList<EpisodicNode> episodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<CommunityNode>> GetCommunitiesByNodesAsync(IReadOnlyList<EntityNode> nodes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<SagaNode?> FindSagaByNameAsync(string name, string groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<string?> GetSagaPreviousEpisodeUuidAsync(string sagaUuid, string currentEpisodeUuid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override Task<IReadOnlyList<SagaEpisodeContent>> GetSagaEpisodeContentsAsync(string sagaUuid, DateTime? since = null, int limit = 200, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
