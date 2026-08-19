// Ported from the cave predicates in Umoria 5.6 source/sets.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Data;

/// <summary>
/// Classification of dungeon squares by their fval. Mirrors set_room(),
/// set_corr() and set_floor() in Umoria's sets.c, which the cave generator
/// passes around as function pointers when flooding regions.
/// </summary>
public static class CaveSets
{
    /// <summary>Part of a room, lit or unlit. Mirrors set_room().</summary>
    public static bool IsRoom(byte feature) =>
        feature is CaveFeature.DarkFloor or CaveFeature.LightFloor;

    /// <summary>
    /// Part of a corridor. Mirrors set_corr(). Blocked corridor squares - those
    /// holding a door or rubble - count, because the generator still needs to
    /// route through them.
    /// </summary>
    public static bool IsCorridor(byte feature) =>
        feature is CaveFeature.CorridorFloor or CaveFeature.BlockedFloor;

    /// <summary>
    /// Any walkable square. Mirrors set_floor(), which is a range test rather
    /// than a list - everything at or below MaxCaveFloor is floor of some kind.
    /// </summary>
    public static bool IsFloor(byte feature) => feature <= CaveFeature.MaxCaveFloor;
}
