using System.Text.Json.Nodes;
using Graphiti.Core.Internal.Helpers;
using Graphiti.Core.Models;
using Graphiti.Core.Models.Nodes;

namespace Graphiti.Core.Tests.Internal;

public class ExtractionContextBuilderTests
{
    [Fact]
    public void BuildExtractionContext_EmitsEquivalentSortedEdgeSignatureFields()
    {
        var episode = new EpisodicNode
        {
            Name = "episode",
            Content = "Alice works at Acme and advises Contoso.",
            Source = EpisodeType.Message,
            SourceDescription = "message",
            ValidAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        var edgeTypes = new Dictionary<string, EntityTypeDefinition>
        {
            ["WORKS_AT"] = new("WORKS_AT", "Employment relationship")
        };
        var edgeTypeMap = new Dictionary<(string SourceType, string TargetType), IReadOnlyList<string>>
        {
            [("Person", "Project")] = new[] { "WORKS_AT" },
            [("Organization", "Person")] = new[] { "RELATED_TO" },
            [("Person", "Organization")] = new[] { "WORKS_AT" }
        };

        var context = ExtractionContextBuilder.BuildExtractionContext(
            episode,
            Array.Empty<EpisodicNode>(),
            entityTypes: null,
            excludedEntityTypes: null,
            edgeTypes,
            edgeTypeMap,
            customExtractionInstructions: null);

        var edgeType = Assert.Single(Assert.IsType<JsonArray>(context["edge_types"]).OfType<JsonObject>());
        var signatures = Assert.IsType<JsonArray>(edgeType["signatures"]);
        var factTypeSignatures = Assert.IsType<JsonArray>(edgeType["fact_type_signatures"]);

        Assert.Equal(signatures.ToJsonString(), factTypeSignatures.ToJsonString());
        Assert.Collection(
            signatures.OfType<JsonObject>(),
            signature =>
            {
                Assert.Equal("Person", signature["source"]?.GetValue<string>());
                Assert.Equal("Organization", signature["target"]?.GetValue<string>());
            },
            signature =>
            {
                Assert.Equal("Person", signature["source"]?.GetValue<string>());
                Assert.Equal("Project", signature["target"]?.GetValue<string>());
            });
    }
}
