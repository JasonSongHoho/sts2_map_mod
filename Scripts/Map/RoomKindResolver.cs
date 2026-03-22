using System.Reflection;
using Sts2MapMod.Core;

namespace Sts2MapMod.Map;

/// <summary>
/// Resolves room data (binding object) to RoomKind.
/// STS2 map widgets bind Godot property <c>Point</c> to game <c>MapPoint</c> with <c>PointType</c> enum.
/// </summary>
public static class RoomKindResolver
{
    public static RoomKind Resolve(object? roomData)
    {
        if (roomData == null) return RoomKind.Unknown;

        var type = roomData.GetType();
        var typeName = type.Name;

        // NMapScreen._mapPointDictionary keys are game MapPoint; also handles GodotObject wrappers with PointType.
        var fromPointType = TryMapPointTypeEnum(roomData, type);
        if (fromPointType.HasValue)
            return fromPointType.Value;

        return typeName switch
        {
            "MonsterRoom" or "Monster" => RoomKind.Monster,
            "EliteRoom" or "Elite" => RoomKind.Elite,
            "RestRoom" or "Rest" => RoomKind.Rest,
            "ShopRoom" or "Shop" => RoomKind.Shop,
            "TreasureRoom" or "Treasure" => RoomKind.Treasure,
            "EventRoom" or "Event" => RoomKind.Event,
            "BossRoom" or "Boss" => RoomKind.Boss,
            _ => RoomKind.Custom
        };
    }

    private static RoomKind? TryMapPointTypeEnum(object mapPoint, Type mapPointClrType)
    {
        try
        {
            var prop = mapPointClrType.GetProperty("PointType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var raw = prop?.GetValue(mapPoint);
            if (raw is not Enum e)
                return null;

            // MegaCrit.Sts2.Core.Map.MapPointType (see decompiled MapPointType.cs).
            // In STS2, event nodes on the act map are represented as MapPointType.Unknown,
            // while Unassigned is the true "not resolved yet" value during map generation.
            return e.ToString() switch
            {
                "Monster" => RoomKind.Monster,
                "Elite" => RoomKind.Elite,
                "RestSite" => RoomKind.Rest,
                "Shop" => RoomKind.Shop,
                "Treasure" => RoomKind.Treasure,
                "Boss" => RoomKind.Boss,
                "Unknown" => RoomKind.Event,
                "Ancient" => RoomKind.Custom,
                "Unassigned" => RoomKind.Unknown,
                _ => RoomKind.Unknown
            };
        }
        catch
        {
            return null;
        }
    }
}
