using System.Text.Json.Nodes;
using Graphiti.Core.Drivers;
using Graphiti.Core.Internal.Helpers;
using Graphiti.Core.Internal.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Graphiti.Core.Tests.Internal;

/// <summary>
/// Covers the edge-type signature resolution endpoint fetch ported from Python
/// <c>resolve_extracted_edges</c> (edge_operations.py:439-486): an edge endpoint absent from the
/// resolved-node set is DB-fetched (scoped by group_id) so its real labels participate in signature
/// matching, and an endpoint that is still missing falls back to ["Entity"] labels. Without these,
/// a custom edge type for an override/cross-pair endpoint was silently lost.
/// </summary>
public class EdgeResolutionEndpointFetchTests
{
    private static EntityEdge BuildWorksAtEdge() => new()
    {
        Uuid = "edge-1",
        SourceNodeUuid = "alice-uuid",
        TargetNodeUuid = "acme-uuid",
        Name = "WORKS_AT",
        Fact = "Alice works at Acme.",
        GroupId = "group"
    };

    private static (IReadOnlyDictionary<string, EntityTypeDefinition> EdgeTypes,
        IReadOnlyDictionary<(string SourceType, string TargetType), IReadOnlyList<string>> EdgeTypeMap)
        BuildWorksAtOntology() =>
        (new Dictionary<string, EntityTypeDefinition>
        {
            ["WORKS_AT"] = new(
                "WORKS_AT",
                attributes: new Dictionary<string, EntityAttributeDefinition>
                {
                    ["confidence"] = new("Extraction confidence", "number")
                })
        },
        new Dictionary<(string SourceType, string TargetType), IReadOnlyList<string>>
        {
            // Only the (Person, Organization) signature unlocks WORKS_AT; Acme must be seen as an
            // Organization for the custom edge type (and its confidence attribute) to survive.
            [("Person", "Organization")] = new[] { "WORKS_AT" }
        });

