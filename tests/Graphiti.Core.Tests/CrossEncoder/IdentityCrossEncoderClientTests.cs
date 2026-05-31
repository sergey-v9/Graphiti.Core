namespace Graphiti.Core.Tests.CrossEncoder;

public class IdentityCrossEncoderClientTests
{
    [Fact]
    public async Task RankAsync_UsesTextScoreOrderingForAllPassages()
    {
        var client = new IdentityCrossEncoderClient();
        var passages = new[]
        {
            "gamma",
            "alpha beta beta gamma",
            "beta"
        };

        var ranked = await client.RankAsync("Alpha alpha beta", passages);

        Assert.Equal(new[] { "alpha beta beta gamma", "beta", "gamma" }, ranked.Select(item => item.Passage));
        Assert.Equal(
            passages.Select(passage => SearchUtilities.TextScore("Alpha alpha beta", passage)).OrderDescending(),
            ranked.Select(item => item.Score));
    }

    [Fact]
    public async Task RankIndexedAsync_PreservesOriginalIndexesForDuplicatePassages()
    {
        var client = new IdentityCrossEncoderClient();
        var passages = new[]
        {
            "alpha beta",
            "gamma",
            "alpha beta"
        };

        var ranked = await client.RankIndexedAsync("alpha", passages);

        Assert.Equal(new[] { 0, 2, 1 }, ranked.Select(item => item.Index));
        Assert.Equal(new[] { "alpha beta", "alpha beta", "gamma" }, ranked.Select(item => item.Passage));
        Assert.True(ranked[0].Score > ranked[2].Score);
        Assert.Equal(ranked[0].Score, ranked[1].Score);
    }
}
