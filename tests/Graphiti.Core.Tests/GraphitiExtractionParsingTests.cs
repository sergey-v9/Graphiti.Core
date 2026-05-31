using System.Text.Json.Nodes;

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
}
