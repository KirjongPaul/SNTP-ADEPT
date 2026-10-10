using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sntp.Adept.Domain.Models;

namespace Sntp.Adept.Domain.Json;

public static class PirJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(PirDocument doc) => JsonSerializer.Serialize(doc, Options);

    public static PirDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<PirDocument>(json, Options)
        ?? throw new InvalidOperationException("Пустой PIR-IR JSON.");
}
