using System.Text.Json.Nodes;
using Graphiti.Core.Internal.Services;

namespace Graphiti.Core.Tests.Internal;

public class EpisodeGraphExtractorTests
{
    [Fact]
    public async Task ExtractCombinedEpisodeGraph_DropsOrphansDerivesAttributionAndAppliesBatchTimestamps()
    {
        var llm = new PromptResponseLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_nodes_and_edges.extract_message"] = new()
            {
                ["extracted_entities"] = new JsonArray
                {
                    new JsonObject { ["name"] = "Alice", ["entity_type_id"] = 1 },
                    new JsonObject { ["name"] = "Bob", ["entity_type_id"] = 1 },
                    new JsonObject { ["name"] = "orphan detail", ["entity_type_id"] = 0 }
                },
                ["edges"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["source_entity_name"] = " alice ",
                        ["target_entity_name"] = "BOB",
                        ["relation_type"] = "KNOWS",
                        ["fact"] = "Alice knows Bob.",
                        ["episode_indices"] = new JsonArray { 0 }
                    }
                }
            },
            ["extract_edges.extract_timestamps_batch"] = new()
            {
                ["timestamps"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["valid_at"] = "2026-01-03T00:00:00Z",
                        ["invalid_at"] = null
                    }
                }
            }
        });
        var now = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var extractor = new EpisodeGraphExtractor(llm, () => now);
        var episode = new EpisodicNode
        {
            Name = "episode",
            Content = "Alice: I know Bob.",
            Source = EpisodeType.Message,
            GroupId = "group",
            ValidAt = now
        };

        var (nodes, edges, attribution) = await extractor.ExtractCombinedEpisodeGraphAsync(
            episode,
            Array.Empty<EpisodicNode>(),
            new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new("Person", "A named person")
            },
            excludedEntityTypes: null,
            edgeTypes: null,
            edgeTypeMap: null,
            customExtractionInstructions: null,
            CancellationToken.None);

        Assert.Equal(new[] { "Alice", "Bob" }, nodes.Select(node => node.Name));
        var edge = Assert.Single(edges);
        Assert.Equal("Alice", edge.SourceName);
        Assert.Equal("Bob", edge.TargetName);
        Assert.Equal("KNOWS", edge.RelationType);
        Assert.Equal("Alice knows Bob.", edge.Fact);
        Assert.Equal(new[] { 0 }, edge.EpisodeIndices);
        Assert.True(edge.AllowSelfEdge);
        Assert.Equal(now, edge.ReferenceTime);
        Assert.Equal(new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), edge.ValidAt);
        Assert.Null(edge.InvalidAt);
        Assert.Equal(new[] { 0 }, attribution[nodes[0].Uuid]);
        Assert.Equal(new[] { 0 }, attribution[nodes[1].Uuid]);
        Assert.DoesNotContain(nodes, node => node.Name == "orphan detail");
        Assert.Equal(
            new[] { "extract_nodes_and_edges.extract_message", "extract_edges.extract_timestamps_batch" },
            llm.PromptNames);
    }

    private sealed class PromptResponseLlmClient(IReadOnlyDictionary<string, JsonObject> responsesByPromptName)
        : ILlmClient
    {
        private readonly List<string?> _promptNames = new();

        public TokenUsageTracker TokenTracker { get; } = new();

        public IReadOnlyList<string?> PromptNames => _promptNames;

        public Task<JsonObject> GenerateResponseAsync(
            IReadOnlyList<Message> messages,
            Type? responseModel = null,
            StructuredResponseSchema? responseSchema = null,
            int? maxTokens = null,
            ModelSize modelSize = ModelSize.Medium,
            string? groupId = null,
            string? promptName = null,
            bool attributeExtraction = false,
            CancellationToken cancellationToken = default)
        {
            _promptNames.Add(promptName);
            return Task.FromResult(
                promptName is not null && responsesByPromptName.TryGetValue(promptName, out var response)
                    ? (JsonObject)response.DeepClone()
                    : new JsonObject());
        }
    }
}
