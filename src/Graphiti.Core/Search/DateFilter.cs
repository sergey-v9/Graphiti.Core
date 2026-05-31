using System.Text.Json.Serialization;

namespace Graphiti.Core.Search;

/// <summary>A predicate over a temporal field, comparing it against a date with an operator.</summary>
public sealed class DateFilter
{
    /// <summary>Creates an empty date filter (for deserialization).</summary>
    public DateFilter()
    {
    }

    /// <summary>Creates a date filter with the given operator and optional date operand.</summary>
    public DateFilter(ComparisonOperator comparisonOperator, DateTime? date = null)
    {
        ComparisonOperator = comparisonOperator;
        Date = date;
    }

    /// <summary>The date operand; ignored for the null/non-null operators.</summary>
    [JsonPropertyName("date")]
    public DateTime? Date { get; set; }

    /// <summary>The comparison applied to the temporal field.</summary>
    [JsonPropertyName("comparison_operator")]
    public ComparisonOperator ComparisonOperator { get; set; }
}
