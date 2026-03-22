using Godot;

namespace Sts2MapMod.Core;

/// <summary>
/// Maps RoomKind + <see cref="MapColorConfig"/> to Godot <see cref="Color"/> (icon modulate).
/// </summary>
public static class ColorPalette
{
    public static Color Get(RoomKind kind, MapColorConfig config) =>
        ToGodotColor(kind switch
        {
            RoomKind.Monster => config.Monster,
            RoomKind.Elite => config.Elite,
            RoomKind.Rest => config.Rest,
            RoomKind.Shop => config.Shop,
            RoomKind.Treasure => config.Treasure,
            RoomKind.Event => config.Event,
            RoomKind.Boss => config.Boss,
            RoomKind.Custom => config.Custom,
            _ => config.Unknown
        });

    /// <summary>Applies hex RGB(A), <see cref="RoomTypeColorSettings.Alpha"/>, and optional <see cref="RoomTypeColorSettings.Brightness"/>.</summary>
    public static Color ToGodotColor(RoomTypeColorSettings s)
    {
        var hex = NormalizeHex(s.Color);
        if (hex.Length < 6)
            return Colors.White;

        float Byte(int offset) => Convert.ToInt32(hex.Substring(offset, 2), 16) / 255f;

        var r = Byte(0);
        var g = Byte(2);
        var b = Byte(4);
        var aFromHex = hex.Length >= 8 ? Byte(6) : 1f;

        var bright = s.Brightness ?? 1f;
        r = Mathf.Clamp(r * bright, 0f, 1f);
        g = Mathf.Clamp(g * bright, 0f, 1f);
        b = Mathf.Clamp(b * bright, 0f, 1f);
        var a = Mathf.Clamp(aFromHex * s.Alpha, 0f, 1f);
        return new Color(r, g, b, a);
    }

    private static string NormalizeHex(string? raw)
    {
        return MapConfigValidator.TryNormalizeHexColor(raw, out var normalized, out _)
            ? normalized.TrimStart('#')
            : string.Empty;
    }
}
