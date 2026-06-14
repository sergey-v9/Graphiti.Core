using System.Collections;
using System.Text.Json.Nodes;
using Graphiti.Core.Internal.Helpers;

namespace Graphiti.Core.Tests;

public class GraphitiExtractionParsingTests
{
    [Fact]
    public void ExtractEntityNames_UsesJsonTextFallbackForNonStringValues()
    {
        var response = new JsonObject
        {
            ["extracted_entities"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = 123,
                    ["entity_type"] = new JsonObject { ["kind"] = "Person" }
                }
            }
        };

        var extracted = Assert.Single(Graphiti.ExtractEntityNames(response, entityTypes: null));

        Assert.Equal("123", extracted.Name);
        Assert.Equal("{\"kind\":\"Person\"}", extracted.Type);
    }

    [Fact]
    public void ExtractEntityNames_CoercesNumericStringEntityTypeIds()
    {
        var response = new JsonObject
        {
            ["extracted_entities"] = new JsonArray
            {
                new JsonObject { ["name"] = "Alice", ["entity_type_id"] = "1" },
                new JsonObject { ["name"] = "Generic", ["entity_type_id"] = "0" },
                new JsonObject { ["name"] = "Out of range", ["entity_type_id"] = "9" },
                new JsonObject { ["name"] = "Invalid", ["entity_type_id"] = "not-number" }
            }
        };

        var extracted = Graphiti.ExtractEntityNames(
            response,
            new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new("Person")
            });

        Assert.Collection(
            extracted,
            item => Assert.Equal(("Alice", "Person"), item),
            item => Assert.Equal(("Generic", "Entity"), item),
            item => Assert.Equal(("Out of range", "Entity"), item),
            item => Assert.Equal(("Invalid", "Entity"), item));
    }

    [Fact]
    public void ExtractEntityNames_DoesNotEnumerateEntityTypesWhenTypeIsExplicit()
    {
        var response = new JsonObject
        {
            ["extracted_entities"] = new JsonArray
            {
                new JsonObject { ["name"] = "Alice", ["entity_type"] = "Person" },
                new JsonObject { ["name"] = "Acme", ["type"] = "Organization" }
            }
        };

        var extracted = Graphiti.ExtractEntityNames(
            response,
            new ThrowingEnumerationEntityTypes());

        Assert.Equal(new[] { ("Alice", "Person"), ("Acme", "Organization") }, extracted);
    }

    [Fact]
    public void ExtractEntities_PreservesEpisodeIndicesAndDefaultsMissingIndices()
    {
        var response = new JsonObject
        {
            ["extracted_entities"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Alice",
                    ["entity_type"] = "Person",
                    ["episode_indices"] = new JsonArray { "2", 0, -1 }
                },
                new JsonObject { ["name"] = "Bob", ["entity_type"] = "Person" },
                new JsonObject
                {
                    ["name"] = "Carol",
                    ["entity_type"] = "Person",
                    ["episode_indices"] = new JsonArray()
                }
            }
        };

        var extracted = Graphiti.ExtractEntities(response, entityTypes: null);

        Assert.Collection(
            extracted,
            item =>
            {
                Assert.Equal("Alice", item.Name);
                Assert.Equal(new[] { 2, 0, -1 }, item.EpisodeIndices);
            },
            item => Assert.Equal(new[] { 0 }, item.EpisodeIndices),
            item => Assert.Empty(item.EpisodeIndices));
    }

    [Fact]
    public void ExtractEdges_UsesJsonTextFallbackForNonStringValues()
    {
        var response = new JsonObject
        {
            ["edges"] = new JsonArray
            {
                new JsonObject
                {
                    ["source"] = 123,
                    ["target"] = true,
                    ["name"] = new JsonArray { "RELATES_TO" },
                    ["fact"] = new JsonObject { ["text"] = "Alice knows Bob." }
                }
            }
        };

        var edge = Assert.Single(Graphiti.ExtractEdges(response));

        Assert.Equal("123", edge.SourceName);
        Assert.Equal("true", edge.TargetName);
        Assert.Equal("[\"RELATES_TO\"]", edge.RelationType);
        Assert.Equal("{\"text\":\"Alice knows Bob.\"}", edge.Fact);
    }

    [Fact]
    public void ExtractEdges_ParsesValidDatesAndIgnoresInvalidDates()
    {
        var response = new JsonObject
        {
            ["edges"] = new JsonArray
            {
                new JsonObject
                {
                    ["source"] = "Alice",
                    ["target"] = "Acme",
                    ["relation_type"] = "WORKED_AT",
                    ["fact"] = "Alice worked at Acme.",
                    ["valid_at"] = "2026-01-02T03:04:05+01:30",
                    ["invalid_at"] = "not-a-date"
                }
            }
        };

        var edge = Assert.Single(Graphiti.ExtractEdges(response));

        Assert.Equal(new DateTime(2026, 1, 2, 1, 34, 5, DateTimeKind.Utc), edge.ValidAt);
        Assert.Null(edge.InvalidAt);
    }

    [Fact]
    public void ExtractEdges_SkipsEdgesMissingRelationTypeInsteadOfFabricatingRelatesTo()
    {
        // relation_type is a required Pydantic field on Edge (extract_edges.py:32-35) and CombinedFact
        // (extract_nodes_and_edges.py:44-48); Python rejects a response whose edge lacks one. The C#
        // parser must NOT invent a "RELATES_TO" default — it skips the edge instead (no-fabrication
        // parity contract in .agents/notes/decisions.md).
        var response = new JsonObject
        {
            ["edges"] = new JsonArray
            {
                // No relation_type and no `name` alias -> must be skipped, not defaulted.
                new JsonObject
                {
                    ["source"] = "Alice",
                    ["target"] = "Bob",
                    ["fact"] = "Alice knows Bob."
                },
                // Blank relation_type -> also skipped (treated as absent).
                new JsonObject
                {
                    ["source_entity_name"] = "Carol",
                    ["target_entity_name"] = "Dave",
                    ["relation_type"] = "   ",
                    ["fact"] = "Carol knows Dave."
                },
                // Valid relation_type -> survives.
                new JsonObject
                {
                    ["source"] = "Eve",
                    ["target"] = "Acme",
                    ["relation_type"] = "WORKS_AT",
                    ["fact"] = "Eve works at Acme."
                },
                // `name` alias supplies the relation_type -> survives.
                new JsonObject
                {
                    ["source"] = "Frank",
                    ["target"] = "Globex",
                    ["name"] = "FOUNDED",
                    ["fact"] = "Frank founded Globex."
                }
            }
        };

        var edges = Graphiti.ExtractEdges(response);

        Assert.Collection(
            edges,
            item =>
            {
                Assert.Equal("Eve", item.SourceName);
                Assert.Equal("WORKS_AT", item.RelationType);
            },
            item =>
            {
                Assert.Equal("Frank", item.SourceName);
                Assert.Equal("FOUNDED", item.RelationType);
            });
        Assert.DoesNotContain(edges, e => e.RelationType == "RELATES_TO");
    }

    [Fact]
    public void ExtractEdges_PreservesEpisodeIndicesAndDefaultsMissingIndices()
    {
        var response = new JsonObject
        {
            ["edges"] = new JsonArray
            {
                new JsonObject
                {
                    ["source"] = "Alice",
                    ["target"] = "Bob",
                    ["relation_type"] = "KNOWS",
                    ["fact"] = "Alice knows Bob.",
                    ["episode_indices"] = new JsonArray { 1, "0", 99 }
                },
                new JsonObject
                {
                    ["source"] = "Alice",
                    ["target"] = "Acme",
                    ["relation_type"] = "WORKS_AT",
                    ["fact"] = "Alice works at Acme."
                },
                new JsonObject
                {
                    ["source"] = "Bob",
                    ["target"] = "Acme",
                    ["relation_type"] = "WORKS_AT",
                    ["fact"] = "Bob works at Acme.",
                    ["episode_indices"] = new JsonArray()
                }
            }
        };

        var edges = Graphiti.ExtractEdges(response);

        Assert.Collection(
            edges,
            item => Assert.Equal(new[] { 1, 0, 99 }, item.EpisodeIndices),
            item => Assert.Equal(new[] { 0 }, item.EpisodeIndices),
            item => Assert.Empty(item.EpisodeIndices));
    }

    [Fact]
    public void EpisodeAttribution_NormalizesIndicesLikePythonExtraction()
    {
        Assert.Equal(
            new[] { 2, 0 },
            EpisodeAttribution.NormalizeIndices(new[] { 2, 0, -1, 99 }, episodeCount: 3));
        Assert.Equal(
            new[] { 0, 1, 2 },
            EpisodeAttribution.NormalizeIndices(Array.Empty<int>(), episodeCount: 3));
        Assert.Equal(
            new[] { 0, 1 },
            EpisodeAttribution.NormalizeIndices(null, episodeCount: 2));
    }

    [Fact]
    public void EpisodeAttribution_ReferenceTimeUsesFirstRawIndexLikePython()
    {
        var fallback = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var second = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);
        var episodes = new[]
        {
            new EpisodicNode { Uuid = "episode-0", ValidAt = fallback },
            new EpisodicNode { Uuid = "episode-1", ValidAt = second }
        };

        Assert.Equal(
            second,
            EpisodeAttribution.ReferenceTimeForFirstIndex(new[] { 1, 0 }, episodes, fallback));
        Assert.Equal(
            fallback,
            EpisodeAttribution.ReferenceTimeForFirstIndex(new[] { 99, 1 }, episodes, fallback));
        Assert.Equal(
            fallback,
            EpisodeAttribution.ReferenceTimeForFirstIndex(Array.Empty<int>(), episodes, fallback));
        Assert.Equal(
            fallback,
            EpisodeAttribution.ReferenceTimeForFirstIndex(null, episodes, fallback));
    }

    private sealed class ThrowingEnumerationEntityTypes : IReadOnlyDictionary<string, EntityTypeDefinition>
    {
        public EntityTypeDefinition this[string key] => throw new NotSupportedException();

        public IEnumerable<string> Keys => throw new NotSupportedException();

        public IEnumerable<EntityTypeDefinition> Values => throw new NotSupportedException();

        public int Count => 1;

        public bool ContainsKey(string key) => throw new NotSupportedException();

        public IEnumerator<KeyValuePair<string, EntityTypeDefinition>> GetEnumerator() =>
            throw new InvalidOperationException("Entity types should not be enumerated without entity_type_id.");

        public bool TryGetValue(string key, out EntityTypeDefinition value) =>
            throw new NotSupportedException();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
