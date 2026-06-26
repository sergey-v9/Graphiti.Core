using System.Globalization;
using Graphiti.Core.Drivers.Ladybug;
using LadybugDB;
using GraphitiSagaNode = Graphiti.Core.Models.Nodes.SagaNode;

namespace Graphiti.Core.Tests.Drivers.Ladybug;

public class LadybugPackageRuntimeTests
{
    private const string LinuxSmokeEnvironmentVariable = "GRAPHITI_RUN_LINUX_LADYBUG_SMOKE";

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
        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(saga);
        var fetched = await driver.GetNodeByUuidAsync<GraphitiSagaNode>("saga-1");
        Assert.Contains("uuid", executor.LastColumnNames);

        var vectorRows = await executor.QueryAsync(new LadybugStatement(
            "RETURN array_cosine_similarity([1.0, 0.0], [1.0, 0.0]) AS score",
            new Dictionary<string, object?>(StringComparer.Ordinal)));

        Assert.Equal(GraphProvider.LadybugDb, driver.Provider);
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
    public async Task PackageRuntime_SaveBulkPersistsGeneratedEmbeddingsAndRelationships()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var createdAt = new DateTime(2026, 2, 2, 3, 4, 5, DateTimeKind.Utc);
        var episode = new EpisodicNode
        {
            Uuid = "bulk-episode",
            Name = "bulk episode",
            GroupId = "tenant",
            CreatedAt = createdAt,
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Alice introduced Bob during the bulk save.",
            ValidAt = createdAt.AddMinutes(1),
            EntityEdges = ["bulk-edge"]
        };
        var source = new EntityNode
        {
            Uuid = "bulk-alice",
            Name = "Bulk Alice",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            Summary = "source saved through direct bulk driver flow"
        };
        var target = new EntityNode
        {
            Uuid = "bulk-bob",
            Name = "Bulk Bob",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            Summary = "target saved through direct bulk driver flow"
        };
        var mention = new EpisodicEdge
        {
            Uuid = "bulk-mention",
            SourceNodeUuid = episode.Uuid,
            TargetNodeUuid = source.Uuid,
            GroupId = "tenant",
            CreatedAt = createdAt
        };
        var fact = new EntityEdge
        {
            Uuid = "bulk-edge",
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "tenant",
            Name = "INTRODUCED",
            Fact = "Bulk Alice introduced Bulk Bob.",
            Episodes = [episode.Uuid],
            CreatedAt = createdAt,
            ValidAt = createdAt.AddMinutes(1),
            ReferenceTime = createdAt.AddMinutes(1)
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveBulkAsync(
            [episode],
            [mention],
            [source, target],
            [fact],
            new HashEmbedder(4));

        var storedEpisode = await driver.GetNodeByUuidAsync<EpisodicNode>(episode.Uuid);
        var storedNodes = await driver.GetNodesByGroupIdsAsync<EntityNode>(
            ["tenant"],
            withEmbeddings: true);
        var storedFact = Assert.Single(await driver.GetEdgesByGroupIdsAsync<EntityEdge>(
            ["tenant"],
            withEmbeddings: true));
        var storedMention = Assert.Single(await driver.GetEdgesByUuidsAsync<EpisodicEdge>(
            [mention.Uuid]));
        var episodesByEntity = await driver.GetEpisodesByEntityNodeUuidAsync(source.Uuid);
        var mentionedNodes = await driver.GetMentionedNodesAsync([episode]);

        Assert.Equal(episode.Uuid, storedEpisode.Uuid);
        Assert.Equal(new[] { fact.Uuid }, storedEpisode.EntityEdges);
        Assert.Equal(new[] { source.Uuid, target.Uuid }, storedNodes.Select(node => node.Uuid).Order(StringComparer.Ordinal));
        Assert.All(storedNodes, node =>
        {
            Assert.NotNull(node.NameEmbedding);
            Assert.Equal(4, node.NameEmbedding.Count);
        });
        Assert.Equal(source.Uuid, storedFact.SourceNodeUuid);
        Assert.Equal(target.Uuid, storedFact.TargetNodeUuid);
        Assert.Equal(new[] { episode.Uuid }, storedFact.Episodes);
        Assert.NotNull(storedFact.FactEmbedding);
        Assert.Equal(4, storedFact.FactEmbedding.Count);
        Assert.Equal(episode.Uuid, storedMention.SourceNodeUuid);
        Assert.Equal(source.Uuid, storedMention.TargetNodeUuid);
        Assert.Equal(episode.Uuid, Assert.Single(episodesByEntity).Uuid);
        Assert.Equal(source.Uuid, Assert.Single(mentionedNodes).Uuid);
    }

    [Fact]
    public async Task PackageRuntime_NamespaceEmbeddingLoadsRestorePersistedVectorsByUuid()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var graphiti = new Graphiti(graphDriver: driver, embedder: new HashEmbedder(4));
        var createdAt = new DateTime(2026, 2, 2, 4, 5, 6, DateTimeKind.Utc);
        var alice = new EntityNode
        {
            Uuid = "embedding-load-alice",
            Name = "Embedding Alice",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            Summary = "source for namespace embedding load",
            NameEmbedding = [1f, 0f, 0f, 0f]
        };
        var bob = new EntityNode
        {
            Uuid = "embedding-load-bob",
            Name = "Embedding Bob",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            Summary = "target for namespace embedding load",
            NameEmbedding = [0f, 1f, 0f, 0f]
        };
        var knows = new EntityEdge
        {
            Uuid = "embedding-load-knows",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "tenant",
            Name = "KNOWS",
            Fact = "Embedding Alice knows Embedding Bob.",
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt,
            FactEmbedding = [0f, 0f, 1f, 0f]
        };
        var helps = new EntityEdge
        {
            Uuid = "embedding-load-helps",
            SourceNodeUuid = bob.Uuid,
            TargetNodeUuid = alice.Uuid,
            GroupId = "tenant",
            Name = "HELPS",
            Fact = "Embedding Bob helps Embedding Alice.",
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt,
            FactEmbedding = [0f, 0f, 0f, 1f]
        };

