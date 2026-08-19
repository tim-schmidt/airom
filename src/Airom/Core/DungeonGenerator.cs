// Ported from Umoria 5.6 source/generate.c, with place_gold() from
// source/misc3.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Carves dungeon levels. Mirrors generate.c.
///
/// Being ported in layers, because the whole file cannot be checked against the
/// original until all of it is present: the terrain primitives first, then
/// rooms, tunnels and finally population. Each layer is verified against the C
/// oracle as it lands.
/// </summary>
public sealed class DungeonGenerator(GameState game)
{
    // Tuning constants from constant.h. They shape every level, so they are
    // frozen until the port is verified end to end.
    private const int StreamerDensity = 5;      // DUN_STR_DEN
    private const int StreamerRange = 2;        // DUN_STR_RNG
    private const int MagmaStreamers = 3;       // DUN_STR_MAG
    private const int MagmaTreasureChance = 90; // DUN_STR_MC
    private const int QuartzStreamers = 2;      // DUN_STR_QUA
    private const int QuartzTreasureChance = 40;// DUN_STR_QC

    private const int GoldTypes = 18;           // MAX_GOLD
    private const int GoldListBase = 399;       // OBJ_GOLD_LIST
    private const int GreatItemChance = 12;     // OBJ_GREAT

    private readonly GameState _game = game;

    private Cave Cave => _game.Cave;

    private Rng Rng => _game.Rng;

    /// <summary>
    /// Replaces every still-empty square with the given rock. Mirrors
    /// fill_cave().
    ///
    /// The border is skipped deliberately - <see cref="PlaceBoundary"/> owns it -
    /// and the two temporary wall values count as empty, since the room and
    /// tunnel carvers use them as scratch marks.
    /// </summary>
    public void FillCave(byte feature)
    {
        for (int row = Cave.Height - 2; row > 0; row--)
        {
            for (int column = Cave.Width - 2; column > 0; column--)
            {
                CaveSquare square = Cave[row, column];
                if (square.Feature is CaveFeature.NullWall
                    or CaveFeature.Temp1Wall
                    or CaveFeature.Temp2Wall)
                {
                    square.Feature = feature;
                }
            }
        }
    }

    /// <summary>
    /// Rings the level in indestructible rock, so nothing can tunnel out.
    /// Mirrors place_boundary().
    /// </summary>
    public void PlaceBoundary()
    {
        for (int row = 0; row < Cave.Height; row++)
        {
            Cave[row, 0].Feature = CaveFeature.BoundaryWall;
            Cave[row, Cave.Width - 1].Feature = CaveFeature.BoundaryWall;
        }

        for (int column = 0; column < Cave.Width; column++)
        {
            Cave[0, column].Feature = CaveFeature.BoundaryWall;
            Cave[Cave.Height - 1, column].Feature = CaveFeature.BoundaryWall;
        }
    }

    /// <summary>
    /// Drives a vein of mineral through the granite, scattering treasure along
    /// it. Mirrors place_streamer().
    ///
    /// The walk starts near the middle and staggers in one of the eight
    /// directions until it leaves the grid, splashing squares around itself as
    /// it goes. Only granite converts, so a streamer never eats a room.
    /// </summary>
    public void PlaceStreamer(byte feature, int treasureChance)
    {
        // The literals are the original's: a starting point scattered around
        // the centre of a 66x198 level.
        int row = (Cave.Height / 2) + 11 - Rng.RandInt(23);
        int column = (Cave.Width / 2) + 16 - Rng.RandInt(33);

        // Directions 1-9 on the keypad, skipping 5 which is "no movement".
        int direction = Rng.RandInt(8);
        if (direction > 4)
        {
            direction++;
        }

        const int Spread = (2 * StreamerRange) + 1;
        const int Offset = StreamerRange + 1;

        do
        {
            for (int i = 0; i < StreamerDensity; i++)
            {
                int y = row + Rng.RandInt(Spread) - Offset;
                int x = column + Rng.RandInt(Spread) - Offset;

                if (!Cave.InBounds(y, x))
                {
                    continue;
                }

                CaveSquare square = Cave[y, x];
                if (square.Feature != CaveFeature.GraniteWall)
                {
                    continue;
                }

                square.Feature = feature;
                if (Rng.RandInt(treasureChance) == 1)
                {
                    PlaceGold(y, x);
                }
            }
        }
        while (Cave.Move(direction, ref row, ref column));
    }

