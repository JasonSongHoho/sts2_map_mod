using System.Collections.Generic;
using Godot;

namespace Sts2MapMod.Utils;

public static class GodotNodeUtil
{
    public static T? FindChildByName<T>(Node parent, string name) where T : Node
    {
        if (parent.Name == name && parent is T t)
            return t;
        foreach (var child in parent.GetChildren())
        {
            if (child is not Node n) continue;
            var found = FindChildByName<T>(n, name);
            if (found != null) return found;
        }
        return null;
    }

    public static IEnumerable<Node> EnumerateChildrenRecursive(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is Node n)
            {
                yield return n;
                foreach (var sub in EnumerateChildrenRecursive(n))
                    yield return sub;
            }
        }
    }
}
