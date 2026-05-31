namespace Graphiti.Core;

public sealed partial class Graphiti
{
    /// <summary>
    /// Regenerates a saga's rolling summary from its episodes (incrementally from the last
    /// summarization watermark) and persists the updated <see cref="SagaNode"/>.
    /// </summary>
    public Task<SagaNode> SummarizeSagaAsync(string sagaId, CancellationToken cancellationToken = default) =>
        _sagaService.SummarizeSagaAsync(sagaId, cancellationToken);
}
