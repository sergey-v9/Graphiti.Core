using System.Text.Json;
using System.Text.Json.Serialization;

namespace Graphiti.Core.Search;

/// <summary>A predicate over a named property, comparing it against a value with an operator.</summary>
public sealed class PropertyFilter
{
    /// <summary>Creates an empty property filter (for deserialization).</summary>
    public PropertyFilter()
    {
    }

    /// <summary>Creates a property filter for the given property, operator, and optional value.</summary>
    public PropertyFilter(
        string propertyName,
        ComparisonOperator comparisonOperator,
        object? propertyValue = null)
    {
        PropertyName = propertyName;
        PropertyValue = propertyValue;
        ComparisonOperator = comparisonOperator;
    }

    /// <summary>Name of the property to filter on.</summary>
    [JsonPropertyName("property_name")]
    public string PropertyName { get; set; } = string.Empty;

    /// <summary>The value operand (string, integer, float, or null).</summary>
    [JsonPropertyName("property_value")]
    [JsonConverter(typeof(PropertyFilterValueJsonConverter))]
    public object? PropertyValue { get; set; }

    /// <summary>The comparison applied to the property.</summary>
    [JsonPropertyName("comparison_operator")]
    public ComparisonOperator ComparisonOperator { get; set; }
}

internal sealed class PropertyFilterValueJsonConverter : JsonConverter<object?>
{
    public override object? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt64(out var value) => value,
            JsonTokenType.Number => reader.GetDouble(),
            _ => throw new JsonException(
                "Property filter values must be string, integer, float, or null.")
        };

    public override void Write(
        Utf8JsonWriter writer,
        object? value,
        JsonSerializerOptions options)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;
            case string text:
                writer.WriteStringValue(text);
                return;
            case byte number:
                writer.WriteNumberValue(number);
                return;
            case sbyte number:
                writer.WriteNumberValue(number);
                return;
            case short number:
                writer.WriteNumberValue(number);
                return;
            case ushort number:
                writer.WriteNumberValue(number);
                return;
            case int number:
                writer.WriteNumberValue(number);
                return;
            case uint number:
                writer.WriteNumberValue(number);
                return;
            case long number:
                writer.WriteNumberValue(number);
                return;
            case ulong number:
                writer.WriteNumberValue(number);
                return;
            case float number:
                writer.WriteNumberValue(number);
                return;
            case double number:
                writer.WriteNumberValue(number);
                return;
            case decimal number:
                writer.WriteNumberValue(number);
                return;
            default:
                throw new JsonException(
                    "Property filter values must be string, integer, float, or null.");
        }
    }
}
