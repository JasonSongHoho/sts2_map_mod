using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2MapMod.Core;

/// <summary>
/// Per room-type visual: RGB <see cref="Color"/> (hex), optional <see cref="Alpha"/> (0–1), optional <see cref="Brightness"/> (RGB multiplier, default 1).
/// JSON may be a string <c>"#RRGGBB"</c> / <c>"#RRGGBBAA"</c> or an object <c>{ "Color", "Alpha", "Brightness" }</c>.
/// </summary>
[JsonConverter(typeof(RoomTypeColorSettingsJsonConverter))]
public sealed class RoomTypeColorSettings
{
    /// <summary>Hex RGB or RGBA, e.g. <c>#7A8CA5</c> or <c>#7A8CA5FF</c>.</summary>
    public string Color { get; set; } = "#FFFFFF";

    /// <summary>Opacity 0–1. For 6-digit hex, final alpha = this value. For 8-digit hex, final alpha = (hex alpha) × this.</summary>
    public float Alpha { get; set; } = 1f;

    /// <summary>Optional RGB multiplier (default 1). Values &gt; 1 brighten until components clamp to 1.</summary>
    public float? Brightness { get; set; }
}

public sealed class RoomTypeColorSettingsJsonConverter : JsonConverter<RoomTypeColorSettings>
{
    public override RoomTypeColorSettings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
            {
                var s = reader.GetString()?.Trim() ?? "#FFFFFF";
                return new RoomTypeColorSettings { Color = s, Alpha = 1f, Brightness = null };
            }
            case JsonTokenType.StartObject:
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                var root = doc.RootElement;
                var result = new RoomTypeColorSettings();
                if (TryGetProperty(root, "Color", out var c))
                    result.Color = c.GetString()?.Trim() ?? "#FFFFFF";
                if (TryGetProperty(root, "Alpha", out var a))
                    result.Alpha = a.GetSingle();
                if (TryGetProperty(root, "Brightness", out var b))
                    result.Brightness = b.ValueKind == JsonValueKind.Null ? null : b.GetSingle();
                return result;
            }
            case JsonTokenType.Null:
                reader.Read();
                return new RoomTypeColorSettings();
            default:
                throw new JsonException($"Unexpected token for RoomTypeColorSettings: {reader.TokenType}");
        }
    }

    public override void Write(Utf8JsonWriter writer, RoomTypeColorSettings value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Color", value.Color);
        writer.WriteNumber("Alpha", value.Alpha);
        if (value.Brightness.HasValue)
            writer.WriteNumber("Brightness", value.Brightness.Value);
        writer.WriteEndObject();
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement el)
    {
        foreach (var p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                el = p.Value;
                return true;
            }
        }

        el = default;
        return false;
    }
}
