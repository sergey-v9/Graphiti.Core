namespace Graphiti.Core.Search;

internal static class SearchConfigValidator
{
    public static void Validate(SearchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateLimit(config.Limit);

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
    }

    public static void Validate(NodeSearchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateMethods(config.SearchMethods, nameof(NodeSearchConfig.SearchMethods));
        ValidateEnum(config.Reranker, nameof(NodeSearchConfig.Reranker));
    }

    public static void Validate(EpisodeSearchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateMethods(config.SearchMethods, nameof(EpisodeSearchConfig.SearchMethods));
        ValidateEnum(config.Reranker, nameof(EpisodeSearchConfig.Reranker));
    }

    public static void Validate(CommunitySearchConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateMethods(config.SearchMethods, nameof(CommunitySearchConfig.SearchMethods));
        ValidateEnum(config.Reranker, nameof(CommunitySearchConfig.Reranker));
    }

    public static void ValidateLimit(int limit)
    {
        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Search limit must be non-negative.");
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

}
