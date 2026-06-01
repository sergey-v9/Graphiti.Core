using Graphiti.Core;

namespace Graphiti.Core.Tests.Drivers;

public class InMemoryGraphDriverSagaTests
{
    [Fact]
    public async Task RetrieveEpisodes_WithSagaAndGroupIdsUsesFirstGroupForSagaLookup()
    {
        var driver = new InMemoryGraphDriver();
        var referenceTime = new DateTime(2026, 5, 27, 12, 0, 0, DateTimeKind.Utc);
        var sagaA = new SagaNode { Name = "onboarding", GroupId = "tenant-a" };
        var sagaB = new SagaNode { Name = "onboarding", GroupId = "tenant-b" };
        var episodeA = new EpisodicNode
        {
            Name = "tenant-a episode",
            GroupId = "tenant-a",
            ValidAt = referenceTime.AddMinutes(-2)
        };
        var episodeB = new EpisodicNode
        {
            Name = "tenant-b episode",
            GroupId = "tenant-b",
            ValidAt = referenceTime.AddMinutes(-1)
        };

        await sagaA.SaveAsync(driver);
        await sagaB.SaveAsync(driver);
        await episodeA.SaveAsync(driver);
        await episodeB.SaveAsync(driver);
        await new HasEpisodeEdge
        {
            SourceNodeUuid = sagaA.Uuid,
            TargetNodeUuid = episodeA.Uuid,
            GroupId = "tenant-a"
        }.SaveAsync(driver);
        await new HasEpisodeEdge
        {
            SourceNodeUuid = sagaB.Uuid,
            TargetNodeUuid = episodeB.Uuid,
            GroupId = "tenant-b"
        }.SaveAsync(driver);

        var retrieved = await driver.RetrieveEpisodesAsync(
            referenceTime,
            lastN: 10,
            groupIds: new[] { "tenant-a", "tenant-b" },
            saga: "onboarding");

        var episode = Assert.Single(retrieved);
        Assert.Equal(episodeA.Uuid, episode.Uuid);

        var missingFirstGroup = await driver.RetrieveEpisodesAsync(
            referenceTime,
            lastN: 10,
            groupIds: new[] { "missing", "tenant-b" },
            saga: "onboarding");

        Assert.Empty(missingFirstGroup);
    }

    [Fact]
    public async Task RetrieveEpisodesAsync_FiltersSourceReferenceTimeGroupsAndReturnsChronologicalClones()
    {
        var driver = new InMemoryGraphDriver();
        var referenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var tooOld = Episode(
            "episode-0",
            "group-a",
            referenceTime.AddMinutes(-4),
            source: EpisodeType.Message,
            content: "too old");
        var older = Episode(
            "episode-1",
            "group-a",
            referenceTime.AddMinutes(-3),
            source: EpisodeType.Message,
            content: "older");
        var json = Episode(
            "episode-json",
            "group-a",
            referenceTime.AddMinutes(-2),
            source: EpisodeType.Json,
            content: "json");
        var newer = Episode(
            "episode-2",
            "group-a",
            referenceTime.AddMinutes(-1),
            source: EpisodeType.Message,
            content: "newer");
        var otherGroup = Episode(
            "episode-other",
            "group-b",
            referenceTime.AddMinutes(-1),
            source: EpisodeType.Message,
            content: "other group");
        var future = Episode(
            "episode-future",
            "group-a",
            referenceTime.AddMinutes(1),
            source: EpisodeType.Message,
            content: "future");
        foreach (var episode in new[] { tooOld, older, json, newer, otherGroup, future })
        {
            await episode.SaveAsync(driver);
        }

        var retrieved = await driver.RetrieveEpisodesAsync(
            referenceTime,
            lastN: 2,
            groupIds: new[] { "group-a" },
            source: EpisodeType.Message);

        Assert.Equal(new[] { "episode-1", "episode-2" }, retrieved.Select(episode => episode.Uuid));
        retrieved[0].Content = "mutated";
        Assert.Equal("older", (await driver.GetNodeByUuidAsync<EpisodicNode>("episode-1")).Content);
    }

