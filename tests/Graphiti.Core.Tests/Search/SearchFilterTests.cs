using Graphiti.Core;

namespace Graphiti.Core.Tests.Search;

public sealed class SearchFilterTests
{
    [Fact]
    public void SearchFilters_ValidateNodeLabelsOnAssignment()
    {
        var filters = new SearchFilters
        {
            NodeLabels = new List<string> { "Person", "_Entity2" }
        };

        Assert.Equal(new[] { "Person", "_Entity2" }, filters.NodeLabels);
        Assert.Throws<NodeLabelValidationException>(() =>
            new SearchFilters
            {
                NodeLabels = new List<string> { "Person", "Invalid Label" }
            });
    }

    [Fact]
    public void NodeSearchFilterQueryConstructor_BuildsAnyLabelQuery()
    {
        var filters = new SearchFilters
        {
            NodeLabels = new List<string> { "Person", "Company" }
        };

        var (queries, parameters) =
            SearchFilterQueryBuilder.NodeSearchFilterQueryConstructor(filters);

        Assert.Equal(new[] { "n:Person|Company" }, queries);
        Assert.Empty(parameters);
    }

    [Fact]
    public void SearchFilterCompiler_IgnoresPropertyFilters()
    {
        var filters = new SearchFilters
        {
            PropertyFilters = new List<PropertyFilter>
            {
                new("status", ComparisonOperator.Equals, "active"),
                new("score", ComparisonOperator.GreaterThanEqual, 0.75),
                new("deleted_at", ComparisonOperator.IsNull)
            }
        };

        var (nodeQueries, nodeParameters) =
            SearchFilterQueryBuilder.NodeSearchFilterQueryConstructor(filters);
        var (edgeQueries, edgeParameters) =
            SearchFilterQueryBuilder.EdgeSearchFilterQueryConstructor(filters);
        var compiled = CompiledSearchFilter.Compile(filters);

        Assert.Empty(nodeQueries);
        Assert.Empty(nodeParameters);
        Assert.Empty(edgeQueries);
        Assert.Empty(edgeParameters);
        Assert.True(compiled.NodeMatches(new EntityNode { Name = "inactive" }));
        Assert.True(compiled.EdgeMatches(new EntityEdge { Name = "LOW_CONFIDENCE" }));
    }

    [Fact]
    public void ComparisonOperator_MapsToCypherAndOpenSearchValues()
    {
        Assert.Equal("=", ComparisonOperator.Equals.ToWireValue());
        Assert.Equal("<>", ComparisonOperator.NotEquals.ToWireValue());
        Assert.Equal(">", ComparisonOperator.GreaterThan.ToWireValue());
        Assert.Equal("<", ComparisonOperator.LessThan.ToWireValue());
        Assert.Equal(">=", ComparisonOperator.GreaterThanEqual.ToWireValue());
        Assert.Equal("<=", ComparisonOperator.LessThanEqual.ToWireValue());
        Assert.Equal("IS NULL", ComparisonOperator.IsNull.ToWireValue());
        Assert.Equal("IS NOT NULL", ComparisonOperator.IsNotNull.ToWireValue());
        Assert.Equal("gt", SearchFilterQueryBuilder.CypherToOpenSearchOperator(ComparisonOperator.GreaterThan));
        Assert.Equal("lt", SearchFilterQueryBuilder.CypherToOpenSearchOperator(ComparisonOperator.LessThan));
        Assert.Equal("gte", SearchFilterQueryBuilder.CypherToOpenSearchOperator(ComparisonOperator.GreaterThanEqual));
        Assert.Equal("lte", SearchFilterQueryBuilder.CypherToOpenSearchOperator(ComparisonOperator.LessThanEqual));
        Assert.Equal("=", SearchFilterQueryBuilder.CypherToOpenSearchOperator(ComparisonOperator.Equals));
    }

    [Fact]
    public void SearchFilterQueryBuilder_PublicMethodsDoNotDifferOnlyByCase()
    {
        var duplicateNames = typeof(SearchFilterQueryBuilder)
            .GetMethods(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.DeclaredOnly)
            .GroupBy(method => method.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(method => method.Name).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => string.Join(", ", group.Select(method => method.Name).Distinct()))
            .ToList();

        Assert.Empty(duplicateNames);
    }

    [Fact]
    public void SearchFilterMatcher_NodeLabelsMatchAnyRequestedLabel()
    {
        var filters = new SearchFilters
        {
            NodeLabels = new List<string> { "Person", "Company" }
        };

        Assert.True(SearchFilterMatcher.NodeMatches(
            new EntityNode { Name = "Alice", Labels = new List<string> { "Entity", "Person" } },
            filters));
        Assert.False(SearchFilterMatcher.NodeMatches(
            new EntityNode { Name = "Project", Labels = new List<string> { "Entity", "Project" } },
            filters));
    }

