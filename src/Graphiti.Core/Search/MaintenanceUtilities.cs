namespace Graphiti.Core.Search;

/// <summary>
/// Helpers for graph-maintenance flows: constructing the episodic and community edges that connect
/// extracted entities back to their episodes/communities, generating embeddings for new nodes and
/// edges, and resolving edge endpoint pointers after node deduplication.
/// </summary>
public static class MaintenanceUtilities
{
    /// <summary>Number of preceding episodes included as context when processing an episode.</summary>
    public const int EpisodeWindowLength = 3;

    /// <summary>Builds episodic edges linking a single episode to the given entity nodes.</summary>
    public static IReadOnlyList<EpisodicEdge> BuildEpisodicEdges(
        IReadOnlyList<EntityNode> entityNodes,
        string episodeUuid,
        DateTime createdAt,
        IReadOnlyDictionary<string, IReadOnlyList<int>>? nodeEpisodeIndexMap = null) =>
        BuildEpisodicEdges(entityNodes, new[] { episodeUuid }, createdAt, nodeEpisodeIndexMap);

    /// <summary>
    /// Builds episodic edges linking the given entity nodes to one or more episodes. The optional
    /// <paramref name="nodeEpisodeIndexMap"/> restricts which episode indices each node is linked to;
    /// when omitted, every node is linked to every episode.
    /// </summary>
    public static IReadOnlyList<EpisodicEdge> BuildEpisodicEdges(
        IReadOnlyList<EntityNode> entityNodes,
        IReadOnlyList<string> episodeUuids,
        DateTime createdAt,
        IReadOnlyDictionary<string, IReadOnlyList<int>>? nodeEpisodeIndexMap = null)
    {
        var edges = new List<EpisodicEdge>();
        foreach (var node in entityNodes)
        {
            if (nodeEpisodeIndexMap is not null && nodeEpisodeIndexMap.TryGetValue(node.Uuid, out var mapped))
            {
                foreach (var index in mapped)
                {
                    AddEdgeIfValid(index);
                }

                continue;
            }

            for (var index = 0; index < episodeUuids.Count; index++)
            {
                AddEdgeIfValid(index);
            }

            void AddEdgeIfValid(int index)
            {
                if ((uint)index >= (uint)episodeUuids.Count)
                {
                    return;
                }

                edges.Add(new EpisodicEdge
                {
                    SourceNodeUuid = episodeUuids[index],
                    TargetNodeUuid = node.Uuid,
                    CreatedAt = createdAt,
                    GroupId = node.GroupId
                });
            }
        }

        return edges;
    }

    /// <summary>Builds community edges linking a community node to its member entity nodes.</summary>
    public static IReadOnlyList<CommunityEdge> BuildCommunityEdges(
        IReadOnlyList<EntityNode> entityNodes,
        CommunityNode communityNode,
        DateTime createdAt) =>
        entityNodes
            .Select(node => new CommunityEdge
            {
                SourceNodeUuid = communityNode.Uuid,
                TargetNodeUuid = node.Uuid,
                CreatedAt = createdAt,
                GroupId = communityNode.GroupId
            })
            .ToList();

    /// <summary>Generates name embeddings for any entity nodes that do not yet have one.</summary>
    public static async Task CreateEntityNodeEmbeddingsAsync(
        IEmbedderClient embedder,
        IReadOnlyList<EntityNode> nodes,
        CancellationToken cancellationToken = default)
    {
        foreach (var node in nodes.Where(node => node.NameEmbedding is null))
        {
            await node.GenerateNameEmbeddingAsync(embedder, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Generates fact embeddings for any entity edges that do not yet have one.</summary>
    public static async Task CreateEntityEdgeEmbeddingsAsync(
        IEmbedderClient embedder,
        IReadOnlyList<EntityEdge> edges,
        CancellationToken cancellationToken = default)
    {
        foreach (var edge in edges.Where(edge => edge.FactEmbedding is null))
        {
            await edge.GenerateEmbeddingAsync(embedder, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Rewrites edge source/target UUIDs through a deduplication map so edges point at canonical nodes.
    /// </summary>
    public static IReadOnlyList<T> ResolveEdgePointers<T>(
        IReadOnlyList<T> edges,
        IReadOnlyDictionary<string, string> uuidMap)
        where T : Edge
    {
        foreach (var edge in edges)
        {
            if (uuidMap.TryGetValue(edge.SourceNodeUuid, out var sourceUuid))
            {
                edge.SourceNodeUuid = sourceUuid;
            }

            if (uuidMap.TryGetValue(edge.TargetNodeUuid, out var targetUuid))
            {
                edge.TargetNodeUuid = targetUuid;
            }
        }

        return edges;
    }
}
