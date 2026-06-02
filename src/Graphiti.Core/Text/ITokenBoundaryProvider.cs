namespace Graphiti.Core.Text;

internal interface ITokenBoundaryProvider
{
    bool TryGetIndexByTokenCount(ReadOnlySpan<char> text, int maxTokens, out int index);

    bool TryGetIndexByTokenCountFromEnd(ReadOnlySpan<char> text, int maxTokens, out int index);
}
