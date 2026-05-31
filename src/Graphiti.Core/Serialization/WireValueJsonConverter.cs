using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Graphiti.Core.Serialization;

internal sealed class WireValueJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private readonly FrozenDictionary<string, TEnum> _byWireValue;
    private readonly FrozenDictionary<TEnum, string> _byEnumValue;

    public WireValueJsonConverter(params (TEnum Value, string WireValue)[] values)
    {
        _byEnumValue = values.ToFrozenDictionary(pair => pair.Value, pair => pair.WireValue);
        _byWireValue = values.ToFrozenDictionary(
            pair => pair.WireValue,
            pair => pair.Value,
            StringComparer.Ordinal);
    }

    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var wireValue = reader.GetString();
            if (wireValue is not null && _byWireValue.TryGetValue(wireValue, out var value))
            {
                return value;
            }

            throw new JsonException($"Unsupported {typeof(TEnum).Name} wire value '{wireValue}'.");
        }

        throw new JsonException($"Expected a {typeof(TEnum).Name} wire value.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        TEnum value,
        JsonSerializerOptions options)
    {
        if (_byEnumValue.TryGetValue(value, out var wireValue))
        {
            writer.WriteStringValue(wireValue);
            return;
        }

        throw new JsonException($"Unsupported {typeof(TEnum).Name} value '{value}'.");
    }
}
