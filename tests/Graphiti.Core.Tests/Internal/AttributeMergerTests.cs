using System.Text.Json.Nodes;
using Graphiti.Core.Internal.Helpers;
using Graphiti.Core.Models;

namespace Graphiti.Core.Tests.Internal;

public class AttributeMergerTests
{
    [Fact]
    public void ReplaceExtractedAttributes_RestoresPriorValuesForDroppedFields()
    {
        var prior = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["role"] = "existing role",
            ["stale"] = "remove me"
        };
        var entityType = new EntityTypeDefinition(
            "Person",
            attributes: new Dictionary<string, EntityAttributeDefinition>
            {
                ["role"] = new("Role"),
                ["confidence"] = new("Confidence", "number")
            });
        var response = new JsonObject
        {
            ["role"] = new string('x', 251),
            ["confidence"] = 0.87
        };

        var merged = AttributeMerger.ReplaceExtractedAttributes(prior, entityType, response);

        Assert.Equal("existing role", merged["role"]);
        Assert.Equal(0.87, merged["confidence"]);
        Assert.False(merged.ContainsKey("stale"));
    }

    [Fact]
    public void OverlayExtractedAttributes_DropsOverlongFieldsWithoutClearingPriorValues()
    {
        var node = new EntityNode
        {
            Name = "Alice",
            Attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["city"] = "Paris",
                ["department"] = "Research"
            }
        };
        var entityType = new EntityTypeDefinition(
            "Person",
            attributes: new Dictionary<string, EntityAttributeDefinition>
            {
                ["role"] = new("Role"),
                ["city"] = new("City")
            });
        var response = new JsonObject
        {
            ["role"] = "lead",
            ["city"] = new string('x', 251)
        };

        AttributeMerger.OverlayExtractedAttributes(node, entityType, response);

        Assert.Equal("lead", node.Attributes["role"]);
        Assert.Equal("Paris", node.Attributes["city"]);
        Assert.Equal("Research", node.Attributes["department"]);
    }

}
