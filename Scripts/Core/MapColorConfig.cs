namespace Sts2MapMod.Core;

/// <summary>
/// Configuration for map node coloring. Loaded once at startup and cached in memory
/// (no disk reads during map render — avoids Colored Map–style lag).
/// </summary>
public sealed class MapColorConfig
{
    public bool Enabled { get; set; } = true;
    public bool ColorNodeIcon { get; set; } = true;
    public bool ColorPathLine { get; set; } = false; // Phase 2; keep off for Phase 1
    public bool AffectCompletedNodes { get; set; } = true;
    public bool AffectUnreachableNodes { get; set; } = true;

    /// <summary>
    /// When true, tints the whole patched map screen root bright magenta so you can see that the Harmony postfix ran.
    /// Set in map_color_config.json, restart game, open the map — if nothing turns pink, the patch target is wrong (see README).
    /// </summary>
    public bool VerifyDiagnosticTintMapScreen { get; set; } = false;

    /// <summary>
    /// When true, prints to Godot console / log on init and each map postfix (spammy; for debugging).
    /// </summary>
    public bool VerifyGodotPrint { get; set; } = false;

    /// <summary>
    /// When true (default), checks <c>map_color_config.json.txt</c> mtime (throttled) when the map screen refreshes and reloads config.
    /// Lets you tune colors without restarting the game. C# / Harmony code changes still need a restart.
    /// </summary>
    public bool HotReloadConfigOnMap { get; set; } = true;

    /// <summary>Per room kind: color (hex), <see cref="RoomTypeColorSettings.Alpha"/>, optional <see cref="RoomTypeColorSettings.Brightness"/>.</summary>
    /// <remarks>Defaults are readable on the parchment (avoid very dark modulate on large UI regions).</remarks>
    public RoomTypeColorSettings Monster { get; set; } = new() { Color = "#DDD2C3", Alpha = 0.92f };
    public RoomTypeColorSettings Elite { get; set; } = new() { Color = "#6B355C", Alpha = 1f };
    public RoomTypeColorSettings Rest { get; set; } = new() { Color = "#D84A2F", Alpha = 1f };
    public RoomTypeColorSettings Shop { get; set; } = new() { Color = "#6B7D2C", Alpha = 1f };
    public RoomTypeColorSettings Treasure { get; set; } = new() { Color = "#D5A63A", Alpha = 1f };
    public RoomTypeColorSettings Event { get; set; } = new() { Color = "#4FA15A", Alpha = 0.95f };
    public RoomTypeColorSettings Boss { get; set; } = new() { Color = "#E67E22", Alpha = 1f };
    public RoomTypeColorSettings Unknown { get; set; } = new() { Color = "#4FA15A", Alpha = 0.95f };
    public RoomTypeColorSettings Custom { get; set; } = new() { Color = "#C9D1DC", Alpha = 1f };

    public static MapColorConfig CreateDefault() => new();

    public void RestoreDefaults()
    {
        var d = CreateDefault();
        Enabled = d.Enabled;
        ColorNodeIcon = d.ColorNodeIcon;
        ColorPathLine = d.ColorPathLine;
        AffectCompletedNodes = d.AffectCompletedNodes;
        AffectUnreachableNodes = d.AffectUnreachableNodes;
        VerifyDiagnosticTintMapScreen = d.VerifyDiagnosticTintMapScreen;
        VerifyGodotPrint = d.VerifyGodotPrint;
        HotReloadConfigOnMap = d.HotReloadConfigOnMap;
        Monster = Clone(d.Monster);
        Elite = Clone(d.Elite);
        Rest = Clone(d.Rest);
        Shop = Clone(d.Shop);
        Treasure = Clone(d.Treasure);
        Event = Clone(d.Event);
        Boss = Clone(d.Boss);
        Unknown = Clone(d.Unknown);
        Custom = Clone(d.Custom);
    }

    private static RoomTypeColorSettings Clone(RoomTypeColorSettings s) => new()
    {
        Color = s.Color,
        Alpha = s.Alpha,
        Brightness = s.Brightness
    };
}
