using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Sts2MapMod.Core;
using Sts2MapMod.Utils;

namespace Sts2MapMod.Map;

/// <summary>
/// Finds map node UI elements and their bound room data from the map screen instance.
/// Uses name/type/tag fallback when game API is unknown.
/// </summary>
public static class MapNodeAdapter
{
    private static readonly ConcurrentDictionary<Type, FieldInfo?> MapPointDictionaryField = new();

    /// <summary>
    /// Collects all visible map node controls from the map screen.
    /// Replace "MapNodes" / "NodeContainer" with actual node paths once you inspect the game scene.
    /// </summary>
    public static IEnumerable<MapNodeView> FindAllVisibleNodes(object mapScreenInstance)
    {
        if (mapScreenInstance == null) yield break;

        // If the instance is a Godot Node, walk the tree
        if (mapScreenInstance is Node root)
        {
            foreach (var view in FindMapNodeViews(root))
                yield return view;
            yield break;
        }

        // Otherwise try reflection for common patterns: GetNode, Nodes, Children, etc.
        var nodes = GetNodesViaReflection(mapScreenInstance);
        foreach (var (node, data) in nodes)
        {
            if (node != null)
                yield return new MapNodeView(node, data);
        }
    }

    /// <summary>
    /// Resolve the room/model object bound to a map widget node (e.g. <c>NMapPoint.Point</c>).
    /// </summary>
    public static object? GetRoomDataFromNode(Node node) => TryGetRoomDataFromNode(node);

    private static IEnumerable<MapNodeView> FindMapNodeViews(Node root)
    {
        // Same source as path highlights in STS2RouteSuggest: NMapScreen._mapPointDictionary → UI node per MapPoint.
        var fromDict = EnumerateFromMapPointDictionary(root).ToList();
        if (fromDict.Count > 0)
        {
            foreach (var v in fromDict)
                yield return v;
            yield break;
        }

        // Do NOT use generic names like "Map", "Content", "VBoxContainer" here — they often match the whole map UI.
        // When no MapPoint-like node exists under such a subtree, the old logic tinted *every* descendant → black map + legend.
        var possibleContainers = new[]
        {
            // NMapScreen._points (scene child name is typically "Points" in STS2)
            "Points", "PathNodes", "MapNodes", "Nodes", "NodeContainer", "MapContainer", "RoomList"
        };
        foreach (var name in possibleContainers)
        {
            var container = root.GetNodeOrNull<Node>(name);
            if (container == null)
                container = GodotNodeUtil.FindChildByName<Node>(root, name);
            if (container == null) continue;

            foreach (var view in EnumerateViewsUnderContainer(container))
                yield return view;
            yield break;
        }

        // Last resort: search under each direct child with strict widget filter only (never bulk-tint all controls).
        foreach (var child in root.GetChildren())
        {
            if (child is not Node n) continue;
            foreach (var view in EnumerateViewsUnderContainer(n))
                yield return view;
        }
    }