    [Fact]
    public async Task ResolveEntityEdges_FetchesMissingEndpointNode_PreservingCustomEdgeType()
    {
        var driver = new InMemoryGraphDriver();
        // The target endpoint (Acme, an Organization) lives ONLY in the database - it is not part of
        // the resolved-node set passed to resolution. Python DB-fetches it before signature matching.
        await driver.SaveNodeAsync(new EntityNode
        {
            Uuid = "acme-uuid",
            Name = "Acme",
            GroupId = "group",
            Labels = new List<string> { "Entity", "Organization" }
        });

        var aliceInSet = new EntityNode
        {
            Uuid = "alice-uuid",
            Name = "Alice",
            GroupId = "group",
            Labels = new List<string> { "Entity", "Person" }
        };

        var llm = new PromptResponseLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_edges.extract_attributes"] = new() { ["confidence"] = 0.91 }
        });
        var service = new EdgeResolutionService(
            () => driver,
            new GraphitiClients(driver, llm, new HashEmbedder(2), new IdentityCrossEncoderClient()),
            llm,
            NullLogger.Instance);

        var episode = new EpisodicNode
        {
            Uuid = "episode-1",
            Name = "episode",
            Content = "Alice works at Acme.",
            Source = EpisodeType.Message,
            GroupId = "group",
            ValidAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };
        var (edgeTypes, edgeTypeMap) = BuildWorksAtOntology();

        var resolved = await service.ResolveEntityEdgesAsync(
            new[] { BuildWorksAtEdge() },
            episode,
            "group",
            now: episode.ValidAt,
            CancellationToken.None,
            existingEdgesOverride: null,
            nodes: new[] { aliceInSet }, // Acme deliberately omitted from the resolved-node set
            edgeTypes,
            edgeTypeMap);

        var edge = Assert.Single(resolved);
        Assert.Equal("WORKS_AT", edge.Name);
        // The custom edge type matched only because the missing Acme endpoint was DB-fetched and seen
        // as an Organization; its declared `confidence` attribute was therefore extracted.
        Assert.True(edge.Attributes.ContainsKey("confidence"));
        Assert.Equal(0.91, edge.Attributes["confidence"]);
        Assert.Contains("extract_edges.extract_attributes", llm.PromptNames);
    }

    [Fact]
    public async Task ResolveEntityEdges_MissingEndpointNotInDb_FallsBackToEntityLabels()
    {
        var driver = new InMemoryGraphDriver();
        var aliceInSet = new EntityNode
        {
            Uuid = "alice-uuid",
            Name = "Alice",
            GroupId = "group",
            Labels = new List<string> { "Entity", "Person" }
        };

        var llm = new PromptResponseLlmClient(new Dictionary<string, JsonObject>
        {
            ["extract_edges.extract_attributes"] = new() { ["confidence"] = 0.5 }
        });
        var service = new EdgeResolutionService(
            () => driver,
            new GraphitiClients(driver, llm, new HashEmbedder(2), new IdentityCrossEncoderClient()),
            llm,
            NullLogger.Instance);

        var episode = new EpisodicNode
        {
            Uuid = "episode-1",
            Name = "episode",
            Content = "Alice works at Acme.",
            Source = EpisodeType.Message,
            GroupId = "group",
            ValidAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };

        // Signature requires (Person, Entity): the missing target endpoint (never saved, so not in the
        // DB either) must still match because Python falls back to labels=['Entity'] for it.
        var edgeTypes = new Dictionary<string, EntityTypeDefinition>
        {
            ["WORKS_AT"] = new(
                "WORKS_AT",
                attributes: new Dictionary<string, EntityAttributeDefinition>
                {
                    ["confidence"] = new("Extraction confidence", "number")
                })
        };
        var edgeTypeMap = new Dictionary<(string SourceType, string TargetType), IReadOnlyList<string>>
        {
            [("Person", "Entity")] = new[] { "WORKS_AT" }
        };

        var resolved = await service.ResolveEntityEdgesAsync(
            new[] { BuildWorksAtEdge() },
            episode,
            "group",
            now: episode.ValidAt,
            CancellationToken.None,
            existingEdgesOverride: null,
            nodes: new[] { aliceInSet },
            edgeTypes,
            edgeTypeMap);

        var edge = Assert.Single(resolved);
        Assert.Equal("WORKS_AT", edge.Name);
        // The ["Entity"] fallback let the (Person, Entity) signature match even though the target node
        // is absent from both the resolved-node set and the DB.
        Assert.True(edge.Attributes.ContainsKey("confidence"));
        Assert.Equal(0.5, edge.Attributes["confidence"]);
    }

    [Fact]
    public void FindEdgeTypeDefinition_MissingEndpoint_UsesEntityFallbackInsteadOfReturningNull()
    {
        var edge = BuildWorksAtEdge();
        var edgeTypes = new Dictionary<string, EntityTypeDefinition>
        {
            ["WORKS_AT"] = new("WORKS_AT")
        };

        // Only the source is present; the target endpoint is absent from nodesByUuid entirely.
        var nodesByUuid = new Dictionary<string, EntityNode>(StringComparer.Ordinal)
        {
            ["alice-uuid"] = new EntityNode
            {
                Uuid = "alice-uuid",
                Name = "Alice",
                GroupId = "group",
                Labels = new List<string> { "Entity", "Person" }
            }
        };

        // (Person, Entity) matches because the missing target falls back to ["Entity"].
        var matched = EntityTypeResolver.FindEdgeTypeDefinition(
            edge,
            nodesByUuid,
            edgeTypes,
            new Dictionary<(string SourceType, string TargetType), IReadOnlyList<string>>
            {
                [("Person", "Entity")] = new[] { "WORKS_AT" }
            });
        Assert.NotNull(matched);

        // (Person, Organization) does NOT match: the Entity-only fallback is not an Organization.
        var notMatched = EntityTypeResolver.FindEdgeTypeDefinition(
            edge,
            nodesByUuid,
            edgeTypes,
            new Dictionary<(string SourceType, string TargetType), IReadOnlyList<string>>
            {
                [("Person", "Organization")] = new[] { "WORKS_AT" }
            });
        Assert.Null(notMatched);
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