    [Fact]
    public void CompiledSearchFilter_EmptyNodeLabelsNoOpButEmptyEdgeListsMatchNone()
    {
        var filters = new SearchFilters
        {
            NodeLabels = new List<string>(),
            EdgeTypes = new List<string>(),
            EdgeUuids = new List<string>()
        };

        var compiled = CompiledSearchFilter.Compile(filters);
        var (nodeQueries, nodeParameters) = compiled.BuildNodeQuery();
        var (edgeQueries, edgeParameters) = compiled.BuildEdgeQuery();

        Assert.Empty(nodeQueries);
        Assert.Empty(nodeParameters);
        Assert.Equal(new[] { "e.name in $edge_types", "e.uuid in $edge_uuids" }, edgeQueries);
        Assert.Same(filters.EdgeTypes, edgeParameters["edge_types"]);
        Assert.Same(filters.EdgeUuids, edgeParameters["edge_uuids"]);
        Assert.True(compiled.NodeMatches(
            new EntityNode { Name = "Project", Labels = new List<string> { "Entity", "Project" } }));
        Assert.False(compiled.EdgeMatches(
            new EntityEdge { Name = "RELATES_TO", Uuid = "edge-uuid" }));
    }

    [Fact]
    public void CompiledSearchFilter_TreatsEmptyDateBranchAsNoOp()
    {
        var filters = new SearchFilters
        {
            ValidAt = new List<List<DateFilter>>
            {
                new(),
                new()
                {
                    new DateFilter(
                        ComparisonOperator.GreaterThan,
                        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                }
            }
        };

        var compiled = CompiledSearchFilter.Compile(filters);
        var (queries, parameters) = compiled.BuildEdgeQuery();

        Assert.Empty(queries);
        Assert.Empty(parameters);
        Assert.True(compiled.EdgeMatches(new EntityEdge
        {
            ValidAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        }));
    }

    [Fact]
    public void CompiledSearchFilter_DateFiltersUseOrOfAndGroups()
    {
        var januaryStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var februaryStart = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var marchStart = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var aprilStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var compiled = CompiledSearchFilter.Compile(new SearchFilters
        {
            ValidAt = new List<List<DateFilter>>
            {
                new()
                {
                    new DateFilter(ComparisonOperator.GreaterThanEqual, januaryStart),
                    new DateFilter(ComparisonOperator.LessThan, februaryStart)
                },
                new()
                {
                    new DateFilter(ComparisonOperator.GreaterThanEqual, marchStart),
                    new DateFilter(ComparisonOperator.LessThan, aprilStart)
                }
            }
        });

        Assert.True(compiled.EdgeMatches(new EntityEdge
        {
            ValidAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)
        }));
        Assert.False(compiled.EdgeMatches(new EntityEdge
        {
            ValidAt = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc)
        }));
        Assert.True(compiled.EdgeMatches(new EntityEdge
        {
            ValidAt = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc)
        }));
    }

    [Fact]
    public void CompiledSearchFilter_EdgeEndpointLabelsRequireBothEndpointNodes()
    {
        var compiled = CompiledSearchFilter.Compile(new SearchFilters
        {
            NodeLabels = new List<string> { "Person", "Company" }
        });
        var edge = new EntityEdge
        {
            SourceNodeUuid = "source",
            TargetNodeUuid = "target"
        };
        var nodes = new Dictionary<string, EntityNode>(StringComparer.Ordinal)
        {
            ["source"] = new() { Labels = new List<string> { "Entity", "Person" } }
        };

        Assert.False(compiled.EdgeMatches(edge, nodes));

        nodes["target"] = new EntityNode { Labels = new List<string> { "Entity", "Company" } };

        Assert.True(compiled.EdgeMatches(edge, nodes));
    }

    [Fact]
    public void DateFilterQueryConstructor_BuildsOperatorAndNullQueries()
    {
        Assert.Equal(
            "(e.valid_at >= $valid_at_0)",
            SearchFilterQueryBuilder.DateFilterQueryConstructor(
                "e.valid_at",
                "$valid_at_0",
                ComparisonOperator.GreaterThanEqual));
        Assert.Equal(
            "(e.valid_at IS NULL)",
            SearchFilterQueryBuilder.DateFilterQueryConstructor(
                "e.valid_at",
                "$valid_at_0",
                ComparisonOperator.IsNull));
    }

    [Fact]
    public void EdgeSearchFilterQueryConstructor_BuildsEdgeNodeAndDateFilters()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var filters = new SearchFilters
        {
            EdgeTypes = new List<string> { "RELATES_TO" },
            EdgeUuids = new List<string> { "edge-uuid" },
            NodeLabels = new List<string> { "Person" },
            ValidAt = new List<List<DateFilter>>
            {
                new()
                {
                    new DateFilter(ComparisonOperator.GreaterThanEqual, start),
                    new DateFilter(ComparisonOperator.LessThan, end)
                },
                new()
                {
                    new DateFilter(ComparisonOperator.IsNull)
                }
            }
        };

        var (queries, parameters) =
            SearchFilterQueryBuilder.EdgeSearchFilterQueryConstructor(filters);

        Assert.Equal("e.name in $edge_types", queries[0]);
        Assert.Equal("e.uuid in $edge_uuids", queries[1]);
        Assert.Equal("n:Person AND m:Person", queries[2]);
        Assert.Equal(
            "((e.valid_at >= $valid_at_0) AND (e.valid_at < $valid_at_1) OR (e.valid_at IS NULL))",
            queries[3]);
        Assert.Same(filters.EdgeTypes, parameters["edge_types"]);
        Assert.Same(filters.EdgeUuids, parameters["edge_uuids"]);
        Assert.Equal(start, parameters["valid_at_0"]);
        Assert.Equal(end, parameters["valid_at_1"]);
    }

    [Fact]
    public void EdgeSearchFilterQueryConstructor_UsesUniqueDateParamsAcrossOrBranches()
    {
        var firstStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstEnd = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var secondStart = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var secondEnd = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var filters = new SearchFilters
        {
            ValidAt = new List<List<DateFilter>>
            {
                new()
                {
                    new DateFilter(ComparisonOperator.GreaterThanEqual, firstStart),
                    new DateFilter(ComparisonOperator.LessThan, firstEnd)
                },
                new()
                {
                    new DateFilter(ComparisonOperator.GreaterThanEqual, secondStart),
                    new DateFilter(ComparisonOperator.LessThan, secondEnd)
                }
            }
        };

        var (queries, parameters) =
            SearchFilterQueryBuilder.EdgeSearchFilterQueryConstructor(filters);

        Assert.Equal(
            "((e.valid_at >= $valid_at_0) AND (e.valid_at < $valid_at_1) OR " +
            "(e.valid_at >= $valid_at_2) AND (e.valid_at < $valid_at_3))",
            queries.Single());
        Assert.Equal(4, parameters.Count);
        Assert.Equal(firstStart, parameters["valid_at_0"]);
        Assert.Equal(firstEnd, parameters["valid_at_1"]);
        Assert.Equal(secondStart, parameters["valid_at_2"]);
        Assert.Equal(secondEnd, parameters["valid_at_3"]);
    }

    [Fact]
    public void EdgeSearchFilterQueryConstructor_SkipsEmptyDateFilterList()
    {
        var filters = new SearchFilters
        {
            ValidAt = new List<List<DateFilter>>()
        };

        var (queries, parameters) =
            SearchFilterQueryBuilder.EdgeSearchFilterQueryConstructor(filters);

        Assert.DoesNotContain("()", queries);
        Assert.Empty(queries);
        Assert.DoesNotContain(parameters, parameter => parameter.Key.StartsWith("valid_at", StringComparison.Ordinal));
    }

    [Fact]
    public void EdgeSearchFilterQueryConstructor_EmptyDateOrBranchDoesNotEmitInvalidCypher()
    {
        var filters = new SearchFilters
        {
            EdgeTypes = new List<string> { "RELATES_TO" },
            ValidAt = new List<List<DateFilter>>
            {
                new(),
                new()
                {
                    new DateFilter(
                        ComparisonOperator.GreaterThanEqual,
                        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                }
            }
        };

        var (queries, parameters) =
            SearchFilterQueryBuilder.EdgeSearchFilterQueryConstructor(filters);

        Assert.Equal(new[] { "e.name in $edge_types" }, queries);
        Assert.Same(filters.EdgeTypes, parameters["edge_types"]);
        Assert.DoesNotContain("()", queries);
        Assert.DoesNotContain(queries, query => query.Contains(" OR )", StringComparison.Ordinal));
        Assert.DoesNotContain(parameters, parameter => parameter.Key.StartsWith("valid_at", StringComparison.Ordinal));
    }

    [Fact]
    public void EdgeSearchFilterQueryConstructor_BuildsAnyLabelQuery()
    {
        var filters = new SearchFilters
        {
            NodeLabels = new List<string> { "Person", "Company" }
        };

        var (queries, parameters) =
            SearchFilterQueryBuilder.EdgeSearchFilterQueryConstructor(filters);

        Assert.Equal(new[] { "n:Person|Company AND m:Person|Company" }, queries);
        Assert.Empty(parameters);
    }

}