    [Fact]
    public async Task FindSagaByNameAsync_ChoosesOrdinalLowestUuidAndReturnsClone()
    {
        var driver = new InMemoryGraphDriver();
        var sagaZ = new SagaNode
        {
            Uuid = "saga-z",
            Name = "launch",
            GroupId = "group",
            Summary = "z summary"
        };
        var sagaA = new SagaNode
        {
            Uuid = "saga-a",
            Name = "launch",
            GroupId = "group",
            Summary = "a summary"
        };
        await sagaZ.SaveAsync(driver);
        await sagaA.SaveAsync(driver);

        var saga = await driver.FindSagaByNameAsync("launch", "group");

        Assert.NotNull(saga);
        Assert.Equal("saga-a", saga.Uuid);
        saga.Summary = "mutated";
        Assert.Equal("a summary", (await driver.GetNodeByUuidAsync<SagaNode>("saga-a")).Summary);
        Assert.Null(await driver.FindSagaByNameAsync("launch", "missing"));
    }

    [Fact]
    public async Task GetSagaPreviousEpisodeUuidAsync_UsesValidAtThenCreatedAtDescendingAndSkipsCurrent()
    {
        var driver = new InMemoryGraphDriver();
        var saga = new SagaNode { Uuid = "saga", Name = "launch", GroupId = "group" };
        await saga.SaveAsync(driver);
        var reference = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var current = Episode("episode-current", "group", reference.AddMinutes(3), reference.AddMinutes(3), content: "current");
        var olderValid = Episode("episode-older", "group", reference.AddMinutes(1), reference.AddMinutes(10), content: "older");
        var earlierCreated = Episode("episode-earlier-created", "group", reference.AddMinutes(2), reference.AddMinutes(4), content: "earlier");
        var laterCreated = Episode("episode-later-created", "group", reference.AddMinutes(2), reference.AddMinutes(5), content: "later");
        foreach (var episode in new[] { current, olderValid, earlierCreated, laterCreated })
        {
            await SaveSagaEpisodeAsync(driver, saga, episode);
        }

        var previous = await driver.GetSagaPreviousEpisodeUuidAsync(saga.Uuid, current.Uuid);

        Assert.Equal(laterCreated.Uuid, previous);
    }

    [Fact]
    public async Task GetSagaEpisodeContentsAsync_SinceFiltersByCreatedAtOrdersAscendingAndDropsEmptyAfterLimit()
    {
        var driver = new InMemoryGraphDriver();
        var saga = new SagaNode { Uuid = "saga", Name = "launch", GroupId = "group" };
        await saga.SaveAsync(driver);
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var beforeSince = Episode("before-since", "group", start.AddMinutes(10), start, content: "before");
        var firstByValidAt = Episode("empty-first", "group", start.AddMinutes(4), start.AddMinutes(3), content: string.Empty);
        var secondByValidAt = Episode("included-1", "group", start.AddMinutes(5), start.AddMinutes(2), content: "included-1");
        var thirdByValidAt = Episode("included-2", "group", start.AddMinutes(6), start.AddMinutes(4), content: "included-2");
        var afterLimit = Episode("after-limit", "group", start.AddMinutes(7), start.AddMinutes(5), content: "after-limit");
        foreach (var episode in new[] { beforeSince, firstByValidAt, secondByValidAt, thirdByValidAt, afterLimit })
        {
            await SaveSagaEpisodeAsync(driver, saga, episode);
        }

        var contents = await driver.GetSagaEpisodeContentsAsync(
            saga.Uuid,
            since: start.AddMinutes(1),
            limit: 3);

        Assert.Equal(new[] { "included-1", "included-2" }, contents.Select(content => content.Content));
    }

    private static EpisodicNode Episode(
        string uuid,
        string groupId,
        DateTime validAt,
        DateTime? createdAt = null,
        EpisodeType source = EpisodeType.Message,
        string content = "") =>
        new()
        {
            Uuid = uuid,
            Name = uuid,
            GroupId = groupId,
            ValidAt = validAt,
            CreatedAt = createdAt ?? validAt,
            Source = source,
            Content = content
        };

    private static async Task SaveSagaEpisodeAsync(
        InMemoryGraphDriver driver,
        SagaNode saga,
        EpisodicNode episode)
    {
        await episode.SaveAsync(driver);
        await new HasEpisodeEdge
        {
            Uuid = $"has-{episode.Uuid}",
            SourceNodeUuid = saga.Uuid,
            TargetNodeUuid = episode.Uuid,
            GroupId = episode.GroupId
        }.SaveAsync(driver);
    }
}
