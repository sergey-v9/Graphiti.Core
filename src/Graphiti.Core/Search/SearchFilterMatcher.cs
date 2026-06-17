namespace Graphiti.Core.Search;

/// <summary>
/// In-memory evaluation of <see cref="SearchFilters"/> against nodes and edges, used by drivers that
/// filter client-side (such as the in-memory driver) rather than pushing predicates to the backend.
/// </summary>
internal static class SearchFilterMatcher
{
    public static bool NodeMatches(EntityNode node, SearchFilters filters) =>
        CompiledSearchFilter.Compile(filters).NodeMatches(node);

    public static bool NodeMatches(EntityNode node, CompiledSearchFilter filter) =>
        filter.NodeMatches(node);

    public static bool EdgeMatches(EntityEdge edge, SearchFilters filters) =>
        CompiledSearchFilter.Compile(filters).EdgeMatches(edge);

    public static bool EdgeMatches(EntityEdge edge, CompiledSearchFilter filter) =>
        filter.EdgeMatches(edge);

    public static bool EdgeMatches(
        EntityEdge edge,
        SearchFilters filters,
        IReadOnlyDictionary<string, EntityNode> nodesByUuid) =>
        CompiledSearchFilter.Compile(filters).EdgeMatches(edge, nodesByUuid);

    public static bool EdgeMatches(
        EntityEdge edge,
        CompiledSearchFilter filter,
        IReadOnlyDictionary<string, EntityNode> nodesByUuid) =>
        filter.EdgeMatches(edge, nodesByUuid);

    public static bool EdgeEndpointLabelsMatch(
        EntityEdge edge,
        SearchFilters filters,
        IReadOnlyDictionary<string, EntityNode> nodesByUuid) =>
        CompiledSearchFilter.Compile(filters).EdgeEndpointLabelsMatch(edge, nodesByUuid);

    public static bool DateFiltersMatch(DateTime? value, IReadOnlyList<List<DateFilter>>? filters) =>
        CompiledSearchFilter.DateFiltersMatch(value, filters);
}
