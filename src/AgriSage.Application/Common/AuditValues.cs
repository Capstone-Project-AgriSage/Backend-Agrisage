using System.Text;
using System.Text.Json;

namespace AgriSage.Application.Common;

// Protect both new writes and legacy JSON returned through the audit API.
public static class AuditValues
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string? Serialize(object? value) => value is null
        ? null : Redact(JsonSerializer.SerializeToElement(value, Json)).GetRawText();

    public static JsonElement? Parse(string? value) => value is null
        ? null : Redact(JsonSerializer.Deserialize<JsonElement>(value));

    private static JsonElement Redact(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, value);
        return JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (IsSensitive(property.Name)) writer.WriteStringValue("[REDACTED]");
                else Write(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) Write(writer, item);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }

    private static bool IsSensitive(string name)
    {
        var key = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return key.Contains("password") || key.Contains("passwd") || key.Contains("token")
            || key.Contains("secret") || key.Contains("apikey") || key.Contains("authorization")
            || key.Contains("cookie") || key is "pwd" or "otp" or "otpcode" or "verificationcode"
                or "resetcode" or "onetimecode";
    }
}
