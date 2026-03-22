using Godot;
using Sts2MapMod.Core;
using Sts2MapMod.Utils;

namespace Sts2MapMod.Patches;

/// <summary>
/// Diagnostic-only logging for legend hover/unhover and map point-type highlight broadcasts.
/// </summary>
public static class MapLegendPatch
{
    public static void LegendFocusPostfix(object __instance)
    {
        LogLegendEvent(__instance, "LegendFocus");
    }

    public static void LegendUnfocusPostfix(object __instance)
    {
        LogLegendEvent(__instance, "LegendUnfocus");
    }

    public static void HighlightPointTypePostfix(object __instance, object __0)
    {
        var config = ConfigLoader.Config;
        var screenName = __instance is Node n ? n.Name.ToString() : __instance?.GetType().Name ?? "null";
        var pointType = __0?.ToString() ?? "null";
        LogUtil.Info($"MapLegendPatch: HighlightPointType screen={screenName} pointType={pointType}");
        LogUtil.Diag(config.VerifyGodotPrint,
            $"MapLegendPatch: HighlightPointType screen={screenName} pointType={pointType}");
    }

    private static void LogLegendEvent(object? instance, string reason)
    {
        var config = ConfigLoader.Config;
        var node = instance as Node;
        var name = node?.Name.ToString() ?? instance?.GetType().Name ?? "null";
        LogUtil.Info($"MapLegendPatch: {reason} legendItem={name}");
        LogUtil.Diag(config.VerifyGodotPrint, $"MapLegendPatch: {reason} legendItem={name}");
    }
}
