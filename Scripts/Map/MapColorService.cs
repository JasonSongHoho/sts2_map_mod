using Godot;
using Sts2MapMod.Core;
using Sts2MapMod.Utils;

namespace Sts2MapMod.Map;

/// <summary>
/// Applies tint via <see cref="CanvasItem.SelfModulate"/> from <see cref="RoomTypeColorSettings"/> (color, alpha, brightness).
/// STS2 map nodes (decompiled reference: <c>NNormalMapPoint</c>, <c>NBossMapPoint</c>, <c>NAncientMapPoint</c>) use
/// <b>SelfModulate</b> on the main icon and <b>Modulate</b> on outline layers — never tint the map-point root Control.
/// </summary>
public static class MapColorService
{
    private static readonly string[] IconNames = { "Icon", "TextureRect", "Sprite2D", "Sprite", "IconTexture" };
    private static readonly StringName MapColorShaderParam = new("map_color");
    private static readonly StringName OriginalSelfMeta = new("_sts2_map_mod_original_self");
    private static readonly StringName OriginalShaderColorMeta = new("_sts2_map_mod_original_shader_map_color");

    private enum Sts2KnownMapPointTint
    {
        /// <summary>Not one of the concrete STS2 map point types (or icon not found — may try generic walk).</summary>
        NotApplicable,
        /// <summary>Tint applied to the correct child drawable.</summary>
        Applied,
        /// <summary>Recognized type but must not use root / shallow <see cref="ResolveTintTarget"/> (e.g. Spine boss).</summary>
        SkipFallbacks
    }

    /// <summary>
    /// <see cref="MegaCrit.Sts2.Core.Nodes.Screens.Map.NNormalMapPoint"/>: <c>%Icon</c> uses SelfModulate;
    /// <c>%Outline</c> uses Modulate for map bg + legend highlight — do not tint.<br/>
    /// <see cref="MegaCrit.Sts2.Core.Nodes.Screens.Map.NBossMapPoint"/>: non-Spine uses <c>%PlaceholderImage</c> / <c>%PlaceholderOutline</c>;
    /// Spine bosses have no icon TextureRect — tinting the root Control breaks rendering (solid white blob).<br/>
    /// <see cref="MegaCrit.Sts2.Core.Nodes.Screens.Map.NAncientMapPoint"/>: <c>Icon</c> + <c>Icon/Outline</c> same pattern as normal.
    /// </summary>
    private static Sts2KnownMapPointTint TryTintKnownSts2MapPoint(Node root, Color color)
    {
        var typeName = root.GetType().Name;
        switch (typeName)
        {
            case "NNormalMapPoint":
            case "NAncientMapPoint":
                if (TryGetMapPointTextureRect(root, "Icon", "%Icon", out var icon))
                {
                    SnapshotOriginalState(icon);
                    var usedShaderColor = TrySetShaderColor(icon, MapColorShaderParam, color);
                    // Normal map points use a shader material on the icon. Driving the shader's map_color gives a much
                    // cleaner hue than multiplying the baked brown sprite via SelfModulate alone.
                    if (usedShaderColor)
                    {
                        var current = icon.SelfModulate;
                        // Preserve the game's current grayscale/brightness state and only override alpha.
                        // Forcing RGB to white makes default nodes look permanently highlighted and inverts hover/unhover.
                        icon.SelfModulate = new Color(current.R, current.G, current.B, color.A);
                    }
                    else
                        icon.SelfModulate = color;
                    LogUtil.Diag(ConfigLoader.Config.VerifyGodotPrint,
                        $"MapColorService: known-point tint type={typeName} node={root.Name} shader={usedShaderColor} localMat={(icon.Material as Resource)?.ResourceLocalToScene} icon.self={icon.SelfModulate}");
                    return Sts2KnownMapPointTint.Applied;
                }

                return Sts2KnownMapPointTint.NotApplicable;

            case "NBossMapPoint":
                if (TryGetMapPointTextureRect(root, "PlaceholderImage", "%PlaceholderImage", out var ph))
                {
                    SnapshotOriginalState(ph);
                    ph.SelfModulate = color;
                    LogUtil.Diag(ConfigLoader.Config.VerifyGodotPrint,
                        $"MapColorService: boss placeholder tint node={root.Name} self={ph.SelfModulate}");
                    return Sts2KnownMapPointTint.Applied;
                }

                // Boss with Spine: no placeholder rects — avoid Modulate on entire NBossMapPoint.
                return Sts2KnownMapPointTint.SkipFallbacks;

            default:
                return Sts2KnownMapPointTint.NotApplicable;
        }
    }

