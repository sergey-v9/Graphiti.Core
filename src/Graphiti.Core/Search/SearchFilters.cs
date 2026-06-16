using System.Text.Json.Serialization;

namespace Graphiti.Core.Search;

/// <summary>
/// Constraints applied to search candidates before ranking. The temporal fields use a list-of-lists
/// shape where each inner list is combined with AND and the outer list is combined with OR (matching
/// the Python filter semantics). Setting <see cref="NodeLabels"/> validates the labels.
/// </summary>
public sealed class SearchFilters
{
    private List<string>? _nodeLabels;

    /// <summary>Restrict to nodes carrying these labels. Validated on assignment.</summary>
    [JsonPropertyName("node_labels")]
    public List<string>? NodeLabels
    {
        get => _nodeLabels;
        set
        {
            GraphitiHelpers.ValidateNodeLabels(value);
            _nodeLabels = value;
        }
    }

    /// <summary>Restrict to edges of these relationship types.</summary>
    [JsonPropertyName("edge_types")]
    public List<string>? EdgeTypes { get; set; }

    /// <summary>Predicates over the fact's <c>valid_at</c> field (OR of AND-groups).</summary>
    [JsonPropertyName("valid_at")]
    public List<List<DateFilter>>? ValidAt { get; set; }

    /// <summary>Predicates over the fact's <c>invalid_at</c> field (OR of AND-groups).</summary>
    [JsonPropertyName("invalid_at")]
    public List<List<DateFilter>>? InvalidAt { get; set; }

    /// <summary>Predicates over the <c>created_at</c> field (OR of AND-groups).</summary>
    [JsonPropertyName("created_at")]
    public List<List<DateFilter>>? CreatedAt { get; set; }

    /// <summary>Predicates over the <c>expired_at</c> field (OR of AND-groups).</summary>
    [JsonPropertyName("expired_at")]
    public List<List<DateFilter>>? ExpiredAt { get; set; }

    /// <summary>Restrict to edges with these UUIDs.</summary>
    [JsonPropertyName("edge_uuids")]
    public List<string>? EdgeUuids { get; set; }

    /// <summary>Predicates over arbitrary node/edge properties.</summary>
    [JsonPropertyName("property_filters")]
    public List<PropertyFilter>? PropertyFilters { get; set; }
}
