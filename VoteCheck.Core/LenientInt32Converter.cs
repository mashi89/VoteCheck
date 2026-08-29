using System;
using Newtonsoft.Json;

namespace VoteCheck.Core
{
    // Reads an integer that upstream may send as a number, as a numeric string ("1109"), or
    // as a placeholder meaning "none" — the vote archive uses "-" for a division with no
    // Speaker recorded (2 of 15,562 as of 2026-08). A plain int property throws on those,
    // which would fail a whole sync page over two rows, so anything unparseable becomes null.
    internal sealed class LenientInt32Converter : JsonConverter<int?>
    {
        public override int? ReadJson(
            JsonReader reader,
            Type objectType,
            int? existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            object? raw = reader.Value;

            if (raw is long l)
                return (int)l;

            return int.TryParse(raw?.ToString(), out int parsed) ? parsed : null;
        }

        public override void WriteJson(JsonWriter writer, int? value, JsonSerializer serializer)
        {
            if (value.HasValue)
                writer.WriteValue(value.Value);
            else
                writer.WriteNull();
        }
    }
}
