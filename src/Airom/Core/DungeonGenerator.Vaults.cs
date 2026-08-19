// Ported from the unusual room builders in Umoria 5.6 source/generate.c,
// summon_monster() in source/misc1.c, and random_object() in source/misc3.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The unusual rooms: the ones with something walled off inside, and the
/// cross-shaped ones. Both appear only when the depth beats a draw against
/// DUN_UNUSUAL, so they are rare near the surface and routine far down.
/// </summary>
public sealed partial class DungeonGenerator
{
    /// <summary>
    /// Spawns a monster beside a point. Mirrors summon_monster().
    ///
    /// It tries up to ten neighbouring squares and gives up if none is free.
    /// The creature is drawn two levels deeper than the floor it appears on, so
    /// summoning is how a level punches above its weight.
    /// </summary>
    /// <param name="row">Centre to summon beside; set to where it landed.</param>
    /// <param name="column">As <paramref name="row"/>.</param>
    /// <param name="asleep">Whether it starts asleep.</param>
    /// <returns>Whether anything was placed.</returns>
    public bool SummonMonster(ref int row, ref int column, bool asleep)
    {
        int kind = GetMonsterNumber(_game.DungeonLevel + SummonLevelAdjust);
        bool summoned = false;
        int attempt = 0;

        do
        {
            int y = row - 2 + Rng.RandInt(3);
            int x = column - 2 + Rng.RandInt(3);

            if (Cave.InBounds(y, x)
                && Cave[y, x].Feature <= CaveFeature.MaxOpenSpace
                && Cave[y, x].MonsterIndex == 0)
            {
                if (!PlaceMonster(y, x, kind, asleep))
                {
                    return false;
                }

                summoned = true;
                attempt = 9; // stop after this pass
                row = y;
                column = x;
            }

            attempt++;
        }
        while (attempt <= 9);

        return summoned;
    }

    /// <summary>
    /// Scatters traps within a box around a point. Mirrors vault_trap().
    ///
    /// Each trap gets five attempts to find clear floor and is dropped if it
    /// cannot, so a cramped vault ends up with fewer traps rather than the
    /// generator spinning.
    /// </summary>
    public void VaultTrap(int row, int column, int rowSpread, int columnSpread, int count)
    {
        for (int i = 0; i < count; i++)
        {
            bool placed = false;
            int attempt = 0;

            do
            {
                int y = row - rowSpread - 1 + Rng.RandInt((2 * rowSpread) + 1);
                int x = column - columnSpread - 1 + Rng.RandInt((2 * columnSpread) + 1);

                CaveSquare square = Cave[y, x];
                if (square.Feature != CaveFeature.NullWall
                    && square.Feature <= CaveFeature.MaxCaveFloor
                    && square.ObjectIndex == 0)
                {
                    PlaceRandomTrap(y, x);
                    placed = true;
                }

                attempt++;
            }
            while (!placed && attempt <= 5);
        }
    }

