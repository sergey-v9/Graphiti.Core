using System.Text.Json.Nodes;
using Graphiti.Core.Internal.Helpers;

namespace Graphiti.Core.Tests.Internal;

public class EdgeMergeHelpersTests
{
    [Fact]
    public void ReadIntArray_CoercesNumericStringsAndSkipsInvalidValues()
    {
        var response = new JsonObject
        {
            ["duplicate_facts"] = new JsonArray
            {
                1,
                "2",
                "-3",
                "not-number",
                new JsonObject(),
                null
            }
        };

        var values = EdgeMergeHelpers.ReadIntArray(response, "duplicate_facts");

        Assert.Equal(new[] { 1, 2, -3 }, values);
        Assert.Empty(EdgeMergeHelpers.ReadIntArray(response, "missing"));
        Assert.Empty(EdgeMergeHelpers.ReadIntArray(new JsonObject { ["duplicate_facts"] = "1" }, "duplicate_facts"));
    }
}
