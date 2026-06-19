using System.Collections.Frozen;
using System.Text;

namespace Graphiti.Core.Search;

/// <summary>
/// A pre-processed form of <see cref="SearchFilters"/>. Compiling once amortizes parsing of label,
/// type, and temporal predicates so they can be efficiently turned into backend query
/// fragments (<c>BuildNodeQuery</c>/<c>BuildEdgeQuery</c>) or evaluated in memory (<c>NodeMatches</c>/<c>EdgeMatches</c>).
/// </summary>
internal sealed class CompiledSearchFilter
{
    private static readonly FrozenSet<string> EmptyStringSet =
        Array.Empty<string>().ToFrozenSet(StringComparer.Ordinal);

    private readonly List<string>? _queryNodeLabels;
    private readonly List<string>? _queryEdgeTypes;
    private readonly List<string>? _queryEdgeUuids;
    private readonly FrozenSet<string> _nodeLabels;
    private readonly FrozenSet<string> _edgeTypes;
    private readonly FrozenSet<string> _edgeUuids;
    private readonly CompiledDateFilter[][]? _validAt;
    private readonly CompiledDateFilter[][]? _invalidAt;
    private readonly CompiledDateFilter[][]? _createdAt;
    private readonly CompiledDateFilter[][]? _expiredAt;

    private CompiledSearchFilter(SearchFilters filters)
    {
        _queryNodeLabels = filters.NodeLabels;
        _queryEdgeTypes = filters.EdgeTypes;
        _queryEdgeUuids = filters.EdgeUuids;
        _nodeLabels = ToFrozenStringSet(filters.NodeLabels);
        _edgeTypes = ToFrozenStringSet(filters.EdgeTypes);
        _edgeUuids = ToFrozenStringSet(filters.EdgeUuids);
        _validAt = CompileDateFilters(filters.ValidAt);
        _invalidAt = CompileDateFilters(filters.InvalidAt);
        _createdAt = CompileDateFilters(filters.CreatedAt);
        _expiredAt = CompileDateFilters(filters.ExpiredAt);
    }

    public bool RequiresEndpointNodeLookup => _nodeLabels.Count > 0;

    public static CompiledSearchFilter Compile(SearchFilters filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        return new CompiledSearchFilter(filters);
    }

    public bool NodeMatches(EntityNode node) =>
        NodeLabelsMatch(node);

    public bool EdgeMatches(EntityEdge edge) =>
        (_queryEdgeTypes is null || _edgeTypes.Contains(edge.Name))
        && (_queryEdgeUuids is null || _edgeUuids.Contains(edge.Uuid))
        && DateFiltersMatch(edge.ValidAt, _validAt)
        && DateFiltersMatch(edge.InvalidAt, _invalidAt)
        && DateFiltersMatch(edge.CreatedAt, _createdAt)
        && DateFiltersMatch(edge.ExpiredAt, _expiredAt);

    public bool EdgeMatches(
        EntityEdge edge,
        IReadOnlyDictionary<string, EntityNode> nodesByUuid) =>
        EdgeMatches(edge) && EdgeEndpointLabelsMatch(edge, nodesByUuid);

    public bool EdgeEndpointLabelsMatch(
        EntityEdge edge,
        IReadOnlyDictionary<string, EntityNode> nodesByUuid)
    {
        if (!RequiresEndpointNodeLookup)
        {
            return true;
        }

        return nodesByUuid.TryGetValue(edge.SourceNodeUuid, out var source)
            && nodesByUuid.TryGetValue(edge.TargetNodeUuid, out var target)
            && NodeLabelsMatch(source)
            && NodeLabelsMatch(target);
    }

    public (List<string> FilterQueries, Dictionary<string, object?> FilterParams) BuildNodeQuery()
    {
        var filterQueries = new List<string>(EstimateNodeQueryCount());
        var filterParams = new Dictionary<string, object?>(
            EstimateNodeParameterCount(),
            StringComparer.Ordinal);

        AppendNodeLabelFilter(filterQueries, includeTarget: false);
        return (filterQueries, filterParams);
    }

