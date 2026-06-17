namespace Graphiti.Core.Drivers.Ladybug;

internal static class LadybugSearchFilter
{
    internal static (List<string> FilterQueries, Dictionary<string, object?> FilterParams)
        BuildNodeQuery(SearchFilters searchFilter)
    {
        ArgumentNullException.ThrowIfNull(searchFilter);
        var labels = searchFilter.NodeLabels;
        var (filterQueries, filterParams) = CompiledSearchFilter
            .Compile(WithoutNodeLabels(searchFilter))
            .BuildNodeQuery();
        AddLabelFilter(filterQueries, filterParams, labels, includeTarget: false, insertIndex: 0);
        return (filterQueries, filterParams);
    }

    internal static (List<string> FilterQueries, Dictionary<string, object?> FilterParams)
        BuildEdgeQuery(SearchFilters searchFilter)
    {
        ArgumentNullException.ThrowIfNull(searchFilter);
        var labels = searchFilter.NodeLabels;
        var (filterQueries, filterParams) = CompiledSearchFilter
            .Compile(WithoutNodeLabels(searchFilter))
            .BuildEdgeQuery();
        var insertIndex = (searchFilter.EdgeTypes is not null ? 1 : 0)
            + (searchFilter.EdgeUuids is not null ? 1 : 0);
        AddLabelFilter(filterQueries, filterParams, labels, includeTarget: true, insertIndex);
        return (filterQueries, filterParams);
    }

    private static void AddLabelFilter(
        List<string> filterQueries,
        Dictionary<string, object?> filterParams,
        List<string>? labels,
        bool includeTarget,
        int insertIndex)
    {
        if (labels is not { Count: > 0 })
        {
            return;
        }

        GraphitiHelpers.ValidateNodeLabels(labels);
        filterQueries.Insert(
            insertIndex,
            includeTarget
                ? "list_has_all(n.labels, $labels) AND list_has_all(m.labels, $labels)"
                : "list_has_all(n.labels, $labels)");
        filterParams["labels"] = labels;
    }

    private static SearchFilters WithoutNodeLabels(SearchFilters source) =>
        new()
        {
            EdgeTypes = source.EdgeTypes,
            EdgeUuids = source.EdgeUuids,
            ValidAt = source.ValidAt,
            InvalidAt = source.InvalidAt,
            CreatedAt = source.CreatedAt,
            ExpiredAt = source.ExpiredAt,
            PropertyFilters = source.PropertyFilters
        };
}
