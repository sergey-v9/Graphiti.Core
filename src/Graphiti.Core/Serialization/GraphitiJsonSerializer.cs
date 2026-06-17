using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Graphiti.Core.Serialization;

/// <summary>
/// Provides the canonical <see cref="JsonSerializerOptions"/> used across the library. Configures
/// snake_case property naming, relaxed escaping, source-generated type metadata, and the wire-value
/// enum converters that produce the library's stable JSON wire shape.
/// </summary>
public static class GraphitiJsonSerializer
{
    private static readonly JsonSerializerOptions OptionsInstance = CreateOptions();

    /// <summary>The shared, read-only serializer options used for all Graphiti JSON.</summary>
    public static JsonSerializerOptions Options => OptionsInstance;

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false
        };
        options.Converters.Add(new EpisodeTypeJsonConverter());
        options.Converters.Add(new WireValueJsonConverter<EdgeSearchMethod>(
            (EdgeSearchMethod.CosineSimilarity, "cosine_similarity"),
            (EdgeSearchMethod.Bm25, "bm25"),
            (EdgeSearchMethod.Bfs, "breadth_first_search")));
        options.Converters.Add(new WireValueJsonConverter<NodeSearchMethod>(
            (NodeSearchMethod.CosineSimilarity, "cosine_similarity"),
            (NodeSearchMethod.Bm25, "bm25"),
            (NodeSearchMethod.Bfs, "breadth_first_search")));
        options.Converters.Add(new WireValueJsonConverter<EpisodeSearchMethod>(
            (EpisodeSearchMethod.Bm25, "bm25")));
        options.Converters.Add(new WireValueJsonConverter<CommunitySearchMethod>(
            (CommunitySearchMethod.CosineSimilarity, "cosine_similarity"),
            (CommunitySearchMethod.Bm25, "bm25")));
        options.Converters.Add(new WireValueJsonConverter<EdgeReranker>(
            (EdgeReranker.Rrf, "reciprocal_rank_fusion"),
            (EdgeReranker.NodeDistance, "node_distance"),
            (EdgeReranker.EpisodeMentions, "episode_mentions"),
            (EdgeReranker.Mmr, "mmr"),
            (EdgeReranker.CrossEncoder, "cross_encoder")));
        options.Converters.Add(new WireValueJsonConverter<NodeReranker>(
            (NodeReranker.Rrf, "reciprocal_rank_fusion"),
            (NodeReranker.NodeDistance, "node_distance"),
            (NodeReranker.EpisodeMentions, "episode_mentions"),
            (NodeReranker.Mmr, "mmr"),
            (NodeReranker.CrossEncoder, "cross_encoder")));
        options.Converters.Add(new WireValueJsonConverter<EpisodeReranker>(
            (EpisodeReranker.Rrf, "reciprocal_rank_fusion"),
            (EpisodeReranker.CrossEncoder, "cross_encoder")));
        options.Converters.Add(new WireValueJsonConverter<CommunityReranker>(
            (CommunityReranker.Rrf, "reciprocal_rank_fusion"),
            (CommunityReranker.Mmr, "mmr"),
            (CommunityReranker.CrossEncoder, "cross_encoder")));
        options.Converters.Add(new WireValueJsonConverter<ComparisonOperator>(
            (ComparisonOperator.Equals, "="),
            (ComparisonOperator.NotEquals, "<>"),
            (ComparisonOperator.GreaterThan, ">"),
            (ComparisonOperator.LessThan, "<"),
            (ComparisonOperator.GreaterThanEqual, ">="),
            (ComparisonOperator.LessThanEqual, "<="),
            (ComparisonOperator.IsNull, "IS NULL"),
            (ComparisonOperator.IsNotNull, "IS NOT NULL")));
        var generatedOptions = new JsonSerializerOptions(options);
        options.TypeInfoResolver = JsonTypeInfoResolver.Combine(
            new GraphitiJsonSerializerContext(generatedOptions),
            new DefaultJsonTypeInfoResolver());
        options.MakeReadOnly();
        return options;
    }
}
