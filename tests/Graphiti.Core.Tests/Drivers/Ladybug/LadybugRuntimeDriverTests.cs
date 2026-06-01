using Graphiti.Core.Configuration;
using Graphiti.Core.Drivers.Ladybug;

namespace Graphiti.Core.Tests.Drivers.Ladybug;

public class LadybugRuntimeDriverTests
{
    [Fact]
    public async Task FactoryCreatesLadybugDriver()
    {
        var driver = LadybugDbGraphDriverFactory.CreateInMemory();
        var createdAt = new DateTime(2026, 8, 9, 10, 11, 12, DateTimeKind.Utc);
        var source = new EntityNode
        {
            Uuid = "ladybug-source",
            Name = "Ladybug Source",
            GroupId = "tenant",
            Labels = ["Person"],
            NameEmbedding = [1.0f, 0.0f],
            CreatedAt = createdAt,
            Summary = "created through the optional package source"
        };
        var target = new EntityNode
        {
            Uuid = "ladybug-target",
            Name = "Ladybug Target",
            GroupId = "tenant",
            Labels = ["Person"],
            NameEmbedding = [0.0f, 1.0f],
            CreatedAt = createdAt,
            Summary = "created through the optional package target"
        };
        var edge = new EntityEdge
        {
            Uuid = "ladybug-edge",
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            GroupId = "tenant",
            Name = "KNOWS",
            Fact = "Ladybug source knows target",
            FactEmbedding = [0.5f, 0.5f],
            Episodes = ["ladybug-episode"],
            CreatedAt = createdAt,
            ValidAt = createdAt,
            ReferenceTime = createdAt
        };

        await driver.BuildIndicesAndConstraintsAsync();
        await driver.SaveNodeAsync(source);
        await driver.SaveNodeAsync(target);
        await driver.SaveEdgeAsync(edge);
        var fetched = await driver.GetNodeByUuidAsync<EntityNode>(source.Uuid);
        var fetchedEdge = await driver.GetEdgeByUuidAsync<EntityEdge>(edge.Uuid);
        await driver.CloseAsync();
        await driver.CloseAsync();

        Assert.Equal(GraphProvider.Kuzu, driver.Provider);
        Assert.Equal(source.Uuid, fetched.Uuid);
        Assert.Equal(new[] { "Person", "Entity" }, fetched.Labels);
        Assert.Equal(edge.Uuid, fetchedEdge.Uuid);
        Assert.Equal(edge.Episodes, fetchedEdge.Episodes);
        Assert.Equal(edge.ReferenceTime, fetchedEdge.ReferenceTime);
    }
}
