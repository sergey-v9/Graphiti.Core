using Graphiti.Core;

namespace Graphiti.Core.Tests;

public class GraphitiConstructorDefaultTests
{
    [Fact]
    public async Task Constructor_WithNoDriverAndNoUri_DefaultsToInMemoryDriver()
    {
        await using var graphiti = new Graphiti();

        Assert.IsType<InMemoryGraphDriver>(graphiti.Driver);
        Assert.Equal(GraphProvider.InMemory, graphiti.Driver.Provider);
    }

    [Fact]
    public async Task Constructor_WithClientsOnly_DefaultsToInMemoryDriver()
    {
        await using var graphiti = new Graphiti(
            llmClient: new NoOpLlmClient(),
            embedder: new HashEmbedder());

        Assert.IsType<InMemoryGraphDriver>(graphiti.Driver);
        Assert.Equal(GraphProvider.InMemory, graphiti.Driver.Provider);
    }

    [Fact]
    public async Task Constructor_WithDatabase_FlowsToInMemoryDefault()
    {
        await using var graphiti = new Graphiti(database: "tenant-db");

        Assert.IsType<InMemoryGraphDriver>(graphiti.Driver);
        Assert.Equal("tenant-db", graphiti.Driver.Database);
    }

    [Fact]
    public async Task Constructor_DefaultInMemoryInstance_SupportsIngestAndRetrieveRoundTrip()
    {
        await using var graphiti = new Graphiti();
        await graphiti.BuildIndicesAndConstraintsAsync();
        var referenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // The default no-op LLM extracts no entities, but the episode itself is persisted and
        // retrievable, proving the default in-memory backend is fully wired.
        var result = await graphiti.AddEpisodeAsync(
            "conversation",
            "Alice likes Bob",
            "message",
            referenceTime,
            groupId: "group");

        Assert.Equal("conversation", result.Episode.Name);

        var episodes = await graphiti.RetrieveEpisodesAsync(
            referenceTime.AddMinutes(1),
            groupIds: new[] { "group" });

        Assert.Equal(result.Episode.Uuid, Assert.Single(episodes).Uuid);
    }

    [Fact]
    public async Task Constructor_WithUri_StillSelectsNeo4jDriver()
    {
        // Constructing the Neo4j driver does not open a connection eagerly, so this asserts driver
        // selection without requiring a running Neo4j instance.
        await using var graphiti = new Graphiti(uri: "bolt://localhost:7687");

        Assert.IsType<Neo4jGraphDriver>(graphiti.Driver);
        Assert.Equal(GraphProvider.Neo4j, graphiti.Driver.Provider);
    }

    [Fact]
    public async Task AddEpisodeAsync_OptionsOverload_DelegatesToParameterListOverload()
    {
        await using var graphiti = new Graphiti();
        var referenceTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var result = await graphiti.AddEpisodeAsync(
            new AddEpisodeOptions
            {
                Name = "conversation",
                EpisodeBody = "Alice likes Bob",
                SourceDescription = "message",
                ReferenceTime = referenceTime,
                GroupId = "group"
            });

        Assert.Equal("conversation", result.Episode.Name);
        Assert.Equal("group", result.Episode.GroupId);
        Assert.Equal(EpisodeType.Message, result.Episode.Source);

        var episodes = await graphiti.RetrieveEpisodesAsync(
            referenceTime.AddMinutes(1),
            groupIds: new[] { "group" });

        Assert.Equal(result.Episode.Uuid, Assert.Single(episodes).Uuid);
    }

    [Fact]
    public async Task AddEpisodeAsync_NullOptions_Throws()
    {
        await using var graphiti = new Graphiti();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => graphiti.AddEpisodeAsync((AddEpisodeOptions)null!));
    }
}
