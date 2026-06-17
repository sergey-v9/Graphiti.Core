using System.Text.Json;
using Graphiti.Core;

namespace Graphiti.Core.Tests;

public class GraphitiHelperTests
{
    public static TheoryData<Type> StructuredLlmResponseTypes =>
        new()
        {
            typeof(Graphiti.EpisodeNodeExtractionResponse),
            typeof(Graphiti.EpisodeEdgeExtractionResponse),
            typeof(Graphiti.EpisodeGraphExtractedEntityResponse),
            typeof(Graphiti.EpisodeGraphExtractedEdgeResponse),
            typeof(Graphiti.SagaSummaryResponse),
            typeof(Graphiti.CommunitySummaryResponse),
            typeof(Graphiti.CommunityNameResponse),
            typeof(Graphiti.CombinedExtractionResponse),
            typeof(Graphiti.CombinedExtractedEntityResponse),
            typeof(Graphiti.CombinedExtractedEdgeResponse),
            typeof(Graphiti.SummarizedEntitiesResponse),
            typeof(Graphiti.SummarizedEntityResponse),
            typeof(Graphiti.NodeResolutionsResponse),
            typeof(Graphiti.NodeDuplicateResponse),
            typeof(Graphiti.EdgeResolutionResponse),
            typeof(Graphiti.EdgeTimestampResponse),
            typeof(Graphiti.BatchEdgeTimestampsResponse)
        };

    [Fact]
    public void GraphitiJsonSerializer_OptionsAreReadOnly()
    {
        Assert.True(GraphitiJsonSerializer.Options.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => GraphitiJsonSerializer.Options.WriteIndented = true);
    }

    [Fact]
    public void GraphitiJsonSerializer_UsesGeneratedMetadataAndReflectionFallback()
    {
        var nodeJson = JsonSerializer.Serialize(
            new EntityNode
            {
                Name = "Alice",
                GroupId = "tenant",
                NameEmbedding = new List<float> { 0.1f }
            },
            GraphitiJsonSerializer.Options);
        var fallbackJson = JsonSerializer.Serialize(
            new { SourceNodeUuid = "source", TargetNodeUuid = "target" },
            GraphitiJsonSerializer.Options);

        Assert.NotNull(GraphitiJsonSerializer.Options.TypeInfoResolver);
        Assert.Contains("\"group_id\":\"tenant\"", nodeJson, StringComparison.Ordinal);
        Assert.Contains("\"name_embedding\":[0.1", nodeJson, StringComparison.Ordinal);
        Assert.Contains("\"source_node_uuid\":\"source\"", fallbackJson, StringComparison.Ordinal);
        Assert.Contains("\"target_node_uuid\":\"target\"", fallbackJson, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(StructuredLlmResponseTypes))]
    public void GraphitiJsonSerializer_SourceGeneratedContextCoversStructuredLlmResponseTypes(Type responseType)
    {
        var typeInfo = GraphitiJsonSerializerContext.Default.GetTypeInfo(responseType);

        Assert.NotNull(typeInfo);
        Assert.Equal(responseType, typeInfo.Type);
    }