    private static bool TryGetMapPointTextureRect(Node root, string path, string uniquePath, out TextureRect tr)
    {
        tr = root.GetNodeOrNull<TextureRect>(path) ?? root.GetNodeOrNull<TextureRect>(uniquePath);
        if (tr != null && GodotObject.IsInstanceValid(tr))
            return true;
        var byName = GodotNodeUtil.FindChildByName<TextureRect>(root, path);
        if (byName != null && GodotObject.IsInstanceValid(byName))
        {
            tr = byName;
            return true;
        }

        tr = null!;
        return false;
    }

    private static bool TrySetShaderColor(CanvasItem item, StringName paramName, Color color)
    {
        if (item.Material is not ShaderMaterial shader)
            return false;

        // STS2 normal map-point icons may share one ShaderMaterial resource across many nodes.
        // If we mutate the shared resource, hovering a single node recolors every room using that material.
        if (shader.ResourceLocalToScene == false)
        {
            shader = (ShaderMaterial)shader.Duplicate();
            shader.ResourceLocalToScene = true;
            item.Material = shader;
        }

        shader.SetShaderParameter(paramName, new Color(color.R, color.G, color.B, 1f));
        return true;
    }

    private static void SnapshotOriginalState(CanvasItem item)
    {
        if (!item.HasMeta(OriginalSelfMeta))
            item.SetMeta(OriginalSelfMeta, item.SelfModulate);

        if (item.Material is ShaderMaterial shader && !item.HasMeta(OriginalShaderColorMeta))
            item.SetMeta(OriginalShaderColorMeta, shader.GetShaderParameter(MapColorShaderParam));
    }