    public (List<string> FilterQueries, Dictionary<string, object?> FilterParams) BuildEdgeQuery()
    {
        var filterQueries = new List<string>(EstimateEdgeQueryCount());
        var filterParams = new Dictionary<string, object?>(
            EstimateEdgeParameterCount(),
            StringComparer.Ordinal);

        if (_queryEdgeTypes is not null)
        {
            filterQueries.Add("e.name in $edge_types");
            filterParams["edge_types"] = _queryEdgeTypes;
        }

        if (_queryEdgeUuids is not null)
        {
            filterQueries.Add("e.uuid in $edge_uuids");
            filterParams["edge_uuids"] = _queryEdgeUuids;
        }

        AppendNodeLabelFilter(filterQueries, includeTarget: true);

        AppendDateFilters(filterQueries, filterParams, _validAt, "valid_at", "e.valid_at");
        AppendDateFilters(filterQueries, filterParams, _invalidAt, "invalid_at", "e.invalid_at");
        AppendDateFilters(filterQueries, filterParams, _createdAt, "created_at", "e.created_at");
        AppendDateFilters(filterQueries, filterParams, _expiredAt, "expired_at", "e.expired_at");

        return (filterQueries, filterParams);
    }

    private int EstimateNodeQueryCount() =>
        _queryNodeLabels is { Count: > 0 } ? 1 : 0;

    private int EstimateEdgeQueryCount() =>
        (_queryEdgeTypes is not null ? 1 : 0)
        + (_queryEdgeUuids is not null ? 1 : 0)
        + (_queryNodeLabels is { Count: > 0 } ? 1 : 0)
        + (_validAt is null ? 0 : 1)
        + (_invalidAt is null ? 0 : 1)
        + (_createdAt is null ? 0 : 1)
        + (_expiredAt is null ? 0 : 1);

    private static int EstimateNodeParameterCount() =>
        0;

    private int EstimateEdgeParameterCount() =>
        DateFilterCount(_validAt)
        + DateFilterCount(_invalidAt)
        + DateFilterCount(_createdAt)
        + DateFilterCount(_expiredAt)
        + (_queryEdgeTypes is not null ? 1 : 0)
        + (_queryEdgeUuids is not null ? 1 : 0);

    private static int DateFilterCount(CompiledDateFilter[][]? filters)
    {
        if (filters is null)
        {
            return 0;
        }

        var count = 0;
        for (var i = 0; i < filters.Length; i++)
        {
            count += filters[i].Length;
        }

        return count;
    }

    private void AppendNodeLabelFilter(
        List<string> filterQueries,
        bool includeTarget)
    {
        if (_queryNodeLabels is not { Count: > 0 } nodeLabels)
        {
            return;
        }

        GraphitiHelpers.ValidateNodeLabels(nodeLabels);
        var labels = JoinLabels(nodeLabels);
        filterQueries.Add(includeTarget
            ? string.Concat("n:", labels, " AND m:", labels)
            : string.Concat("n:", labels));
    }

    private static string JoinLabels(List<string> labels)
    {
        var builder = new StringBuilder(labels.Count * 12);
        builder.Append(labels[0]);
        for (var i = 1; i < labels.Count; i++)
        {
            builder.Append('|');
            builder.Append(labels[i]);
        }

        return builder.ToString();
    }

    public static bool DateFiltersMatch(DateTime? value, IReadOnlyList<List<DateFilter>>? filters) =>
        DateFiltersMatch(value, CompileDateFilters(filters));

