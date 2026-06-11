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
                    ["fact"] = "Alice knows Bob.",
                    ["episode_indices"] = new JsonArray { 1, "0", 99 }
                },
                new JsonObject
                {
                    ["source"] = "Alice",
                    ["target"] = "Acme",
                    ["fact"] = "Alice works at Acme."
                },
                new JsonObject
                {
                    ["source"] = "Bob",
                    ["target"] = "Acme",
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
    public void EpisodeAttribution_RemapsExtractedNodeIndicesToResolvedNodes()
    {
        var singleRemap = EpisodeAttribution.RemapNodeIndexMap(
            new[] { new EntityNode { Uuid = "ordered-extracted", Name = "Ordered" } },
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["ordered-extracted"] = new[] { 2, 0 }
            },
            new Dictionary<string, EntityNode>(StringComparer.OrdinalIgnoreCase)
            {
                ["Ordered"] = new EntityNode { Uuid = "ordered-resolved", Name = "Ordered" }
            });

        Assert.Equal(new[] { 2, 0 }, Assert.Single(singleRemap).Value);

        var firstExtracted = new EntityNode { Uuid = "first-extracted", Name = "Alice" };
        var secondExtracted = new EntityNode { Uuid = "second-extracted", Name = "Alicia" };
        var resolved = new EntityNode { Uuid = "resolved-alice", Name = "Alice" };

        var remapped = EpisodeAttribution.RemapNodeIndexMap(
            new[] { firstExtracted, secondExtracted },
            new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
            {
                ["first-extracted"] = new[] { 2, 0 },
                ["second-extracted"] = new[] { 1, 2 }
            },
            new Dictionary<string, EntityNode>(StringComparer.OrdinalIgnoreCase)
            {
                ["Alice"] = resolved,
                ["Alicia"] = resolved
            });

        Assert.Equal(new[] { 0, 1, 2 }, Assert.Single(remapped).Value);
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
