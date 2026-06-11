using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Graphiti.Core.Search;

/// <summary>
/// A pre-processed form of <see cref="SearchFilters"/>. Compiling once amortizes parsing of label,
/// type, temporal, and property predicates so they can be efficiently turned into backend query
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
    private readonly CompiledPropertyFilter[] _propertyFilters;

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
        _propertyFilters = CompilePropertyFilters(filters.PropertyFilters);
    }

    public bool RequiresEndpointNodeLookup => _nodeLabels.Count > 0;

    public static CompiledSearchFilter Compile(SearchFilters filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        return new CompiledSearchFilter(filters);
    }

    public bool NodeMatches(EntityNode node) =>
        NodeLabelsMatch(node) && NodePropertyFiltersMatch(node);

    public bool EdgeMatches(EntityEdge edge) =>
        (_edgeTypes.Count == 0 || _edgeTypes.Contains(edge.Name))
        && (_edgeUuids.Count == 0 || _edgeUuids.Contains(edge.Uuid))
        && DateFiltersMatch(edge.ValidAt, _validAt)
        && DateFiltersMatch(edge.InvalidAt, _invalidAt)
        && DateFiltersMatch(edge.CreatedAt, _createdAt)
        && DateFiltersMatch(edge.ExpiredAt, _expiredAt)
        && EdgePropertyFiltersMatch(edge);

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

    public (List<string> FilterQueries, Dictionary<string, object?> FilterParams) BuildNodeQuery(
        GraphProvider provider)
    {
        var filterQueries = new List<string>(EstimateNodeQueryCount());
        var filterParams = new Dictionary<string, object?>(
            EstimateNodeParameterCount(provider),
            StringComparer.Ordinal);

        AppendNodeLabelFilter(filterQueries, filterParams, provider, includeTarget: false);
        AppendPropertyFilters(filterQueries, filterParams, _propertyFilters, "node_property", "n");
        return (filterQueries, filterParams);
    }

    public (List<string> FilterQueries, Dictionary<string, object?> FilterParams) BuildEdgeQuery(
        GraphProvider provider)
    {
        var filterQueries = new List<string>(EstimateEdgeQueryCount());
        var filterParams = new Dictionary<string, object?>(
            EstimateEdgeParameterCount(provider),
            StringComparer.Ordinal);

        if (_queryEdgeTypes is { Count: > 0 })
        {
            filterQueries.Add("e.name in $edge_types");
            filterParams["edge_types"] = _queryEdgeTypes;
        }

        if (_queryEdgeUuids is { Count: > 0 })
        {
            filterQueries.Add("e.uuid in $edge_uuids");
            filterParams["edge_uuids"] = _queryEdgeUuids;
        }

        AppendNodeLabelFilter(filterQueries, filterParams, provider, includeTarget: true);

        AppendDateFilters(filterQueries, filterParams, _validAt, "valid_at", "e.valid_at");
        AppendDateFilters(filterQueries, filterParams, _invalidAt, "invalid_at", "e.invalid_at");
        AppendDateFilters(filterQueries, filterParams, _createdAt, "created_at", "e.created_at");
        AppendDateFilters(filterQueries, filterParams, _expiredAt, "expired_at", "e.expired_at");
        AppendPropertyFilters(filterQueries, filterParams, _propertyFilters, "edge_property", "e");

        return (filterQueries, filterParams);
    }

    private int EstimateNodeQueryCount() =>
        (_queryNodeLabels is { Count: > 0 } ? 1 : 0)
        + _propertyFilters.Length;

    private int EstimateEdgeQueryCount() =>
        (_queryEdgeTypes is { Count: > 0 } ? 1 : 0)
        + (_queryEdgeUuids is { Count: > 0 } ? 1 : 0)
        + (_queryNodeLabels is { Count: > 0 } ? 1 : 0)
        + (_validAt is null ? 0 : 1)
        + (_invalidAt is null ? 0 : 1)
        + (_createdAt is null ? 0 : 1)
        + (_expiredAt is null ? 0 : 1)
        + _propertyFilters.Length;

    private int EstimateNodeParameterCount(GraphProvider provider) =>
        _propertyFilters.Length * 2
        + (provider == GraphProvider.Kuzu && _queryNodeLabels is { Count: > 0 } ? 1 : 0);

    private int EstimateEdgeParameterCount(GraphProvider provider) =>
        _propertyFilters.Length * 2
        + DateFilterCount(_validAt)
        + DateFilterCount(_invalidAt)
        + DateFilterCount(_createdAt)
        + DateFilterCount(_expiredAt)
        + (_queryEdgeTypes is { Count: > 0 } ? 1 : 0)
        + (_queryEdgeUuids is { Count: > 0 } ? 1 : 0)
        + (provider == GraphProvider.Kuzu && _queryNodeLabels is { Count: > 0 } ? 1 : 0);

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
        Dictionary<string, object?> filterParams,
        GraphProvider provider,
        bool includeTarget)
    {
        if (_queryNodeLabels is not { Count: > 0 } nodeLabels)
        {
            return;
        }

        GraphitiHelpers.ValidateNodeLabels(nodeLabels);
        // NOTE: Active Ladybug query construction owns this syntax in Drivers/Ladybug.
        // Keep this branch for GraphProvider.Kuzu compatibility callers outside the driver.
        if (provider == GraphProvider.Kuzu)
        {
            filterQueries.Add(includeTarget
                ? "list_has_all(n.labels, $labels) AND list_has_all(m.labels, $labels)"
                : "list_has_all(n.labels, $labels)");
            filterParams["labels"] = nodeLabels;
            return;
        }

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

    public static bool PropertyFiltersMatch(
        IReadOnlyDictionary<string, object?> attributes,
        IReadOnlyList<PropertyFilter>? filters) =>
        PropertyFiltersMatch(attributes, CompilePropertyFilters(filters));

    private bool NodeLabelsMatch(EntityNode node)
    {
        if (_nodeLabels.Count == 0)
        {
            return true;
        }

        foreach (var label in node.Labels)
        {
            if (_nodeLabels.Contains(label))
            {
                return true;
            }
        }

        return false;
    }

    private static FrozenSet<string> ToFrozenStringSet(List<string>? values) =>
        values is { Count: > 0 }
            ? values.ToFrozenSet(StringComparer.Ordinal)
            : EmptyStringSet;

    private static CompiledPropertyFilter[] CompilePropertyFilters(
        IReadOnlyList<PropertyFilter>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return Array.Empty<CompiledPropertyFilter>();
        }

        var compiled = new CompiledPropertyFilter[filters.Count];
        for (var i = 0; i < filters.Count; i++)
        {
            var filter = filters[i];
            compiled[i] = new CompiledPropertyFilter(
                filter.PropertyName,
                filter.PropertyValue,
                filter.ComparisonOperator);
        }

        return compiled;
    }

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

    private static bool PropertyFiltersMatch(
        IReadOnlyDictionary<string, object?> attributes,
        ReadOnlySpan<CompiledPropertyFilter> filters)
    {
        foreach (var filter in filters)
        {
            attributes.TryGetValue(filter.PropertyName, out var actual);
            if (!Compare(actual, filter.PropertyValue, filter.ComparisonOperator))
            {
                return false;
            }
        }

        return true;
    }

    private bool NodePropertyFiltersMatch(EntityNode node)
    {
        if (_propertyFilters.Length == 0)
        {
            return true;
        }

        foreach (var filter in _propertyFilters)
        {
            var actual = GetNodeProperty(node, filter.PropertyName);
            if (!Compare(actual, filter.PropertyValue, filter.ComparisonOperator))
            {
                return false;
            }
        }

        return true;
    }

    private bool EdgePropertyFiltersMatch(EntityEdge edge)
    {
        if (_propertyFilters.Length == 0)
        {
            return true;
        }

        foreach (var filter in _propertyFilters)
        {
            var actual = GetEdgeProperty(edge, filter.PropertyName);
            if (!Compare(actual, filter.PropertyValue, filter.ComparisonOperator))
            {
                return false;
            }
        }

        return true;
    }

    private static object? GetNodeProperty(EntityNode node, string propertyName) =>
        propertyName switch
        {
            "uuid" => node.Uuid,
            "name" => node.Name,
            "group_id" => node.GroupId,
            "summary" => node.Summary,
            "created_at" => node.CreatedAt,
            "name_embedding" => node.NameEmbedding,
            _ => node.Attributes.TryGetValue(propertyName, out var value) ? value : null
        };

    private static object? GetEdgeProperty(EntityEdge edge, string propertyName) =>
        propertyName switch
        {
            "uuid" => edge.Uuid,
            "group_id" => edge.GroupId,
            "source_node_uuid" => edge.SourceNodeUuid,
            "target_node_uuid" => edge.TargetNodeUuid,
            "name" => edge.Name,
            "fact" => edge.Fact,
            "fact_embedding" => edge.FactEmbedding,
            "episodes" => edge.Episodes,
            "created_at" => edge.CreatedAt,
            "expired_at" => edge.ExpiredAt,
            "valid_at" => edge.ValidAt,
            "invalid_at" => edge.InvalidAt,
            "reference_time" => edge.ReferenceTime,
            _ => edge.Attributes.TryGetValue(propertyName, out var value) ? value : null
        };

    private static bool Compare(object? actual, object? expected, ComparisonOperator op)
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
            return ValuesEqual(actual, expected);
        }

        if (op == ComparisonOperator.NotEquals)
        {
            return !ValuesEqual(actual, expected);
        }

        if (actual is null || expected is null)
        {
            return false;
        }

        var comparison = CompareValues(actual, expected);
        return op switch
        {
            ComparisonOperator.GreaterThan => comparison > 0,
            ComparisonOperator.LessThan => comparison < 0,
            ComparisonOperator.GreaterThanEqual => comparison >= 0,
            ComparisonOperator.LessThanEqual => comparison <= 0,
            _ => false
        };
    }

    private static int CompareValues(object actual, object expected)
    {
        actual = NormalizeJsonScalar(actual) ?? string.Empty;
        expected = NormalizeJsonScalar(expected) ?? string.Empty;

        if (actual is DateTime actualDate && expected is DateTime expectedDate)
        {
            return actualDate.CompareTo(expectedDate);
        }

        if (TryConvertNumber(actual, allowNumericStrings: true, out var left)
            && TryConvertNumber(expected, allowNumericStrings: true, out var right))
        {
            return left.CompareTo(right);
        }

        return string.CompareOrdinal(
            Convert.ToString(actual, CultureInfo.InvariantCulture),
            Convert.ToString(expected, CultureInfo.InvariantCulture));
    }

    private static bool ValuesEqual(object? actual, object? expected)
    {
        actual = NormalizeJsonScalar(actual);
        expected = NormalizeJsonScalar(expected);

        if (actual is null || expected is null)
        {
            return actual is null && expected is null;
        }

        if (TryConvertNumber(actual, allowNumericStrings: false, out var left)
            && TryConvertNumber(expected, allowNumericStrings: false, out var right))
        {
            return left.Equals(right);
        }

        return Equals(actual, expected);
    }

    private static object? NormalizeJsonScalar(object? value)
    {
        if (value is not JsonElement element)
        {
            return value;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number when decimal.TryParse(
                element.GetRawText(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number) => number,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.GetRawText()
        };
    }

    private static bool TryConvertNumber(object value, bool allowNumericStrings, out decimal number)
    {
        switch (value)
        {
            case byte typed:
                number = typed;
                return true;
            case sbyte typed:
                number = typed;
                return true;
            case short typed:
                number = typed;
                return true;
            case ushort typed:
                number = typed;
                return true;
            case int typed:
                number = typed;
                return true;
            case uint typed:
                number = typed;
                return true;
            case long typed:
                number = typed;
                return true;
            case ulong typed:
                number = typed;
                return true;
            case decimal typed:
                number = typed;
                return true;
            case float typed when float.IsFinite(typed)
                && typed <= (float)decimal.MaxValue
                && typed >= (float)decimal.MinValue:
                number = (decimal)typed;
                return true;
            case double typed when double.IsFinite(typed)
                && typed <= (double)decimal.MaxValue
                && typed >= (double)decimal.MinValue:
                number = (decimal)typed;
                return true;
            case string text when allowNumericStrings:
                return decimal.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out number);
        }

        number = 0;
        return false;
    }

    private static void AppendPropertyFilters(
        List<string> filterQueries,
        Dictionary<string, object?> filterParams,
        ReadOnlySpan<CompiledPropertyFilter> filters,
        string paramPrefix,
        string valueName)
    {
        for (var i = 0; i < filters.Length; i++)
        {
            var filter = filters[i];
            var nameParam = $"{paramPrefix}_name_{i}";
            var valueParam = $"{paramPrefix}_value_{i}";
            var propertyAccess = $"{valueName}[${nameParam}]";
            filterParams[nameParam] = filter.PropertyName;

            if (filter.ComparisonOperator == ComparisonOperator.IsNull
                || (filter.ComparisonOperator == ComparisonOperator.Equals && filter.PropertyValue is null))
            {
                filterQueries.Add($"({propertyAccess} IS NULL)");
                continue;
            }

            if (filter.ComparisonOperator == ComparisonOperator.IsNotNull
                || (filter.ComparisonOperator == ComparisonOperator.NotEquals && filter.PropertyValue is null))
            {
                filterQueries.Add($"({propertyAccess} IS NOT NULL)");
                continue;
            }

            filterParams[valueParam] = filter.PropertyValue;
            if (filter.ComparisonOperator == ComparisonOperator.NotEquals)
            {
                filterQueries.Add($"({propertyAccess} IS NULL OR {propertyAccess} <> ${valueParam})");
                continue;
            }

            filterQueries.Add($"({propertyAccess} {filter.ComparisonOperator.ToWireValue()} ${valueParam})");
        }
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

    private readonly record struct CompiledPropertyFilter(
        string PropertyName,
        object? PropertyValue,
        ComparisonOperator ComparisonOperator);

    private readonly record struct CompiledDateFilter(
        DateTime? Date,
        ComparisonOperator ComparisonOperator);
}
