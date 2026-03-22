using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using Sts2MapMod.Core;
using Sts2MapMod.Map;
using Sts2MapMod.ModConfig;
using Sts2MapMod.Utils;

namespace Sts2MapMod.Patches;

/// <summary>
/// Postfix logic: after map screen refresh, apply colors to all visible nodes.
/// Applied from Entry via Harmony.Patch(targetMethod, postfix: Postfix).
/// </summary>
public static class MapScreenPatch
{
    public static void Postfix(object __instance)
    {
        if (__instance == null)
        {
            LogUtil.Warn("MapScreenPatch: Postfix skipped — __instance is null.");
            return;
        }

        try
        {
            var prev = ConfigLoader.Config;
            var allowHotReload = prev.HotReloadConfigOnMap && !MapModConfigBridge.UsedModConfig;
            RunTintPass(__instance, allowHotReload);
            ScheduleDeferredTintPasses(__instance as Node);
        }
        catch (Exception ex)
        {
            LogUtil.Error("MapScreenPatch: Postfix unhandled exception (Harmony postfix).", ex);
        }
    }

    /// <summary>
    /// After ModConfig (or file) changes while the map is open, re-apply tints without waiting for the next Harmony refresh.
    /// </summary>
    public static void RefreshOpenMapTint()
    {
        try
        {
            var screen = NMapScreen.Instance;
            if (screen == null)
                return;
            LogUtil.Info($"MapScreenPatch.RefreshOpenMapTint: screen={screen.GetType().Name} visible={screen.Visible} inTree={screen.IsInsideTree()}");
            ForceGameMapRefresh(screen);
            // In-memory config already updated; do not reload disk here (avoids racing ModConfig mirror save).
            RunTintPass(screen, runDiskHotReload: false);
            ScheduleDeferredTintPasses(screen);
        }
        catch (Exception ex)
        {
            LogUtil.Warn($"MapScreenPatch.RefreshOpenMapTint: {ex.Message}");
        }
    }

    private static void ForceGameMapRefresh(NMapScreen screen)
    {
        try
        {
            screen.RefreshAllPointVisuals();
            screen.RefreshAllMapPointVotes();
            LogUtil.Info("MapScreenPatch: ForceGameMapRefresh -> RefreshAllPointVisuals + RefreshAllMapPointVotes");
        }
        catch (Exception ex)
        {
            LogUtil.Warn($"MapScreenPatch.ForceGameMapRefresh immediate: {ex.Message}");
        }

        var tree = screen.GetTree();
        if (tree == null)
            return;

        foreach (var delaySec in new[] { 0.01f, 0.08f, 0.22f })
        {
            var timer = tree.CreateTimer(delaySec);
            timer.Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(screen))
                    return;
                try
                {
                    screen.RefreshAllPointVisuals();
                    screen.RefreshAllMapPointVotes();
                    LogUtil.Diag(ConfigLoader.Config.VerifyGodotPrint,
                        $"MapScreenPatch: deferred ForceGameMapRefresh delay={delaySec:0.00}s");
                }
                catch (Exception ex)
                {
                    LogUtil.Warn($"MapScreenPatch.ForceGameMapRefresh deferred {delaySec:0.00}s: {ex.Message}");
                }
            };
        }
    }

    private static void RunTintPass(object __instance, bool runDiskHotReload)
    {
        if (runDiskHotReload)
            ConfigLoader.TryHotReloadConfigFromDiskThrottled(true);

        var config = ConfigLoader.Config;
        LogUtil.Diag(config.VerifyGodotPrint,
            $"MapScreenPatch: RunTintPass type={__instance.GetType().FullName} Enabled={config.Enabled} hotReload={runDiskHotReload}");

        if (config.VerifyDiagnosticTintMapScreen && __instance is Node screenRoot)
        {
            var tintTarget = screenRoot as CanvasItem ?? FindFirstCanvasItemDescendant(screenRoot);
            if (tintTarget != null)
            {
                tintTarget.Modulate = new Color(1f, 0.35f, 1f, 1f);
                LogUtil.Info(
                    $"MapScreenPatch: VerifyDiagnosticTintMapScreen OK screen={screenRoot.GetType().Name} tintTarget={tintTarget.GetType().Name}");
                if (config.VerifyGodotPrint)
                    GD.Print("[STS2_MAP_MOD] VerifyDiagnosticTintMapScreen on ", screenRoot.GetType().FullName,
                        " tinted ", tintTarget.GetType().FullName);
            }
            else
            {
                LogUtil.Warn(
                    $"MapScreenPatch: VerifyDiagnosticTintMapScreen — no CanvasItem under {screenRoot.GetType().FullName}");
                if (config.VerifyGodotPrint)
                    GD.Print("[STS2_MAP_MOD] VerifyDiagnosticTintMapScreen: no CanvasItem under ", screenRoot.GetType().FullName);
            }
        }

        var nodes = MapNodeAdapter.FindAllVisibleNodes(__instance).ToList();
        if (nodes.Count == 0)
        {
            Sts2MapMod.Entry.Logger.Warn("MapScreenPatch: no map nodes found; check MapNodeAdapter / _mapPointDictionary.");
            LogUtil.Warn(
                $"MapScreenPatch: zero map nodes under instance={__instance.GetType().Name}.");
        }

        if (!config.Enabled)
            LogUtil.Diag(config.VerifyGodotPrint, "MapScreenPatch: config.Enabled=false — restoring node colors.");

        if (config.VerifyGodotPrint)
            GD.Print("[STS2_MAP_MOD] MapScreenPatch tint pass, instance=", __instance.GetType().FullName, " nodes=", nodes.Count);

        var applied = 0;
        var skippedNoTarget = 0;
        foreach (var view in nodes)
        {
            var kind = RoomKindResolver.Resolve(view.RoomData);
            if (MapColorService.ApplyAndReport(view.Node, kind, config, config.VerifyGodotPrint))
                applied++;
            else
                skippedNoTarget++;
        }

        LogUtil.Diag(config.VerifyGodotPrint,
            $"MapScreenPatch: tint pass nodes={nodes.Count} appliedModulate={applied} noCanvasTarget={skippedNoTarget}");
    }

    /// <summary>
    /// Game may reset icon colors on deferred/layout ticks; re-apply a few times (see STS2RouteSuggest timing patterns).
    /// </summary>
    private static void ScheduleDeferredTintPasses(Node? screen)
    {
        if (screen == null || !GodotObject.IsInstanceValid(screen))
            return;
        var tree = screen.GetTree();
        if (tree == null)
            return;

        foreach (var delaySec in new[] { 0.05f, 0.12f, 0.28f, 0.60f })
        {
            var timer = tree.CreateTimer(delaySec);
            timer.Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(screen))
                    return;
                RunTintPass(screen, runDiskHotReload: false);
            };
        }
    }

    private static CanvasItem? FindFirstCanvasItemDescendant(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is not Node n)
                continue;
            if (n is CanvasItem ci)
                return ci;
            var nested = FindFirstCanvasItemDescendant(n);
            if (nested != null)
                return nested;
        }

        return null;
    }
}