    /// <summary>
    /// Reflects <c>NMapScreen._mapPointDictionary</c>: <c>Dictionary&lt;MapCoord, NMapPoint&gt;</c>.
    /// The dictionary key is only a coordinate, so the actual room kind must be read back from <c>NMapPoint.Point</c>.
    /// </summary>
    private static IEnumerable<MapNodeView> EnumerateFromMapPointDictionary(Node root)
    {
        var t = root.GetType();
        if (t.Name != "NMapScreen" && t.FullName?.EndsWith(".NMapScreen", StringComparison.Ordinal) != true)
            yield break;

        var field = MapPointDictionaryField.GetOrAdd(t, ty =>
            ty.GetField("_mapPointDictionary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        if (field == null)
        {
            LogUtil.Warn($"MapNodeAdapter: _mapPointDictionary field not found on {t.FullName}");
            yield break;
        }

        if (field.GetValue(root) is not IDictionary dict)
        {
            LogUtil.Warn($"MapNodeAdapter: _mapPointDictionary exists on {t.FullName} but value is not IDictionary.");
            yield break;
        }

        LogUtil.Diag(ConfigLoader.Config.VerifyGodotPrint,
            $"MapNodeAdapter: _mapPointDictionary count={dict.Count} on {t.FullName}");
        if (dict.Count == 0)
            yield break;

        foreach (DictionaryEntry e in dict)
        {
            if (e.Value is Node n && GodotObject.IsInstanceValid(n))
            {
                var roomData = TryGetRoomDataFromNode(n) ?? e.Key;
                yield return new MapNodeView(n, roomData);
            }
        }
    }

    /// <summary>
    /// Only yields nodes that look like map room widgets or carry room binding data.
    /// Never yields arbitrary labels/parchment/legend controls — that was causing full-screen dark Modulate.
    /// </summary>
    private static IEnumerable<MapNodeView> EnumerateViewsUnderContainer(Node container)
    {
        var descendants = GodotNodeUtil.EnumerateChildrenRecursive(container).ToList();
        descendants.Insert(0, container);

        foreach (var child in descendants)
        {
            if (!IsLikelyMapRoomWidget(child) && !HasRoomBinding(child))
                continue;
            var data = TryGetRoomDataFromNode(child);
            yield return new MapNodeView(child, data);
        }
    }

    /// <summary>True if this node exposes a non-nil RoomData / Data / Room / … (same props as <see cref="TryGetRoomDataFromNode"/>).</summary>
    private static bool HasRoomBinding(Node node)
    {
        if (node is not GodotObject gd)
            return false;
        foreach (var prop in new[] { "Point", "RoomData", "Data", "Room", "RoomInfo", "Model" })
        {
            try
            {
                var val = gd.Get(prop);
                if (val.VariantType != Variant.Type.Nil)
                    return true;
            }
            catch
            {
                // ignore
            }
        }

        return false;
    }

    private static bool IsLikelyMapRoomWidget(Node n)
    {
        var typeName = n.GetType().Name;
        if (typeName.Contains("MapPoint", StringComparison.OrdinalIgnoreCase))
            return true;
        if (typeName.Contains("RoomNode", StringComparison.OrdinalIgnoreCase))
            return true;
        if (typeName.Contains("MapRoom", StringComparison.OrdinalIgnoreCase))
            return true;
        var nodeName = n.Name.ToString();
        if (nodeName.Contains("MapPoint", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static object? TryGetRoomDataFromNode(Node node)
    {
        // STS2 map-point types expose Point as a CLR property (NMapPoint.Point). In practice this is more reliable than
        // querying GodotObject.Get("Point"), which may return Nil depending on generated property bindings.
        var clrType = node.GetType();
        foreach (var propName in new[] { "Point", "RoomData", "Data", "Room", "RoomInfo", "Model" })
        {
            try
            {
                var prop = clrType.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var raw = prop?.GetValue(node);
                if (raw != null)
                    return raw;
            }
            catch
            {
                // ignore
            }
        }

        // Try script property like RoomData, Data, Room, etc.
        if (node is GodotObject gdObj)
        {
            foreach (var prop in new[] { "Point", "RoomData", "Data", "Room", "RoomInfo", "Model" })
            {
                try
                {
                    var value = gdObj.Get(prop);
                    if (value.VariantType != Variant.Type.Nil)
                    {
                        var unwrapped = UnwrapVariantToObject(value);
                        if (unwrapped != null)
                            return unwrapped;
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }
        return null;
    }

    private static object? UnwrapVariantToObject(Variant v)
    {
        if (v.VariantType == Variant.Type.Nil)
            return null;
        if (v.VariantType == Variant.Type.Object)
            return v.AsGodotObject();
        return null;
    }

    private static IEnumerable<(Node? node, object? roomData)> GetNodesViaReflection(object instance)
    {
        var type = instance.GetType();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(Node) && !typeof(Node).IsAssignableFrom(prop.PropertyType))
                continue;
            Node? n = null;
            try
            {
                var value = prop.GetValue(instance);
                if (value is Node node)
                    n = node;
            }
            catch { /* ignore */ }

            if (n != null && (IsLikelyMapRoomWidget(n) || HasRoomBinding(n)))
            {
                var data = TryGetRoomDataFromNode(n);
                yield return (n, data);
            }
        }
    }
}

public readonly record struct MapNodeView(Node Node, object? RoomData);
