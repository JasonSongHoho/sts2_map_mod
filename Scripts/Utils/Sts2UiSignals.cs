using System.Reflection;
using Godot;

namespace Sts2MapMod.Utils;

/// <summary>
/// Resolves Godot <see cref="StringName"/> signals from sts2 UI types (avoids hard compile refs to internal generated names).
/// </summary>
internal static class Sts2UiSignals
{
    /// <summary>
    /// <c>NClickableControl.SignalName.Released</c> — settings tabs use this for clicks (not the string <c>"released"</c> on <see cref="Control"/>).
    /// </summary>
    internal static StringName? NClickableReleased
    {
        get
        {
            if (_nClickableReleasedTried)
                return _nClickableReleased;
            _nClickableReleasedTried = true;
            _nClickableReleased = ResolveNClickableReleased();
            return _nClickableReleased;
        }
    }

    private static bool _nClickableReleasedTried;
    private static StringName? _nClickableReleased;

    private static StringName? ResolveNClickableReleased()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(asm.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase))
                continue;
            Type? clickable = null;
            try
            {
                foreach (var ty in asm.GetTypes())
                {
                    if (ty.Name != "NClickableControl")
                        continue;
                    clickable = ty;
                    break;
                }
            }
            catch (ReflectionTypeLoadException)
            {
                continue;
            }

            if (clickable == null)
                continue;

            var signalNameType = clickable.GetNestedType("SignalName", BindingFlags.Public | BindingFlags.NonPublic);
            if (signalNameType == null)
                continue;

            foreach (var f in signalNameType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.Name != "Released" || f.FieldType != typeof(StringName))
                    continue;
                if (f.GetValue(null) is StringName s)
                    return s;
            }

            foreach (var p in signalNameType.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (p.Name != "Released" || p.PropertyType != typeof(StringName))
                    continue;
                if (p.GetValue(null) is StringName s2)
                    return s2;
            }
        }

        return null;
    }

    /// <summary>Fallback: walk <paramref name="node"/>'s CLR type hierarchy for <c>SignalName.Released</c>.</summary>
    internal static StringName? FindReleasedOnType(Node node)
    {
        for (var t = node.GetType(); t != null && t != typeof(object); t = t.BaseType)
        {
            var sn = t.GetNestedType("SignalName", BindingFlags.Public | BindingFlags.NonPublic);
            if (sn == null)
                continue;
            foreach (var f in sn.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.Name == "Released" && f.GetValue(null) is StringName s)
                    return s;
            }

            foreach (var p in sn.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (p.Name == "Released" && p.GetValue(null) is StringName s2)
                    return s2;
            }
        }

        return null;
    }
}
