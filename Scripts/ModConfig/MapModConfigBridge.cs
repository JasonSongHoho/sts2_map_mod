using System.Reflection;
using Godot;
using Sts2MapMod.Core;
using Sts2MapMod.Localization;
using Sts2MapMod.Patches;
using Sts2MapMod.Utils;

namespace Sts2MapMod.ModConfig;

/// <summary>
/// Optional integration with <see href="https://github.com/xhyrzldf/ModConfig-STS2">ModConfig-STS2</see>:
/// registers map color settings under the game's <b>Mods</b> / <b>模组配置</b> tab via reflection (no DLL reference).
/// If ModConfig is not installed, <see cref="TryRegister"/> returns false and the map mod uses its own settings tab.
/// </summary>
public static class MapModConfigBridge
{
    /// <summary>Must match <c>sts2_map_mod.json</c> <c>id</c> — ModConfig persists <c>user://ModConfig/{id}.json</c>.</summary>
    public const string ModConfigModId = "sts2_map_mod";

    private static bool _completed;
    private static bool _usedModConfig;
    private static Type? _apiType;
    private static Type? _entryType;
    private static Type? _configTypeEnum;
    private static ulong _mirrorSaveGen;

    /// <summary>True if map settings were registered with ModConfig (own tab injector should stay off).</summary>
    public static bool UsedModConfig => _usedModConfig;

