using Graphiti.Core;

namespace Graphiti.Core.Tests.Drivers;

public class Neo4jGraphDriverSchemaTests
{
    [Fact]
    public void BuildSchemaStatements_IncludesPythonRangeIndexSurface()
    {
        var statements = Neo4jGraphDriver.BuildSchemaStatements();

        Assert.Contains("CREATE INDEX community_group_id IF NOT EXISTS FOR (n:Community) ON (n.group_id)", statements);
        Assert.Contains("CREATE INDEX saga_group_id IF NOT EXISTS FOR (n:Saga) ON (n.group_id)", statements);
        Assert.Contains("CREATE INDEX mention_group_id IF NOT EXISTS FOR ()-[e:MENTIONS]-() ON (e.group_id)", statements);
        Assert.Contains("CREATE INDEX has_episode_group_id IF NOT EXISTS FOR ()-[e:HAS_EPISODE]-() ON (e.group_id)", statements);
        Assert.Contains("CREATE INDEX next_episode_group_id IF NOT EXISTS FOR ()-[e:NEXT_EPISODE]-() ON (e.group_id)", statements);
        Assert.Contains("CREATE INDEX name_entity_index IF NOT EXISTS FOR (n:Entity) ON (n.name)", statements);
        Assert.Contains("CREATE INDEX saga_name IF NOT EXISTS FOR (n:Saga) ON (n.name)", statements);
        Assert.Contains("CREATE INDEX created_at_entity_index IF NOT EXISTS FOR (n:Entity) ON (n.created_at)", statements);
        Assert.Contains("CREATE INDEX created_at_episodic_index IF NOT EXISTS FOR (n:Episodic) ON (n.created_at)", statements);
        Assert.Contains("CREATE INDEX valid_at_episodic_index IF NOT EXISTS FOR (n:Episodic) ON (n.valid_at)", statements);
        Assert.Contains("CREATE INDEX name_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.name)", statements);
        Assert.Contains("CREATE INDEX created_at_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.created_at)", statements);
        Assert.Contains("CREATE INDEX expired_at_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.expired_at)", statements);
        Assert.Contains("CREATE INDEX valid_at_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.valid_at)", statements);
        Assert.Contains("CREATE INDEX invalid_at_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.invalid_at)", statements);
    }

    [Fact]
    public void BuildSchemaStatements_IndexesRelationshipUuidsNotCoveredByNodeConstraints()
    {
        var statements = Neo4jGraphDriver.BuildSchemaStatements();

        Assert.Contains("CREATE INDEX relation_uuid IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.uuid)", statements);
        Assert.Contains("CREATE INDEX mention_uuid IF NOT EXISTS FOR ()-[e:MENTIONS]-() ON (e.uuid)", statements);
        Assert.Contains("CREATE INDEX has_member_uuid IF NOT EXISTS FOR ()-[e:HAS_MEMBER]-() ON (e.uuid)", statements);
        Assert.Contains("CREATE INDEX has_episode_uuid IF NOT EXISTS FOR ()-[e:HAS_EPISODE]-() ON (e.uuid)", statements);
        Assert.Contains("CREATE INDEX next_episode_uuid IF NOT EXISTS FOR ()-[e:NEXT_EPISODE]-() ON (e.uuid)", statements);
    }

    [Fact]
    public void BuildSchemaStatements_CreatesConstraintsBeforeIndexesAndHasNoDuplicates()
    {
        var statements = Neo4jGraphDriver.BuildSchemaStatements();

        Assert.All(statements.Take(4), statement => Assert.StartsWith("CREATE CONSTRAINT ", statement));
        Assert.DoesNotContain(statements.Skip(4), statement => statement.StartsWith("CREATE CONSTRAINT ", StringComparison.Ordinal));
        Assert.Equal(statements.Count, statements.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void BuildSchemaResetStatements_UsesConcreteDropStatementsForGraphitiSchema()
    {
        var statements = Neo4jGraphDriver.BuildSchemaResetStatements();

        Assert.Equal("DROP CONSTRAINT entity_uuid IF EXISTS", statements[0]);
        Assert.Equal("DROP CONSTRAINT episodic_uuid IF EXISTS", statements[1]);
        Assert.Equal("DROP CONSTRAINT community_uuid IF EXISTS", statements[2]);
        Assert.Equal("DROP CONSTRAINT saga_uuid IF EXISTS", statements[3]);
        Assert.All(statements, statement =>
        {
            Assert.DoesNotContain("SHOW ", statement);
            Assert.DoesNotContain("CALL ", statement);
            Assert.DoesNotContain("DROP CONSTRAINT name IF EXISTS", statement);
            Assert.DoesNotContain("DROP INDEX name IF EXISTS", statement);
        });
    }

    [Fact]
    public void BuildSchemaResetStatements_DropsEveryCreatedSchemaName()
    {
        var createStatements = Neo4jGraphDriver.BuildSchemaStatements();
        var resetStatements = Neo4jGraphDriver.BuildSchemaResetStatements();

        Assert.Equal(createStatements.Count, resetStatements.Count);
        Assert.Equal(
            createStatements.Select(ExpectedDropStatement),
            resetStatements);
    }

    [Fact]
    public void BuildSchemaResetStatements_DropsConstraintsBeforeIndexes()
    {
        var statements = Neo4jGraphDriver.BuildSchemaResetStatements();

        Assert.All(statements.Take(4), statement => Assert.StartsWith("DROP CONSTRAINT ", statement));
        Assert.DoesNotContain(statements.Skip(4), statement => statement.StartsWith("DROP CONSTRAINT ", StringComparison.Ordinal));
        Assert.All(statements.Skip(4), statement => Assert.StartsWith("DROP INDEX ", statement));
    }

    private static string ExpectedDropStatement(string createStatement)
    {
        const string constraintPrefix = "CREATE CONSTRAINT ";
        const string fullTextIndexPrefix = "CREATE FULLTEXT INDEX ";
        const string indexPrefix = "CREATE INDEX ";

        if (createStatement.StartsWith(constraintPrefix, StringComparison.Ordinal))
        {
            return $"DROP CONSTRAINT {ExtractSchemaName(createStatement, constraintPrefix)} IF EXISTS";
        }

        if (createStatement.StartsWith(fullTextIndexPrefix, StringComparison.Ordinal))
        {
            return $"DROP INDEX {ExtractSchemaName(createStatement, fullTextIndexPrefix)} IF EXISTS";
        }

        if (createStatement.StartsWith(indexPrefix, StringComparison.Ordinal))
        {
            return $"DROP INDEX {ExtractSchemaName(createStatement, indexPrefix)} IF EXISTS";
        }

        throw new InvalidOperationException(createStatement);
    }

    private static string ExtractSchemaName(string createStatement, string prefix)
    {
        var start = prefix.Length;
        var end = createStatement.IndexOf(" IF NOT EXISTS", start, StringComparison.Ordinal);
        return createStatement[start..end];
    }
}
