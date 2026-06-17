using Graphiti.Core;

namespace Graphiti.Core.Tests.Drivers;

public sealed class InMemoryGraphDriverCloneTests
{
    [Fact]
    public async Task SaveNodeAsync_DeepClonesNestedAttributes()
    {
        var driver = new InMemoryGraphDriver();
        var nested = new Dictionary<string, object?> { ["value"] = "stored" };
        var tags = new List<object?> { "initial" };
        var node = new EntityNode
        {
            Uuid = "node",
            Name = "Node",
            Attributes =
            {
                ["nested"] = nested,
                ["tags"] = tags
            }
        };

        await driver.SaveNodeAsync(node);
        nested["value"] = "mutated";
        tags.Add("mutated");

        var fetched = await driver.GetNodeByUuidAsync<EntityNode>("node");

        Assert.Equal("stored", NestedDictionary(fetched.Attributes, "nested")["value"]);
        Assert.Equal(new object?[] { "initial" }, NestedList(fetched.Attributes, "tags"));
    }

    [Fact]
    public async Task GetNodeByUuidAsync_DeepClonesNestedAttributes()
    {
        var driver = new InMemoryGraphDriver();
        await driver.SaveNodeAsync(new EntityNode
        {
            Uuid = "node",
            Name = "Node",
            Attributes =
            {
                ["nested"] = new Dictionary<string, object?> { ["value"] = "stored" },
                ["tags"] = new List<object?> { "initial" }
            }
        });

        var fetched = await driver.GetNodeByUuidAsync<EntityNode>("node");
        NestedDictionary(fetched.Attributes, "nested")["value"] = "fetched";
        NestedList(fetched.Attributes, "tags").Add("fetched");
        var refetched = await driver.GetNodeByUuidAsync<EntityNode>("node");

        Assert.Equal("stored", NestedDictionary(refetched.Attributes, "nested")["value"]);
        Assert.Equal(new object?[] { "initial" }, NestedList(refetched.Attributes, "tags"));
    }

    [Fact]
    public async Task SaveEdgeAsync_DeepClonesNestedAttributes()
    {
        var driver = new InMemoryGraphDriver();
        var nested = new Dictionary<string, object?> { ["value"] = "stored" };
        var weights = new List<object?> { 1L };
        var edge = new EntityEdge
        {
            Uuid = "edge",
            SourceNodeUuid = "source",
            TargetNodeUuid = "target",
            Name = "RELATES_TO",
            Fact = "fact",
            Attributes =
            {
                ["nested"] = nested,
                ["weights"] = weights
            }
        };

        await driver.SaveNodeAsync(new EntityNode { Uuid = "source", Name = "Source" });
        await driver.SaveNodeAsync(new EntityNode { Uuid = "target", Name = "Target" });
        await driver.SaveEdgeAsync(edge);
        nested["value"] = "mutated";
        weights.Add(2L);

        var fetched = await driver.GetEdgeByUuidAsync<EntityEdge>("edge");

        Assert.Equal("stored", NestedDictionary(fetched.Attributes, "nested")["value"]);
        Assert.Equal(new object?[] { 1L }, NestedList(fetched.Attributes, "weights"));
    }

    [Fact]
    public async Task SaveNodeAsync_DoesNotPersistEpisodeMetadataLikePython()
    {
        var driver = new InMemoryGraphDriver();
        var nested = new Dictionary<string, object?> { ["source"] = "stored" };
        var flags = new List<object?> { true };
        var episode = new EpisodicNode
        {
            Uuid = "episode",
            Name = "Episode",
            EpisodeMetadata = new Dictionary<string, object?>
            {
                ["nested"] = nested,
                ["flags"] = flags
            }
        };

        await driver.SaveNodeAsync(episode);

        var fetched = await driver.GetNodeByUuidAsync<EpisodicNode>("episode");

        Assert.Null(fetched.EpisodeMetadata);
    }

    private static Dictionary<string, object?> NestedDictionary(
        Dictionary<string, object?> attributes,
        string key) =>
        Assert.IsType<Dictionary<string, object?>>(attributes[key]);

    private static List<object?> NestedList(
        Dictionary<string, object?> attributes,
        string key) =>
        Assert.IsType<List<object?>>(attributes[key]);
}