    private bool NodeLabelsMatch(EntityNode node)
    {
        if (_nodeLabels.Count == 0)
        {
            return true;
        }

        foreach (var label in _nodeLabels)
        {
            if (!node.Labels.Contains(label, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static FrozenSet<string> ToFrozenStringSet(List<string>? values) =>
        values is { Count: > 0 }
            ? values.ToFrozenSet(StringComparer.Ordinal)
            : EmptyStringSet;

    private static CompiledDateFilter[][]? CompileDateFilters(IReadOnlyList<List<DateFilter>>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return null;
        }

        foreach (var orList in filters)
        {
            if (orList.Count == 0)
            {
                return null;
            }
        }

        var compiled = new CompiledDateFilter[filters.Count][];
        for (var branchIndex = 0; branchIndex < filters.Count; branchIndex++)
        {
            var branch = filters[branchIndex];
            var compiledBranch = new CompiledDateFilter[branch.Count];
            for (var filterIndex = 0; filterIndex < branch.Count; filterIndex++)
            {
                var filter = branch[filterIndex];
                compiledBranch[filterIndex] = new CompiledDateFilter(
                    filter.Date,
                    filter.ComparisonOperator);
            }

            compiled[branchIndex] = compiledBranch;
        }

        return compiled;
    }

    private static bool DateFiltersMatch(DateTime? value, CompiledDateFilter[][]? filters)
    {
        if (filters is null)
        {
            return true;
        }

        foreach (var orList in filters)
        {
            var branchMatches = true;
            foreach (var filter in orList)
            {
                if (!Compare(value, filter.Date, filter.ComparisonOperator))
                {
                    branchMatches = false;
                    break;
                }
            }

            if (branchMatches)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Compare(DateTime? actual, DateTime? expected, ComparisonOperator op)
    {
        if (op == ComparisonOperator.IsNull)
        {
            return actual is null;
        }

        if (op == ComparisonOperator.IsNotNull)
        {
            return actual is not null;
        }

        if (op == ComparisonOperator.Equals)
        {
            return actual == expected;
        }

        if (op == ComparisonOperator.NotEquals)
        {
            return actual != expected;
        }

        if (actual is null || expected is null)
        {
            return false;
        }

        var comparison = actual.Value.CompareTo(expected.Value);
        return op switch
        {
            ComparisonOperator.GreaterThan => comparison > 0,
            ComparisonOperator.LessThan => comparison < 0,
            ComparisonOperator.GreaterThanEqual => comparison >= 0,
            ComparisonOperator.LessThanEqual => comparison <= 0,
            _ => false
        };
    }

    private static void AppendDateFilters(
        List<string> filterQueries,
        Dictionary<string, object?> filterParams,
        CompiledDateFilter[][]? filters,
        string paramPrefix,
        string valueName)
    {
        if (filters is null)
        {
            return;
        }

        var builder = new StringBuilder(filters.Length * 64);
        builder.Append('(');
        var parameterIndex = 0;
        for (var branchIndex = 0; branchIndex < filters.Length; branchIndex++)
        {
            if (branchIndex > 0)
            {
                builder.Append(" OR ");
            }

            var orList = filters[branchIndex];
            for (var filterIndex = 0; filterIndex < orList.Length; filterIndex++)
            {
                if (filterIndex > 0)
                {
                    builder.Append(" AND ");
                }

                var dateFilter = orList[filterIndex];
                var parameterName = $"{paramPrefix}_{parameterIndex++}";
                if (dateFilter.ComparisonOperator is not ComparisonOperator.IsNull
                    and not ComparisonOperator.IsNotNull)
                {
                    filterParams[parameterName] = dateFilter.Date;
                }

                builder.Append(SearchFilterQueryBuilder.DateFilterQueryConstructor(
                    valueName,
                    $"${parameterName}",
                    dateFilter.ComparisonOperator));
            }
        }

        builder.Append(')');
        filterQueries.Add(builder.ToString());
    }

    private readonly record struct CompiledDateFilter(
        DateTime? Date,
        ComparisonOperator ComparisonOperator);
}
