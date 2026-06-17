using System.Text.Json.Nodes;
using Graphiti.Core.Drivers;
using Graphiti.Core.Internal.Helpers;
using Graphiti.Core.Internal.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Graphiti.Core.Tests.Internal;

/// <summary>
/// Covers edge-type signature resolution's endpoint fetch: an edge endpoint absent from the
/// resolved-node set is DB-fetched by UUID only (no group filter) so its real labels participate in
/// signature matching, and an endpoint that is still missing falls back to ["Entity"] labels.
/// Without these, a custom edge type for an override/cross-pair endpoint was silently lost.
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
    public void BuildExtractedEdgeCandidates_UsesFirstRawEpisodeIndexForReferenceTime()
    {
        var primaryReference = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var secondaryReference = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 1, 3, 12, 0, 0, DateTimeKind.Utc);
        var episodes = new[]
        {
            new EpisodicNode { Uuid = "episode-0", ValidAt = primaryReference },
            new EpisodicNode { Uuid = "episode-1", ValidAt = secondaryReference }
        };
        var nodesByExtractedName = new Dictionary<string, EntityNode>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alice"] = new EntityNode { Uuid = "alice-uuid", Name = "Alice", GroupId = "group" },
            ["Acme"] = new EntityNode { Uuid = "acme-uuid", Name = "Acme", GroupId = "group" }
        };

        var candidates = EdgeResolutionService.BuildExtractedEdgeCandidates(
            new[]
            {
                new Graphiti.ExtractedEdge(
                    "Alice",
                    "Acme",
                    "WORKS_AT",
                    "Alice works at Acme.",
                    validAt: null,
                    invalidAt: null,
                    episodeIndices: new[] { 99, 1 })
            },
            nodesByExtractedName,
            episodes,
            "group",
            now,
            out var skippedEdges);

        var edge = Assert.Single(candidates);
        Assert.Equal(0, skippedEdges);
        Assert.Equal(new[] { "episode-1" }, edge.Episodes);
        Assert.Equal(primaryReference, edge.ReferenceTime);
    }

    [Fact]
    public async Task ResolveEntityEdges_FetchesMissingEndpointNode_PreservingCustomEdgeType()
    {
        var driver = new InMemoryGraphDriver();
        // The target endpoint (Acme, an Organization) lives ONLY in the database - it is not part of
        // the resolved-node set passed to resolution. It is DB-fetched before signature matching.
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
    public async Task ResolveEntityEdges_FetchesCrossGroupEndpointNode_PreservingCustomEdgeType()
    {
        var driver = new InMemoryGraphDriver();
        // The target endpoint (Acme, an Organization) lives in a DIFFERENT group than the edge/episode.
        // The endpoint fetch matches by UUID only and ignores group_id, so this cross-group endpoint
        // is still fetched and its real Organization label survives. Scoping the fetch by the edge's
        // group_id would drop it and the
        // custom WORKS_AT edge type (with its confidence attribute) would be silently lost.
        await driver.SaveNodeAsync(new EntityNode
        {
            Uuid = "acme-uuid",
            Name = "Acme",
            GroupId = "other-group",
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
            ["extract_edges.extract_attributes"] = new() { ["confidence"] = 0.88 }
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
            new[] { BuildWorksAtEdge() }, // edge.GroupId = "group", endpoint lives in "other-group"
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
        // The custom edge type matched only because the cross-group Acme endpoint was DB-fetched by
        // UUID (not scoped by group_id) and seen as an Organization; its confidence attribute survived.
        Assert.True(edge.Attributes.ContainsKey("confidence"));
        Assert.Equal(0.88, edge.Attributes["confidence"]);
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
        // DB either) must still match because a missing endpoint falls back to labels=['Entity'].
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
    public async Task ResolveEntityEdges_PreservesDuplicateResolvedEdgeAppearances()
    {
        var driver = new InMemoryGraphDriver();
        var alice = new EntityNode
        {
            Uuid = "alice-uuid",
            Name = "Alice",
            GroupId = "group",
            Labels = new List<string> { "Entity", "Person" }
        };
        var acme = new EntityNode
        {
            Uuid = "acme-uuid",
            Name = "Acme",
            GroupId = "group",
            Labels = new List<string> { "Entity", "Organization" }
        };
        await driver.SaveNodeAsync(alice);
        await driver.SaveNodeAsync(acme);
        var existing = new EntityEdge
        {
            Uuid = "existing-edge",
            SourceNodeUuid = alice.Uuid,
            TargetNodeUuid = acme.Uuid,
            Name = "WORKS_AT",
            Fact = "Alice works at Acme.",
            GroupId = "group"
        };
        await driver.SaveEdgeAsync(existing);

        var llm = new PromptResponseLlmClient(new Dictionary<string, JsonObject>
        {
            ["dedupe_edges.resolve_edge"] = new()
            {
                ["duplicate_facts"] = new JsonArray { 0 },
                ["contradicted_facts"] = new JsonArray()
            }
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
        var newEdgeUuids = new HashSet<string>(StringComparer.Ordinal);

        var resolved = await service.ResolveEntityEdgesAsync(
            new[]
            {
                new EntityEdge
                {
                    Uuid = "candidate-1",
                    SourceNodeUuid = alice.Uuid,
                    TargetNodeUuid = acme.Uuid,
                    Name = "WORKS_AT",
                    Fact = "Alice works at Acme.",
                    GroupId = "group"
                },
                new EntityEdge
                {
                    Uuid = "candidate-2",
                    SourceNodeUuid = alice.Uuid,
                    TargetNodeUuid = acme.Uuid,
                    Name = "WORKS_AT",
                    Fact = "Alice works at Acme as an engineer.",
                    GroupId = "group"
                }
            },
            episode,
            "group",
            now: episode.ValidAt,
            CancellationToken.None,
            existingEdgesOverride: null,
            nodes: new[] { alice, acme },
            newlyCreatedEdgeUuids: newEdgeUuids);

        Assert.Equal(new[] { existing.Uuid, existing.Uuid }, resolved.Select(edge => edge.Uuid));
        Assert.Empty(newEdgeUuids);
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

    [Fact]
    public async Task ResolveEdgeWithLlm_ExactDuplicateFastPathScansRelatedEdgesBeforePrompt()
    {
        var driver = new InMemoryGraphDriver();
        var llm = new PromptResponseLlmClient(new Dictionary<string, JsonObject>());
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
        var existing = BuildWorksAtEdge();
        existing.Uuid = "existing-edge";
        existing.Fact = "Alice knows Acme.";
        var extracted = BuildWorksAtEdge();
        extracted.Uuid = "new-edge";
        extracted.Fact = " Alice   knows Acme. ";

        var (resolved, invalidated) = await service.ResolveEdgeWithLlmAsync(
            extracted,
            new[] { existing },
            Array.Empty<EntityEdge>(),
            episode,
            CancellationToken.None);

        Assert.Equal(existing.Uuid, resolved.Uuid);
        Assert.Empty(invalidated);
        Assert.Contains(episode.Uuid, existing.Episodes);
        Assert.DoesNotContain("dedupe_edges.resolve_edge", llm.PromptNames);
    }

    [Fact]
    public async Task ResolveEdgeWithLlm_UsesFreshResolutionTimeForEachExpiredEdge()
    {
        var driver = new InMemoryGraphDriver();
        var resolutionTimes = new[]
        {
            new DateTime(2026, 4, 1, 12, 0, 1, DateTimeKind.Utc),
            new DateTime(2026, 4, 1, 12, 0, 2, DateTimeKind.Utc)
        };
        var nextTimeIndex = -1;
        DateTime NextResolutionTime()
        {
            var index = Interlocked.Increment(ref nextTimeIndex);
            return resolutionTimes[Math.Min(index, resolutionTimes.Length - 1)];
        }

        var llm = new PromptResponseLlmClient(new Dictionary<string, JsonObject>
        {
            ["dedupe_edges.resolve_edge"] = new()
            {
                ["duplicate_facts"] = new JsonArray(),
                ["contradicted_facts"] = new JsonArray()
            }
        });
        var service = new EdgeResolutionService(
            () => driver,
            new GraphitiClients(driver, llm, new HashEmbedder(2), new IdentityCrossEncoderClient()),
            llm,
            NullLogger.Instance,
            utcNow: NextResolutionTime);
        var episode = new EpisodicNode
        {
            Uuid = "episode-1",
            Name = "episode",
            Content = "Alice plans work.",
            Source = EpisodeType.Message,
            GroupId = "group",
            ValidAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var existingCandidate = new EntityEdge
        {
            Uuid = "candidate",
            SourceNodeUuid = "alice-uuid",
            TargetNodeUuid = "acme-uuid",
            GroupId = "group",
            Name = "WORKS_AT",
            Fact = "Alice works at Acme.",
            ValidAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var first = BuildWorksAtEdge();
        first.Uuid = "first";
        first.Fact = "Alice worked at Acme until February.";
        first.ValidAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        first.InvalidAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var second = BuildWorksAtEdge();
        second.Uuid = "second";
        second.Fact = "Alice consulted for Acme until March.";
        second.ValidAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        second.InvalidAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        var (firstResolved, _) = await service.ResolveEdgeWithLlmAsync(
            first,
            Array.Empty<EntityEdge>(),
            new[] { existingCandidate },
            episode,
            CancellationToken.None);
        var (secondResolved, _) = await service.ResolveEdgeWithLlmAsync(
            second,
            Array.Empty<EntityEdge>(),
            new[] { existingCandidate },
            episode,
            CancellationToken.None);

        Assert.Equal(resolutionTimes[0], firstResolved.ExpiredAt);
        Assert.Equal(resolutionTimes[1], secondResolved.ExpiredAt);
    }

    [Fact]
    public async Task ResolveEdgeWithLlm_SortsInvalidatedEdgesByValidAt()
    {
        var driver = new InMemoryGraphDriver();
        var fixedNow = new DateTime(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var llm = new PromptResponseLlmClient(new Dictionary<string, JsonObject>
        {
            ["dedupe_edges.resolve_edge"] = new()
            {
                ["duplicate_facts"] = new JsonArray(),
                ["contradicted_facts"] = new JsonArray { 0, 1 }
            }
        });
        var service = new EdgeResolutionService(
            () => driver,
            new GraphitiClients(driver, llm, new HashEmbedder(2), new IdentityCrossEncoderClient()),
            llm,
            NullLogger.Instance,
            utcNow: () => fixedNow);
        var episode = new EpisodicNode
        {
            Uuid = "episode-1",
            Name = "episode",
            Content = "Alice joined a new team.",
            Source = EpisodeType.Message,
            GroupId = "group",
            ValidAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var resolved = BuildWorksAtEdge();
        resolved.Uuid = "new-edge";
        resolved.Fact = "Alice joined the platform team.";
        resolved.ValidAt = episode.ValidAt;
        var februaryCandidate = BuildWorksAtEdge();
        februaryCandidate.Uuid = "february";
        februaryCandidate.Fact = "Alice joined the data team.";
        februaryCandidate.ValidAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var januaryCandidate = BuildWorksAtEdge();
        januaryCandidate.Uuid = "january";
        januaryCandidate.Fact = "Alice joined the support team.";
        januaryCandidate.ValidAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var (_, invalidated) = await service.ResolveEdgeWithLlmAsync(
            resolved,
            Array.Empty<EntityEdge>(),
            new[] { februaryCandidate, januaryCandidate },
            episode,
            CancellationToken.None);

        Assert.Equal(new[] { januaryCandidate, februaryCandidate }, invalidated);
        Assert.Equal(resolved.ValidAt, januaryCandidate.InvalidAt);
        Assert.Equal(resolved.ValidAt, februaryCandidate.InvalidAt);
        Assert.Equal(fixedNow, januaryCandidate.ExpiredAt);
        Assert.Equal(fixedNow, februaryCandidate.ExpiredAt);
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
