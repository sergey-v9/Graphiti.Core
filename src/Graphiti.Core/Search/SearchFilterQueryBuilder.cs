namespace Graphiti.Core.Search;

/// <summary>
/// Builds backend query fragments and parameters from <see cref="SearchFilters"/> for node and edge
/// searches, and maps comparison operators to provider-specific operator strings.
/// </summary>
public static class SearchFilterQueryBuilder
{
    /// <summary>Maps a comparison operator to its OpenSearch range-operator keyword.</summary>
    public static string CypherToOpenSearchOperator(ComparisonOperator op) =>
        op switch
        {
            ComparisonOperator.GreaterThan => "gt",
            ComparisonOperator.LessThan => "lt",
            ComparisonOperator.GreaterThanEqual => "gte",
            ComparisonOperator.LessThanEqual => "lte",
            _ => op.ToWireValue()
        };

    /// <summary>Builds the node-search filter query fragments and parameters for a provider.</summary>
    public static (List<string> FilterQueries, Dictionary<string, object?> FilterParams)
        NodeSearchFilterQueryConstructor(SearchFilters filters, GraphProvider provider) =>
        CompiledSearchFilter.Compile(filters).BuildNodeQuery(provider);

    /// <summary>Builds a single date-comparison query fragment for a field/parameter pair.</summary>
    public static string DateFilterQueryConstructor(
        string valueName,
        string paramName,
        ComparisonOperator comparisonOperator)
    {
        if (comparisonOperator is ComparisonOperator.IsNull or ComparisonOperator.IsNotNull)
        {
            return $"({valueName} {comparisonOperator.ToWireValue()})";
        }

        return $"({valueName} {comparisonOperator.ToWireValue()} {paramName})";
    }

    /// <summary>Builds the edge-search filter query fragments and parameters for a provider.</summary>
    public static (List<string> FilterQueries, Dictionary<string, object?> FilterParams)
        EdgeSearchFilterQueryConstructor(SearchFilters filters, GraphProvider provider) =>
        CompiledSearchFilter.Compile(filters).BuildEdgeQuery(provider);
}
