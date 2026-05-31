namespace Graphiti.Core.Drivers.Ladybug;

/// <summary>
/// Foundation LadybugDB schema ported from Python's Kuzu driver. Entity facts are represented as
/// intermediate <c>RelatesToNode_</c> nodes because Kuzu/Ladybug cannot index relationship
/// properties the same way Neo4j can.
/// </summary>
internal static class LadybugSchema
{
    internal static readonly IReadOnlyList<string> NodeTables = Array.AsReadOnly(new[]
    {
        "Episodic",
        "Entity",
        "Community",
        "RelatesToNode_",
        "Saga"
    });

    internal static readonly IReadOnlyList<string> RelationshipTables = Array.AsReadOnly(new[]
    {
        "RELATES_TO",
        "MENTIONS",
        "HAS_MEMBER",
        "HAS_EPISODE",
        "NEXT_EPISODE"
    });

    internal const string SchemaQueries = """
        CREATE NODE TABLE IF NOT EXISTS Episodic (
            uuid STRING PRIMARY KEY,
            name STRING,
            group_id STRING,
            created_at TIMESTAMP,
            source STRING,
            source_description STRING,
            content STRING,
            valid_at TIMESTAMP,
            entity_edges STRING[]
        );
        CREATE NODE TABLE IF NOT EXISTS Entity (
            uuid STRING PRIMARY KEY,
            name STRING,
            group_id STRING,
            labels STRING[],
            created_at TIMESTAMP,
            name_embedding FLOAT[],
            summary STRING,
            attributes STRING
        );
        CREATE NODE TABLE IF NOT EXISTS Community (
            uuid STRING PRIMARY KEY,
            name STRING,
            group_id STRING,
            created_at TIMESTAMP,
            name_embedding FLOAT[],
            summary STRING
        );
        CREATE NODE TABLE IF NOT EXISTS RelatesToNode_ (
            uuid STRING PRIMARY KEY,
            group_id STRING,
            created_at TIMESTAMP,
            name STRING,
            fact STRING,
            fact_embedding FLOAT[],
            episodes STRING[],
            expired_at TIMESTAMP,
            valid_at TIMESTAMP,
            invalid_at TIMESTAMP,
            reference_time TIMESTAMP,
            attributes STRING
        );
        CREATE REL TABLE IF NOT EXISTS RELATES_TO(
            FROM Entity TO RelatesToNode_,
            FROM RelatesToNode_ TO Entity
        );
        CREATE REL TABLE IF NOT EXISTS MENTIONS(
            FROM Episodic TO Entity,
            uuid STRING PRIMARY KEY,
            group_id STRING,
            created_at TIMESTAMP
        );
        CREATE REL TABLE IF NOT EXISTS HAS_MEMBER(
            FROM Community TO Entity,
            FROM Community TO Community,
            uuid STRING,
            group_id STRING,
            created_at TIMESTAMP
        );
        CREATE NODE TABLE IF NOT EXISTS Saga (
            uuid STRING PRIMARY KEY,
            name STRING,
            group_id STRING,
            created_at TIMESTAMP,
            summary STRING,
            first_episode_uuid STRING,
            last_episode_uuid STRING,
            last_summarized_at TIMESTAMP,
            last_summarized_episode_valid_at TIMESTAMP
        );
        CREATE REL TABLE IF NOT EXISTS HAS_EPISODE(
            FROM Saga TO Episodic,
            uuid STRING,
            group_id STRING,
            created_at TIMESTAMP
        );
        CREATE REL TABLE IF NOT EXISTS NEXT_EPISODE(
            FROM Episodic TO Episodic,
            uuid STRING,
            group_id STRING,
            created_at TIMESTAMP
        );
        """;
}