    [Fact]
    public void GraphitiJsonSerializer_StructuredLlmResponsesKeepSnakeCaseShape()
    {
        var timestampJson = JsonSerializer.Serialize(
            new Graphiti.EdgeTimestampResponse
            {
                ValidAt = "2026-01-01T00:00:00Z",
                InvalidAt = null
            },
            GraphitiJsonSerializer.Options);
        var resolutions = JsonSerializer.Deserialize<Graphiti.NodeResolutionsResponse>(
            """
            {"entity_resolutions":[{"id":0,"name":"Alice","duplicate_candidate_id":2}]}
            """,
            GraphitiJsonSerializer.Options)!;
        var schemaJson = StructuredResponseValidator.GetSchemaJson(
            typeof(Graphiti.NodeResolutionsResponse),
            responseSchema: null);

        Assert.Contains("\"valid_at\":\"2026-01-01T00:00:00Z\"", timestampJson, StringComparison.Ordinal);
        Assert.Contains("\"invalid_at\":null", timestampJson, StringComparison.Ordinal);
        var duplicate = Assert.Single(resolutions.EntityResolutions);
        Assert.Equal(0, duplicate.Id);
        Assert.Equal("Alice", duplicate.Name);
        Assert.Equal(2, duplicate.DuplicateCandidateId);
        Assert.Contains("\"entity_resolutions\"", schemaJson, StringComparison.Ordinal);
        Assert.Contains("\"duplicate_candidate_id\"", schemaJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ChunkConstants_AliasLiveContentChunkingDefaults()
    {
        Assert.Equal(ContentChunking.DefaultChunkTokenSize, GraphitiHelpers.ChunkTokenSize);
        Assert.Equal(ContentChunking.DefaultChunkOverlapTokens, GraphitiHelpers.ChunkOverlapTokens);
        Assert.Equal(ContentChunking.DefaultChunkMinTokens, GraphitiHelpers.ChunkMinTokens);
        Assert.Equal(ContentChunking.DefaultChunkDensityThreshold, GraphitiHelpers.ChunkDensityThreshold);
    }

    [Fact]
    public void LuceneSanitize_EscapesSpecialCharactersLikePython()
    {
        var sanitized = GraphitiHelpers.LuceneSanitize(
            "This has every escape character + - && || ! ( ) { } [ ] ^ \" ~ * ? : \\ /");

        Assert.Equal(
            "\\This has every escape character \\+ \\- \\&\\& \\|\\| \\! \\( \\) \\{ \\} \\[ \\] \\^ \\\" \\~ \\* \\? \\: \\\\ \\/",
            sanitized);
        Assert.Equal("this has no escape characters", GraphitiHelpers.LuceneSanitize("this has no escape characters"));
    }

    [Fact]
    public void GetDefaultGroupId_MatchesPythonProviderDefaults()
    {
        Assert.Equal(string.Empty, GraphitiHelpers.GetDefaultGroupId(GraphProvider.Neo4j));
        // '_' (not the old '\_') after upstream #1549 (ff7e29c): the backslash failed
        // validate_group_id and broke the FalkorDB quickstart. Mirrors get_default_group_id.
        Assert.Equal("_", GraphitiHelpers.GetDefaultGroupId(GraphProvider.FalkorDb));
        Assert.Equal(string.Empty, GraphitiHelpers.GetDefaultGroupId(GraphProvider.LadybugDb));
        // Kuzu remains the LadybugDB parity/compatibility alias; assert defaults for existing enum values.
#pragma warning disable GRPH0001
        Assert.Equal(string.Empty, GraphitiHelpers.GetDefaultGroupId(GraphProvider.Kuzu));
#pragma warning restore GRPH0001
        Assert.Equal(string.Empty, GraphitiHelpers.GetDefaultGroupId(GraphProvider.Neptune));
        Assert.Equal(string.Empty, GraphitiHelpers.GetDefaultGroupId(GraphProvider.InMemory));
    }

    [Fact]
    public void GetDefaultGroupId_AlwaysProducesValidatorSafeValue()
    {
        // Pins the cross-method composition invariant the #1549 fix established: every provider's
        // default group id must survive ValidateGroupId. The old '\_' FalkorDB default failed the
        // validator (backslashes are rejected), so this loop guards every enum value against a
        // regression of that latent self-inconsistency.
        foreach (var provider in Enum.GetValues<GraphProvider>())
        {
            var defaultGroupId = GraphitiHelpers.GetDefaultGroupId(provider);

            var exception = Record.Exception(() => GraphitiHelpers.ValidateGroupId(defaultGroupId));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void ValidateGroupId_MatchesPythonSafeIdentifierRules()
    {
        GraphitiHelpers.ValidateGroupId(null);
        GraphitiHelpers.ValidateGroupId(string.Empty);
        GraphitiHelpers.ValidateGroupId("tenant_123-ABC");
        GraphitiHelpers.ValidateGroupIds(new[] { "tenant-a", "tenant_b", string.Empty });
    }

    [Theory]
    [InlineData("tenant bad")]
    [InlineData("tenant.bad")]
    [InlineData("tenant/bad")]
    [InlineData("tenant:bad")]
    [InlineData(" tenant")]
    public void ValidateGroupId_RejectsCharactersPythonRejects(string groupId)
    {
        var exception = Assert.Throws<GroupIdValidationException>(() =>
            GraphitiHelpers.ValidateGroupId(groupId));

        Assert.Contains(groupId, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateNodeLabels_MatchesPythonCypherIdentifierRules()
    {
        GraphitiHelpers.ValidateNodeLabels(null);
        GraphitiHelpers.ValidateNodeLabels(Array.Empty<string>());
        GraphitiHelpers.ValidateNodeLabels(new[] { "Entity", "_Private", "Entity_1" });
    }

    [Fact]
    public void ValidateNodeLabels_RejectsUnsafeCypherIdentifiers()
    {
        var exception = Assert.Throws<NodeLabelValidationException>(() =>
            GraphitiHelpers.ValidateNodeLabels(new[] { string.Empty, "1Entity", "Bad-Label", "Bad Label" }));

        Assert.Contains("\"\"", exception.Message, StringComparison.Ordinal);
        Assert.Contains("\"1Entity\"", exception.Message, StringComparison.Ordinal);
        Assert.Contains("\"Bad-Label\"", exception.Message, StringComparison.Ordinal);
        Assert.Contains("\"Bad Label\"", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateExcludedEntityTypes_AllowsBuiltInAndDeclaredTypeKeys()
    {
        var entityTypes = new Dictionary<string, EntityTypeDefinition>
        {
            ["person_alias"] = new("Person")
        };

        GraphitiHelpers.ValidateExcludedEntityTypes(
            new[] { "Entity", "person_alias" },
            entityTypes);
    }

    [Fact]
    public void ValidateExcludedEntityTypes_RejectsDeclaredDisplayNamesLikePython()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            GraphitiHelpers.ValidateExcludedEntityTypes(
                new[] { "Person" },
                new Dictionary<string, EntityTypeDefinition>
                {
                    ["person_alias"] = new("Person")
                }));

        Assert.Contains(
            "Invalid excluded entity types: ['Person']. Available types: ['Entity', 'person_alias']",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateExcludedEntityTypes_ReportsDistinctInvalidTypesInSortedOrder()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            GraphitiHelpers.ValidateExcludedEntityTypes(
                new[] { "Location", "Animal", "Location", "Zed" },
                new Dictionary<string, EntityTypeDefinition>
                {
                    ["Person"] = new("Person")
                }));

        Assert.Contains(
            "Invalid excluded entity types: ['Animal', 'Location', 'Zed']. Available types: ['Entity', 'Person']",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateEntityTypes_RejectsProtectedNamesCaseInsensitively()
    {
        var exception = Assert.Throws<EntityTypeValidationException>(() =>
            GraphitiHelpers.ValidateEntityTypes(new Dictionary<string, EntityTypeDefinition>
            {
                ["Person"] = new(
                    "Person",
                    attributes: new Dictionary<string, EntityAttributeDefinition>
                    {
                        ["NAMEEMBEDDING"] = new("Protected field")
                    })
            }));

        Assert.Contains("NAMEEMBEDDING", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NewUuid_ReturnsVersion7UuidString()
    {
        var uuid = GraphitiHelpers.NewUuid();

        Assert.True(Guid.TryParse(uuid, out _));
        Assert.Equal('7', uuid[14]);
        Assert.Contains(char.ToLowerInvariant(uuid[19]), new[] { '8', '9', 'a', 'b' });
    }

    [Fact]
    public void NodesAndEdgesUseVersion7UuidDefaults()
    {
        var node = new EntityNode();
        var edge = new EntityEdge();

        Assert.Equal('7', node.Uuid[14]);
        Assert.Equal('7', edge.Uuid[14]);
    }

    [Fact]
    public void NormalizeL2_ReturnsNormalizedCopy()
    {
        var input = new[] { 3f, 4f };

        var normalized = GraphitiHelpers.NormalizeL2(input);

        Assert.Equal(new[] { 3f, 4f }, input);
        Assert.Equal(0.6f, normalized[0], precision: 6);
        Assert.Equal(0.8f, normalized[1], precision: 6);
    }

    [Fact]
    public void NormalizeL2_RejectsNullEmbedding()
    {
        Assert.Throws<ArgumentNullException>(() => GraphitiHelpers.NormalizeL2(null!));
    }

    [Fact]
    public void NormalizeL2_MaterializesDeferredEnumerableOnce()
    {
        var enumerations = 0;

        IEnumerable<float> Source()
        {
            enumerations++;
            yield return 3f;
            yield return 4f;
        }

        var normalized = GraphitiHelpers.NormalizeL2(Source());

        Assert.Equal(1, enumerations);
        Assert.Equal(0.6f, normalized[0], precision: 6);
        Assert.Equal(0.8f, normalized[1], precision: 6);
    }

    [Fact]
    public void NormalizeL2InPlace_UsesVectorizedNormalization()
    {
        var input = new[] { 3f, 4f };

        GraphitiHelpers.NormalizeL2InPlace(input);

        Assert.Equal(0.6f, input[0], precision: 6);
        Assert.Equal(0.8f, input[1], precision: 6);
    }

    [Fact]
    public void NormalizeL2InPlace_LeavesZeroAndInvalidVectorsUnchanged()
    {
        var zero = new[] { 0f, 0f };
        var invalid = new[] { float.NaN, 1f };

        GraphitiHelpers.NormalizeL2InPlace(zero);
        GraphitiHelpers.NormalizeL2InPlace(invalid);

        Assert.Equal(new[] { 0f, 0f }, zero);
        Assert.True(float.IsNaN(invalid[0]));
        Assert.Equal(1f, invalid[1]);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("  ACME   Corp  ", "acme corp")]
    [InlineData("Alice\tBob\r\nCarol", "alice bob carol")]
    [InlineData("Café  NAÏVE_2", "café naïve_2")]
    public void NormalizeEntityKey_MatchesPythonWhitespaceAndLowercaseBehavior(
        string? input,
        string expected)
    {
        Assert.Equal(expected, GraphitiHelpers.NormalizeEntityKey(input!));
    }

    [Fact]
    public void ParseDbDate_UsesInvariantUtcParsing()
    {
        Assert.Null(GraphitiHelpers.ParseDbDate(null));
        Assert.Equal(
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            GraphitiHelpers.ParseDbDate("2026-01-02T03:04:05Z"));
        Assert.Equal(
            new DateTime(2026, 1, 2, 1, 34, 5, DateTimeKind.Utc),
            GraphitiHelpers.ParseDbDate("2026-01-02T03:04:05+01:30"));
        Assert.Equal(
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            GraphitiHelpers.ParseDbDate("2026-01-02T03:04:05"));
        Assert.Equal(
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            GraphitiHelpers.ParseDbDate("2026-01-02"));
        Assert.Equal(
            new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Utc),
            GraphitiHelpers.ParseDbDate("2026-01-02 03:04"));
        Assert.Equal(
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1_234_560),
            GraphitiHelpers.ParseDbDate("2026-01-02T03:04:05.1234567Z"));
    }

    [Fact]
    public void ParseDbDate_AcceptsPythonIsoformatVariants()
    {
        Assert.Equal(
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            GraphitiHelpers.ParseDbDate("20260102"));
        Assert.Equal(
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            GraphitiHelpers.ParseDbDate("2026-W01-5"));
        Assert.Equal(
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            GraphitiHelpers.ParseDbDate("2026-01-02X03:04:05"));
        Assert.Equal(
            new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Utc).AddTicks(5_000_000),
            GraphitiHelpers.ParseDbDate("2026-01-02T0304.5"));
        Assert.Equal(
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1_234_560),
            GraphitiHelpers.ParseDbDate("2026-01-02T03:04:05,1234567"));
    }

    [Fact]
    public void ParseDbDate_RejectsPythonInvalidStringsWithFormatException()
    {
        foreach (var value in new[] { "", "  ", " 2026-01-02T03:04:05 ", "01/02/2026", "not-a-date" })
        {
            Assert.Throws<FormatException>(() => GraphitiHelpers.ParseDbDate(value));
        }
    }

    [Fact]
    public void TryParseDbDate_ReturnsFalseForInvalidValuesWithoutThrowing()
    {
        Assert.True(GraphitiHelpers.TryParseDbDate("2026-01-02T03:04:05Z", out var parsed));
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), parsed);

        Assert.False(GraphitiHelpers.TryParseDbDate("  ", out var blank));
        Assert.Null(blank);

        Assert.False(GraphitiHelpers.TryParseDbDate("01/02/2026", out var slashDate));
        Assert.Null(slashDate);

        Assert.False(GraphitiHelpers.TryParseDbDate("not-a-date", out var invalid));
        Assert.Null(invalid);
    }

    [Fact]
    public async Task InMemoryClone_SharesStateAcrossDatabases()
    {
        var driver = new InMemoryGraphDriver();
        var clone = driver.Clone("tenant");
        var node = new EntityNode { Name = "Alice", GroupId = "tenant" };

        await clone.SaveNodeAsync(node);

        var found = await driver.GetNodeByUuidAsync<EntityNode>(node.Uuid);
        Assert.NotNull(found);
        Assert.Equal("Alice", found.Name);
    }
}
