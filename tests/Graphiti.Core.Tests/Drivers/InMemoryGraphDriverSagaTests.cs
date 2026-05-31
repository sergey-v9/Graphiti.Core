using Graphiti.Core;

namespace Graphiti.Core.Tests.Drivers;

public class InMemoryGraphDriverSagaTests
{
    [Fact]
    public async Task RetrieveEpisodes_WithSagaAndGroupIdsUsesMatchingSagaGroup()
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
            groupIds: new[] { "tenant-a" },
            saga: "onboarding");

        var episode = Assert.Single(retrieved);
        Assert.Equal(episodeA.Uuid, episode.Uuid);
    }
}
