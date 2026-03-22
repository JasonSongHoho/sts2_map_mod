using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using Sts2MapMod.Core;
using Sts2MapMod.Localization;
using Sts2MapMod.Utils;

namespace Sts2MapMod.ModConfig;

public static class ModConfigPreviewInjector
{
    private static readonly Dictionary<RoomKind, string> PreviewIconPaths = new()
    {
        [RoomKind.Monster] = "res://images/atlases/ui_atlas.sprites/map/icons/map_monster.tres",
        [RoomKind.Elite] = "res://images/atlases/ui_atlas.sprites/map/icons/map_elite.tres",
        [RoomKind.Rest] = "res://images/atlases/ui_atlas.sprites/map/icons/map_rest.tres",
        [RoomKind.Shop] = "res://images/atlases/ui_atlas.sprites/map/icons/map_shop.tres",
        [RoomKind.Treasure] = "res://images/atlases/ui_atlas.sprites/map/icons/map_chest.tres",
        [RoomKind.Event] = "res://images/atlases/ui_atlas.sprites/map/icons/map_unknown.tres",
        [RoomKind.Unknown] = "res://images/atlases/ui_atlas.sprites/map/icons/map_unknown.tres"
    };

    private static readonly RoomKind[] PreviewKinds =
    {
        RoomKind.Unknown,
        RoomKind.Monster,
        RoomKind.Elite,
        RoomKind.Rest,
        RoomKind.Shop,
        RoomKind.Treasure,
        RoomKind.Event
    };

    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        tree.NodeAdded += OnNodeAdded;
        tree.ProcessFrame += ScanOpenSettings;
        LogUtil.Info("ModConfigPreviewInjector: initialized.");
    }

    public static void RefreshAll()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;
        tree.ProcessFrame += ScanOpenSettings;
    }

    private static void OnNodeAdded(Node node)
    {
        if (node is not Control)
            return;
        if (Engine.GetMainLoop() is SceneTree tree)
            tree.ProcessFrame += ScanOpenSettings;
    }

    private static void ScanOpenSettings()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
            return;
        tree.ProcessFrame -= ScanOpenSettings;

        var root = tree.Root;
        if (root == null)
            return;

        foreach (var panel in GodotNodeUtil.EnumerateChildrenRecursive(root).OfType<NSettingsPanel>())
            TryInjectPanel(panel);
    }

    private static void TryInjectPanel(NSettingsPanel panel)
    {
        foreach (var kind in PreviewKinds)
        {
            var title = MapModL10n.KindTitle(kind);
            foreach (var label in GodotNodeUtil.EnumerateChildrenRecursive(panel).OfType<Label>())
            {
                if (!string.Equals(label.Text?.Trim(), title, StringComparison.Ordinal))
                    continue;
                if (label.GetParent() is not HBoxContainer row)
                    continue;
                if (row.GetNodeOrNull<TextureRect>($"Sts2Preview_{kind}") != null)
                    continue;

                InjectPreview(row, kind);
            }
        }

        RefreshPreviewsUnder(panel);
    }

    private static void InjectPreview(HBoxContainer row, RoomKind kind)
    {
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });

        var previewLabel = new Label
        {
            Text = MapModL10n.T("Preview"),
            Name = $"Sts2PreviewLabel_{kind}",
            VerticalAlignment = VerticalAlignment.Center
        };
        previewLabel.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(previewLabel);

        var preview = new TextureRect
        {
            Name = $"Sts2Preview_{kind}",
            Texture = LoadPreviewTexture(kind),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(56, 56),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            TooltipText = MapModL10n.KindTitle(kind)
        };
        row.AddChild(preview);
        ApplyPreview(preview, kind, ConfigLoader.Config);
    }

    private static void RefreshPreviewsUnder(Node root)
    {
        var config = ConfigLoader.Config;
        foreach (var node in GodotNodeUtil.EnumerateChildrenRecursive(root).OfType<TextureRect>())
        {
            if (!node.Name.ToString().StartsWith("Sts2Preview_", StringComparison.Ordinal))
                continue;
            if (!Enum.TryParse<RoomKind>(node.Name.ToString()["Sts2Preview_".Length..], out var kind))
                continue;
            ApplyPreview(node, kind, config);
        }
    }

    private static void ApplyPreview(TextureRect preview, RoomKind kind, MapColorConfig config)
    {
        preview.Texture ??= LoadPreviewTexture(kind);
        var color = kind switch
        {
            RoomKind.Monster => config.Monster,
            RoomKind.Elite => config.Elite,
            RoomKind.Rest => config.Rest,
            RoomKind.Shop => config.Shop,
            RoomKind.Treasure => config.Treasure,
            RoomKind.Event => config.Event,
            _ => config.Unknown
        };
        preview.SelfModulate = config.Enabled && config.ColorNodeIcon
            ? Core.ColorPalette.ToGodotColor(color)
            : Colors.White;
    }

    private static Texture2D? LoadPreviewTexture(RoomKind kind)
    {
        if (!PreviewIconPaths.TryGetValue(kind, out var path))
            path = PreviewIconPaths[RoomKind.Unknown];
        return ResourceLoader.Load<Texture2D>(path, cacheMode: ResourceLoader.CacheMode.Reuse);
    }
}
