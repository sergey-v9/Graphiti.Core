using Graphiti.Core;

namespace Graphiti.Core.Tests.Drivers;

public class Neo4jGraphDriverBulkSaveTests
{
    [Fact]
    public void BuildBulkSaveStatements_UsesBulkCypherAndPythonCompatiblePayloads()
    {
        var createdAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var validAt = new DateTime(2026, 1, 3, 4, 5, 6, DateTimeKind.Utc);
        var episode = new EpisodicNode
        {
            Uuid = "episode-1",
            Name = "Episode",
            GroupId = "group",
            Source = EpisodeType.Json,
            SourceDescription = "source",
            Content = """{"text":"hello"}""",
            EntityEdges = new List<string> { "rel-1" },
            CreatedAt = createdAt,
            ValidAt = validAt
        };
        var node = new EntityNode
        {
            Uuid = "node-1",
            Name = "Alice",
            GroupId = "group",
            Labels = new List<string> { "Person", "Entity", "Person" },
            Summary = "summary",
            NameEmbedding = new List<float> { 0.1f, 0.2f },
            CreatedAt = createdAt,
            Attributes = new Dictionary<string, object?>
            {
                ["role"] = "engineer",
                ["labels"] = new List<string> { "Wrong" }
            }
        };
        var episodicEdge = new EpisodicEdge
        {
            Uuid = "mention-1",
            SourceNodeUuid = episode.Uuid,
            TargetNodeUuid = node.Uuid,
            GroupId = "group",
            CreatedAt = createdAt
        };
        var entityEdge = new EntityEdge
        {
            Uuid = "rel-1",
            SourceNodeUuid = node.Uuid,
            TargetNodeUuid = "node-2",
            GroupId = "group",
            Name = "KNOWS",
            Fact = "Alice knows Bob",
            FactEmbedding = new List<float> { 0.3f, 0.4f },
            Episodes = new List<string> { episode.Uuid },
            CreatedAt = createdAt,
            ValidAt = validAt,
            ReferenceTime = validAt,
            Attributes = new Dictionary<string, object?>
            {
                ["confidence"] = 0.9,
                ["source_node_uuid"] = "wrong"
            }
        };

        var statements = Neo4jStatementBuilder.BuildBulkSaveStatements(
            new[] { episode },
            new[] { episodicEdge },
            new[] { node },
            new[] { entityEdge });

        Assert.Collection(
            statements,
            statement => Assert.Contains("UNWIND $episodes AS episode", statement.Query, StringComparison.Ordinal),
            statement =>
            {
                Assert.Contains("UNWIND $nodes AS node", statement.Query, StringComparison.Ordinal);
                Assert.Contains("SET n:$(node.labels)", statement.Query, StringComparison.Ordinal);
                Assert.Contains("SET n = node", statement.Query, StringComparison.Ordinal);
            },
            statement => Assert.Contains("UNWIND $episodic_edges AS edge", statement.Query, StringComparison.Ordinal),
            statement =>
            {
                Assert.Contains("UNWIND $entity_edges AS edge", statement.Query, StringComparison.Ordinal);
                Assert.Contains("SET e = edge", statement.Query, StringComparison.Ordinal);
            });

        var episodeRows = Rows(statements[0], "episodes");
        Assert.Equal("json", episodeRows[0]["source"]);
        Assert.Equal(new List<string> { "rel-1" }, Assert.IsType<List<string>>(episodeRows[0]["entity_edges"]));

        var nodeRows = Rows(statements[1], "nodes");
        Assert.Equal(new List<string> { "Person", "Entity" }, Assert.IsType<List<string>>(nodeRows[0]["labels"]));
        Assert.Equal(new List<float> { 0.1f, 0.2f }, Assert.IsType<List<float>>(nodeRows[0]["name_embedding"]));
        Assert.Equal("engineer", nodeRows[0]["role"]);

        var mentionRows = Rows(statements[2], "episodic_edges");
        Assert.Equal(episode.Uuid, mentionRows[0]["source_node_uuid"]);
        Assert.Equal(node.Uuid, mentionRows[0]["target_node_uuid"]);

        var relationRows = Rows(statements[3], "entity_edges");
        Assert.Equal(node.Uuid, relationRows[0]["source_node_uuid"]);
        Assert.Equal("node-2", relationRows[0]["target_node_uuid"]);
        Assert.Equal(new List<string> { episode.Uuid }, Assert.IsType<List<string>>(relationRows[0]["episodes"]));
        Assert.Equal(validAt, relationRows[0]["reference_time"]);
        Assert.Equal(0.9, relationRows[0]["confidence"]);
    }

    [Fact]
    public void BuildBulkSaveStatements_SkipsEmptyScopes()
    {
        var statements = Neo4jStatementBuilder.BuildBulkSaveStatements(
            Array.Empty<EpisodicNode>(),
            Array.Empty<EpisodicEdge>(),
            Array.Empty<EntityNode>(),
            Array.Empty<EntityEdge>());

        Assert.Empty(statements);
    }

    private static List<Dictionary<string, object?>> Rows(Neo4jStatement statement, string parameterName) =>
        Assert.IsType<List<Dictionary<string, object?>>>(statement.Parameters[parameterName]);
}
