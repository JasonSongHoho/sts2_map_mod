using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using GameLogger = MegaCrit.Sts2.Core.Logging.Logger;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using Sts2MapMod.Core;
using Sts2MapMod.ModConfig;
using Sts2MapMod.Patches;
using Sts2MapMod.UI;
using Sts2MapMod.Utils;

namespace Sts2MapMod;

/// <summary>
/// Mod entry. Game discovers this via [ModInitializer("Init")] and calls Init().
/// </summary>
[ModInitializer("Init")]
public static class Entry
{
    /// <summary>Game logger (same pattern as quickRestart2). Visible in getlogs.</summary>
    public static GameLogger Logger { get; } = new(nameof(Sts2MapMod), LogType.Generic);

    private static Harmony? _harmony;

    public static void Init()
    {
        try
        {
            LogUtil.Info("Init: start — loading config…");
            ConfigLoader.EnsureLoaded();
            var asmPath = typeof(Entry).Assembly.Location;
            Logger.Info($"MapColorMod initializing. Assembly={asmPath}");
            LogUtil.Info($"Init: assembly path={asmPath}");
            GD.Print("[STS2_MAP_MOD] Init() called — Map Color Mod loaded. Assembly=", asmPath);

            ScheduleMapSettingsUi();

            LogUtil.Info("Init: Harmony id=sts2.mapcolormod, applying map postfix…");
            _harmony = new Harmony("sts2.mapcolormod");
            ApplyMapPatch();
            ApplyMapPointPatches();
            ApplyMapLegendPatches();
            ApplyGlobalInputPatches();
            Logger.Info("MapColorMod initialized.");
            LogUtil.Info("Init: finished successfully.");
            if (ConfigLoader.Config.VerifyGodotPrint)
                GD.Print("[STS2_MAP_MOD] Init finished. VerifyGodotPrint=true.");
        }
        catch (Exception ex)
        {
            LogUtil.Error("Init: fatal — mod Init() threw; map tab / Harmony may be incomplete.", ex);
            Logger.Warn($"MapColorMod Init failed: {ex}");
        }
    }

    /// <summary>
    /// Always use the standalone <c>地图配置</c> tab.
    /// We intentionally avoid registering with ModConfig because its fixed entry types do not support the
    /// richer live-preview UI this mod needs.
    /// </summary>
    private static void ScheduleMapSettingsUi()
    {
        MapSettingsTabInjector.Initialize();
        EnsureHotkeyController();
        LogUtil.Info("Init: using standalone MapSettingsTab only.");
    }

    private static void EnsureHotkeyController()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
            return;
        if (tree.Root.GetNodeOrNull(MapHotkeyController.ControllerNodeName) != null)
            return;

