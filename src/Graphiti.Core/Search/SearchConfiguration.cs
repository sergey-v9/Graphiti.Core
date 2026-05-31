namespace Graphiti.Core.Search;

/// <summary>Default values shared across the search configuration types.</summary>
public static class SearchConfiguration
{
    /// <summary>Default maximum number of results returned by a search.</summary>
    public const int DefaultSearchLimit = 10;

    /// <summary>Default minimum cosine similarity score for a candidate to be considered.</summary>
    public const double DefaultMinScore = 0.6;

    /// <summary>Default MMR trade-off between relevance and diversity (0 = diversity, 1 = relevance).</summary>
    public const double DefaultMmrLambda = 0.5;

    /// <summary>Default maximum depth for breadth-first graph traversal during search.</summary>
    public const int MaxSearchDepth = 3;
}
