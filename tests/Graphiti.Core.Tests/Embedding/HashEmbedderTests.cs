using Graphiti.Core;

namespace Graphiti.Core.Tests.Embedding;

public class HashEmbedderTests
{
    [Fact]
    public async Task HashEmbedder_ProducesDeterministicCaseInsensitiveVectors()
    {
        var first = new HashEmbedder(embeddingDimension: 16);
        var second = new HashEmbedder(embeddingDimension: 16);

        var firstVector = await first.CreateAsync("Alice likes Bob");
        var secondVector = await second.CreateAsync("alice LIKES bob");

        Assert.Equal(firstVector, secondVector);
        Assert.Equal(16, firstVector.Count);
    }

    [Fact]
    public async Task HashEmbedder_NormalizesNonEmptyVectors()
    {
        var embedder = new HashEmbedder(embeddingDimension: 32);

        var vector = await embedder.CreateAsync("Alice Alice Bob");
        var magnitude = Math.Sqrt(vector.Sum(value => value * value));

        Assert.Equal(1, magnitude, precision: 5);
    }

    [Fact]
    public async Task HashEmbedder_ReturnsZeroVectorForEmptyInput()
    {
        var embedder = new HashEmbedder(embeddingDimension: 8);

        var vector = await embedder.CreateAsync("");

        Assert.Equal(new float[8], vector);
    }

    [Fact]
    public async Task HashEmbedder_MatchesWhitespaceSeparatorSemantics()
    {
        var embedder = new HashEmbedder(embeddingDimension: 16);

        var spaced = await embedder.CreateAsync("Alice Bob Carol");
        var mixedWhitespace = await embedder.CreateAsync("Alice\tBob\r\nCarol");
        var unicodeWhitespace = await embedder.CreateAsync("Alice\u00a0Bob\u2003Carol");

        Assert.Equal(spaced, mixedWhitespace);
        Assert.Equal(spaced, unicodeWhitespace);
    }

    [Fact]
    public async Task HashEmbedder_HandlesLongTokensThroughPooledBuffers()
    {
        var embedder = new HashEmbedder(embeddingDimension: 32);
        var longToken = new string('a', 600);

        var lower = await embedder.CreateAsync(longToken);
        var upper = await embedder.CreateAsync(longToken.ToUpperInvariant());

        Assert.Equal(lower, upper);
        Assert.Equal(1, Math.Sqrt(lower.Sum(value => value * value)), precision: 5);
    }
}
