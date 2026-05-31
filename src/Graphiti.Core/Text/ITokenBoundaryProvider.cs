namespace Graphiti.Core.Text;

internal interface ITokenBoundaryProvider
{
    bool TryGetIndexByTokenCount(string text, int maxTokens, out int index);

    bool TryGetIndexByTokenCountFromEnd(string text, int maxTokens, out int index);
}
