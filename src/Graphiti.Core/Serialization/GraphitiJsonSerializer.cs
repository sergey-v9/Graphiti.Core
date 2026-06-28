using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            CreateReflectionFallbackResolver());
        options.MakeReadOnly();
        return options;
    }

    // The reflection-based fallback resolver lets open Dictionary<string, object?> attribute values
    // (whose runtime value types are arbitrary/consumer-defined) round-trip through the shared options.
    // The known model and response DTOs are served first by the source-generated context above and never
    // reach this resolver; only the open-attribute value types do. This resolver is the single documented
    // reflection-serialization trim boundary for the library (see decisions.md). A trimmed host that feeds
    // exotic attribute value types into ingestion is responsible for preserving those types.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification =
            "Reflection fallback for arbitrary, consumer-defined open-attribute value types. This is the "
            + "single documented open-attribute serialization trim boundary; see decisions.md.")]
    private static DefaultJsonTypeInfoResolver CreateReflectionFallbackResolver() =>
        new();

    /// <summary>
    /// Resolves the <see cref="JsonTypeInfo{T}"/> for <typeparamref name="T"/> from the shared options. For
    /// the known model and response DTOs this is source-generated metadata with no reflection; the metadata
    /// carries the same naming policy and wire-value converters as <see cref="Options"/>, so the wire shape
    /// is identical. Open-attribute container types resolve their arbitrary <c>object</c> values through the
    /// reflection fallback resolver (the documented trim boundary).
    /// </summary>
    internal static JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)OptionsInstance.GetTypeInfo(typeof(T));

    /// <summary>Serializes a value to a string through the resolved <see cref="JsonTypeInfo{T}"/>.</summary>
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, TypeInfo<T>());

    /// <summary>Serializes a value to a <see cref="JsonNode"/> through the resolved <see cref="JsonTypeInfo{T}"/>.</summary>
    internal static JsonNode? SerializeToNode<T>(T value) => JsonSerializer.SerializeToNode(value, TypeInfo<T>());

    /// <summary>Serializes a value to a <see cref="JsonElement"/> through the resolved <see cref="JsonTypeInfo{T}"/>.</summary>
    internal static JsonElement SerializeToElement<T>(T value) => JsonSerializer.SerializeToElement(value, TypeInfo<T>());

    /// <summary>Deserializes a value from a string through the resolved <see cref="JsonTypeInfo{T}"/>.</summary>
    internal static T? Deserialize<T>(string json) => JsonSerializer.Deserialize(json, TypeInfo<T>());

    /// <summary>Deserializes a value from a <see cref="JsonNode"/> through the resolved <see cref="JsonTypeInfo{T}"/>.</summary>
    internal static T? Deserialize<T>(JsonNode node) => node.Deserialize(TypeInfo<T>());

    /// <summary>Deserializes a value from a <see cref="JsonElement"/> through the resolved <see cref="JsonTypeInfo{T}"/>.</summary>
    internal static T? Deserialize<T>(JsonElement element) => element.Deserialize(TypeInfo<T>());
}