    /// <summary>Summons several monsters around a point. Mirrors vault_monster().</summary>
    public void VaultMonster(int row, int column, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int y = row;
            int x = column;
            SummonMonster(ref y, ref x, asleep: true);
        }
    }

    /// <summary>
    /// Scatters treasure around a point. Mirrors random_object() in misc3.c.
    ///
    /// Three quarters of the time it drops an item, otherwise gold. The
    /// original's inner loop makes one further attempt after each success, so it
    /// can place more than asked when the surrounding squares are free. That is
    /// reproduced rather than corrected - it is part of how a vault fills up.
    /// </summary>
    public void RandomObject(int row, int column, int count)
    {
        do
        {
            int attempt = 0;
            do
            {
                int y = row - 3 + Rng.RandInt(5);
                int x = column - 4 + Rng.RandInt(7);

                if (Cave.InBounds(y, x)
                    && Cave[y, x].Feature <= CaveFeature.MaxCaveFloor
                    && Cave[y, x].ObjectIndex == 0)
                {
                    if (Rng.RandInt(100) < 75)
                    {
                        PlaceObject(y, x, mustBeSmall: false);
                    }
                    else
                    {
                        PlaceGold(y, x);
                    }

                    attempt = 9;
                }

                attempt++;
            }
            while (attempt <= 10);

            count--;
        }
        while (count != 0);
    }

    /// <summary>Places a secret door on one of the inner room's four walls.</summary>
    private void InnerRoomDoor(
        int top, int bottom, int left, int right, int centreRow, int centreColumn)
    {
        int side = Rng.RandInt(4);
        if (side < 3)
        {
            PlaceSecretDoor(side == 1 ? top - 1 : bottom + 1, centreColumn);
        }
        else
        {
            PlaceSecretDoor(centreRow, side == 3 ? left - 1 : right + 1);
        }
    }

    private void FillBlock(int top, int bottom, int left, int right)
    {
        for (int row = top; row <= bottom; row++)
        {
            for (int column = left; column <= right; column++)
            {
                Cave[row, column].Feature = CaveFeature.Temp1Wall;
            }
        }
    }

    /// <summary>
    /// Builds a room with something walled off inside it. Mirrors build_type2().
    ///
    /// The outer room is a fixed 9 by 23 with a second wall two squares in, and
    /// what fills that inner space is one of five things: an empty cell, a
    /// treasure vault, pillars, a maze, or four small rooms. All five are
    /// guarded, which is what makes an inner room worth breaking into.
    ///
    /// The inner walls are laid as scratch marks rather than granite. fill_cave
    /// turns them to rock later, so they can be drawn over freely while the
    /// variation is built.
    /// </summary>
    public void BuildInnerRoom(int centreRow, int centreColumn)
    {
        byte floor = RoomFloor();

        int top = centreRow - 4;
        int bottom = centreRow + 4;
        int left = centreColumn - 11;
        int right = centreColumn + 11;

        for (int row = top; row <= bottom; row++)
        {
            for (int column = left; column <= right; column++)
            {
                Lay(row, column, floor);
            }
        }

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

        // The inner wall, two squares in from the outer one.
        top += 2;
        bottom -= 2;
        left += 2;
        right -= 2;

        for (int row = top - 1; row <= bottom + 1; row++)
        {
            Cave[row, left - 1].Feature = CaveFeature.Temp1Wall;
            Cave[row, right + 1].Feature = CaveFeature.Temp1Wall;
        }

        for (int column = left; column <= right; column++)
        {
            Cave[top - 1, column].Feature = CaveFeature.Temp1Wall;
            Cave[bottom + 1, column].Feature = CaveFeature.Temp1Wall;
        }

        switch (Rng.RandInt(5))
        {
            case 1: // an empty cell with one occupant
                InnerRoomDoor(top, bottom, left, right, centreRow, centreColumn);
                VaultMonster(centreRow, centreColumn, 1);
                break;

            case 2:
                BuildTreasureVault(top, bottom, left, right, centreRow, centreColumn);
                break;

            case 3:
                BuildPillaredRoom(top, bottom, left, right, centreRow, centreColumn);
                break;

            case 4:
                BuildMazeRoom(top, bottom, left, right, centreRow, centreColumn);
                break;

            default: // 5: four small rooms
                BuildQuadrantRooms(top, bottom, left, right, centreRow, centreColumn);
                break;
        }
    }

    private void BuildTreasureVault(
        int top, int bottom, int left, int right, int centreRow, int centreColumn)
    {
        InnerRoomDoor(top, bottom, left, right, centreRow, centreColumn);

        // A one-square cell right in the middle.
        for (int row = centreRow - 1; row <= centreRow + 1; row++)
        {
            Cave[row, centreColumn - 1].Feature = CaveFeature.Temp1Wall;
            Cave[row, centreColumn + 1].Feature = CaveFeature.Temp1Wall;
        }

        Cave[centreRow - 1, centreColumn].Feature = CaveFeature.Temp1Wall;
        Cave[centreRow + 1, centreColumn].Feature = CaveFeature.Temp1Wall;

        // A locked door on one of the cell's four sides.
        int side = Rng.RandInt(4);
        if (side < 3)
        {
            PlaceLockedDoor(centreRow - 3 + (side << 1), centreColumn);
        }
        else
        {
            PlaceLockedDoor(centreRow, centreColumn - 7 + (side << 1));
        }

        // Usually treasure, occasionally a staircase instead.
        int prize = Rng.RandInt(10);
        if (prize > 2)
        {
            PlaceObject(centreRow, centreColumn, mustBeSmall: false);
        }
        else if (prize == 2)
        {
            PlaceDownStairs(centreRow, centreColumn);
        }
        else
        {
            PlaceUpStairs(centreRow, centreColumn);
        }

        VaultMonster(centreRow, centreColumn, 2 + Rng.RandInt(3));
        VaultTrap(centreRow, centreColumn, 4, 10, 2 + Rng.RandInt(3));
    }

    private void BuildPillaredRoom(
        int top, int bottom, int left, int right, int centreRow, int centreColumn)
    {
        InnerRoomDoor(top, bottom, left, right, centreRow, centreColumn);

        FillBlock(centreRow - 1, centreRow + 1, centreColumn - 1, centreColumn + 1);

        if (Rng.RandInt(2) == 1)
        {
            int offset = Rng.RandInt(2);
            FillBlock(
                centreRow - 1, centreRow + 1, centreColumn - 5 - offset, centreColumn - 3 - offset);
            FillBlock(
                centreRow - 1, centreRow + 1, centreColumn + 3 + offset, centreColumn + 5 + offset);
        }

        if (Rng.RandInt(3) != 1)
        {
            return;
        }

        // A pair of cells tucked between the pillars.
        for (int column = centreColumn - 5; column <= centreColumn + 5; column++)
        {
            Cave[centreRow - 1, column].Feature = CaveFeature.Temp1Wall;
            Cave[centreRow + 1, column].Feature = CaveFeature.Temp1Wall;
        }

        Cave[centreRow, centreColumn - 5].Feature = CaveFeature.Temp1Wall;
        Cave[centreRow, centreColumn + 5].Feature = CaveFeature.Temp1Wall;

        PlaceSecretDoor(centreRow - 3 + (Rng.RandInt(2) << 1), centreColumn - 3);
        PlaceSecretDoor(centreRow - 3 + (Rng.RandInt(2) << 1), centreColumn + 3);

        if (Rng.RandInt(3) == 1)
        {
            PlaceObject(centreRow, centreColumn - 2, mustBeSmall: false);
        }

        if (Rng.RandInt(3) == 1)
        {
            PlaceObject(centreRow, centreColumn + 2, mustBeSmall: false);
        }

        VaultMonster(centreRow, centreColumn - 2, Rng.RandInt(2));
        VaultMonster(centreRow, centreColumn + 2, Rng.RandInt(2));
    }

    private void BuildMazeRoom(
        int top, int bottom, int left, int right, int centreRow, int centreColumn)
    {
        InnerRoomDoor(top, bottom, left, right, centreRow, centreColumn);

        // A checkerboard, which is the whole maze.
        for (int row = top; row <= bottom; row++)
        {
            for (int column = left; column <= right; column++)
            {
                if ((1 & (column + row)) != 0)
                {
                    Cave[row, column].Feature = CaveFeature.Temp1Wall;
                }
            }
        }

        VaultMonster(centreRow, centreColumn - 5, Rng.RandInt(3));
        VaultMonster(centreRow, centreColumn + 5, Rng.RandInt(3));
        VaultTrap(centreRow, centreColumn - 3, 2, 8, Rng.RandInt(3));
        VaultTrap(centreRow, centreColumn + 3, 2, 8, Rng.RandInt(3));

        for (int i = 0; i < 3; i++)
        {
            RandomObject(centreRow, centreColumn, 1);
        }
    }

    private void BuildQuadrantRooms(
        int top, int bottom, int left, int right, int centreRow, int centreColumn)
    {
        // A cross of wall, splitting the inner space into four.
        for (int row = top; row <= bottom; row++)
        {
            Cave[row, centreColumn].Feature = CaveFeature.Temp1Wall;
        }

        for (int column = left; column <= right; column++)
        {
            Cave[centreRow, column].Feature = CaveFeature.Temp1Wall;
        }

        // Four doors, all on the top and bottom walls or all on the sides.
        if (Rng.RandInt(2) == 1)
        {
            int offset = Rng.RandInt(10);
            PlaceSecretDoor(top - 1, centreColumn - offset);
            PlaceSecretDoor(top - 1, centreColumn + offset);
            PlaceSecretDoor(bottom + 1, centreColumn - offset);
            PlaceSecretDoor(bottom + 1, centreColumn + offset);
        }
        else
        {
            int offset = Rng.RandInt(3);
            PlaceSecretDoor(centreRow + offset, left - 1);
            PlaceSecretDoor(centreRow - offset, left - 1);
            PlaceSecretDoor(centreRow + offset, right + 1);
            PlaceSecretDoor(centreRow - offset, right + 1);
        }

        RandomObject(centreRow, centreColumn, 2 + Rng.RandInt(2));

        VaultMonster(centreRow + 2, centreColumn - 4, Rng.RandInt(2));
        VaultMonster(centreRow + 2, centreColumn + 4, Rng.RandInt(2));
        VaultMonster(centreRow - 2, centreColumn - 4, Rng.RandInt(2));
        VaultMonster(centreRow - 2, centreColumn + 4, Rng.RandInt(2));
    }

    /// <summary>
    /// Builds a cross-shaped room. Mirrors build_type3().
    ///
    /// Two overlapping corridors, one tall and narrow, one long and wide. The
    /// second lays wall only where there is no floor, which is what joins the
    /// arms into a cross rather than bricking the junction.
    /// </summary>
    public void BuildCrossRoom(int centreRow, int centreColumn)
    {
        byte floor = RoomFloor();

        // The vertical arm.
        int reach = 2 + Rng.RandInt(2);
        int top = centreRow - reach;
        int bottom = centreRow + reach;
        int left = centreColumn - 1;
        int right = centreColumn + 1;

        for (int row = top; row <= bottom; row++)
        {
            for (int column = left; column <= right; column++)
            {
                Lay(row, column, floor);
            }
        }

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

        // The horizontal arm, which must not wall off the vertical one.
        reach = 2 + Rng.RandInt(9);
        top = centreRow - 1;
        bottom = centreRow + 1;
        left = centreColumn - reach;
        right = centreColumn + reach;

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

        switch (Rng.RandInt(4))
        {
            case 1: // a solid block in the middle
                FillBlock(centreRow - 1, centreRow + 1, centreColumn - 1, centreColumn + 1);
                break;

            case 2:
                BuildCrossVault(centreRow, centreColumn);
                break;

            case 3:
                BuildCrossOrnament(centreRow, centreColumn);
                break;

            default: // 4: nothing at all
                break;
        }
    }

    private void BuildCrossVault(int centreRow, int centreColumn)
    {
        for (int row = centreRow - 1; row <= centreRow + 1; row++)
        {
            Cave[row, centreColumn - 1].Feature = CaveFeature.Temp1Wall;
            Cave[row, centreColumn + 1].Feature = CaveFeature.Temp1Wall;
        }

        Cave[centreRow - 1, centreColumn].Feature = CaveFeature.Temp1Wall;
        Cave[centreRow + 1, centreColumn].Feature = CaveFeature.Temp1Wall;

        int side = Rng.RandInt(4);
        if (side < 3)
        {
            PlaceSecretDoor(centreRow - 3 + (side << 1), centreColumn);
        }
        else
        {
            PlaceSecretDoor(centreRow, centreColumn - 7 + (side << 1));
        }

        PlaceObject(centreRow, centreColumn, mustBeSmall: false);
        VaultMonster(centreRow, centreColumn, 2 + Rng.RandInt(2));
        VaultTrap(centreRow, centreColumn, 4, 4, 1 + Rng.RandInt(3));
    }

    /// <summary>
    /// The decorative variations: a ring of corner blocks, a plus sign, or a
    /// single square. Each is gated behind its own one-in-three roll, so most of
    /// the time this leaves the room plain.
    /// </summary>
    private void BuildCrossOrnament(int centreRow, int centreColumn)
    {
        if (Rng.RandInt(3) == 1)
        {
            Cave[centreRow - 1, centreColumn - 2].Feature = CaveFeature.Temp1Wall;
            Cave[centreRow + 1, centreColumn - 2].Feature = CaveFeature.Temp1Wall;
            Cave[centreRow - 1, centreColumn + 2].Feature = CaveFeature.Temp1Wall;
            Cave[centreRow + 1, centreColumn + 2].Feature = CaveFeature.Temp1Wall;
            Cave[centreRow - 2, centreColumn - 1].Feature = CaveFeature.Temp1Wall;
            Cave[centreRow - 2, centreColumn + 1].Feature = CaveFeature.Temp1Wall;
            Cave[centreRow + 2, centreColumn - 1].Feature = CaveFeature.Temp1Wall;
            Cave[centreRow + 2, centreColumn + 1].Feature = CaveFeature.Temp1Wall;

            if (Rng.RandInt(3) == 1)
            {
                PlaceSecretDoor(centreRow, centreColumn - 2);
                PlaceSecretDoor(centreRow, centreColumn + 2);
                PlaceSecretDoor(centreRow - 2, centreColumn);
                PlaceSecretDoor(centreRow + 2, centreColumn);
            }
        }
        else if (Rng.RandInt(3) == 1)
        {
            Cave[centreRow, centreColumn].Feature = CaveFeature.Temp1Wall;
            Cave[centreRow - 1, centreColumn].Feature = CaveFeature.Temp1Wall;
            Cave[centreRow + 1, centreColumn].Feature = CaveFeature.Temp1Wall;
            Cave[centreRow, centreColumn - 1].Feature = CaveFeature.Temp1Wall;
            Cave[centreRow, centreColumn + 1].Feature = CaveFeature.Temp1Wall;
        }
        else if (Rng.RandInt(3) == 1)
        {
            Cave[centreRow, centreColumn].Feature = CaveFeature.Temp1Wall;
        }
    }
}
