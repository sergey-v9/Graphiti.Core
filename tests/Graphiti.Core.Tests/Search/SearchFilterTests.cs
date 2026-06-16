using Graphiti.Core;
using System.Globalization;
using System.Text.Json;

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
    public void NodeSearchFilterQueryConstructor_BuildsNeo4jAnyLabelQuery()
    {
        var filters = new SearchFilters
        {
            NodeLabels = new List<string> { "Person", "Company" }
        };

        var (queries, parameters) =
            SearchFilterQueryBuilder.NodeSearchFilterQueryConstructor(filters, GraphProvider.Neo4j);

        Assert.Equal(new[] { "n:Person|Company" }, queries);
        Assert.Empty(parameters);
    }

    [Fact]
    public void NodeSearchFilterQueryConstructor_BuildsPropertyFilters()
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

        var (queries, parameters) =
            SearchFilterQueryBuilder.NodeSearchFilterQueryConstructor(filters, GraphProvider.Neo4j);

        Assert.Equal(
            new[]
            {
                "(n[$node_property_name_0] = $node_property_value_0)",
                "(n[$node_property_name_1] >= $node_property_value_1)",
                "(n[$node_property_name_2] IS NULL)"
            },
            queries);
        Assert.Equal("status", parameters["node_property_name_0"]);
        Assert.Equal("active", parameters["node_property_value_0"]);
        Assert.Equal("score", parameters["node_property_name_1"]);
        Assert.Equal(0.75, parameters["node_property_value_1"]);
        Assert.Equal("deleted_at", parameters["node_property_name_2"]);
        Assert.False(parameters.ContainsKey("node_property_value_2"));
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
    public void SearchFilterMatcher_UsesInvariantCultureForNumericComparisons()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var attributes = new Dictionary<string, object?>
            {
                ["score"] = "10.0"
            };
            var filters = new List<PropertyFilter>
            {
                new("score", ComparisonOperator.GreaterThan, 2.0)
            };

            Assert.True(SearchFilterMatcher.PropertyFiltersMatch(attributes, filters));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void SearchFilterMatcher_MatchesJsonOriginatedNumericPropertyFilters()
    {
        const string json = """
            {
              "property_filters": [
                {"property_name": "score", "property_value": 1.0, "comparison_operator": "="},
                {"property_name": "score", "property_value": 0.5, "comparison_operator": ">"},
                {"property_name": "rank", "property_value": 2, "comparison_operator": "<>"}
              ]
            }
            """;
        var filters = JsonSerializer.Deserialize<SearchFilters>(json, GraphitiJsonSerializer.Options)!;
        var attributes = new Dictionary<string, object?>
        {
            ["score"] = 1L,
            ["rank"] = 3
        };

        Assert.True(SearchFilterMatcher.PropertyFiltersMatch(attributes, filters.PropertyFilters));
    }

    [Fact]
    public void SearchFilterMatcher_DoesNotRoundLargeIntegersThroughDouble()
    {
        var attributes = new Dictionary<string, object?>
        {
            ["score"] = 9_007_199_254_740_993L
        };

        Assert.False(SearchFilterMatcher.PropertyFiltersMatch(
            attributes,
            new[]
            {
                new PropertyFilter(
                    "score",
                    ComparisonOperator.Equals,
                    9_007_199_254_740_992L)
            }));
        Assert.True(SearchFilterMatcher.PropertyFiltersMatch(
            attributes,
            new[]
            {
                new PropertyFilter(
                    "score",
                    ComparisonOperator.GreaterThan,
                    9_007_199_254_740_992L)
            }));
    }

    [Fact]
    public void SearchFilterMatcher_ParsesJsonElementNumbersWithoutDoubleRounding()
    {
        var attributes = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            "{\"score\":9007199254740993}",
            GraphitiJsonSerializer.Options)!;

        Assert.True(SearchFilterMatcher.PropertyFiltersMatch(
            attributes,
            new[]
            {
                new PropertyFilter(
                    "score",
                    ComparisonOperator.Equals,
                    9_007_199_254_740_993L)
            }));
        Assert.False(SearchFilterMatcher.PropertyFiltersMatch(
            attributes,
            new[]
            {
                new PropertyFilter(
                    "score",
                    ComparisonOperator.Equals,
                    9_007_199_254_740_992L)
            }));
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
    public void SearchFilterMatcher_NodePropertyFiltersMatchCanonicalFields()
    {
        var createdAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var node = new EntityNode
        {
            Uuid = "node-uuid",
            Name = "Alice",
            GroupId = "tenant-a",
            Summary = "project lead",
            CreatedAt = createdAt,
            Attributes = new Dictionary<string, object?>
            {
                ["name"] = "attribute-name",
                ["summary"] = "attribute-summary"
            }
        };
        var filters = new SearchFilters
        {
            PropertyFilters = new List<PropertyFilter>
            {
                new("uuid", ComparisonOperator.Equals, node.Uuid),
                new("name", ComparisonOperator.Equals, node.Name),
                new("group_id", ComparisonOperator.Equals, node.GroupId),
                new("summary", ComparisonOperator.Equals, node.Summary),
                new("created_at", ComparisonOperator.LessThanEqual, createdAt)
            }
        };

        Assert.True(SearchFilterMatcher.NodeMatches(node, filters));
    }

    [Fact]
    public void SearchFilterMatcher_EdgePropertyFiltersMatchCanonicalFields()
    {
        var createdAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var expiredAt = createdAt.AddDays(1);
        var validAt = createdAt.AddMinutes(1);
        var invalidAt = createdAt.AddMinutes(2);
        var referenceTime = createdAt.AddMinutes(3);
        var edge = new EntityEdge
        {
            Uuid = "edge-uuid",
            GroupId = "tenant-a",
            SourceNodeUuid = "source-uuid",
            TargetNodeUuid = "target-uuid",
            Name = "WORKS_AT",
            Fact = "Alice works at Acme",
            Episodes = new List<string> { "episode-uuid" },
            CreatedAt = createdAt,
            ExpiredAt = expiredAt,
            ValidAt = validAt,
            InvalidAt = invalidAt,
            ReferenceTime = referenceTime,
            Attributes = new Dictionary<string, object?>
            {
                ["fact"] = "attribute fact",
                ["source_node_uuid"] = "attribute-source"
            }
        };
        var filters = new SearchFilters
        {
            PropertyFilters = new List<PropertyFilter>
            {
                new("uuid", ComparisonOperator.Equals, edge.Uuid),
                new("group_id", ComparisonOperator.Equals, edge.GroupId),
                new("source_node_uuid", ComparisonOperator.Equals, edge.SourceNodeUuid),
                new("target_node_uuid", ComparisonOperator.Equals, edge.TargetNodeUuid),
                new("name", ComparisonOperator.Equals, edge.Name),
                new("fact", ComparisonOperator.Equals, edge.Fact),
                new("episodes", ComparisonOperator.IsNotNull),
                new("created_at", ComparisonOperator.Equals, createdAt),
                new("expired_at", ComparisonOperator.Equals, expiredAt),
                new("valid_at", ComparisonOperator.Equals, validAt),
                new("invalid_at", ComparisonOperator.Equals, invalidAt),
                new("reference_time", ComparisonOperator.Equals, referenceTime)
            }
        };

        Assert.True(SearchFilterMatcher.EdgeMatches(edge, filters));
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
        var (nodeQueries, nodeParameters) = compiled.BuildNodeQuery(GraphProvider.Neo4j);
        var (edgeQueries, edgeParameters) = compiled.BuildEdgeQuery(GraphProvider.Neo4j);

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
        var (queries, parameters) = compiled.BuildEdgeQuery(GraphProvider.Neo4j);

        Assert.Empty(queries);
        Assert.Empty(parameters);
        Assert.True(compiled.EdgeMatches(new EntityEdge
        {
            ValidAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        }));
    }

    [Fact]
    public void CompiledSearchFilter_DateFiltersUsePythonOrOfAndGroups()
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
    public void CompiledSearchFilter_MatchesMissingAndNullPropertyFilters()
    {
        var attributes = new Dictionary<string, object?>
        {
            ["deleted_at"] = null,
            ["archived_at"] = "2026-01-01",
            ["status"] = "active"
        };
        var compiled = CompiledSearchFilter.Compile(new SearchFilters
        {
            PropertyFilters = new List<PropertyFilter>
            {
                new("deleted_at", ComparisonOperator.IsNull),
                new("missing", ComparisonOperator.Equals, null),
                new("archived_at", ComparisonOperator.NotEquals, null),
                new("status", ComparisonOperator.IsNotNull)
            }
        });

        Assert.True(compiled.NodeMatches(new EntityNode { Attributes = attributes }));
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
            SearchFilterQueryBuilder.EdgeSearchFilterQueryConstructor(filters, GraphProvider.Neo4j);

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
            SearchFilterQueryBuilder.EdgeSearchFilterQueryConstructor(filters, GraphProvider.Neo4j);

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
            SearchFilterQueryBuilder.EdgeSearchFilterQueryConstructor(filters, GraphProvider.Neo4j);

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
            SearchFilterQueryBuilder.EdgeSearchFilterQueryConstructor(filters, GraphProvider.Neo4j);

        Assert.Equal(new[] { "e.name in $edge_types" }, queries);
        Assert.Same(filters.EdgeTypes, parameters["edge_types"]);
        Assert.DoesNotContain("()", queries);
        Assert.DoesNotContain(queries, query => query.Contains(" OR )", StringComparison.Ordinal));
        Assert.DoesNotContain(parameters, parameter => parameter.Key.StartsWith("valid_at", StringComparison.Ordinal));
    }

    [Fact]
    public void EdgeSearchFilterQueryConstructor_BuildsNeo4jAnyLabelQuery()
    {
        var filters = new SearchFilters
        {
            NodeLabels = new List<string> { "Person", "Company" }
        };

        var (queries, parameters) =
            SearchFilterQueryBuilder.EdgeSearchFilterQueryConstructor(filters, GraphProvider.Neo4j);

        Assert.Equal(new[] { "n:Person|Company AND m:Person|Company" }, queries);
        Assert.Empty(parameters);
    }

    [Fact]
    public void EdgeSearchFilterQueryConstructor_BuildsPropertyFilters()
    {
        var filters = new SearchFilters
        {
            PropertyFilters = new List<PropertyFilter>
            {
                new("source", ComparisonOperator.IsNotNull),
                new("confidence", ComparisonOperator.LessThan, 0.8),
                new("archived_at", ComparisonOperator.NotEquals, null)
            }
        };

        var (queries, parameters) =
            SearchFilterQueryBuilder.EdgeSearchFilterQueryConstructor(filters, GraphProvider.Neo4j);

        Assert.Equal(
            new[]
            {
                "(e[$edge_property_name_0] IS NOT NULL)",
                "(e[$edge_property_name_1] < $edge_property_value_1)",
                "(e[$edge_property_name_2] IS NOT NULL)"
            },
            queries);
        Assert.Equal("source", parameters["edge_property_name_0"]);
        Assert.Equal("confidence", parameters["edge_property_name_1"]);
        Assert.Equal(0.8, parameters["edge_property_value_1"]);
        Assert.Equal("archived_at", parameters["edge_property_name_2"]);
        Assert.False(parameters.ContainsKey("edge_property_value_0"));
        Assert.False(parameters.ContainsKey("edge_property_value_2"));
    }

    [Fact]
    public void EdgeSearchFilterQueryConstructor_UsesPrimitiveJsonPropertyFilterValues()
    {
        const string json = """
            {
              "property_filters": [
                {"property_name": "confidence", "property_value": 0.2, "comparison_operator": "<"}
              ]
            }
            """;
        var filters = JsonSerializer.Deserialize<SearchFilters>(json, GraphitiJsonSerializer.Options)!;

        var (queries, parameters) =
            SearchFilterQueryBuilder.EdgeSearchFilterQueryConstructor(filters, GraphProvider.Neo4j);

        Assert.Equal(new[] { "(e[$edge_property_name_0] < $edge_property_value_0)" }, queries);
        Assert.Equal("confidence", parameters["edge_property_name_0"]);
        Assert.Equal(0.2, Assert.IsType<double>(parameters["edge_property_value_0"]));
    }
}