        await graphiti.BuildIndicesAndConstraintsAsync();
        await graphiti.Nodes.Entity.SaveBulkAsync([alice, bob]);
        await graphiti.Edges.Entity.SaveBulkAsync([knows, helps]);
        var aliceShell = new EntityNode { Uuid = alice.Uuid };
        var bobShell = new EntityNode { Uuid = bob.Uuid };
        var knowsShell = new EntityEdge { Uuid = knows.Uuid };
        var helpsShell = new EntityEdge { Uuid = helps.Uuid };

        await graphiti.Nodes.Entity.LoadEmbeddingsAsync(aliceShell);
        await graphiti.Nodes.Entity.LoadEmbeddingsBulkAsync([bobShell]);
        await graphiti.Edges.Entity.LoadEmbeddingsAsync(knowsShell);
        await graphiti.Edges.Entity.LoadEmbeddingsBulkAsync([helpsShell]);

        Assert.Equal(alice.NameEmbedding, aliceShell.NameEmbedding);
        Assert.Equal(bob.NameEmbedding, bobShell.NameEmbedding);
        Assert.Equal(knows.FactEmbedding, knowsShell.FactEmbedding);
        Assert.Equal(helps.FactEmbedding, helpsShell.FactEmbedding);
        Assert.Equal(4, aliceShell.NameEmbedding?.Count);
        Assert.Equal(4, bobShell.NameEmbedding?.Count);
        Assert.Equal(4, knowsShell.FactEmbedding?.Count);
        Assert.Equal(4, helpsShell.FactEmbedding?.Count);

        aliceShell.NameEmbedding![0] = 99f;
        bobShell.NameEmbedding![0] = 88f;
        knowsShell.FactEmbedding![0] = 77f;
        helpsShell.FactEmbedding![0] = 66f;
        var storedNodes = await driver.GetNodesByGroupIdsAsync<EntityNode>(
            ["tenant"],
            withEmbeddings: true);
        var storedEdges = await driver.GetEdgesByGroupIdsAsync<EntityEdge>(
            ["tenant"],
            withEmbeddings: true);

