namespace Sts2MapMod.Core;

/// <summary>
/// Room type abstraction for map node coloring.
/// Unknown = unrecognized; Custom = mod-added room types (extensibility).
/// </summary>
public enum RoomKind
{
    Unknown,
    Monster,
    Elite,
    Rest,
    Shop,
    Treasure,
    Event,
    Boss,
    Custom
}