    /// <summary>
    /// Resolves ModConfig types from loaded assemblies. Caches a positive resolution only.
    /// </summary>
    public static bool IsModConfigLoaded()
    {
        if (_apiType != null && _entryType != null && _configTypeEnum != null)
            return true;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(asm.GetName().Name, "ModConfig", StringComparison.OrdinalIgnoreCase))
                continue;
            _apiType ??= asm.GetType("ModConfig.ModConfigApi");
            _entryType ??= asm.GetType("ModConfig.ConfigEntry");
            _configTypeEnum ??= asm.GetType("ModConfig.ConfigType");
            if (_apiType != null && _entryType != null && _configTypeEnum != null)
                return true;
        }

        _apiType = Type.GetType("ModConfig.ModConfigApi, ModConfig");
        _entryType = Type.GetType("ModConfig.ConfigEntry, ModConfig");
        _configTypeEnum = Type.GetType("ModConfig.ConfigType, ModConfig");
        return _apiType != null && _entryType != null && _configTypeEnum != null;
    }

    /// <summary>
    /// Registers entries once. Returns true if ModConfig owns the settings UI.
    /// </summary>
    public static bool TryRegister()
    {
        if (_completed)
            return _usedModConfig;

        if (!IsModConfigLoaded())
            return false;

        try
        {
            ConfigLoader.EnsureLoaded();
            var cfg = ConfigLoader.Config;
            var entries = BuildEntries(cfg);
            var displayNames = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["en"] = "Map Color",
                ["zhs"] = "地图颜色"
            };

            var register = FindRegisterMethod(_apiType!, _entryType!);
            if (register == null)
            {
                LogUtil.Warn("MapModConfigBridge: ModConfigApi.Register not found.");
                _completed = true;
                return false;
            }

            var entryArray = ToConfigEntryArray(entries);
            if (register.GetParameters().Length == 4)
                register.Invoke(null, new object[] { ModConfigModId, displayNames["en"], displayNames, entryArray });
            else
                register.Invoke(null, new object[] { ModConfigModId, displayNames["en"], entryArray });

            PullFromModConfigIntoCfg(cfg);
            _completed = true;
            _usedModConfig = true;
            LogUtil.Info("MapModConfigBridge: registered with ModConfig (Mods tab).");
            Entry.Logger.Info("Map colors: settings are under Settings → Mods (ModConfig).");
            return true;
        }
        catch (Exception ex)
        {
            LogUtil.Error("MapModConfigBridge: Register failed.", ex);
            Entry.Logger.Warn($"MapModConfigBridge failed: {ex.Message}");
            _completed = true;
            _usedModConfig = false;
            return false;
        }
    }

    private static MethodInfo? FindRegisterMethod(Type apiType, Type entryType)
    {
        var arrType = entryType.MakeArrayType();
        foreach (var m in apiType.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (m.Name != "Register")
                continue;
            var ps = m.GetParameters();
            if (ps.Length == 3 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string) &&
                ps[2].ParameterType == arrType)
                return m;
            if (ps.Length == 4 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string) &&
                ps[3].ParameterType == arrType &&
                ps[2].ParameterType.IsGenericType &&
                ps[2].ParameterType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                return m;
        }

        return null;
    }

    private static Array ToConfigEntryArray(List<object> entries)
    {
        var array = Array.CreateInstance(_entryType!, entries.Count);
        for (var i = 0; i < entries.Count; i++)
            array.SetValue(entries[i], i);
        return array;
    }

    private static List<object> BuildEntries(MapColorConfig cfg)
    {
        var list = new List<object>();
        object Ct(string name) => Enum.Parse(_configTypeEnum!, name);

        var sectionLabels = MapModL10n.I18nPair("TabTitle");
        list.Add(MkEntry("map_section", sectionLabels["en"], Ct("Header"), false, null, null, null, null, sectionLabels, null, null));

        list.Add(MkToggle("map_tint_enabled", "EnableTint", "EnableTintDesc", cfg.Enabled, v => cfg.Enabled = v));
        list.Add(MkToggle("map_tint_icons", "TintIcons", "TintIconsDesc", cfg.ColorNodeIcon, v => cfg.ColorNodeIcon = v));
        list.Add(MkEntry("map_sep_after_global", "", Ct("Separator"), false, null, null, null, null, null, null, null));

        foreach (RoomKind kind in Enum.GetValues<RoomKind>().Where(k => k is not RoomKind.Boss and not RoomKind.Custom))
        {
            var st = SettingsFor(cfg, kind);
            var prefix = KindKeyPrefix(kind);
            var hdr = MapModL10n.I18nKindPair(kind);
            list.Add(MkEntry($"{prefix}_hdr", hdr["en"], Ct("Header"), false, null, null, null, null, hdr, null, null));

            list.Add(MkTextInput($"{prefix}_color", "Color", st.Color, t =>
            {
                if (!string.IsNullOrWhiteSpace(t))
                    st.Color = t.Trim();
            }));
            list.Add(MkSlider($"{prefix}_alpha", "Opacity", st.Alpha, 0f, 1f, 0.01f, "F2", v => st.Alpha = v));
            var b = st.Brightness ?? 1f;
            list.Add(MkSlider($"{prefix}_brightness", "Brightness", b, 0.25f, 2f, 0.05f, "F2", v =>
            {
                st.Brightness = Math.Abs(v - 1f) < 0.001f ? null : v;
            }));
        }

        return list;
    }

    private static object MkToggle(string key, string l10nKey, string descL10nKey, bool initial, Action<bool> apply)
    {
        var labels = MapModL10n.I18nPair(l10nKey);
        var descriptions = MapModL10n.I18nPair(descL10nKey);
        object Ct(string name) => Enum.Parse(_configTypeEnum!, name);
        Action<object> onChanged = v =>
        {
            var b = v is bool bb ? bb : Convert.ToBoolean(v);
            apply(b);
            PullFromModConfigIntoCfg(ConfigLoader.Config);
            ScheduleMirrorSave();
            ModConfigPreviewInjector.RefreshAll();
            MapScreenPatch.RefreshOpenMapTint();
        };
        return MkEntry(key, labels["en"], Ct("Toggle"), initial, null, null, null, null, labels, descriptions, onChanged);
    }

    private static object MkTextInput(string key, string l10nKey, string initial, Action<string> apply)
    {
        var labels = MapModL10n.I18nPair(l10nKey);
        object Ct(string name) => Enum.Parse(_configTypeEnum!, name);
        Action<object> onChanged = v =>
        {
            apply(v?.ToString() ?? "");
            PullFromModConfigIntoCfg(ConfigLoader.Config);
            ScheduleMirrorSave();
            ModConfigPreviewInjector.RefreshAll();
            MapScreenPatch.RefreshOpenMapTint();
        };
        return MkEntry(key, labels["en"], Ct("TextInput"), initial ?? "#FFFFFF", null, null, null, null, labels, null, onChanged);
    }

    private static object MkSlider(string key, string l10nKey, float initial, float min, float max, float step, string format,
        Action<float> apply)
    {
        var labels = MapModL10n.I18nPair(l10nKey);
        object Ct(string name) => Enum.Parse(_configTypeEnum!, name);
        Action<object> onChanged = v =>
        {
            var f = v is float fl ? fl : Convert.ToSingle(v);
            apply(f);
            PullFromModConfigIntoCfg(ConfigLoader.Config);
            ScheduleMirrorSave();
            ModConfigPreviewInjector.RefreshAll();
            MapScreenPatch.RefreshOpenMapTint();
        };
        return MkEntry(key, labels["en"], Ct("Slider"), initial, min, max, step, format, labels, null, onChanged);
    }

    private static object MkEntry(
        string key,
        string label,
        object configType,
        object defaultValue,
        float? min,
        float? max,
        float? step,
        string? format,
        Dictionary<string, string>? labels,
        Dictionary<string, string>? descriptions,
        Action<object>? onChanged)
    {
        var e = Activator.CreateInstance(_entryType!)!;
        Set(e, "Key", key);
        Set(e, "Label", label);
        Set(e, "Type", configType);
        Set(e, "DefaultValue", defaultValue);
        if (min.HasValue) Set(e, "Min", min.Value);
        if (max.HasValue) Set(e, "Max", max.Value);
        if (step.HasValue) Set(e, "Step", step.Value);
        if (format != null) Set(e, "Format", format);
        if (labels != null) Set(e, "Labels", labels);
        if (descriptions != null) Set(e, "Descriptions", descriptions);
        if (onChanged != null)
            Set(e, "OnChanged", onChanged);
        return e;
    }

    private static void ScheduleMirrorSave()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            try
            {
                ConfigLoader.SaveToDisk();
            }
            catch (Exception ex)
            {
                LogUtil.Error("MapModConfigBridge: mirror save failed.", ex);
            }

            return;
        }

        var gen = ++_mirrorSaveGen;
        var timer = tree.CreateTimer(0.35);
        timer.Timeout += () =>
        {
            if (gen != _mirrorSaveGen)
                return;
            try
            {
                ConfigLoader.SaveToDisk();
            }
            catch (Exception ex)
            {
                LogUtil.Error("MapModConfigBridge: mirror save failed.", ex);
            }
        };
    }

    private static void PullFromModConfigIntoCfg(MapColorConfig cfg)
    {
        if (_apiType == null)
            return;
        try
        {
            cfg.Enabled = GetValue<bool>(ModConfigModId, "map_tint_enabled");
            cfg.ColorNodeIcon = GetValue<bool>(ModConfigModId, "map_tint_icons");
            foreach (RoomKind kind in Enum.GetValues<RoomKind>().Where(k => k is not RoomKind.Boss and not RoomKind.Custom))
            {
                var st = SettingsFor(cfg, kind);
                var p = KindKeyPrefix(kind);
                var c = GetValue<string>(ModConfigModId, $"{p}_color");
                if (!string.IsNullOrWhiteSpace(c))
                    st.Color = c.Trim();
                st.Alpha = GetValue<float>(ModConfigModId, $"{p}_alpha");
                var br = GetValue<float>(ModConfigModId, $"{p}_brightness");
                st.Brightness = Math.Abs(br - 1f) < 0.001f ? null : br;
            }
        }
        catch (Exception ex)
        {
            LogUtil.Warn($"MapModConfigBridge: PullFromModConfigIntoCfg: {ex.Message}");
        }
    }

    private static T GetValue<T>(string modId, string key)
    {
        var gm = _apiType!.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == "GetValue" && m.IsGenericMethodDefinition);
        var m = gm.MakeGenericMethod(typeof(T));
        return (T)m.Invoke(null, new object[] { modId, key })!;
    }

    private static void Set(object target, string prop, object? value)
    {
        var p = target.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
        p?.SetValue(target, value);
    }

    private static string KindKeyPrefix(RoomKind k) => k switch
    {
        RoomKind.Monster => "monster",
        RoomKind.Elite => "elite",
        RoomKind.Rest => "rest",
        RoomKind.Shop => "shop",
        RoomKind.Treasure => "treasure",
        RoomKind.Event => "event",
        RoomKind.Boss => "boss",
        RoomKind.Unknown => "unknown",
        RoomKind.Custom => "custom",
        _ => "unknown"
    };

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
