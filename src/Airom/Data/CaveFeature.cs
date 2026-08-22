// Cave floor and wall constants from Umoria 5.6 source/constant.h.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Data;

/// <summary>
/// What occupies a dungeon square (Umoria's fval).
///
/// The values are ordered and compared as ranges, not just matched: anything at
/// or below <see cref="MaxCaveFloor"/> is walkable, anything at or above
/// <see cref="MinCaveWall"/> can be tunnelled. constant.h warns that changing
/// <see cref="MinCaveWall"/> also means changing the save routines, which pack
/// these values.
/// </summary>
public static class CaveFeature
{
    public const byte NullWall = 0;
    public const byte DarkFloor = 1;
    public const byte LightFloor = 2;

    /// <summary>Highest value that counts as part of a room.</summary>
    public const byte MaxCaveRoom = 2;

    public const byte CorridorFloor = 3;

    /// <summary>A corridor square holding a closed or secret door, or rubble.</summary>
    public const byte BlockedFloor = 4;

    /// <summary>Highest value that can be walked on.</summary>
    public const byte MaxCaveFloor = 4;

    /// <summary>Highest value that does not block movement.</summary>
    public const byte MaxOpenSpace = 3;

    /// <summary>Lowest value that blocks movement.</summary>
    public const byte MinClosedSpace = 4;

    /// <summary>Scratch values used while carving the cave; never persisted.</summary>
    public const byte Temp1Wall = 8;

    /// <inheritdoc cref="Temp1Wall"/>
    public const byte Temp2Wall = 9;

    /// <summary>Lowest value that is a wall. Changing this changes the save format.</summary>
    public const byte MinCaveWall = 12;

    public const byte GraniteWall = 12;
    public const byte MagmaWall = 13;
    public const byte QuartzWall = 14;
    public const byte BoundaryWall = 15;
}
