namespace Sts2MapMod.Core;

/// <summary>
/// Validation helpers for map color settings.
/// </summary>
public static class MapConfigValidator
{
    public static bool TryNormalizeHexColor(string? raw, out string normalized, out string errorKey)
    {
        var hex = (raw ?? string.Empty).Trim().TrimStart('#');
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(hex))
        {
            errorKey = "empty";
            return false;
        }

        if (hex.Length is 3 or 4)
            hex = string.Concat(hex.Select(c => $"{c}{c}"));

        if (hex.Length is not (6 or 8))
        {
            errorKey = "length";
            return false;
        }

        foreach (var c in hex)
        {
            if (!Uri.IsHexDigit(c))
            {
                errorKey = "chars";
                return false;
            }
        }

        normalized = "#" + hex.ToUpperInvariant();
        errorKey = string.Empty;
        return true;
    }

    public static IReadOnlyList<string> ValidateAndNormalize(MapColorConfig cfg)
    {
        var warnings = new List<string>();

        foreach (RoomKind kind in Enum.GetValues<RoomKind>())
        {
            var settings = SettingsFor(cfg, kind);
            if (TryNormalizeHexColor(settings.Color, out var normalized, out _))
            {
                settings.Color = normalized;
            }
            else
            {
                warnings.Add($"{kind}: invalid color '{settings.Color}', reset to #FFFFFF");
                settings.Color = "#FFFFFF";
            }

            var alpha = Math.Clamp(settings.Alpha, 0f, 1f);
            if (Math.Abs(alpha - settings.Alpha) > 0.0001f)
            {
                warnings.Add($"{kind}: alpha {settings.Alpha:0.###} clamped to {alpha:0.###}");
                settings.Alpha = alpha;
            }

            if (!settings.Brightness.HasValue)
                continue;

            var brightness = Math.Clamp(settings.Brightness.Value, 0.25f, 2f);
            if (Math.Abs(brightness - settings.Brightness.Value) > 0.0001f)
            {
                warnings.Add($"{kind}: brightness {settings.Brightness.Value:0.###} clamped to {brightness:0.###}");
                settings.Brightness = brightness;
            }

            if (Math.Abs(settings.Brightness.Value - 1f) < 0.0001f)
                settings.Brightness = null;
        }

        return warnings;
    }

    private static RoomTypeColorSettings SettingsFor(MapColorConfig c, RoomKind k) => k switch
    {
        RoomKind.Monster => c.Monster,
        RoomKind.Elite => c.Elite,
        RoomKind.Rest => c.Rest,
        RoomKind.Shop => c.Shop,
        RoomKind.Treasure => c.Treasure,
        RoomKind.Event => c.Event,
        RoomKind.Boss => c.Boss,
        RoomKind.Unknown => c.Unknown,
        RoomKind.Custom => c.Custom,
        _ => c.Unknown
    };
}