        Assert.Equal(alice.NameEmbedding, Assert.Single(storedNodes, node => node.Uuid == alice.Uuid).NameEmbedding);
        Assert.Equal(bob.NameEmbedding, Assert.Single(storedNodes, node => node.Uuid == bob.Uuid).NameEmbedding);
        Assert.Equal(knows.FactEmbedding, Assert.Single(storedEdges, edge => edge.Uuid == knows.Uuid).FactEmbedding);
        Assert.Equal(helps.FactEmbedding, Assert.Single(storedEdges, edge => edge.Uuid == helps.Uuid).FactEmbedding);
    }

    [Fact]
    public async Task PackageRuntime_DirectStatementsRoundTripEntityEdgeListsAndNulls()
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
    public async Task PackageRuntime_GraphDriverReturnsEntityEdgesIncidentToEitherEndpoint()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var referenceTime = new DateTime(2026, 2, 4, 5, 6, 7, DateTimeKind.Utc);
        var alice = new EntityNode
        {
            Uuid = "entity-incident-alice",
            Name = "Alice",
            GroupId = "tenant",
            Labels = ["Person"],
            Summary = "incident center"
        };
        var bob = new EntityNode
        {
            Uuid = "entity-incident-bob",
            Name = "Bob",
            GroupId = "tenant",
            Labels = ["Person"],
            Summary = "outgoing target"
        };
        var carol = new EntityNode
        {
            Uuid = "entity-incident-carol",
            Name = "Carol",
            GroupId = "tenant",
            Labels = ["Person"],
            Summary = "incoming source"
        };
        var dave = new EntityNode
        {
            Uuid = "entity-incident-dave",
            Name = "Dave",
            GroupId = "tenant",
            Labels = ["Person"],
            Summary = "unrelated source"
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(alice);
        await driver.SaveNodeAsync(bob);
        await driver.SaveNodeAsync(carol);
        await driver.SaveNodeAsync(dave);
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-incident-outgoing",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "tenant",
            Name = "MENTORS",
            Fact = "Alice mentors Bob",
            CreatedAt = referenceTime,
            ValidAt = referenceTime,
            ReferenceTime = referenceTime
        });
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-incident-incoming",
            SourceNodeUuid = carol.Uuid,
            TargetNodeUuid = alice.Uuid,
            GroupId = "tenant",
            Name = "SUPPORTS",
            Fact = "Carol supports Alice",
            CreatedAt = referenceTime,
            ValidAt = referenceTime,
            ReferenceTime = referenceTime
        });
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-incident-unrelated",
            SourceNodeUuid = dave.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "tenant",
            Name = "KNOWS",
            Fact = "Dave knows Bob",
            CreatedAt = referenceTime,
            ValidAt = referenceTime,
            ReferenceTime = referenceTime
        });

        var incidentEdges = await driver.GetEntityEdgesByNodeUuidAsync(alice.Uuid);

        Assert.Equal(
            new[] { "edge-incident-incoming", "edge-incident-outgoing" },
            incidentEdges.Select(edge => edge.Uuid).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task PackageRuntime_GraphDriverReturnsEntityEdgesBetweenDirectedEndpoints()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var referenceTime = new DateTime(2026, 2, 5, 6, 7, 8, DateTimeKind.Utc);
        var alice = new EntityNode
        {
            Uuid = "entity-between-alice",
            Name = "Alice",
            GroupId = "tenant",
            Labels = ["Person"],
            Summary = "directed source"
        };
        var bob = new EntityNode
        {
            Uuid = "entity-between-bob",
            Name = "Bob",
            GroupId = "tenant",
            Labels = ["Person"],
            Summary = "directed target"
        };
        var carol = new EntityNode
        {
            Uuid = "entity-between-carol",
            Name = "Carol",
            GroupId = "tenant",
            Labels = ["Person"],
            Summary = "unrelated source"
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(alice);
        await driver.SaveNodeAsync(bob);
        await driver.SaveNodeAsync(carol);
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-between-forward",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "tenant",
            Name = "MENTORS",
            Fact = "Alice mentors Bob",
            CreatedAt = referenceTime,
            ValidAt = referenceTime,
            ReferenceTime = referenceTime
        });
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-between-reverse",
            SourceNodeUuid = bob.Uuid,
            TargetNodeUuid = alice.Uuid,
            GroupId = "tenant",
            Name = "REVIEWS",
            Fact = "Bob reviews Alice",
            CreatedAt = referenceTime,
            ValidAt = referenceTime,
            ReferenceTime = referenceTime
        });
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-between-unrelated",
            SourceNodeUuid = carol.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "tenant",
            Name = "KNOWS",
            Fact = "Carol knows Bob",
            CreatedAt = referenceTime,
            ValidAt = referenceTime,
            ReferenceTime = referenceTime
        });

        var forward = await driver.GetEntityEdgesBetweenNodesAsync(alice.Uuid, bob.Uuid);
        var reverse = await driver.GetEntityEdgesBetweenNodesAsync(bob.Uuid, alice.Uuid);

        Assert.Equal("edge-between-forward", Assert.Single(forward).Uuid);
        Assert.Equal("edge-between-reverse", Assert.Single(reverse).Uuid);
    }

    [Fact]
    public async Task PackageRuntime_DirectStatementsRoundTripEpisodeMentionsAndGroupFilters()
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
    public async Task PackageRuntime_RetrieveEpisodesWithSagaUsesFirstGroupSourceAndReferenceTime()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var createdAt = new DateTime(2026, 3, 5, 6, 7, 8, DateTimeKind.Utc);
        var referenceTime = createdAt.AddMinutes(30);
        var tenantASaga = new GraphitiSagaNode
        {
            Uuid = "saga-retrieve-tenant-a",
            Name = "onboarding",
            GroupId = "tenant-a",
            CreatedAt = createdAt,
            Summary = "tenant a onboarding"
        };
        var tenantBSaga = new GraphitiSagaNode
        {
            Uuid = "saga-retrieve-tenant-b",
            Name = "onboarding",
            GroupId = "tenant-b",
            CreatedAt = createdAt,
            Summary = "tenant b onboarding"
        };
        var olderTenantA = new EpisodicNode
        {
            Uuid = "episode-saga-tenant-a-older",
            Name = "tenant a older",
            GroupId = "tenant-a",
            CreatedAt = createdAt,
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Tenant A older onboarding message.",
            ValidAt = createdAt.AddMinutes(10),
            EntityEdges = []
        };
        var newerTenantA = new EpisodicNode
        {
            Uuid = "episode-saga-tenant-a-newer",
            Name = "tenant a newer",
            GroupId = "tenant-a",
            CreatedAt = createdAt.AddMinutes(1),
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Tenant A newer onboarding message.",
            ValidAt = createdAt.AddMinutes(20),
            EntityEdges = []
        };
        var wrongSourceTenantA = new EpisodicNode
        {
            Uuid = "episode-saga-tenant-a-wrong-source",
            Name = "tenant a wrong source",
            GroupId = "tenant-a",
            CreatedAt = createdAt.AddMinutes(2),
            Source = EpisodeType.Text,
            SourceDescription = "document",
            Content = "Tenant A text source.",
            ValidAt = createdAt.AddMinutes(25),
            EntityEdges = []
        };
        var futureTenantA = new EpisodicNode
        {
            Uuid = "episode-saga-tenant-a-future",
            Name = "tenant a future",
            GroupId = "tenant-a",
            CreatedAt = createdAt.AddMinutes(3),
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Tenant A future onboarding message.",
            ValidAt = createdAt.AddMinutes(40),
            EntityEdges = []
        };
        var tenantB = new EpisodicNode
        {
            Uuid = "episode-saga-tenant-b",
            Name = "tenant b",
            GroupId = "tenant-b",
            CreatedAt = createdAt.AddMinutes(4),
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Tenant B onboarding message.",
            ValidAt = createdAt.AddMinutes(15),
            EntityEdges = []
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(tenantASaga);
        await driver.SaveNodeAsync(tenantBSaga);
        await driver.SaveNodeAsync(olderTenantA);
        await driver.SaveNodeAsync(newerTenantA);
        await driver.SaveNodeAsync(wrongSourceTenantA);
        await driver.SaveNodeAsync(futureTenantA);
        await driver.SaveNodeAsync(tenantB);
        await driver.SaveEdgeAsync(HasEpisode(tenantASaga.Uuid, olderTenantA.Uuid, "has-tenant-a-older", "tenant-a", createdAt));
        await driver.SaveEdgeAsync(HasEpisode(tenantASaga.Uuid, newerTenantA.Uuid, "has-tenant-a-newer", "tenant-a", createdAt));
        await driver.SaveEdgeAsync(HasEpisode(tenantASaga.Uuid, wrongSourceTenantA.Uuid, "has-tenant-a-wrong-source", "tenant-a", createdAt));
        await driver.SaveEdgeAsync(HasEpisode(tenantASaga.Uuid, futureTenantA.Uuid, "has-tenant-a-future", "tenant-a", createdAt));
        await driver.SaveEdgeAsync(HasEpisode(tenantBSaga.Uuid, tenantB.Uuid, "has-tenant-b", "tenant-b", createdAt));

        var retrieved = await driver.RetrieveEpisodesAsync(
            referenceTime,
            lastN: 2,
            groupIds: ["tenant-a", "tenant-b"],
            source: EpisodeType.Message,
            saga: "onboarding");
        var missingFirstGroup = await driver.RetrieveEpisodesAsync(
            referenceTime,
            lastN: 2,
            groupIds: ["missing", "tenant-b"],
            source: EpisodeType.Message,
            saga: "onboarding");

        Assert.Equal(new[] { olderTenantA.Uuid, newerTenantA.Uuid }, retrieved.Select(episode => episode.Uuid));
        Assert.All(retrieved, episode => Assert.Equal("tenant-a", episode.GroupId));
        Assert.Empty(missingFirstGroup);

        static HasEpisodeEdge HasEpisode(
            string sagaUuid,
            string episodeUuid,
            string uuid,
            string groupId,
            DateTime createdAt) =>
            new()
            {
                Uuid = uuid,
                SourceNodeUuid = sagaUuid,
                TargetNodeUuid = episodeUuid,
                GroupId = groupId,
                CreatedAt = createdAt
            };
    }

    [Fact]
    public async Task PackageRuntime_SagaEpisodeContentsSinceFiltersOrdersAndDropsEmptyAfterLimit()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var start = new DateTime(2026, 3, 6, 7, 8, 9, DateTimeKind.Utc);
        var saga = new GraphitiSagaNode
        {
            Uuid = "saga-contents",
            Name = "launch",
            GroupId = "tenant",
            CreatedAt = start,
            Summary = "launch summary"
        };
        var beforeSince = Episode(
            "episode-contents-before-since",
            "tenant",
            validAt: start.AddMinutes(10),
            createdAt: start,
            content: "before");
        var emptyFirst = Episode(
            "episode-contents-empty-first",
            "tenant",
            validAt: start.AddMinutes(4),
            createdAt: start.AddMinutes(3),
            content: string.Empty);
        var included1 = Episode(
            "episode-contents-included-1",
            "tenant",
            validAt: start.AddMinutes(5),
            createdAt: start.AddMinutes(2),
            content: "included-1");
        var included2 = Episode(
            "episode-contents-included-2",
            "tenant",
            validAt: start.AddMinutes(6),
            createdAt: start.AddMinutes(4),
            content: "included-2");
        var afterLimit = Episode(
            "episode-contents-after-limit",
            "tenant",
            validAt: start.AddMinutes(7),
            createdAt: start.AddMinutes(5),
            content: "after-limit");

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(saga);
        foreach (var episode in new[] { beforeSince, emptyFirst, included1, included2, afterLimit })
        {
            await driver.SaveNodeAsync(episode);
            await driver.SaveEdgeAsync(new HasEpisodeEdge
            {
                Uuid = "has-" + episode.Uuid,
                SourceNodeUuid = saga.Uuid,
                TargetNodeUuid = episode.Uuid,
                GroupId = "tenant",
                CreatedAt = start
            });
        }

        var contents = await driver.GetSagaEpisodeContentsAsync(
            saga.Uuid,
            since: start.AddMinutes(1),
            limit: 3);

        Assert.Equal(new[] { "included-1", "included-2" }, contents.Select(content => content.Content));
        Assert.Equal(
            new DateTime?[] { included1.ValidAt, included2.ValidAt },
            contents.Select(content => content.ValidAt).ToArray());

        static EpisodicNode Episode(
            string uuid,
            string groupId,
            DateTime validAt,
            DateTime createdAt,
            string content) =>
            new()
            {
                Uuid = uuid,
                Name = uuid,
                GroupId = groupId,
                CreatedAt = createdAt,
                Source = EpisodeType.Message,
                SourceDescription = "chat",
                Content = content,
                ValidAt = validAt,
                EntityEdges = []
            };
    }

    [Fact]
    public async Task PackageRuntime_GraphDriverEnumeratesEntityAndCommunityGroupIds()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var createdAt = new DateTime(2026, 8, 9, 10, 11, 12, DateTimeKind.Utc);

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(new EntityNode
        {
            Uuid = "entity-group-b",
            Name = "Entity Group B",
            GroupId = "tenant-b",
            Labels = ["Person"],
            CreatedAt = createdAt,
            Summary = "entity group b"
        });
        await driver.SaveNodeAsync(new EntityNode
        {
            Uuid = "entity-group-a-1",
            Name = "Entity Group A",
            GroupId = "tenant-a",
            Labels = ["Person"],
            CreatedAt = createdAt,
            Summary = "entity group a"
        });
        await driver.SaveNodeAsync(new EntityNode
        {
            Uuid = "entity-group-a-2",
            Name = "Entity Group A Duplicate",
            GroupId = "tenant-a",
            Labels = ["Person"],
            CreatedAt = createdAt,
            Summary = "entity group a duplicate"
        });
        await driver.SaveNodeAsync(new EntityNode
        {
            Uuid = "entity-group-blank",
            Name = "Entity Blank Group",
            GroupId = string.Empty,
            Labels = ["Person"],
            CreatedAt = createdAt,
            Summary = "entity blank group"
        });
        await driver.SaveNodeAsync(new CommunityNode
        {
            Uuid = "community-group-z",
            Name = "Community Z",
            GroupId = "community-z",
            CreatedAt = createdAt,
            Summary = "community z"
        });
        await driver.SaveNodeAsync(new CommunityNode
        {
            Uuid = "community-group-a",
            Name = "Community A",
            GroupId = "community-a",
            CreatedAt = createdAt,
            Summary = "community a"
        });
        await driver.SaveNodeAsync(new CommunityNode
        {
            Uuid = "community-group-blank",
            Name = "Community Blank",
            GroupId = string.Empty,
            CreatedAt = createdAt,
            Summary = "community blank"
        });

        var entityGroupIds = await driver.GetEntityGroupIdsAsync();
        var communityGroupIds = await driver.GetCommunityGroupIdsAsync();

        Assert.Equal(new[] { string.Empty, "tenant-a", "tenant-b" }, entityGroupIds);
        Assert.Equal(new[] { string.Empty, "community-a", "community-z" }, communityGroupIds);
    }

    [Fact]
    public async Task PackageRuntime_GraphDriverPagesNodeAndEdgeGroupReadsWithCursors()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var createdAt = new DateTime(2026, 8, 10, 11, 12, 13, DateTimeKind.Utc);
        var nodeA = new EntityNode
        {
            Uuid = "entity-page-a",
            Name = "Page A",
            GroupId = "tenant",
            Labels = ["Person"],
            NameEmbedding = [0.1f, 0.2f],
            CreatedAt = createdAt,
            Summary = "page a"
        };
        var nodeB = new EntityNode
        {
            Uuid = "entity-page-b",
            Name = "Page B",
            GroupId = "tenant",
            Labels = ["Person"],
            NameEmbedding = [0.3f, 0.4f],
            CreatedAt = createdAt,
            Summary = "page b"
        };
        var nodeC = new EntityNode
        {
            Uuid = "entity-page-c",
            Name = "Page C",
            GroupId = "tenant",
            Labels = ["Person"],
            NameEmbedding = [0.5f, 0.6f],
            CreatedAt = createdAt,
            Summary = "page c"
        };
        var otherNode = new EntityNode
        {
            Uuid = "entity-page-z-other",
            Name = "Other Page",
            GroupId = "other",
            Labels = ["Person"],
            NameEmbedding = [0.7f, 0.8f],
            CreatedAt = createdAt,
            Summary = "other page"
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(nodeA);
        await driver.SaveNodeAsync(nodeB);
        await driver.SaveNodeAsync(nodeC);
        await driver.SaveNodeAsync(otherNode);
        await driver.SaveEdgeAsync(PageEdge("edge-page-a", nodeA.Uuid, nodeB.Uuid, [0.1f, 0.2f]));
        await driver.SaveEdgeAsync(PageEdge("edge-page-b", nodeB.Uuid, nodeC.Uuid, [0.3f, 0.4f]));
        await driver.SaveEdgeAsync(PageEdge("edge-page-c", nodeC.Uuid, nodeA.Uuid, [0.5f, 0.6f]));
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-page-z-other",
            SourceNodeUuid = otherNode.Uuid,
            TargetNodeUuid = otherNode.Uuid,
            GroupId = "other",
            Name = "SELF",
            Fact = "Other page links to itself",
            FactEmbedding = [0.7f, 0.8f],
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt
        });

        var nodeFirstPage = await driver.GetNodesByGroupIdsAsync<EntityNode>(
            ["tenant"],
            limit: 2);
        var nodeSecondPage = await driver.GetNodesByGroupIdsAsync<EntityNode>(
            ["tenant"],
            limit: 2,
            uuidCursor: Assert.Single(nodeFirstPage.Skip(1)).Uuid,
            withEmbeddings: true);
        var edgeFirstPage = await driver.GetEdgesByGroupIdsAsync<EntityEdge>(
            ["tenant"],
            limit: 2);
        var edgeSecondPage = await driver.GetEdgesByGroupIdsAsync<EntityEdge>(
            ["tenant"],
            limit: 2,
            uuidCursor: Assert.Single(edgeFirstPage.Skip(1)).Uuid,
            withEmbeddings: true);

        Assert.Equal(new[] { nodeC.Uuid, nodeB.Uuid }, nodeFirstPage.Select(node => node.Uuid));
        Assert.All(nodeFirstPage, node => Assert.Null(node.NameEmbedding));
        var nodeSecond = Assert.Single(nodeSecondPage);
        Assert.Equal(nodeA.Uuid, nodeSecond.Uuid);
        Assert.Equal(new[] { 0.1f, 0.2f }, nodeSecond.NameEmbedding);
        Assert.Equal(new[] { "edge-page-c", "edge-page-b" }, edgeFirstPage.Select(edge => edge.Uuid));
        Assert.All(edgeFirstPage, edge => Assert.Null(edge.FactEmbedding));
        var edgeSecond = Assert.Single(edgeSecondPage);
        Assert.Equal("edge-page-a", edgeSecond.Uuid);
        Assert.Equal(new[] { 0.1f, 0.2f }, edgeSecond.FactEmbedding);

        EntityEdge PageEdge(
            string uuid,
            string sourceUuid,
            string targetUuid,
            List<float> embedding) =>
            new()
            {
                Uuid = uuid,
                SourceNodeUuid = sourceUuid,
                TargetNodeUuid = targetUuid,
                GroupId = "tenant",
                Name = "LINKS",
                Fact = uuid + " fact",
                FactEmbedding = embedding,
                CreatedAt = createdAt,
                ValidAt = createdAt,
                ReferenceTime = createdAt
            };
    }

    [Fact]
    public async Task PackageRuntime_PublicNamespacesRoundTripCommunitySagaReadsAndTypedDeletes()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var graphiti = new Graphiti(graphDriver: driver, embedder: new HashEmbedder(8));
        var createdAt = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var referenceTime = createdAt.AddHours(1);
        var alice = new EntityNode
        {
            Uuid = "entity-alice",
            Name = "Alice",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt
        };
        var bob = new EntityNode
        {
            Uuid = "entity-bob",
            Name = "Bob",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt
        };
        var otherEntity = new EntityNode
        {
            Uuid = "entity-other",
            Name = "Mallory",
            GroupId = "other",
            Labels = ["Person"],
            CreatedAt = createdAt
        };
        var tenantCommunity = new CommunityNode
        {
            Uuid = "community-tenant",
            Name = "Tenant community",
            GroupId = "tenant",
            Summary = "tenant summary",
            CreatedAt = createdAt
        };
        var otherCommunity = new CommunityNode
        {
            Uuid = "community-other",
            Name = "Other community",
            GroupId = "other",
            Summary = "other summary",
            CreatedAt = createdAt
        };
        var olderEpisode = new EpisodicNode
        {
            Uuid = "episode-older",
            Name = "older",
            GroupId = "tenant",
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Alice opened the case.",
            CreatedAt = createdAt,
            ValidAt = createdAt.AddMinutes(1)
        };
        var newerEpisode = new EpisodicNode
        {
            Uuid = "episode-newer",
            Name = "newer",
            GroupId = "tenant",
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Bob closed the case.",
            CreatedAt = createdAt.AddMinutes(1),
            ValidAt = createdAt.AddMinutes(2)
        };
        var otherEpisode = new EpisodicNode
        {
            Uuid = "episode-other",
            Name = "other",
            GroupId = "other",
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Mallory watched.",
            CreatedAt = createdAt,
            ValidAt = createdAt.AddMinutes(3)
        };
        var tenantSaga = new GraphitiSagaNode
        {
            Uuid = "saga-tenant",
            Name = "tenant saga",
            GroupId = "tenant",
            CreatedAt = createdAt,
            Summary = "tenant saga summary",
            FirstEpisodeUuid = olderEpisode.Uuid,
            LastEpisodeUuid = newerEpisode.Uuid,
            LastSummarizedAt = referenceTime,
            LastSummarizedEpisodeValidAt = newerEpisode.ValidAt
        };
        var otherSaga = new GraphitiSagaNode
        {
            Uuid = "saga-other",
            Name = "other saga",
            GroupId = "other",
            CreatedAt = createdAt,
            Summary = "other saga summary",
            FirstEpisodeUuid = otherEpisode.Uuid,
            LastEpisodeUuid = otherEpisode.Uuid
        };
        var knows = new EntityEdge
        {
            Uuid = "entity-edge-knows",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = bob.Uuid,
            GroupId = "tenant",
            Name = "KNOWS",
            Fact = "Alice knows Bob",
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = referenceTime
        };
        var communityEdge = new CommunityEdge
        {
            Uuid = "community-edge-tenant",
            SourceNodeUuid = tenantCommunity.Uuid,
            TargetNodeUuid = alice.Uuid,
            GroupId = "tenant",
            CreatedAt = createdAt
        };
        var otherCommunityEdge = new CommunityEdge
        {
            Uuid = "community-edge-other",
            SourceNodeUuid = otherCommunity.Uuid,
            TargetNodeUuid = otherEntity.Uuid,
            GroupId = "other",
            CreatedAt = createdAt
        };
        var hasEpisode = new HasEpisodeEdge
        {
            Uuid = "has-episode-tenant",
            SourceNodeUuid = tenantSaga.Uuid,
            TargetNodeUuid = olderEpisode.Uuid,
            GroupId = "tenant",
            CreatedAt = createdAt
        };
        var otherHasEpisode = new HasEpisodeEdge
        {
            Uuid = "has-episode-other",
            SourceNodeUuid = otherSaga.Uuid,
            TargetNodeUuid = otherEpisode.Uuid,
            GroupId = "other",
            CreatedAt = createdAt
        };
        var nextEpisode = new NextEpisodeEdge
        {
            Uuid = "next-episode-tenant",
            SourceNodeUuid = olderEpisode.Uuid,
            TargetNodeUuid = newerEpisode.Uuid,
            GroupId = "tenant",
            CreatedAt = createdAt
        };
        var otherNextEpisode = new NextEpisodeEdge
        {
            Uuid = "next-episode-other",
            SourceNodeUuid = otherEpisode.Uuid,
            TargetNodeUuid = otherEpisode.Uuid,
            GroupId = "other",
            CreatedAt = createdAt
        };

        await graphiti.BuildIndicesAndConstraintsAsync();
        await graphiti.Nodes.Entity.SaveBulkAsync([alice, bob, otherEntity]);
        await graphiti.Nodes.Community.SaveBulkAsync([tenantCommunity, otherCommunity]);
        await graphiti.Nodes.Episodic.SaveBulkAsync([olderEpisode, newerEpisode, otherEpisode]);
        await graphiti.Nodes.Saga.SaveBulkAsync([tenantSaga, otherSaga]);
        await graphiti.Edges.Entity.SaveBulkAsync([knows]);
        await graphiti.Edges.Community.SaveBulkAsync([communityEdge, otherCommunityEdge]);
        await graphiti.Edges.HasEpisode.SaveBulkAsync([hasEpisode, otherHasEpisode]);
        await graphiti.Edges.NextEpisode.SaveBulkAsync([nextEpisode, otherNextEpisode]);

        var entities = await graphiti.Nodes.Entity.GetByUuidsAsync([alice.Uuid, bob.Uuid]);
        var entityEdge = Assert.Single(await graphiti.Edges.Entity.GetByUuidsAsync([knows.Uuid]));
        var tenantCommunityEdge = Assert.Single(await graphiti.Edges.Community.GetByGroupIdsAsync(["tenant"]));
        var tenantHasEpisode = Assert.Single(await graphiti.Edges.HasEpisode.GetByGroupIdsAsync(["tenant"]));
        var tenantNextEpisode = Assert.Single(await graphiti.Edges.NextEpisode.GetByGroupIdsAsync(["tenant"]));

        Assert.Equal(new[] { alice.Uuid, bob.Uuid }, entities.Select(node => node.Uuid).Order(StringComparer.Ordinal));
        Assert.All(entities, node => Assert.Null(node.NameEmbedding));
        Assert.Equal(knows.Fact, entityEdge.Fact);
        Assert.Equal(alice.Uuid, entityEdge.SourceNodeUuid);
        Assert.Equal(bob.Uuid, entityEdge.TargetNodeUuid);
        Assert.Null(entityEdge.FactEmbedding);
        Assert.Equal(tenantCommunity.Uuid, Assert.Single(await graphiti.Nodes.Community.GetByGroupIdsAsync(["tenant"])).Uuid);
        Assert.Equal(tenantSaga.Uuid, Assert.Single(await graphiti.Nodes.Saga.GetByGroupIdsAsync(["tenant"])).Uuid);
        Assert.Equal((communityEdge.SourceNodeUuid, communityEdge.TargetNodeUuid),
            (tenantCommunityEdge.SourceNodeUuid, tenantCommunityEdge.TargetNodeUuid));
        Assert.Equal((hasEpisode.SourceNodeUuid, hasEpisode.TargetNodeUuid),
            (tenantHasEpisode.SourceNodeUuid, tenantHasEpisode.TargetNodeUuid));
        Assert.Equal((nextEpisode.SourceNodeUuid, nextEpisode.TargetNodeUuid),
            (tenantNextEpisode.SourceNodeUuid, tenantNextEpisode.TargetNodeUuid));

        await graphiti.Edges.Community.DeleteByUuidsAsync([
            communityEdge.Uuid,
            hasEpisode.Uuid,
            nextEpisode.Uuid
        ]);

        await Assert.ThrowsAsync<EdgeNotFoundException>(() => graphiti.Edges.Community.GetByUuidAsync(communityEdge.Uuid));
        Assert.Equal(hasEpisode.Uuid, (await graphiti.Edges.HasEpisode.GetByUuidAsync(hasEpisode.Uuid)).Uuid);
        Assert.Equal(nextEpisode.Uuid, (await graphiti.Edges.NextEpisode.GetByUuidAsync(nextEpisode.Uuid)).Uuid);
        Assert.Equal(otherCommunityEdge.Uuid, (await graphiti.Edges.Community.GetByUuidAsync(otherCommunityEdge.Uuid)).Uuid);

        await graphiti.Nodes.Saga.DeleteByUuidsAsync([
            tenantSaga.Uuid,
            tenantCommunity.Uuid,
            olderEpisode.Uuid
        ]);

        await Assert.ThrowsAsync<NodeNotFoundException>(() => graphiti.Nodes.Saga.GetByUuidAsync(tenantSaga.Uuid));
        Assert.Equal(tenantCommunity.Uuid, (await graphiti.Nodes.Community.GetByUuidAsync(tenantCommunity.Uuid)).Uuid);
        Assert.Equal(olderEpisode.Uuid, (await graphiti.Nodes.Episodic.GetByUuidAsync(olderEpisode.Uuid)).Uuid);
        Assert.Equal(otherSaga.Uuid, (await graphiti.Nodes.Saga.GetByUuidAsync(otherSaga.Uuid)).Uuid);
    }

    [Fact]
    public async Task PackageRuntime_SearchExecutorRunsFtsAndVectorStatements()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var search = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
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
    public async Task PackageRuntime_SearchExecutorRunsNonEmptySearchFilters()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var search = Assert.IsAssignableFrom<ISearchGraphDriver>(driver);
        var createdAt = new DateTime(2026, 7, 8, 9, 10, 11, DateTimeKind.Utc);
        var source = new EntityNode
        {
            Uuid = "entity-filter-source",
            Name = "Filtered Alice",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            NameEmbedding = [1.0f, 0.0f],
            Summary = "Graphiti filter person source"
        };
        var target = new EntityNode
        {
            Uuid = "entity-filter-target",
            Name = "Filtered Bob",
            GroupId = "tenant",
            Labels = ["Person"],
            CreatedAt = createdAt,
            NameEmbedding = [0.95f, 0.05f],
            Summary = "Graphiti filter person target"
        };
        var organization = new EntityNode
        {
            Uuid = "entity-filter-organization",
            Name = "Filtered Company",
            GroupId = "tenant",
            Labels = ["Organization"],
            CreatedAt = createdAt,
            NameEmbedding = [0.99f, 0.01f],
            Summary = "Graphiti filter organization"
        };
        var otherGroup = new EntityNode
        {
            Uuid = "entity-filter-other-group",
            Name = "Other Group Person",
            GroupId = "other",
            Labels = ["Person"],
            CreatedAt = createdAt,
            NameEmbedding = [1.0f, 0.0f],
            Summary = "Graphiti filter other group"
        };
        var keepEdge = new EntityEdge
        {
            Uuid = "edge-filter-keep",
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "tenant",
            Name = "KNOWS",
            Fact = "Filtered Alice knows Filtered Bob",
            FactEmbedding = [1.0f, 0.0f],
            Episodes = ["episode-filter"],
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(source);
        await driver.SaveNodeAsync(target);
        await driver.SaveNodeAsync(organization);
        await driver.SaveNodeAsync(otherGroup);
        await driver.SaveEdgeAsync(keepEdge);
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-filter-wrong-type",
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "tenant",
            Name = "MENTORS",
            Fact = "Filtered Alice mentors Filtered Bob",
            FactEmbedding = [1.0f, 0.0f],
            Episodes = ["episode-filter"],
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt
        });
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-filter-wrong-uuid",
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "tenant",
            Name = "KNOWS",
            Fact = "Filtered Alice knows Filtered Bob by another fact",
            FactEmbedding = [1.0f, 0.0f],
            Episodes = ["episode-filter"],
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt
        });
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-filter-wrong-label",
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = organization.Uuid,
            GroupId = "tenant",
            Name = "KNOWS",
            Fact = "Filtered Alice knows Filtered Company",
            FactEmbedding = [1.0f, 0.0f],
            Episodes = ["episode-filter"],
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt
        });
        await driver.SaveEdgeAsync(new EntityEdge
        {
            Uuid = "edge-filter-wrong-group",
            SourceNodeUuid = otherGroup.Uuid,
            TargetNodeUuid = otherGroup.Uuid,
            GroupId = "other",
            Name = "KNOWS",
            Fact = "Other group person knows themselves",
            FactEmbedding = [1.0f, 0.0f],
            Episodes = ["episode-filter"],
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt
        });

        var nodeFilters = new SearchFilters { NodeLabels = new List<string> { "Person" } };
        var edgeFilters = new SearchFilters
        {
            NodeLabels = new List<string> { "Person" },
            EdgeTypes = new List<string> { "KNOWS" },
            EdgeUuids = new List<string> { keepEdge.Uuid }
        };

        var nodeVector = await search.SearchEntityNodesByEmbeddingAsync(
            [1.0f, 0.0f],
            nodeFilters,
            ["tenant"],
            limit: 10,
            minScore: 0.8f);
        var edgeVector = await search.SearchEntityEdgesByEmbeddingAsync(
            [1.0f, 0.0f],
            edgeFilters,
            ["tenant"],
            limit: 10,
            minScore: 0.8f);
        var edgeFulltext = await search.SearchEntityEdgesFulltextAsync(
            "Filtered",
            edgeFilters,
            ["tenant"],
            limit: 10);

        var nodeUuids = nodeVector.Select(hit => hit.Item.Uuid).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(source.Uuid, nodeUuids);
        Assert.Contains(target.Uuid, nodeUuids);
        Assert.DoesNotContain(organization.Uuid, nodeUuids);
        Assert.DoesNotContain(otherGroup.Uuid, nodeUuids);
        Assert.All(nodeVector, hit => Assert.Contains("Person", hit.Item.Labels));
        Assert.Equal(keepEdge.Uuid, Assert.Single(edgeVector).Item.Uuid);
        Assert.Equal(keepEdge.Uuid, Assert.Single(edgeFulltext).Item.Uuid);

        var emptyEdgeTypesFilters = new SearchFilters { EdgeTypes = [] };
        var emptyEdgeUuidsFilters = new SearchFilters { EdgeUuids = [] };

        Assert.Empty(await search.SearchEntityEdgesByEmbeddingAsync(
            [1.0f, 0.0f],
            emptyEdgeTypesFilters,
            ["tenant"],
            limit: 10,
            minScore: 0.8f));
        Assert.Empty(await search.SearchEntityEdgesFulltextAsync(
            "Filtered",
            emptyEdgeTypesFilters,
            ["tenant"],
            limit: 10));
        Assert.Empty(await search.SearchEntityEdgesByEmbeddingAsync(
            [1.0f, 0.0f],
            emptyEdgeUuidsFilters,
            ["tenant"],
            limit: 10,
            minScore: 0.8f));
        Assert.Empty(await search.SearchEntityEdgesFulltextAsync(
            "Filtered",
            emptyEdgeUuidsFilters,
            ["tenant"],
            limit: 10));
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

        await driver.ClearDataAsync(Array.Empty<string>());
        var sourceAfterEmptyClear = await driver.GetNodeByUuidAsync<EntityNode>(source.Uuid);
        var edgeAfterEmptyClear = await driver.GetEdgeByUuidAsync<EntityEdge>("edge-delete");
        Assert.Equal(source.Uuid, sourceAfterEmptyClear.Uuid);
        Assert.Equal("edge-delete", edgeAfterEmptyClear.Uuid);

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
    public async Task PackageRuntime_ClearDataByGroupPreservesSagaNodes()
    {
        await using var executor = new PackageLadybugExecutor();
        var driver = new LadybugGraphDriver(executor);
        var createdAt = new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        var saga = new GraphitiSagaNode
        {
            Uuid = "clear-saga",
            Name = "clear saga",
            GroupId = "tenant",
            CreatedAt = createdAt,
            FirstEpisodeUuid = "clear-episode",
            LastEpisodeUuid = "clear-episode"
        };
        var episode = new EpisodicNode
        {
            Uuid = "clear-episode",
            Name = "clear episode",
            GroupId = "tenant",
            Source = EpisodeType.Message,
            SourceDescription = "chat",
            Content = "Saga episode content",
            CreatedAt = createdAt,
            ValidAt = createdAt
        };
        var entity = new EntityNode
        {
            Uuid = "clear-entity",
            Name = "Clear Entity",
            GroupId = "tenant",
            CreatedAt = createdAt,
            Summary = "entity removed by scoped clear"
        };
        var edge = new HasEpisodeEdge
        {
            Uuid = "clear-has-episode",
            SourceNodeUuid = saga.Uuid,
            TargetNodeUuid = episode.Uuid,
            GroupId = "tenant",
            CreatedAt = createdAt
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(saga);
        await driver.SaveNodeAsync(episode);
        await driver.SaveNodeAsync(entity);
        await driver.SaveEdgeAsync(edge);

        await driver.ClearDataAsync(["tenant"]);

        Assert.Equal(saga.Uuid, (await driver.GetNodeByUuidAsync<GraphitiSagaNode>(saga.Uuid)).Uuid);
        await Assert.ThrowsAsync<NodeNotFoundException>(() => driver.GetNodeByUuidAsync<EpisodicNode>(episode.Uuid));
        await Assert.ThrowsAsync<NodeNotFoundException>(() => driver.GetNodeByUuidAsync<EntityNode>(entity.Uuid));
        await Assert.ThrowsAsync<EdgeNotFoundException>(() => driver.GetEdgeByUuidAsync<HasEpisodeEdge>(edge.Uuid));

        await driver.ClearDataAsync();

        await Assert.ThrowsAsync<NodeNotFoundException>(() => driver.GetNodeByUuidAsync<GraphitiSagaNode>(saga.Uuid));
    }

    [Fact]
    public void PackageRuntime_BindsGraphitiListArrayEmptyListAndNullParametersDirectly()
    {
        using var database = new Database("");
        using var connection = new Connection(database);

        using var result = connection.Execute(
            """
            RETURN $strings AS strings,
                   $array AS array,
                   $floats AS floats,
                   $empty AS empty,
                   $missing AS missing
            """,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["strings"] = new List<string> { "tenant", "archive" },
                ["array"] = new[] { "entity-1", "entity-2" },
                ["floats"] = new[] { 0.1f, 0.2f },
                ["empty"] = Array.Empty<string>(),
                ["missing"] = null
            });

        var row = Assert.Single(result.Rows());

        Assert.Equal(new object?[] { "tenant", "archive" }, Assert.IsType<object?[]>(row[0]));
        Assert.Equal(new object?[] { "entity-1", "entity-2" }, Assert.IsType<object?[]>(row[1]));
        Assert.Equal(new object?[] { 0.1f, 0.2f }, Assert.IsType<object?[]>(row[2]));
        Assert.Empty(Assert.IsType<object?[]>(row[3]));
        Assert.Null(row[4]);
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
    [Trait("Category", "LinuxLadybugSmoke")]
    public void PackageRuntime_LinuxFtsAndVectorExtensionsCreateAndQuery()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Linux-only LadybugDB extension smoke.");
        }

        if (Environment.GetEnvironmentVariable(LinuxSmokeEnvironmentVariable) != "1")
        {
            Assert.Skip($"Set {LinuxSmokeEnvironmentVariable}=1 to run the linux-x64 LadybugDB extension smoke.");
        }

        using var database = new Database("");
        using var connection = new Connection(database);

        connection.Query("INSTALL FTS;").Dispose();
        connection.Query("LOAD EXTENSION FTS;").Dispose();
        connection.Query("CREATE NODE TABLE SmokeFts(uuid STRING PRIMARY KEY, body STRING);").Dispose();
        connection.Query("CREATE (:SmokeFts {uuid: 'fts-1', body: 'Alice builds temporal graph search'});").Dispose();
        connection.Query("CREATE (:SmokeFts {uuid: 'fts-2', body: 'Bob paints murals'});").Dispose();
        connection.Query("CALL CREATE_FTS_INDEX('SmokeFts', 'smoke_fts_body', ['body']);").Dispose();

        using (var result = connection.Execute(
            """
            CALL QUERY_FTS_INDEX('SmokeFts', 'smoke_fts_body', $query, TOP := $limit)
            RETURN node.uuid AS uuid, score AS score
            ORDER BY score DESC;
            """,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["query"] = "temporal graph",
                ["limit"] = 3
            }))
        {
            var row = Assert.Single(result.Rows());
            Assert.Equal("fts-1", Assert.IsType<string>(row[0]));
            Assert.True(Assert.IsType<double>(row[1]) > 0);
        }

        connection.Query("INSTALL VECTOR;").Dispose();
        connection.Query("LOAD EXTENSION VECTOR;").Dispose();
        connection.Query("CREATE NODE TABLE SmokeVector(uuid STRING PRIMARY KEY, embedding FLOAT[3]);").Dispose();
        connection.Query("CREATE (:SmokeVector {uuid: 'vec-1', embedding: [1.0, 0.0, 0.0]});").Dispose();
        connection.Query("CREATE (:SmokeVector {uuid: 'vec-2', embedding: [0.0, 1.0, 0.0]});").Dispose();
        connection.Query("CALL CREATE_VECTOR_INDEX('SmokeVector', 'smoke_vector_embedding', 'embedding');").Dispose();

        using (var result = connection.Query(
            """
            CALL QUERY_VECTOR_INDEX('SmokeVector', 'smoke_vector_embedding', [1.0, 0.0, 0.0], 2)
            RETURN node.uuid AS uuid, distance AS distance
            ORDER BY distance;
            """))
        {
            var row = Assert.Single(result.Rows().Take(1));
            Assert.Equal("vec-1", Assert.IsType<string>(row[0]));
            Assert.True(Convert.ToDouble(row[1], CultureInfo.InvariantCulture) >= 0.0);
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
            return statement.Parameters.Count == 0
                ? _connection.Query(statement.Query)
                : _connection.Execute(statement.Query, statement.Parameters);
        }
    }
}
