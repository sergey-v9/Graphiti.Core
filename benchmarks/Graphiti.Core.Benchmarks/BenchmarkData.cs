using System.Globalization;
using System.Text;
using Graphiti.Core.Models.Edges;
using Graphiti.Core.Models.Nodes;

namespace Graphiti.Core.Benchmarks;

/// <summary>
/// Deterministic, seeded generators for representative search/text inputs. Kept seeded so repeated
/// benchmark runs measure the same data distribution.
/// </summary>
internal static class BenchmarkData
{
    private static readonly string[] Vocabulary =
    [
        "alice", "bob", "carol", "graph", "node", "edge", "temporal", "knowledge", "entity",
        "fact", "relationship", "embedding", "semantic", "search", "retrieval", "memory", "agent",
        "context", "episode", "summary", "vector", "cosine", "similarity", "ranking", "fusion",
        "company", "product", "person", "concept", "location", "event", "founded", "acquired",
        "released", "works", "lives", "owns", "manages", "develops", "supports", "integrates",
    ];

    public static List<(EntityEdge Item, float Score)> CreateRankedEdges(int count, int seed = 17)
    {
        var random = new Random(seed);
        var ranked = new List<(EntityEdge Item, float Score)>(count);
        for (var i = 0; i < count; i++)
        {
            var edge = new EntityEdge
            {
                Uuid = $"edge-{i:D5}",
                SourceNodeUuid = $"node-{random.Next(0, count / 4 + 1):D5}",
                TargetNodeUuid = $"node-{random.Next(0, count / 4 + 1):D5}",
                Name = "RELATES_TO",
                Fact = CreateSentence(random, random.Next(8, 24)),
                GroupId = "group-a",
            };
            var episodeCount = random.Next(1, 6);
            for (var e = 0; e < episodeCount; e++)
            {
                edge.Episodes.Add($"episode-{random.Next(0, 50):D4}");
            }

            ranked.Add((edge, (float)random.NextDouble()));
        }

        return ranked;
    }

    public static List<(EntityNode Item, float Score)> CreateRankedNodes(int count, int seed = 23)
    {
        var random = new Random(seed);
        var ranked = new List<(EntityNode Item, float Score)>(count);
        for (var i = 0; i < count; i++)
        {
            var node = new EntityNode
            {
                Uuid = $"node-{i:D5}",
                Name = CreateSentence(random, random.Next(1, 4)),
                Summary = CreateSentence(random, random.Next(10, 40)),
                GroupId = "group-a",
            };
            ranked.Add((node, (float)random.NextDouble()));
        }

        return ranked;
    }

    /// <summary>Builds N ranked lists of edges that share a fraction of keys (to exercise RRF merge).</summary>
    public static List<List<(EntityEdge Item, float Score)>> CreateOverlappingEdgeLists(
        int listCount,
        int perList,
        int seed = 31)
    {
        var pool = CreateRankedEdges(perList * 2, seed);
        var random = new Random(seed + 1);
        var lists = new List<List<(EntityEdge Item, float Score)>>(listCount);
        for (var l = 0; l < listCount; l++)
        {
            var list = new List<(EntityEdge Item, float Score)>(perList);
            for (var i = 0; i < perList; i++)
            {
                var pick = pool[random.Next(pool.Count)];
                list.Add((pick.Item, (float)random.NextDouble()));
            }

            // Stable per-list ordering by descending score (RRF consumes rank order).
            list.Sort(static (a, b) => b.Score.CompareTo(a.Score));
            lists.Add(list);
        }

        return lists;
    }

    public static float[] CreateUnitVector(int dimension, int seed)
    {
        var random = new Random(seed);
        var vector = new float[dimension];
        double norm = 0;
        for (var i = 0; i < dimension; i++)
        {
            var value = (float)(random.NextDouble() * 2 - 1);
            vector[i] = value;
            norm += value * (double)value;
        }

        var inverse = norm > 0 ? 1.0 / Math.Sqrt(norm) : 0;
        for (var i = 0; i < dimension; i++)
        {
            vector[i] = (float)(vector[i] * inverse);
        }

        return vector;
    }

    public static List<(EntityNode Item, float Score)> CreateRankedNodesWithEmbeddings(
        int count,
        int dimension,
        int seed = 41)
    {
        var ranked = CreateRankedNodes(count, seed);
        for (var i = 0; i < ranked.Count; i++)
        {
            ranked[i].Item.NameEmbedding = [.. CreateUnitVector(dimension, seed + i + 1)];
        }

        return ranked;
    }

    public static string CreateDocument(int approximateWords, int seed = 53)
    {
        var random = new Random(seed);
        var builder = new StringBuilder(approximateWords * 7);
        var wordsThisSentence = 0;
        var targetSentence = random.Next(8, 20);
        for (var i = 0; i < approximateWords; i++)
        {
            if (wordsThisSentence == 0)
            {
                var word = Vocabulary[random.Next(Vocabulary.Length)];
                builder.Append(char.ToUpperInvariant(word[0])).Append(word.AsSpan(1));
            }
            else
            {
                builder.Append(Vocabulary[random.Next(Vocabulary.Length)]);
            }

            wordsThisSentence++;
            if (wordsThisSentence >= targetSentence)
            {
                builder.Append(". ");
                wordsThisSentence = 0;
                targetSentence = random.Next(8, 20);
            }
            else
            {
                builder.Append(' ');
            }
        }

        return builder.ToString();
    }

    private static string CreateSentence(Random random, int words)
    {
        var builder = new StringBuilder(words * 7);
        for (var i = 0; i < words; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(Vocabulary[random.Next(Vocabulary.Length)]);
        }

        return builder.ToString();
    }

    public static string Query => string.Create(
        CultureInfo.InvariantCulture,
        $"knowledge graph temporal entity fact relationship search");
}
