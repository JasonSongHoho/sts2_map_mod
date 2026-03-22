using System.Collections.Generic;
using System.Reflection;
using Godot;
using Sts2MapMod.Core;

namespace Sts2MapMod.Localization;

/// <summary>
/// English / 简体中文 strings for the map mod settings UI.
/// Uses <see cref="TranslationServer.GetLocale"/> first, then tries sts2 <c>I18n.CurrentLang</c> via reflection.
/// </summary>
public static class MapModL10n
{
    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["TabTitle"] = "Map colors",
        ["EnableTint"] = "Master switch: map colors",
        ["TintIcons"] = "Tint room markers (icons)",
        ["EnableTintDesc"] = "When off, this mod does not change any colors on the map.",
        ["TintIconsDesc"] =
            "When off, room node icons stay vanilla. (Tinting path lines is not implemented yet — this only affects room markers.)",
        ["Hint"] = "Set color (#RRGGBB), opacity, and brightness per room type.",
        ["Color"] = "Color",
        ["Opacity"] = "Opacity",
        ["Brightness"] = "Brightness",
        ["Preview"] = "Preview",
        ["EnabledStateOn"] = "On",
        ["EnabledStateOff"] = "Off",
        ["AppliedNow"] = "Applied to the open map.",
        ["BrightnessHint"] = "(1 = default; omitted in file if default)",
        ["ColorDesc"] = "Use a hex color such as #FF0000.",
        ["OpacityDesc"] = "Controls how transparent the icon is.",
        ["BrightnessDesc"] = "Scales the icon brightness without changing the base color.",
        ["ValidationOk"] = "Settings look good.",
        ["ValidationSaveOk"] = "Saved successfully.",
        ["ValidationFixErrors"] = "Fix invalid values before saving.",
        ["ValidationColorHex"] = "Use #RGB, #RGBA, #RRGGBB, or #RRGGBBAA.",
        ["RestoreDefaults"] = "Restore recommended defaults",
        ["RestoreDefaultsOk"] = "Recommended defaults restored.",
        ["KindMonster"] = "Enemy",
        ["KindElite"] = "Elite",
        ["KindRest"] = "Rest",
        ["KindShop"] = "Merchant",
        ["KindTreasure"] = "Treasure",
        ["KindEvent"] = "Unknown",
        ["KindBoss"] = "Boss",
        ["KindUnknown"] = "Unknown",
        ["KindCustom"] = "Custom"
    };

    private static readonly IReadOnlyDictionary<string, string> Zhs = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["TabTitle"] = "地图配置",
        ["EnableTint"] = "总开关：地图染色",
        ["TintIcons"] = "给房间节点图标上色",
        ["EnableTintDesc"] = "关闭后，本 mod 不会对地图上的任何颜色生效。",
        ["TintIconsDesc"] = "关闭后，地图上的房间节点图标保持原版样式。（路径线等着色尚未实现，本项只影响房间节点。）",
        ["Hint"] = "按房间类型设置颜色（#RRGGBB）、透明度、亮度。",
        ["Color"] = "颜色",
        ["Opacity"] = "透明度",
        ["Brightness"] = "亮度",
        ["Preview"] = "预览",
        ["EnabledStateOn"] = "已开启",
        ["EnabledStateOff"] = "已关闭",
        ["AppliedNow"] = "已立即应用到当前地图。",
        ["BrightnessHint"] = "（1=默认，保存时可省略）",
        ["ColorDesc"] = "输入十六进制颜色，例如 #FF0000。",
        ["OpacityDesc"] = "控制图标的透明程度。",
        ["BrightnessDesc"] = "在不改变底色的情况下调整图标亮度。",
        ["ValidationOk"] = "当前设置有效。",
        ["ValidationSaveOk"] = "保存成功。",
        ["ValidationFixErrors"] = "请先修正无效输入，再保存。",
        ["ValidationColorHex"] = "颜色格式仅支持 #RGB、#RGBA、#RRGGBB、#RRGGBBAA。",
        ["RestoreDefaults"] = "恢复推荐默认配色",
        ["RestoreDefaultsOk"] = "已恢复推荐默认设置。",
        ["KindMonster"] = "敌人",
        ["KindElite"] = "精英",
        ["KindRest"] = "休息",
        ["KindShop"] = "商人",
        ["KindTreasure"] = "宝箱",
        ["KindEvent"] = "未知",
        ["KindBoss"] = "Boss",
        ["KindUnknown"] = "未知",
        ["KindCustom"] = "自定义"
    };

    /// <summary>English + 简体中文 labels for ModConfig <c>ConfigEntry.Labels</c> (<c>en</c> / <c>zhs</c>).</summary>
    public static Dictionary<string, string> I18nPair(string key)
    {
        var en = En.TryGetValue(key, out var e) ? e : key;
        var zhs = Zhs.TryGetValue(key, out var z) ? z : en;
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = en,
            ["zhs"] = zhs
        };
    }

    /// <summary>Per-<see cref="RoomKind"/> title pair for ModConfig.</summary>
    public static Dictionary<string, string> I18nKindPair(RoomKind kind) => I18nPair(KindTitleKey(kind));

    /// <summary>Localized UI string. Falls back to English table, then to <paramref name="key"/>.</summary>
    public static string T(string key)
    {
        var table = UseChinese ? Zhs : En;
        if (table.TryGetValue(key, out var z))
            return z;
        return En.TryGetValue(key, out var e) ? e : key;
    }

    /// <summary>True when game locale / sts2 I18n indicates Chinese.</summary>
    public static bool UseChinese => PreferChinese();

    private static bool PreferChinese()
    {
        try
        {
            var loc = TranslationServer.GetLocale();
            if (!string.IsNullOrEmpty(loc) &&
                (loc.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
                 loc.Contains("CN", StringComparison.Ordinal) ||
                 loc.Equals("zhs", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        catch
        {
            // TranslationServer may not be ready in edge cases
        }

        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(asm.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase))
                    continue;
                Type? i18n = null;
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name != "I18n") continue;
                        var p = t.GetProperty("CurrentLang", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (p != null)
                        {
                            i18n = t;
                            break;
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    continue;
                }

                if (i18n == null) continue;
                var langObj = i18n.GetProperty("CurrentLang", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?.GetValue(null);
                var lang = langObj?.ToString() ?? "";
                if (lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
                    lang.StartsWith("zhs", StringComparison.OrdinalIgnoreCase) ||
                    lang.Contains("Chinese", StringComparison.OrdinalIgnoreCase))
                    return true;
                break;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    public static string KindTitle(RoomKind kind) => T(KindTitleKey(kind));

    private static string KindTitleKey(RoomKind kind) => kind switch
    {
        RoomKind.Monster => "KindMonster",
        RoomKind.Elite => "KindElite",
        RoomKind.Rest => "KindRest",
        RoomKind.Shop => "KindShop",
        RoomKind.Treasure => "KindTreasure",
        RoomKind.Event => "KindEvent",
        RoomKind.Boss => "KindBoss",
        RoomKind.Unknown => "KindUnknown",
        RoomKind.Custom => "KindCustom",
        _ => "KindUnknown"
    };
}