    private static bool RestoreKnownSts2MapPoint(Node root, bool diag)
    {
        var typeName = root.GetType().Name;
        switch (typeName)
        {
            case "NNormalMapPoint":
            case "NAncientMapPoint":
                if (TryGetMapPointTextureRect(root, "Icon", "%Icon", out var icon))
                {
                    RestoreOriginalState(icon);
                    LogUtil.Diag(diag, $"MapColorService: restore known-point type={typeName} node={root.Name}");
                    return true;
                }

                return false;

            case "NBossMapPoint":
                if (TryGetMapPointTextureRect(root, "PlaceholderImage", "%PlaceholderImage", out var ph))
                {
                    RestoreOriginalState(ph);
                    LogUtil.Diag(diag, $"MapColorService: restore boss placeholder node={root.Name}");
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private static void RestoreOriginalState(CanvasItem item)
    {
        if (item.HasMeta(OriginalSelfMeta))
        {
            var val = item.GetMeta(OriginalSelfMeta);
            if (val.VariantType == Variant.Type.Color)
                item.SelfModulate = val.AsColor();
        }

        if (item.Material is ShaderMaterial shader && item.HasMeta(OriginalShaderColorMeta))
        {
            var val = item.GetMeta(OriginalShaderColorMeta);
            shader.SetShaderParameter(MapColorShaderParam, val);
        }
    }

    private static bool IsConcreteSts2MapPointType(Node n) =>
        n.GetType().Name is "NNormalMapPoint" or "NBossMapPoint" or "NAncientMapPoint";

    /// <summary>
    /// Skip drawable leaves the game drives with <see cref="CanvasItem.Modulate"/> for hover / path-available / outline.
    /// Forcing <c>Modulate = White</c> on those (previous behavior) made hidden overlays fully opaque → "everything highlighted".
    /// </summary>
    private static bool IsLikelyOverlayOrHighlightNode(Node n)
    {
        var name = n.Name.ToString();
        foreach (var frag in new[]
                 {
                     "Highlight", "Glow", "Halo", "Outline", "PlaceholderOutline", "Hover", "Focus", "Selected",
                     "Available", "Reachable", "Active", "Pulse", "Ring", "Shadow", "QuestIcon", "Vote"
                 })
        {
            if (name.Contains(frag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static void Apply(Node node, RoomKind kind, MapColorConfig config) =>
        ApplyAndReport(node, kind, config, diag: false);

    /// <summary>
    /// Applies tint; returns true if a <see cref="CanvasItem"/> was found and modulated.
    /// When <paramref name="diag"/> is true, logs per-node misses (verbose — use only with VerifyGodotPrint).
    /// </summary>
    public static bool ApplyAndReport(Node node, RoomKind kind, MapColorConfig config, bool diag)
    {
        if (kind is RoomKind.Boss or RoomKind.Custom)
        {
            var restoredBoss = RestoreKnownSts2MapPoint(node, diag);
            LogUtil.Diag(diag, $"MapColorService: special point left vanilla node={node.Name} kind={kind} restored={restoredBoss}");
            return restoredBoss;
        }

        if (!config.Enabled || !config.ColorNodeIcon)
        {
            if (RestoreKnownSts2MapPoint(node, diag))
                return true;
            LogUtil.Diag(diag,
                $"MapColorService: skip node={node.Name} kind={kind} (Enabled={config.Enabled} ColorNodeIcon={config.ColorNodeIcon})");
            return false;
        }

        var color = Core.ColorPalette.Get(kind, config);

        var sts2 = TryTintKnownSts2MapPoint(node, color);
        if (sts2 == Sts2KnownMapPointTint.Applied)
        {
            LogUtil.Diag(diag,
                $"MapColorService: STS2 map-point direct tint OK node={node.Name} kind={kind} color={color}");
            return true;
        }

        if (sts2 == Sts2KnownMapPointTint.SkipFallbacks)
        {
            LogUtil.Diag(diag,
                $"MapColorService: STS2 map-point skip fallbacks node={node.Name} kind={kind} (e.g. Spine boss — no PlaceholderImage)");
            return false;
        }

        // Generic / unknown layout: recurse drawables, but never assign Modulate on outline-like nodes.
        var leaves = ApplyModulateToDrawableLeaves(node, color, maxDepth: 14, maxNodes: 48);
        if (leaves > 0)
        {
            LogUtil.Diag(diag,
                $"MapColorService: leaf SelfModulate OK node={node.Name} kind={kind} leaves={leaves} color={color}");
            return true;
        }

        // Never Modulate the whole NMapPoint / NBossMapPoint root — game relies on per-child Modulate for outlines.
        if (IsConcreteSts2MapPointType(node))
        {
            LogUtil.Diag(diag,
                $"MapColorService: STS2 map-point no drawable leaves node={node.Name} kind={kind} type={node.GetType().Name}");
            return false;
        }

        var target = ResolveTintTarget(node);
        if (target != null)
        {
            target.SelfModulate = color;
            LogUtil.Diag(diag,
                $"MapColorService: shallow SelfModulate OK node={node.Name} kind={kind} target={target.GetType().Name} color={color}");
            return true;
        }

        LogUtil.Diag(diag,
            $"MapColorService: no CanvasItem target node={node.Name} kind={kind} type={node.GetType().Name}");
        return false;
    }

    /// <summary>Sets <see cref="CanvasItem.Modulate"/> on <see cref="TextureRect"/> / <see cref="Sprite2D"/> under the map-point subtree.</summary>
    private static int ApplyModulateToDrawableLeaves(Node root, Color color, int maxDepth, int maxNodes)
    {
        var count = 0;
        ApplyRecursive(root, 0);
        return count;

        void ApplyRecursive(Node n, int depth)
        {
            if (count >= maxNodes || depth > maxDepth)
                return;
            if (n is TextureRect tr)
            {
                if (IsLikelyOverlayOrHighlightNode(tr))
                    return;
                // Do not assign Modulate here: STS2 uses local Modulate (often with alpha) on overlay TextureRects for
                // path / hover; resetting to white made every room look permanently highlighted until the game rewrote Modulate (e.g. on hover).
                tr.SelfModulate = color;
                count++;
            }
            else if (n is Sprite2D s2)
            {
                if (IsLikelyOverlayOrHighlightNode(s2))
                    return;
                s2.SelfModulate = color;
                count++;
            }

            foreach (var c in n.GetChildren())
            {
                if (c is Node child)
                    ApplyRecursive(child, depth + 1);
            }
        }
    }

    /// <summary>Best target for modulate: map-point root if it draws, else named icon child, else first shallow CanvasItem.</summary>
    private static CanvasItem? ResolveTintTarget(Node node)
    {
        if (node is CanvasItem root)
            return root;

        var named = FindChildByNames(node, IconNames);
        if (named != null)
            return named;

        foreach (var child in node.GetChildren())
        {
            if (child is CanvasItem ci)
                return ci;
        }

        return FindFirstCanvasItemDepthFirst(node, maxDepth: 4, skipRoot: true);
    }

    private static CanvasItem? FindFirstCanvasItemDepthFirst(Node n, int maxDepth, bool skipRoot)
    {
        if (maxDepth < 0) return null;
        if (!skipRoot && n is CanvasItem ci)
            return ci;
        foreach (var c in n.GetChildren())
        {
            if (c is not Node child) continue;
            if (child is CanvasItem leaf)
                return leaf;
            var nested = FindFirstCanvasItemDepthFirst(child, maxDepth - 1, skipRoot: false);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static CanvasItem? FindChildByNames(Node parent, string[] names)
    {
        foreach (var name in names)
        {
            var n = parent.GetNodeOrNull<Node>(name);
            if (n == null)
                n = GodotNodeUtil.FindChildByName<Node>(parent, name);
            if (n is CanvasItem ci)
                return ci;
        }
        return null;
    }
}
