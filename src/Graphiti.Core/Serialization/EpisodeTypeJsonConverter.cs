using System.Text.Json;
using System.Text.Json.Serialization;

namespace Graphiti.Core.Serialization;

internal sealed class EpisodeTypeJsonConverter : JsonConverter<EpisodeType>
{
    public override EpisodeType Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var source = reader.GetString();
            if (EpisodeTypeExtensions.TryFromWireValue(source, out var episodeType))
            {
                return episodeType;
            }

            if (source is not null)
            {
                throw new JsonException($"Unsupported episode source value '{source}'.");
            }
        }

        throw new JsonException("Expected an episode source wire value.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        EpisodeType value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToWireValue());
}
