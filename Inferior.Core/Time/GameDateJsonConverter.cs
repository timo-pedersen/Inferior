using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inferior.Core.Time;

public sealed class GameDateJsonConverter : JsonConverter<GameDate>
{
    public override GameDate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out int absoluteDay))
            throw new JsonException("GameDate must be represented by an integer absolute day.");

        try
        {
            return new GameDate(absoluteDay);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new JsonException("GameDate absolute day is outside the supported calendar range.", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, GameDate value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.AbsoluteDay);
}
