// Ported from cave_gen() in Umoria 5.6 source/generate.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The whole of a dungeon level, start to finish.
/// </summary>
public sealed partial class DungeonGenerator
{
    private const int RoomCountMean = 32;    // DUN_ROO_MEA
    private const int UnusualRoomOdds = 300; // DUN_UNUSUAL
    private const int MinimumMonsters = 14;  // MIN_MALLOC_LEVEL

    /// <summary>
    /// The viewport, which is also what the room grid is spaced by. Umoria uses
    /// one constant for both; they are separable in principle but frozen here
    /// until the port is verified, because changing them changes every level.
    /// </summary>
    private const int RoomGridHeight = 22; // SCREEN_HEIGHT

    /// <inheritdoc cref="RoomGridHeight"/>
    private const int RoomGridWidth = 66; // SCREEN_WIDTH

    /// <summary>
    /// Builds the level the player is about to arrive on. Mirrors
    /// generate_cave().
    ///
    /// The town is one screen and the dungeon is nine, so the size is settled
    /// here along with how far the view can scroll. The panel indices are set to
    /// their maximum without the window being recomputed - see
    /// <see cref="Panel.Resize"/> for why that matters - and the player is left
    /// unplaced for whichever builder runs.
    /// </summary>
    public void Generate()
    {
        _game.Objects.Reset();
        _game.Monsters.Reset();

        _game.CharacterRow = -1;
        _game.CharacterColumn = -1;

        if (_game.DungeonLevel == 0)
        {
            _game.Cave.Resize(TownHeight, TownWidth);
            _game.Cave.Blank();
            GenerateTown();
        }
        else
        {
            _game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
            _game.Cave.Blank();
            CarveCave();
        }
    }

    /// <summary>
    /// Carves a complete dungeon level. Mirrors cave_gen().
    ///
    /// Rooms are scattered over a coarse grid, joined in a shuffled ring so the
    /// corridors wander rather than running in order, then the rock is filled
    /// in, veined, walled and doored. Stairs, the player's spot, monsters and
    /// treasure come last.
    ///
    /// Room slots are drawn with repeats allowed, so asking for 32 usually
    /// yields fewer - which is why a level has around twenty rooms rather than
    /// the mean the constant names.
    /// </summary>
    public void CarveCave()
    {
        int rowRooms = 2 * (Cave.Height / RoomGridHeight);
        int columnRooms = 2 * (Cave.Width / RoomGridWidth);

        var occupied = new bool[rowRooms, columnRooms];
        int wanted = Rng.RandNor(RoomCountMean, 2);
        for (int i = 0; i < wanted; i++)
        {
            occupied[Rng.RandInt(rowRooms) - 1, Rng.RandInt(columnRooms) - 1] = true;
        }

        List<int> roomRows = [];
        List<int> roomColumns = [];

        for (int i = 0; i < rowRooms; i++)
        {
            for (int j = 0; j < columnRooms; j++)
            {
                if (!occupied[i, j])
                {
                    continue;
                }

                int row = (i * (RoomGridHeight >> 1)) + (RoomGridHeight / 4);
                int column = (j * (RoomGridWidth >> 1)) + (RoomGridWidth / 4);
                roomRows.Add(row);
                roomColumns.Add(column);

                // Unusual rooms become near-certain with depth: the draw is
                // against 300, so they are almost unheard of near the surface.
                if (_game.DungeonLevel > Rng.RandInt(UnusualRoomOdds))
                {
                    switch (Rng.RandInt(3))
                    {
                        case 1:
                            BuildOverlappingRoom(row, column);
                            break;
                        case 2:
                            BuildInnerRoom(row, column);
                            break;
                        default:
                            BuildCrossRoom(row, column);
                            break;
                    }
                }
                else
                {
                    BuildRoom(row, column);
                }
            }
        }

        int count = roomRows.Count;

        // Shuffle the join order, so corridors do not simply run along the grid.
        for (int i = 0; i < count; i++)
        {
            int a = Rng.RandInt(count) - 1;
            int b = Rng.RandInt(count) - 1;
            (roomRows[a], roomRows[b]) = (roomRows[b], roomRows[a]);
            (roomColumns[a], roomColumns[b]) = (roomColumns[b], roomColumns[a]);
        }

        ResetDoorCandidates();

        // The first room is repeated at the end so the ring closes.
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            BuildTunnel(roomRows[next], roomColumns[next], roomRows[i], roomColumns[i]);
        }

        FillCave(CaveFeature.GraniteWall);
        PlaceStreamers();
        PlaceBoundary();
        PlaceJunctionDoors();

        int allocLevel = Math.Clamp(_game.DungeonLevel / 3, 2, 10);

        PlaceStairs(2, Rng.RandInt(2) + 2, 3);
        PlaceStairs(1, Rng.RandInt(2), 3);

        (int charRow, int charColumn) = NewSpot();
        _game.CharacterRow = charRow;
        _game.CharacterColumn = charColumn;

        AllocMonster(
            Rng.RandInt(8) + MinimumMonsters + allocLevel,
            minimumDistance: 0,
            asleep: true);

        PopulateLevel(allocLevel);

        if (_game.DungeonLevel >= WinMonsterDepth)
        {
            PlaceWinMonster();
        }
    }
}
