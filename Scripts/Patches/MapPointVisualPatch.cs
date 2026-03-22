using System.Linq;
using Godot;
using Sts2MapMod.Core;
using Sts2MapMod.Map;
using Sts2MapMod.Utils;

namespace Sts2MapMod.Patches;

/// <summary>
/// Re-applies custom map colors after the game's per-point visual refresh / hover tweens.
/// STS2's map points rewrite SelfModulate during refresh, hover, unhover, and select flows.
/// </summary>
public static class MapPointVisualPatch
{
    private static int _sampleRetintLogs;

    public static void ReadyPostfix(object __instance)
    {
        ScheduleRetint(__instance, "_Ready", 0.01f, 0.12f, 0.40f, 0.90f);
    }

    public static void RefreshPostfix(object __instance)
    {
        RetintNode(__instance, "RefreshVisualsInstantly");
    }

    public static void RefreshColorPostfix(object __instance)
    {
        RetintNode(__instance, "RefreshColorInstantly");
    }

    public static void HoverPostfix(object __instance)
    {
        ScheduleRetint(__instance, "Hover", 0.06f, 0.16f);
    }

    public static void UnhoverPostfix(object __instance)
    {
        ScheduleRetint(__instance, "Unhover", 0.06f, 0.3f, 0.56f);
    }

    public static void SelectedPostfix(object __instance)
    {
        ScheduleRetint(__instance, "OnSelected", 0.06f, 0.36f);
    }

    public static void VisibilityChangedPostfix(object __instance)
    {
        if (__instance is not Node node)
            return;
        if (!node.GetType().Name.Contains("MapPoint", StringComparison.Ordinal))
            return;
        ScheduleRetint(node, "VisibilityChanged", 0.02f, 0.18f, 0.42f);
    }

    private static void ScheduleRetint(object instance, string reason, params float[] delays)
    {
        if (instance is not Node node || !GodotObject.IsInstanceValid(node))
            return;

        var config = ConfigLoader.Config;
        LogUtil.Diag(config.VerifyGodotPrint,
            $"MapPointVisualPatch: schedule reason={reason} node={node.Name} type={node.GetType().Name} delays=[{string.Join(", ", delays.Select(d => d.ToString("0.00")))}]");

        foreach (var delay in delays)
        {
            var tree = node.GetTree();
            if (tree == null)
                return;

            var timer = tree.CreateTimer(delay);
            timer.Timeout += () =>
            {
                RetintNode(node, $"{reason}@{delay:0.00}s");
            };
        }
    }

    private static void RetintNode(object instance, string reason)
    {
        if (instance is not Node node || !GodotObject.IsInstanceValid(node))
            return;

        var config = ConfigLoader.Config;
        var roomData = MapNodeAdapter.GetRoomDataFromNode(node);
        var kind = RoomKindResolver.Resolve(roomData);
        var applied = MapColorService.ApplyAndReport(node, kind, config, config.VerifyGodotPrint);
        if (_sampleRetintLogs < 20)
        {
            _sampleRetintLogs++;
            var color = kind switch
            {
                RoomKind.Monster => config.Monster.Color,
                RoomKind.Elite => config.Elite.Color,
                RoomKind.Rest => config.Rest.Color,
                RoomKind.Shop => config.Shop.Color,
                RoomKind.Treasure => config.Treasure.Color,
                RoomKind.Event => config.Event.Color,
                RoomKind.Boss => config.Boss.Color,
                RoomKind.Custom => config.Custom.Color,
                _ => config.Unknown.Color
            };
            LogUtil.Info(
                $"MapPointVisualPatch: sample reason={reason} node={node.Name} type={node.GetType().Name} roomData={(roomData?.GetType().FullName ?? "null")} kind={kind} color={color} enabled={config.Enabled} applied={applied}");
        }
        LogUtil.Diag(config.VerifyGodotPrint,
            $"MapPointVisualPatch: reason={reason} node={node.Name} type={node.GetType().Name} kind={kind} enabled={config.Enabled} applied={applied}");
    }
}
