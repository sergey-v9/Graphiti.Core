using Graphiti.Core.Internal.Helpers;

namespace Graphiti.Core.Tests.Internal;

public class ExtractionContextBuilderTests
{
    [Fact]
    public void BuildAttributeResponseSchema_RendersRequiredAndMaxLengthMetadata()
    {
        var typeDefinition = new EntityTypeDefinition(
            "Project",
            attributes: new Dictionary<string, EntityAttributeDefinition>
            {
                ["score"] = new("Confidence score", "number", required: true),
                ["name"] = new("Display name", maxLength: 80, required: true),
                ["bio"] = new("Short biography", maxLength: 500, required: false)
            });

        var schema = ExtractionContextBuilder.BuildAttributeResponseSchema(
            typeDefinition,
            "AttributeSchemaProbe");

        Assert.Equal(
            """{"type":"object","additionalProperties":false,"required":["attributes"],"properties":{"attributes":{"type":"object","additionalProperties":false,"required":["name","score"],"properties":{"bio":{"type":["string","null"],"description":"Short biography","maxLength":500},"name":{"type":["string","null"],"description":"Display name","maxLength":80},"score":{"type":["number","null"],"description":"Confidence score"}}}}}""",
            schema.SchemaElement.GetRawText());
    }
}
