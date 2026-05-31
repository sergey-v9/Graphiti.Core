using Graphiti.Core;

namespace Graphiti.Core.Tests.Models;

public class ModelEmbeddingValidationTests
{
    [Fact]
    public async Task EntityNode_GenerateNameEmbeddingRejectsDimensionMismatchBeforeAssignment()
    {
        var node = new EntityNode { Name = "Alice" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => node.GenerateNameEmbeddingAsync(new FixedVectorEmbedder(2, new[] { 1f })));

        Assert.Contains("entity node", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dimension 1", exception.Message, StringComparison.Ordinal);
        Assert.Null(node.NameEmbedding);
    }

    [Fact]
    public async Task CommunityNode_GenerateNameEmbeddingRejectsDimensionMismatchBeforeAssignment()
    {
        var node = new CommunityNode { Name = "Community" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => node.GenerateNameEmbeddingAsync(new FixedVectorEmbedder(3, new[] { 1f, 2f })));

        Assert.Contains("community node", exception.Message, StringComparison.Ordinal);
        Assert.Contains("expected 3", exception.Message, StringComparison.Ordinal);
        Assert.Null(node.NameEmbedding);
    }

    [Fact]
    public async Task EntityEdge_GenerateEmbeddingRejectsDimensionMismatchBeforeAssignment()
    {
        var edge = new EntityEdge { Fact = "Alice knows Bob" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => edge.GenerateEmbeddingAsync(new FixedVectorEmbedder(2, new[] { 1f, 2f, 3f })));

        Assert.Contains("entity edge", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dimension 3", exception.Message, StringComparison.Ordinal);
        Assert.Null(edge.FactEmbedding);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public async Task EntityNode_GenerateNameEmbeddingRejectsNonFiniteValuesBeforeAssignment(
        float value)
    {
        var node = new EntityNode { Name = "Alice" };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => node.GenerateNameEmbeddingAsync(new FixedVectorEmbedder(2, new[] { 1f, value })));

        Assert.Contains("non-finite value", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dimension 1", exception.Message, StringComparison.Ordinal);
        Assert.Null(node.NameEmbedding);
    }

    private sealed class FixedVectorEmbedder : EmbedderClient
    {
        private readonly IReadOnlyList<float> _embedding;

        public FixedVectorEmbedder(int embeddingDimension, IReadOnlyList<float> embedding)
            : base(new EmbedderConfig(embeddingDimension))
        {
            _embedding = embedding;
        }

        public override Task<IReadOnlyList<float>> CreateAsync(
            string input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_embedding);
    }
}
