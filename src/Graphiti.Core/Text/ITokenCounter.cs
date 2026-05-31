namespace Graphiti.Core.Text;

/// <summary>Counts the number of tokens in a piece of text under some tokenization scheme.</summary>
public interface ITokenCounter
{
    /// <summary>Returns the token count for <paramref name="text"/>; 0 for null/empty.</summary>
    int CountTokens(string? text);
}
