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

    // Tunnelling constants from constant.h.
    private const int TunnelRandomDirection = 9;  // DUN_TUN_RND
    private const int TunnelDirectionChange = 70; // DUN_TUN_CHG
    private const int TunnelContinueChance = 15;  // DUN_TUN_CON
    private const int TunnelRoomDoorChance = 25;  // DUN_TUN_PEN
    private const int TunnelJunctionDoor = 15;    // DUN_TUN_JCT

    private const int OpenDoorObject = 367;   // OBJ_OPEN_DOOR
    private const int ClosedDoorObject = 368; // OBJ_CLOSED_DOOR
    private const int SecretDoorObject = 369; // OBJ_SECRET_DOOR

    /// <summary>
    /// Junctions where a tunnel met an existing corridor. cave_gen revisits
    /// these afterwards to decide which become doors, so the list outlives the
    /// tunnel that recorded it. Umoria caps it at 100 and silently drops the
    /// rest; that cap is reproduced because reaching it changes the level.
    /// </summary>
    private readonly List<(int Row, int Column)> _doorCandidates = [];

    /// <summary>Clears the junction list. Mirrors setting doorindex to zero.</summary>
    public void ResetDoorCandidates() => _doorCandidates.Clear();

    /// <summary>Junctions recorded by the tunnels built so far.</summary>
    public IReadOnlyList<(int Row, int Column)> DoorCandidates => _doorCandidates;

    /// <summary>
    /// Points the step towards the target, then throws away one axis so the
    /// tunnel moves in a straight line rather than diagonally. Mirrors
    /// correct_dir().
    /// </summary>
    private void CorrectDirection(
        ref int rowStep, ref int columnStep, int row1, int column1, int row2, int column2)
    {
        rowStep = row1 < row2 ? 1 : row1 == row2 ? 0 : -1;
        columnStep = column1 < column2 ? 1 : column1 == column2 ? 0 : -1;

        if (rowStep != 0 && columnStep != 0)
        {
            if (Rng.RandInt(2) == 1)
            {
                rowStep = 0;
            }
            else
            {
                columnStep = 0;
            }
        }
    }

    /// <summary>
    /// Picks one of the four compass directions at random, which is what makes
    /// corridors wander instead of running straight. Mirrors rand_dir().
    /// </summary>
    private void RandomDirection(ref int rowStep, ref int columnStep)
    {
        int roll = Rng.RandInt(4);
        if (roll < 3)
        {
            columnStep = 0;
            rowStep = -3 + (roll << 1); // 1 gives -1, 2 gives 1
        }
        else
        {
            rowStep = 0;
            columnStep = -7 + (roll << 1); // 3 gives -1, 4 gives 1
        }
    }

    private void PlaceDoorObject(int row, int column, int objectIndex, byte feature)
    {
        int slot = _game.Objects.Allocate();
        Cave[row, column].ObjectIndex = slot;
        _game.Objects[slot].CopyFrom(objectIndex);
        Cave[row, column].Feature = feature;
    }

    /// <summary>An open doorway; walkable, so it counts as corridor.</summary>
    public void PlaceOpenDoor(int row, int column) =>
        PlaceDoorObject(row, column, OpenDoorObject, CaveFeature.CorridorFloor);

    /// <summary>A door already broken off its hinges. p1 marks it as broken.</summary>
    public void PlaceBrokenDoor(int row, int column)
    {
        PlaceDoorObject(row, column, OpenDoorObject, CaveFeature.CorridorFloor);
        _game.Objects[Cave[row, column].ObjectIndex].P1 = 1;
    }

    /// <summary>A shut door; blocks movement until opened.</summary>
    public void PlaceClosedDoor(int row, int column) =>
        PlaceDoorObject(row, column, ClosedDoorObject, CaveFeature.BlockedFloor);

    /// <summary>
    /// A locked door. p1 holds the lock strength as a positive number, which is
    /// how the game tells locked from stuck.
    /// </summary>
    public void PlaceLockedDoor(int row, int column)
    {
        PlaceDoorObject(row, column, ClosedDoorObject, CaveFeature.BlockedFloor);
        _game.Objects[Cave[row, column].ObjectIndex].P1 = (short)(Rng.RandInt(10) + 10);
    }

    /// <summary>
    /// A jammed door. The same p1 field, negated - a negative value means stuck
    /// rather than locked, so one field carries both states.
    /// </summary>
    public void PlaceStuckDoor(int row, int column)
    {
        PlaceDoorObject(row, column, ClosedDoorObject, CaveFeature.BlockedFloor);
        _game.Objects[Cave[row, column].ObjectIndex].P1 = (short)(-Rng.RandInt(10) - 10);
    }

    /// <summary>A door disguised as wall until the player searches it out.</summary>
    public void PlaceSecretDoor(int row, int column) =>
        PlaceDoorObject(row, column, SecretDoorObject, CaveFeature.BlockedFloor);

    /// <summary>
    /// Chooses what kind of door to put here. Mirrors place_door().
    ///
    /// Roughly a third open, a third closed in some form, a third secret. Within
    /// the closed third, most are simply shut - locked and stuck are the
    /// uncommon cases.
    /// </summary>
    public void PlaceDoor(int row, int column)
    {
        int kind = Rng.RandInt(3);
        if (kind == 1)
        {
            if (Rng.RandInt(4) == 1)
            {
                PlaceBrokenDoor(row, column);
            }
            else
            {
                PlaceOpenDoor(row, column);
            }
        }
        else if (kind == 2)
        {
            int shut = Rng.RandInt(12);
            if (shut > 3)
            {
                PlaceClosedDoor(row, column);
            }
            else if (shut == 3)
            {
                PlaceStuckDoor(row, column);
            }
            else
            {
                PlaceLockedDoor(row, column);
            }
        }
        else
        {
            PlaceSecretDoor(row, column);
        }
    }

    /// <summary>
    /// Counts the corridor squares touching this one, itself included. Mirrors
    /// next_to_corr() in misc1.c.
    ///
    /// A square already holding a door does not count, which is what stops the
    /// junction logic stacking two doors beside each other.
    /// </summary>
    private int CountAdjacentCorridor(int row, int column)
    {
        int found = 0;
        for (int y = row - 1; y <= row + 1; y++)
        {
            for (int x = column - 1; x <= column + 1; x++)
            {
                CaveSquare square = Cave[y, x];
                if (square.Feature != CaveFeature.CorridorFloor)
                {
                    continue;
                }

                if (square.ObjectIndex == 0
                    || _game.Objects[square.ObjectIndex].TVal < ItemCategory.MinDoors)
                {
                    found++;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Whether this square is a corridor junction worth putting a door on:
    /// several corridors meeting, with walls squeezing it from opposite sides.
    /// Mirrors next_to().
    /// </summary>
    private bool IsDoorwayJunction(int row, int column)
    {
        if (CountAdjacentCorridor(row, column) <= 2)
        {
            return false;
        }

        bool wallsAbove = Cave[row - 1, column].Feature >= CaveFeature.MinCaveWall
            && Cave[row + 1, column].Feature >= CaveFeature.MinCaveWall;

        bool wallsBeside = Cave[row, column - 1].Feature >= CaveFeature.MinCaveWall
            && Cave[row, column + 1].Feature >= CaveFeature.MinCaveWall;

        return wallsAbove || wallsBeside;
    }

    /// <summary>
    /// Puts a door on a junction, sometimes. Mirrors try_door().
    /// </summary>
    public void TryDoor(int row, int column)
    {
        if (Cave[row, column].Feature == CaveFeature.CorridorFloor
            && Rng.RandInt(100) > TunnelJunctionDoor
            && IsDoorwayJunction(row, column))
        {
            PlaceDoor(row, column);
        }
    }

    /// <summary>
    /// Digs a corridor from one room towards another. Mirrors build_tunnel().
    ///
    /// The digger walks towards the target, wandering off course now and then,
    /// and reacts to what it runs into: untouched rock is carved, granite is
    /// remembered as a possible doorway, and meeting an existing corridor may
    /// end the run. Nothing is written to the cave during the walk - the carved
    /// squares and candidate walls are collected and applied at the end, which
    /// is what lets the walk read the terrain as it was before it started.
    /// </summary>
    public void BuildTunnel(int row1, int column1, int row2, int column2)
    {
        // Umoria's fixed stacks. The caps are reproduced rather than replaced by
        // growable lists: reaching one truncates the corridor, which is part of
        // the generated level.
        const int StackLimit = 1000;
        const int DoorLimit = 100;

        List<(int Row, int Column)> carved = [];
        List<(int Row, int Column)> walls = [];

        bool stop = false;
        bool recordedDoor = false;
        int loops = 0;
        int startRow = row1;
        int startColumn = column1;

        int rowStep = 0;
        int columnStep = 0;
        CorrectDirection(ref rowStep, ref columnStep, row1, column1, row2, column2);

        do
        {
            // The original guards against a walk that never arrives.
            loops++;
            if (loops > 2000)
            {
                stop = true;
            }

            if (Rng.RandInt(100) > TunnelDirectionChange)
            {
                if (Rng.RandInt(TunnelRandomDirection) == 1)
                {
                    RandomDirection(ref rowStep, ref columnStep);
                }
                else
                {
                    CorrectDirection(ref rowStep, ref columnStep, row1, column1, row2, column2);
                }
            }

            int nextRow = row1 + rowStep;
            int nextColumn = column1 + columnStep;
            while (!Cave.InBounds(nextRow, nextColumn))
            {
                if (Rng.RandInt(TunnelRandomDirection) == 1)
                {
                    RandomDirection(ref rowStep, ref columnStep);
                }
                else
                {
                    CorrectDirection(ref rowStep, ref columnStep, row1, column1, row2, column2);
                }

                nextRow = row1 + rowStep;
                nextColumn = column1 + columnStep;
            }

            byte feature = Cave[nextRow, nextColumn].Feature;

            if (feature == CaveFeature.NullWall)
            {
                row1 = nextRow;
                column1 = nextColumn;
                if (carved.Count < StackLimit)
                {
                    carved.Add((row1, column1));
                }

                recordedDoor = false;
            }
            else if (feature == CaveFeature.Temp2Wall)
            {
                // Already marked as the surround of a wall this tunnel broke
                // through. Stepping onto it would double back.
            }
            else if (feature == CaveFeature.GraniteWall)
            {
                row1 = nextRow;
                column1 = nextColumn;
                if (walls.Count < StackLimit)
                {
                    walls.Add((row1, column1));
                }

                // Mark the granite around the breach so the tunnel does not
                // chew sideways through the room wall it just pierced.
                for (int y = row1 - 1; y <= row1 + 1; y++)
                {
                    for (int x = column1 - 1; x <= column1 + 1; x++)
                    {
                        if (Cave.InBounds(y, x)
                            && Cave[y, x].Feature == CaveFeature.GraniteWall)
                        {
                            Cave[y, x].Feature = CaveFeature.Temp2Wall;
                        }
                    }
                }
            }
            else if (feature is CaveFeature.CorridorFloor or CaveFeature.BlockedFloor)
            {
                row1 = nextRow;
                column1 = nextColumn;

                if (!recordedDoor)
                {
                    if (_doorCandidates.Count < DoorLimit)
                    {
                        _doorCandidates.Add((row1, column1));
                    }

                    recordedDoor = true;
                }

                if (Rng.RandInt(100) > TunnelContinueChance)
                {
                    // Only stop once the corridor has covered some ground,
                    // which is what stops rooms being left unreachable.
                    int travelledRows = Math.Abs(row1 - startRow);
                    int travelledColumns = Math.Abs(column1 - startColumn);
                    if (travelledRows > 10 || travelledColumns > 10)
                    {
                        stop = true;
                    }
                }
            }
            else
            {
                // Room floor and everything else: walk over it.
                row1 = nextRow;
                column1 = nextColumn;
            }
        }
        while ((row1 != row2 || column1 != column2) && !stop);

        foreach ((int row, int column) in carved)
        {
            Cave[row, column].Feature = CaveFeature.CorridorFloor;
        }

        foreach ((int row, int column) in walls)
        {
            if (Cave[row, column].Feature != CaveFeature.Temp2Wall)
            {
                continue;
            }

            if (Rng.RandInt(100) < TunnelRoomDoorChance)
            {
                PlaceDoor(row, column);
            }
            else
            {
                // The rest become plain openings into the room.
                Cave[row, column].Feature = CaveFeature.CorridorFloor;
            }
        }
    }

    /// <summary>
    /// Considers a door on each side of every junction the tunnels recorded.
    /// Mirrors the loop cave_gen runs after tunnelling.
    /// </summary>
    public void PlaceJunctionDoors()
    {
        foreach ((int row, int column) in _doorCandidates)
        {
            TryDoor(row, column - 1);
            TryDoor(row, column + 1);
            TryDoor(row - 1, column);
            TryDoor(row + 1, column);
        }
    }

    private const int UpStairObject = 370;   // OBJ_UP_STAIR
    private const int DownStairObject = 371; // OBJ_DOWN_STAIR
    private const int TrapListBase = 378;    // OBJ_TRAP_LIST
    private const int RubbleObject = 396;    // OBJ_RUBBLE
    private const int TrapKinds = 18;        // MAX_TRAP

    /// <summary>
    /// Puts a particular trap on a square. Mirrors place_trap() in misc3.c.
    /// </summary>
    /// <param name="kind">Offset into the trap rows, 0 to <c>MAX_TRAP - 1</c>.</param>
    public void PlaceTrap(int row, int column, int kind)
    {
        int slot = _game.Objects.Allocate();
        Cave[row, column].ObjectIndex = slot;
        _game.Objects[slot].CopyFrom(TrapListBase + kind);
    }

    /// <summary>Picks a trap at random. Mirrors the typ == 1 case of alloc_object().</summary>
    public void PlaceRandomTrap(int row, int column) =>
        PlaceTrap(row, column, Rng.RandInt(TrapKinds) - 1);

    /// <summary>
    /// Drops a pile of rubble. Mirrors place_rubble() in misc3.c.
    ///
    /// Unlike a trap, rubble also blocks the square - it is the one scattered
    /// object that changes the terrain under it.
    /// </summary>
    public void PlaceRubble(int row, int column)
    {
        int slot = _game.Objects.Allocate();
        CaveSquare square = Cave[row, column];
        square.ObjectIndex = slot;
        square.Feature = CaveFeature.BlockedFloor;
        _game.Objects[slot].CopyFrom(RubbleObject);
    }

    /// <summary>
    /// Chooses which object to generate at a given depth, as an index into
    /// <see cref="ObjectLevels.Sorted"/>. Mirrors get_obj_num() in misc3.c.
    ///
    /// The distribution is deliberately not uniform. Half the time it draws once
    /// from everything available at this depth; the other half it draws three
    /// times, keeps the deepest, and then redraws within that level. The
    /// original's comment explains the intent: a level-n object turns up roughly
    /// 2/n of the time on level n, so deep items stay rare without being
    /// unreachable. One draw in twelve also lifts the effective depth first,
    /// which is what lets a shallow level occasionally cough up something far
    /// better than it should.
    /// </summary>
    /// <param name="level">Dungeon depth to generate for.</param>
    /// <param name="mustBeSmall">
    /// True when the object has to fit in a chest, which rejects and redraws
    /// anything bulky.
    /// </param>
    public int GetObjectNumber(int level, bool mustBeSmall)
    {
        ReadOnlySpan<int> totals = ObjectLevels.LevelTotals;
        ReadOnlySpan<int> sorted = ObjectLevels.Sorted;

        if (level == 0)
        {
            return Rng.RandInt(totals[0]) - 1;
        }

        if (level >= ObjectLevels.MaxObjectLevel)
        {
            level = ObjectLevels.MaxObjectLevel;
        }
        else if (Rng.RandInt(GreatItemChance) == 1)
        {
            level = (level * ObjectLevels.MaxObjectLevel
                / Rng.RandInt(ObjectLevels.MaxObjectLevel)) + 1;
            if (level > ObjectLevels.MaxObjectLevel)
            {
                level = ObjectLevels.MaxObjectLevel;
            }
        }

        int index;
        do
        {
            if (Rng.RandInt(2) == 1)
            {
                index = Rng.RandInt(totals[level]) - 1;
            }
            else
            {
                // Best of three, then redraw within whatever level that landed
                // on - which is what biases the result deeper.
                index = Rng.RandInt(totals[level]) - 1;
                int other = Rng.RandInt(totals[level]) - 1;
                if (index < other)
                {
                    index = other;
                }

                other = Rng.RandInt(totals[level]) - 1;
                if (index < other)
                {
                    index = other;
                }

                int chosenLevel = GameTables.ObjectList[sorted[index]].Level;
                index = chosenLevel == 0
                    ? Rng.RandInt(totals[0]) - 1
                    : Rng.RandInt(totals[chosenLevel] - totals[chosenLevel - 1])
                        - 1 + totals[chosenLevel - 1];
            }
        }
        while (mustBeSmall && ItemSets.IsTooLargeForChest(GameTables.ObjectList[sorted[index]]));

        return index;
    }

    /// <summary>
    /// Counts how many of the four orthogonal neighbours are wall. Mirrors
    /// next_to_walls() in misc1.c.
    ///
    /// Staircases prefer corners, so this is what "at least three walls" means
    /// when picking a spot for one.
    /// </summary>
    private int CountAdjacentWalls(int row, int column)
    {
        int walls = 0;
        if (Cave[row - 1, column].Feature >= CaveFeature.MinCaveWall)
        {
            walls++;
        }

        if (Cave[row + 1, column].Feature >= CaveFeature.MinCaveWall)
        {
            walls++;
        }

        if (Cave[row, column - 1].Feature >= CaveFeature.MinCaveWall)
        {
            walls++;
        }

        if (Cave[row, column + 1].Feature >= CaveFeature.MinCaveWall)
        {
            walls++;
        }

        return walls;
    }

    /// <summary>
    /// Removes whatever object is on a square. Mirrors delete_object() in
    /// moria3.c, less its display half - the original also redraws the square
    /// and reports whether the player could see it, neither of which touches the
    /// generator or the level.
    ///
    /// A blocked square reverts to corridor, because what was blocking it was
    /// the door being removed.
    /// </summary>
    public void DeleteObject(int row, int column)
    {
        CaveSquare square = Cave[row, column];

        if (square.Feature == CaveFeature.BlockedFloor)
        {
            square.Feature = CaveFeature.CorridorFloor;
        }

        _game.Objects.Release(square.ObjectIndex, Cave);
        square.ObjectIndex = 0;
        square.FieldMark = false;
    }

    private void PlaceStair(int row, int column, int objectIndex)
    {
        // Stairs displace whatever was here - they are placed last and win.
        if (Cave[row, column].ObjectIndex != 0)
        {
            DeleteObject(row, column);
        }

        int slot = _game.Objects.Allocate();
        Cave[row, column].ObjectIndex = slot;
        _game.Objects[slot].CopyFrom(objectIndex);
    }

    /// <summary>Mirrors place_up_stairs().</summary>
    public void PlaceUpStairs(int row, int column) =>
        PlaceStair(row, column, UpStairObject);

    /// <summary>Mirrors place_down_stairs().</summary>
    public void PlaceDownStairs(int row, int column) =>
        PlaceStair(row, column, DownStairObject);

    /// <summary>
    /// Scatters staircases about the level. Mirrors place_stairs().
    ///
    /// Each one is found by picking a random twelve-by-twelve window and
    /// scanning it for open floor with nothing on it and enough surrounding
    /// wall. After thirty-one failed windows the wall requirement drops by one
    /// and it tries again, so a level with no corners left still gets its
    /// stairs rather than looping forever.
    ///
    /// Note the requirement is not reset between staircases, and drops once per
    /// staircase even when the first window succeeds. So later stairs are placed
    /// under a looser rule than earlier ones - a quirk, but a load-bearing one.
    /// </summary>
    /// <param name="kind">1 for up, anything else for down.</param>
    /// <param name="count">How many to place.</param>
    /// <param name="walls">Neighbouring walls required to begin with.</param>
    public void PlaceStairs(int kind, int count, int walls)
    {
        for (int i = 0; i < count; i++)
        {
            bool placed = false;
            do
            {
                int attempt = 0;
                do
                {
                    // The window is kept clear of the boundary ring at both
                    // ends, which is why the range stops fourteen short.
                    int row = Rng.RandInt(Cave.Height - 14);
                    int column = Rng.RandInt(Cave.Width - 14);
                    int lastRow = row + 12;
                    int lastColumn = column + 12;

                    do
                    {
                        do
                        {
                            CaveSquare square = Cave[row, column];
                            if (square.Feature <= CaveFeature.MaxOpenSpace
                                && square.ObjectIndex == 0
                                && CountAdjacentWalls(row, column) >= walls)
                            {
                                placed = true;
                                if (kind == 1)
                                {
                                    PlaceUpStairs(row, column);
                                }
                                else
                                {
                                    PlaceDownStairs(row, column);
                                }
                            }

                            column++;
                        }
                        while (column != lastColumn && !placed);

                        column = lastColumn - 12;
                        row++;
                    }
                    while (row != lastRow && !placed);

                    attempt++;
                }
                while (!placed && attempt <= 30);

                walls--;
            }
            while (!placed);
        }
    }

    /// <summary>
    /// Finds an empty walkable square anywhere on the level. Mirrors new_spot().
    ///
    /// Used to drop the player in, and to place things that only need somewhere
    /// free. It simply retries until it lands on open floor with no monster and
    /// no object, which is why a level with almost no floor left would spin -
    /// the original accepts that risk and so does this.
    /// </summary>
    public (int Row, int Column) NewSpot()
    {
        int row;
        int column;

        do
        {
            row = Rng.RandInt(Cave.Height - 2);
            column = Rng.RandInt(Cave.Width - 2);
        }
        while (Cave[row, column].Feature >= CaveFeature.MinClosedSpace
            || Cave[row, column].MonsterIndex != 0
            || Cave[row, column].ObjectIndex != 0);

        return (row, column);
    }

    /// <summary>
    /// Generates and enchants an object on a square. Mirrors place_object() in
    /// misc3.c.
    ///
    /// Two steps: <see cref="GetObjectNumber"/> decides which item this is, then
    /// the enchantment decides what it became. Both draw from the generator, in
    /// that order.
    /// </summary>
    /// <param name="mustBeSmall">True when the object has to fit in a chest.</param>
    public void PlaceObject(int row, int column, bool mustBeSmall)
    {
        int slot = _game.Objects.Allocate();
        Cave[row, column].ObjectIndex = slot;

        int pick = GetObjectNumber(_game.DungeonLevel, mustBeSmall);
        InvenType item = _game.Objects[slot];
        item.CopyFrom(ObjectLevels.Sorted[pick]);

        _game.Enchantment.Apply(item, _game.DungeonLevel);
    }

    /// <summary>
    /// What alloc_object() scatters. The numbers are Umoria's; 2 is absent
    /// because it once meant visible traps and no longer does.
    /// </summary>
    public static class Scatter
    {
        public const int Trap = 1;
        public const int Rubble = 3;
        public const int Gold = 4;
        public const int Item = 5;
    }

    /// <summary>
    /// Scatters things about the finished level. Mirrors alloc_object() in
    /// misc3.c.
    ///
    /// Positions are drawn at random and rejected until one satisfies the given
    /// terrain predicate, holds nothing already, and is not where the player is
    /// standing. The original notes why the last check matters: an object under
    /// the player causes trouble if it turns out to be rubble or a trap.
    ///
    /// A negative count is possible and harmless - cave_gen draws several of
    /// these counts from randnor, which can go below zero, and the loop simply
    /// does nothing.
    /// </summary>
    /// <param name="allowed">Terrain test, one of the <see cref="CaveSets"/> predicates.</param>
    /// <param name="type">One of the <see cref="Scatter"/> kinds.</param>
    /// <param name="count">How many to place.</param>
    public void AllocObject(Func<byte, bool> allowed, int type, int count)
    {
        ArgumentNullException.ThrowIfNull(allowed);

        for (int placed = 0; placed < count; placed++)
        {
            int row;
            int column;
            do
            {
                row = Rng.RandInt(Cave.Height) - 1;
                column = Rng.RandInt(Cave.Width) - 1;
            }
            while (!allowed(Cave[row, column].Feature)
                || Cave[row, column].ObjectIndex != 0
                || (row == _game.CharacterRow && column == _game.CharacterColumn));

            switch (type)
            {
                case Scatter.Trap:
                    PlaceRandomTrap(row, column);
                    break;
                case Scatter.Rubble:
                    PlaceRubble(row, column);
                    break;
                case Scatter.Gold:
                    PlaceGold(row, column);
                    break;
                default:
                    PlaceObject(row, column, mustBeSmall: false);
                    break;
            }
        }
    }

    /// <summary>
    /// Scatters everything a finished level gets. Mirrors the tail of
    /// cave_gen(), less the monsters.
    ///
    /// The order and the counts are both load-bearing: each call consumes draws,
    /// and three of the counts come from randnor and can land below zero, which
    /// simply means nothing of that kind appears on this level.
    /// </summary>
    public void PopulateLevel(int allocLevel)
    {
        AllocObject(CaveSets.IsCorridor, Scatter.Rubble, Rng.RandInt(allocLevel));
        AllocObject(CaveSets.IsRoom, Scatter.Item, Rng.RandNor(TreasureInRooms, 3));
        AllocObject(CaveSets.IsFloor, Scatter.Item, Rng.RandNor(TreasureAnywhere, 3));
        AllocObject(CaveSets.IsFloor, Scatter.Gold, Rng.RandNor(GoldAnywhere, 3));
        AllocObject(CaveSets.IsFloor, Scatter.Trap, Rng.RandInt(allocLevel));
    }

    private const int TreasureInRooms = 7;  // TREAS_ROOM_ALLOC
    private const int TreasureAnywhere = 2; // TREAS_ANY_ALLOC
    private const int GoldAnywhere = 2;     // TREAS_GOLD_ALLOC

    private const int NastyMonsterChance = 50; // MON_NASTY
    private const int SummonLevelAdjust = 2;   // MON_SUMMON_ADJ

    /// <summary>
    /// Chooses a monster suitable for a given depth, as an index into the
    /// creature table. Mirrors get_mons_num().
    ///
    /// Like the object draw this leans deeper than uniform, by the original's
    /// own account making a level-n monster appear roughly 2/n of the time on
    /// level n. One draw in fifty ignores that and reaches deeper still, by a
    /// normal deviate - which is where the occasional monster far out of its
    /// depth comes from.
    ///
    /// The final line always redraws within the chosen level, so the result is
    /// a monster of exactly that depth rather than anything shallower.
    /// </summary>
    public int GetMonsterNumber(int level)
    {
        ReadOnlySpan<int> totals = MonsterLevels.LevelTotals;

        if (level == 0)
        {
            return Rng.RandInt(totals[0]) - 1;
        }

        if (level > MonsterLevels.MaxMonsterLevel)
        {
            level = MonsterLevels.MaxMonsterLevel;
        }

        if (Rng.RandInt(NastyMonsterChance) == 1)
        {
            level += Math.Abs(Rng.RandNor(0, 4)) + 1;
            if (level > MonsterLevels.MaxMonsterLevel)
            {
                level = MonsterLevels.MaxMonsterLevel;
            }
        }
        else
        {
            // Best of two, over everything below this depth but above level
            // zero - the town monsters are excluded from the draw.
            int span = totals[level] - totals[0];
            int pick = Rng.RandInt(span) - 1;
            int other = Rng.RandInt(span) - 1;
            if (other > pick)
            {
                pick = other;
            }

            level = GameTables.CreatureList[pick + totals[0]].Level;
        }

        return Rng.RandInt(totals[level] - totals[level - 1]) - 1 + totals[level - 1];
    }

    /// <summary>
    /// Spawns one monster at a position. Mirrors place_monster().
    /// </summary>
    /// <param name="asleep">
    /// Whether it starts asleep. Even then a monster with no sleep value in the
    /// table wakes immediately - that column is what makes some creatures
    /// permanently alert.
    /// </param>
    /// <returns>False if the monster list is full.</returns>
    public bool PlaceMonster(int row, int column, int creatureIndex, bool asleep)
    {
        int slot = _game.Monsters.Allocate();
        if (slot < 0)
        {
            return false;
        }

        CreatureType kind = GameTables.CreatureList[creatureIndex];
        Monster monster = _game.Monsters[slot];

        monster.Row = row;
        monster.Column = column;
        monster.CreatureIndex = creatureIndex;

        // Some creatures always roll maximum hit points instead of dice.
        monster.HitPoints = kind.HasDefense(CreatureDefense.MaxHitPoints)
            ? kind.HitDiceCount * kind.HitDiceSides
            : Rng.DamRoll(kind.HitDiceCount, kind.HitDiceSides);

        // The table stores speed offset by ten so it fits a byte.
        monster.Speed = kind.Speed - 10 + _game.PlayerSpeed;
        monster.Stunned = 0;
        monster.DistanceToPlayer =
            Cave.Distance(_game.CharacterRow, _game.CharacterColumn, row, column);
        monster.Visible = false;

        Cave[row, column].MonsterIndex = slot;

        monster.Sleep = asleep && kind.Sleep != 0
            ? (kind.Sleep * 2) + Rng.RandInt(kind.Sleep * 10)
            : 0;

        return true;
    }

    /// <summary>
    /// Scatters monsters across the level. Mirrors alloc_monster().
    ///
    /// Positions are drawn until one is open, empty, and at least
    /// <paramref name="minimumDistance"/> from the player - which is what stops
    /// a new level opening with something already breathing down your neck.
    /// </summary>
    /// <param name="count">How many to place.</param>
    /// <param name="minimumDistance">How far from the player they must start.</param>
    /// <param name="asleep">Whether they start asleep.</param>
    public void AllocMonster(int count, int minimumDistance, bool asleep)
    {
        for (int placed = 0; placed < count; placed++)
        {
            int row;
            int column;
            do
            {
                row = Rng.RandInt(Cave.Height - 2);
                column = Rng.RandInt(Cave.Width - 2);
            }
            while (Cave[row, column].Feature >= CaveFeature.MinClosedSpace
                || Cave[row, column].MonsterIndex != 0
                || Cave.Distance(row, column, _game.CharacterRow, _game.CharacterColumn)
                    <= minimumDistance);

            int kind = GetMonsterNumber(_game.DungeonLevel);

            // Dragons always start asleep, which the original notes is to give
            // the player a sporting chance.
            bool sleeping = asleep
                || GameTables.CreatureList[kind].DisplayChar is 'd' or 'D';

            PlaceMonster(row, column, kind, sleeping);
        }
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
