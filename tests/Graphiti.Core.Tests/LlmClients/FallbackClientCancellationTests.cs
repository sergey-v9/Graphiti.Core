using Graphiti.Core;

namespace Graphiti.Core.Tests.LlmClients;

public class FallbackClientCancellationTests
{
    [Fact]
    public async Task FallbackClientsObserveCanceledToken()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var token = cancellation.Token;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new NoOpLlmClient().GenerateResponseAsync(
                new[] { new Message("user", "hello") },
                cancellationToken: token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new HashEmbedder(embeddingDimension: 8).CreateAsync("hello world", token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new IdentityCrossEncoderClient().RankAsync("hello", new[] { "hello world" }, token));
    }
}
