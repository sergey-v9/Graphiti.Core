using Graphiti.Core;

namespace Graphiti.Core.Tests.Models;

public class EdgeModelTests
{
    [Fact]
    public async Task EntityEdgeGetByGroupIdsAsync_ThrowsWhenNoEdgesLikePython()
    {
        var driver = new InMemoryGraphDriver();

        await Assert.ThrowsAsync<GroupsEdgesNotFoundException>(() =>
            EntityEdge.GetByGroupIdsAsync(driver, new[] { "missing" }));
    }

    [Fact]
    public async Task EpisodicEdgeGetByGroupIdsAsync_ThrowsWhenNoEdgesLikePython()
    {
        var driver = new InMemoryGraphDriver();

        await Assert.ThrowsAsync<GroupsEdgesNotFoundException>(() =>
            EpisodicEdge.GetByGroupIdsAsync(driver, new[] { "missing" }));
    }
}