        var controller = new MapHotkeyController
        {
            Name = MapHotkeyController.ControllerNodeName,
            ProcessMode = Node.ProcessModeEnum.Always
        };
        tree.Root.AddChild(controller);
        LogUtil.Info("Init: MapHotkeyController added to SceneTree root.");
    }

    /// <summary>
    /// Find map screen type in sts2 and patch methods that run when the map is built/refreshed.
    /// _Ready alone is not enough: it runs once before child nodes exist; we need refresh/build hooks too.
    /// </summary>
    private static void ApplyMapPatch()
    {
        var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
        var sts2 = assemblies.FirstOrDefault(a => a.GetName().Name == "sts2");
        if (sts2 == null)
        {
            Logger.Warn("sts2 assembly not found; map patch skipped.");
            LogUtil.Warn("ApplyMapPatch: sts2 assembly not found in AppDomain — map Harmony postfix skipped.");
            GD.PrintErr("[STS2_MAP_MOD] sts2 assembly not found — map patch skipped.");
            return;
        }

        LogUtil.Info($"ApplyMapPatch: sts2 assembly found FullName={sts2.FullName}");

        // Prefer the real map screen type (seen in logs as NMapScreen).
        var mapType = sts2.GetType("MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen")
            ?? sts2.GetTypes().FirstOrDefault(t => t.Name == "NMapScreen");

        if (mapType == null)
        {
            mapType = sts2.GetTypes().FirstOrDefault(t =>
                t.Name.Contains("Map") && !t.Name.Contains("MapData") && !t.Name.Contains("MapNode") &&
                (t.Name.Contains("Screen") || t.Name.Contains("UI") || t.Name.Contains("View") || t.Name.Contains("Panel") || t.Name.Contains("Scene") || t.Name.Contains("Window")));
        }

        if (mapType == null)
        {
            mapType = sts2.GetTypes().FirstOrDefault(t =>
                (t.Name.EndsWith("Map") || t.Name.Contains("MapScreen")) && t.Name.Length < 40);
        }

        if (mapType == null)
        {
            Logger.Warn("Map screen type not found in sts2; map patch skipped. Discover actual type name in Entry.");
            LogUtil.Warn("ApplyMapPatch: NMapScreen (or heuristic) type not found — Harmony skipped.");
            GD.PrintErr("[STS2_MAP_MOD] Map screen type not found — patch skipped. Use dnSpy on sts2.dll (see README).");
            return;
        }

        LogUtil.Info($"ApplyMapPatch: resolved map screen type Name={mapType.Name} FullName={mapType.FullName}");

        var postfix = typeof(MapScreenPatch).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);
        if (postfix == null)
        {
            LogUtil.Error("ApplyMapPatch: MapScreenPatch.Postfix not found (reflection). Harmony not applied.");
            return;
        }

        var harmonyMethod = new HarmonyMethod(postfix);
        var patched = new List<string>();

        foreach (var method in SelectMapPatchTargets(mapType))
        {
            try
            {
                _harmony!.Patch(method, postfix: harmonyMethod);
                patched.Add(method.Name);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not patch {mapType.Name}.{method.Name}: {ex.Message}");
                LogUtil.Error($"ApplyMapPatch: Patch failed {mapType.Name}.{method.Name} sig={GetParamSignature(method)}", ex);
            }
        }

        if (patched.Count == 0)
        {
            Logger.Warn($"Map type {mapType.Name} found but no methods could be patched.");
            LogUtil.Warn($"ApplyMapPatch: zero methods patched on {mapType.FullName} — map tint postfix will never run.");
            GD.PrintErr("[STS2_MAP_MOD] No patch targets on ", mapType.FullName);
            return;
        }

        Logger.Info($"Map patch applied to {mapType.Name}: {string.Join(", ", patched)}");
        LogUtil.Info($"ApplyMapPatch: OK patched {patched.Count} method(s) on {mapType.Name}: {string.Join(", ", patched)}");
        GD.Print("[STS2_MAP_MOD] Harmony postfix on ", mapType.FullName, " methods: ", string.Join(", ", patched));
    }

    /// <summary>Instance methods on the map screen that likely run when nodes are shown or updated.</summary>
    private static IEnumerable<MethodInfo> SelectMapPatchTargets(Type mapType)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var seen = new HashSet<string>();

        // Explicit names first (game version may vary).
        foreach (var name in new[]
                 {
                     // STS2 NMapScreen: visuals refresh after layout (not only _Ready).
                     "RefreshAllPointVisuals", "RefreshAllMapPointVotes",
                     "RefreshMap", "Refresh", "RebuildMap", "BuildMap", "UpdateMap", "RedrawMap",
                     "OnMapOpened", "Show", "Display", "PopulateMap", "RefreshPaths", "UpdateVisuals",
                     "_Ready"
                 })
        {
            foreach (var m in mapType.GetMethods(flags).Where(x => x.Name == name))
            {
                if (!IsPlausibleHarmonyTarget(m) || !seen.Add($"{m.Name}:{GetParamSignature(m)}"))
                    continue;
                yield return m;
            }
        }

        // Heuristic: refresh / rebuild / populate / layout related void methods (declared on this type only).
        foreach (var m in mapType.GetMethods(flags))
        {
            if (!IsPlausibleHarmonyTarget(m))
                continue;
            var n = m.Name;
            if (n.StartsWith("get_", StringComparison.Ordinal) || n.StartsWith("set_", StringComparison.Ordinal))
                continue;
            if (n is "_Process" or "_PhysicsProcess" or "_Input" or "_UnhandledInput" or "_GuiInput")
                continue;

            var hit = n.Contains("Refresh", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Rebuild", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Populate", StringComparison.OrdinalIgnoreCase)
                || (n.Contains("Update", StringComparison.OrdinalIgnoreCase) && n.Contains("Map", StringComparison.OrdinalIgnoreCase))
                || (n.Contains("Build", StringComparison.OrdinalIgnoreCase) && n.Contains("Map", StringComparison.OrdinalIgnoreCase))
                || n.Contains("Redraw", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Layout", StringComparison.OrdinalIgnoreCase) && n.Contains("Map", StringComparison.OrdinalIgnoreCase);

            if (!hit)
                continue;
            if (!seen.Add($"{m.Name}:{GetParamSignature(m)}"))
                continue;
            yield return m;
        }
    }

    private static string GetParamSignature(MethodInfo m)
    {
        return string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name));
    }

    private static void ApplyMapPointPatches()
    {
        if (_harmony == null)
            return;

        var patched = new List<string>();

        PatchPointMethod(typeof(NMapPoint), "RefreshVisualsInstantly",
            nameof(MapPointVisualPatch.RefreshPostfix), patched);

        foreach (var pointType in new[] { typeof(NNormalMapPoint), typeof(NAncientMapPoint) })
        {
            PatchPointMethod(pointType, "RefreshColorInstantly",
                nameof(MapPointVisualPatch.RefreshColorPostfix), patched);
        }

        foreach (var pointType in new[] { typeof(NNormalMapPoint), typeof(NAncientMapPoint) })
        {
            PatchPointMethod(pointType, "_Ready", nameof(MapPointVisualPatch.ReadyPostfix), patched);
        }

        foreach (var pointType in new[] { typeof(NNormalMapPoint), typeof(NAncientMapPoint) })
        {
            PatchPointMethod(pointType, "AnimHover", nameof(MapPointVisualPatch.HoverPostfix), patched);
            PatchPointMethod(pointType, "AnimUnhover", nameof(MapPointVisualPatch.UnhoverPostfix), patched);
            PatchPointMethod(pointType, "OnSelected", nameof(MapPointVisualPatch.SelectedPostfix), patched);
        }

        PatchPointMethod(typeof(NClickableControl), "OnVisibilityChanged",
            nameof(MapPointVisualPatch.VisibilityChangedPostfix), patched);

        if (patched.Count == 0)
        {
            LogUtil.Warn("ApplyMapPointPatches: no map-point methods patched.");
            return;
        }

        LogUtil.Info($"ApplyMapPointPatches: OK patched {patched.Count} method(s): {string.Join(", ", patched)}");
    }

    private static void PatchPointMethod(Type targetType, string targetMethodName, string patchMethodName, List<string> patched)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var target = targetType.GetMethod(targetMethodName, flags, binder: null, types: Type.EmptyTypes, modifiers: null);
        if (target == null)
        {
            LogUtil.Warn($"ApplyMapPointPatches: target not found {targetType.FullName}.{targetMethodName}()");
            return;
        }

        var patch = typeof(MapPointVisualPatch).GetMethod(patchMethodName, BindingFlags.Public | BindingFlags.Static);
        if (patch == null)
        {
            LogUtil.Error($"ApplyMapPointPatches: patch method missing {patchMethodName}");
            return;
        }

        try
        {
            _harmony!.Patch(target, postfix: new HarmonyMethod(patch));
            patched.Add($"{targetType.Name}.{targetMethodName}");
        }
        catch (Exception ex)
        {
            LogUtil.Error($"ApplyMapPointPatches: patch failed {targetType.FullName}.{targetMethodName}()", ex);
        }
    }

    private static bool IsPlausibleHarmonyTarget(MethodInfo m)
    {
        if (m.IsStatic || m.IsAbstract)
            return false;
        if (m.ContainsGenericParameters)
            return false;
        // Avoid patching very hot paths with many parameters (unlikely to be map layout anyway).
        if (m.GetParameters().Length > 12)
            return false;
        return true;
    }

    private static void ApplyMapLegendPatches()
    {
        if (_harmony == null)
            return;

        var patched = new List<string>();
        PatchLegendMethod(typeof(NMapLegendItem), "OnFocus", nameof(MapLegendPatch.LegendFocusPostfix), patched);
        PatchLegendMethod(typeof(NMapLegendItem), "OnUnfocus", nameof(MapLegendPatch.LegendUnfocusPostfix), patched);
        PatchLegendMethod(typeof(NMapScreen), "HighlightPointType", nameof(MapLegendPatch.HighlightPointTypePostfix), patched,
            new[] { typeof(MegaCrit.Sts2.Core.Map.MapPointType) });

        if (patched.Count > 0)
            LogUtil.Info($"ApplyMapLegendPatches: OK patched {patched.Count} method(s): {string.Join(", ", patched)}");
    }

    private static void PatchLegendMethod(Type targetType, string targetMethodName, string patchMethodName, List<string> patched,
        Type[]? parameterTypes = null)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var target = targetType.GetMethod(targetMethodName, flags, binder: null, types: parameterTypes ?? Type.EmptyTypes, modifiers: null);
        if (target == null)
        {
            LogUtil.Warn($"ApplyMapLegendPatches: target not found {targetType.FullName}.{targetMethodName}");
            return;
        }

        var patch = typeof(MapLegendPatch).GetMethod(patchMethodName, BindingFlags.Public | BindingFlags.Static);
        if (patch == null)
        {
            LogUtil.Error($"ApplyMapLegendPatches: patch method missing {patchMethodName}");
            return;
        }

        try
        {
            _harmony!.Patch(target, postfix: new HarmonyMethod(patch));
            patched.Add($"{targetType.Name}.{targetMethodName}");
        }
        catch (Exception ex)
        {
            LogUtil.Error($"ApplyMapLegendPatches: patch failed {targetType.FullName}.{targetMethodName}", ex);
        }
    }

    private static void ApplyGlobalInputPatches()
    {
        if (_harmony == null)
            return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var target = typeof(NGame).GetMethod("_Input", flags, binder: null, types: new[] { typeof(InputEvent) }, modifiers: null);
        var patch = typeof(MapHotkeyController).GetMethod(nameof(MapHotkeyController.GameInputPostfix),
            BindingFlags.Public | BindingFlags.Static);

        if (target == null || patch == null)
        {
            LogUtil.Warn("ApplyGlobalInputPatches: failed to resolve NGame._Input or MapHotkeyController.GameInputPostfix.");
            return;
        }

        try
        {
            _harmony.Patch(target, postfix: new HarmonyMethod(patch));
            LogUtil.Info("ApplyGlobalInputPatches: OK patched NGame._Input for F8 toggle.");
        }
        catch (Exception ex)
        {
            LogUtil.Error("ApplyGlobalInputPatches: patch failed NGame._Input(InputEvent).", ex);
        }
    }
}
