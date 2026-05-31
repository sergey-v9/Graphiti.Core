namespace Graphiti.Core.Search;

internal static class SearchConfigValidator
{
    public static void Validate(SearchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateLimit(config.Limit);
        ValidateFinite(config.RerankerMinScore, nameof(SearchConfig.RerankerMinScore));

        if (config.EdgeConfig is not null)
        {
            Validate(config.EdgeConfig);
        }

        if (config.NodeConfig is not null)
        {
            Validate(config.NodeConfig);
        }

        if (config.EpisodeConfig is not null)
        {
            Validate(config.EpisodeConfig);
        }

        if (config.CommunityConfig is not null)
        {
            Validate(config.CommunityConfig);
        }
    }

    public static void Validate(EdgeSearchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateMethods(config.SearchMethods, nameof(EdgeSearchConfig.SearchMethods));
        ValidateEnum(config.Reranker, nameof(EdgeSearchConfig.Reranker));
        ValidateFiniteRange(config.SimMinScore, nameof(EdgeSearchConfig.SimMinScore), -1, 1);
        ValidateFiniteRange(config.MmrLambda, nameof(EdgeSearchConfig.MmrLambda), 0, 1);
        ValidateBfsMaxDepth(config.BfsMaxDepth, nameof(EdgeSearchConfig.BfsMaxDepth));
    }

    public static void Validate(NodeSearchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateMethods(config.SearchMethods, nameof(NodeSearchConfig.SearchMethods));
        ValidateEnum(config.Reranker, nameof(NodeSearchConfig.Reranker));
        ValidateFiniteRange(config.SimMinScore, nameof(NodeSearchConfig.SimMinScore), -1, 1);
        ValidateFiniteRange(config.MmrLambda, nameof(NodeSearchConfig.MmrLambda), 0, 1);
        ValidateBfsMaxDepth(config.BfsMaxDepth, nameof(NodeSearchConfig.BfsMaxDepth));
    }

    public static void Validate(EpisodeSearchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateMethods(config.SearchMethods, nameof(EpisodeSearchConfig.SearchMethods));
        ValidateEnum(config.Reranker, nameof(EpisodeSearchConfig.Reranker));
        ValidateFiniteRange(config.SimMinScore, nameof(EpisodeSearchConfig.SimMinScore), -1, 1);
        ValidateFiniteRange(config.MmrLambda, nameof(EpisodeSearchConfig.MmrLambda), 0, 1);
        ValidateBfsMaxDepth(config.BfsMaxDepth, nameof(EpisodeSearchConfig.BfsMaxDepth));
    }

    public static void Validate(CommunitySearchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateMethods(config.SearchMethods, nameof(CommunitySearchConfig.SearchMethods));
        ValidateEnum(config.Reranker, nameof(CommunitySearchConfig.Reranker));
        ValidateFiniteRange(config.SimMinScore, nameof(CommunitySearchConfig.SimMinScore), -1, 1);
        ValidateFiniteRange(config.MmrLambda, nameof(CommunitySearchConfig.MmrLambda), 0, 1);
        ValidateBfsMaxDepth(config.BfsMaxDepth, nameof(CommunitySearchConfig.BfsMaxDepth));
    }

    public static void ValidateLimit(int limit)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Search limit must be greater than zero.");
        }
    }

    private static void ValidateMethods<TEnum>(IReadOnlyList<TEnum>? methods, string parameterName)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(methods, parameterName);
        foreach (var method in methods)
        {
            if (!Enum.IsDefined(method))
            {
                throw new ArgumentOutOfRangeException(parameterName, method, $"Unknown {typeof(TEnum).Name} value.");
            }
        }
    }

    private static void ValidateEnum<TEnum>(TEnum value, string parameterName)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"Unknown {typeof(TEnum).Name} value.");
        }
    }

    private static void ValidateBfsMaxDepth(int value, string parameterName)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "BFS max depth must be at least one.");
        }
    }

    private static void ValidateFiniteRange(double value, string parameterName, double minInclusive, double maxInclusive)
    {
        ValidateFinite(value, parameterName);
        if (value < minInclusive || value > maxInclusive)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"{parameterName} must be between {minInclusive} and {maxInclusive}.");
        }
    }

    private static void ValidateFinite(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, $"{parameterName} must be finite.");
        }
    }
}
