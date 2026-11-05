using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgriSage.Api.Extensions;

// A date-time in a request body may carry any offset ("2026-01-01T00:00:00+07:00" from a browser or a phone). The database column
// is timestamptz and Npgsql refuses to write a value whose offset is not zero, which used to surface as HTTP 500. The instant is
// what matters, so every incoming DateTimeOffset is normalised to UTC before it reaches the Application layer. Output is unchanged.
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