    /// <summary>
    /// Drops a pile of gold or gems. Mirrors place_gold() in misc3.c.
    ///
    /// The value climbs with depth, and one pile in twelve is upgraded further.
    /// The final cost adjustment is rolled from the base cost itself, so richer
    /// kinds of gold also vary more.
    /// </summary>
    public void PlaceGold(int row, int column)
    {
        int index = _game.Objects.Allocate();

        int kind = ((Rng.RandInt(_game.DungeonLevel + 2) + 2) / 2) - 1;
        if (Rng.RandInt(GreatItemChance) == 1)
        {
            kind += Rng.RandInt(_game.DungeonLevel + 1);
        }

        if (kind >= GoldTypes)
        {
            kind = GoldTypes - 1;
        }

        Cave[row, column].ObjectIndex = index;

        InvenType gold = _game.Objects[index];
        gold.CopyFrom(GoldListBase + kind);
        gold.Cost += (8 * Rng.RandInt(gold.Cost)) + Rng.RandInt(8);
    }

    /// <summary>
    /// Whether a room built at this depth is lit. Mirrors the test the room
    /// builders open with: shallow levels are almost always lit, and by depth 25
    /// a lit room is impossible.
    /// </summary>
    private byte RoomFloor() =>
        _game.DungeonLevel <= Rng.RandInt(25)
            ? CaveFeature.LightFloor
            : CaveFeature.DarkFloor;

    /// <summary>
    /// Carves one rectangular room with a granite wall around it. Mirrors
    /// build_room().
    ///
    /// The room is drawn around the given centre, extending further left and
    /// right than up and down - the original notes the x dimension "tends to be
    /// much larger than the y dim". The four draws happen in a fixed order and
    /// each shifts the generator, so they cannot be rearranged.
    /// </summary>
    public void BuildRoom(int centreRow, int centreColumn)
    {
        byte floor = RoomFloor();

        int top = centreRow - Rng.RandInt(4);
        int bottom = centreRow + Rng.RandInt(3);
        int left = centreColumn - Rng.RandInt(11);
        int right = centreColumn + Rng.RandInt(11);

        for (int row = top; row <= bottom; row++)
        {
            for (int column = left; column <= right; column++)
            {
                Lay(row, column, floor);
            }
        }

        // Walls down each side, one row taller than the floor at both ends so
        // the corners are covered.
        for (int row = top - 1; row <= bottom + 1; row++)
        {
            Lay(row, left - 1, CaveFeature.GraniteWall);
            Lay(row, right + 1, CaveFeature.GraniteWall);
        }

        for (int column = left; column <= right; column++)
        {
            Lay(top - 1, column, CaveFeature.GraniteWall);
            Lay(bottom + 1, column, CaveFeature.GraniteWall);
        }
    }

    /// <summary>
    /// Carves two or three overlapping rectangles into one irregular room.
    /// Mirrors build_type1().
    ///
    /// The difference from <see cref="BuildRoom"/> is that walls are only laid
    /// where there is not already floor, so a later rectangle does not brick up
    /// the middle of an earlier one. That check is what turns overlapping boxes
    /// into a single connected space.
    /// </summary>
    public void BuildOverlappingRoom(int centreRow, int centreColumn)
    {
        byte floor = RoomFloor();
        int rectangles = 1 + Rng.RandInt(2);

        for (int i = 0; i < rectangles; i++)
        {
            int top = centreRow - Rng.RandInt(4);
            int bottom = centreRow + Rng.RandInt(3);
            int left = centreColumn - Rng.RandInt(11);
            int right = centreColumn + Rng.RandInt(11);

            for (int row = top; row <= bottom; row++)
            {
                for (int column = left; column <= right; column++)
                {
                    Lay(row, column, floor);
                }
            }

            for (int row = top - 1; row <= bottom + 1; row++)
            {
                LayWallUnlessFloor(row, left - 1, floor);
                LayWallUnlessFloor(row, right + 1, floor);
            }

            for (int column = left; column <= right; column++)
            {
                LayWallUnlessFloor(top - 1, column, floor);
                LayWallUnlessFloor(bottom + 1, column, floor);
            }
        }
    }

    /// <summary>
    /// Sets a square and marks it as belonging to a room, so it lights up as one
    /// piece when the player walks in.
    /// </summary>
    private void Lay(int row, int column, byte feature)
    {
        CaveSquare square = Cave[row, column];
        square.Feature = feature;
        square.LitRoom = true;
    }

    private void LayWallUnlessFloor(int row, int column, byte floor)
    {
        if (Cave[row, column].Feature != floor)
        {
            Lay(row, column, CaveFeature.GraniteWall);
        }
    }

    /// <summary>
    /// The mineral veins for a level: magma first, then quartz. Mirrors the
    /// streamer loop in cave_gen(). The order matters, since each streamer
    /// consumes draws that shift everything after it.
    /// </summary>
    public void PlaceStreamers()
    {
        for (int i = 0; i < MagmaStreamers; i++)
        {
            PlaceStreamer(CaveFeature.MagmaWall, MagmaTreasureChance);
        }

        for (int i = 0; i < QuartzStreamers; i++)
        {
            PlaceStreamer(CaveFeature.QuartzWall, QuartzTreasureChance);
        }
    }
}
