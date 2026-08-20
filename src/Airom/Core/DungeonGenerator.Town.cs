// Ported from place_win_monster() in Umoria 5.6 source/misc1.c, and
// build_store()/town_gen() in source/generate.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

public sealed partial class DungeonGenerator
{
    private const int StoreDoorObject = 372; // OBJ_STORE_DOOR
    private const int MaxSight = 20;         // MAX_SIGHT
    private const int TownMonstersDay = 4;   // MIN_MALLOC_TD
    private const int TownMonstersNight = 8; // MIN_MALLOC_TN

    /// <summary>Umoria's WIN_MON_APPEAR: the depth win monsters start at.</summary>
    public const int WinMonsterDepth = 50;

    /// <summary>Rows and columns of the town, which is one screen exactly.</summary>
    public const int TownHeight = 22;

    /// <inheritdoc cref="TownHeight"/>
    public const int TownWidth = 66;

    /// <summary>
    /// Places one of the two game-winning monsters. Mirrors place_win_monster().
    ///
    /// It is dropped somewhere out of sight rather than near the player, and
    /// always awake - unlike everything else on the level, which arrives asleep.
    /// Nothing happens once the player has already won.
    ///
    /// Note it also skips the visibility field that place_monster clears. The
    /// list is blanked per level, so the effect is the same; the asymmetry is
    /// the original's, and is left alone.
    /// </summary>
    public void PlaceWinMonster()
    {
        if (_game.TotalWinner)
        {
            return;
        }

        int slot = _game.Monsters.Allocate();

        int row;
        int column;
        do
        {
            row = Rng.RandInt(Cave.Height - 2);
            column = Rng.RandInt(Cave.Width - 2);
        }
        while (Cave[row, column].Feature >= CaveFeature.MinClosedSpace
            || Cave[row, column].MonsterIndex != 0
            || Cave[row, column].ObjectIndex != 0
            || Cave.Distance(row, column, _game.CharacterRow, _game.CharacterColumn) <= MaxSight);

        Monster monster = _game.Monsters[slot];
        monster.Row = row;
        monster.Column = column;

        // The win monsters are the last rows of the creature table, which is
        // exactly why the draw index stops short of them.
        monster.CreatureIndex = Rng.RandInt(MonsterLevels.WinMonsterCount) - 1
            + MonsterLevels.LevelTotals[MonsterLevels.MaxMonsterLevel];

        CreatureType kind = GameTables.CreatureList[monster.CreatureIndex];
        monster.HitPoints = kind.HasDefense(CreatureDefense.MaxHitPoints)
            ? kind.HitDiceCount * kind.HitDiceSides
            : Rng.DamRoll(kind.HitDiceCount, kind.HitDiceSides);

        monster.Speed = kind.Speed - 10 + _game.PlayerSpeed;
        monster.Stunned = 0;
        monster.DistanceToPlayer =
            Cave.Distance(_game.CharacterRow, _game.CharacterColumn, row, column);

        Cave[row, column].MonsterIndex = slot;
        monster.Sleep = 0;
    }

    /// <summary>
    /// Builds one store: a solid block of permanent wall with a single doorway.
    /// Mirrors build_store().
    ///
    /// The building is deliberately impenetrable - the walls are boundary rock,
    /// the same as the edge of the map - so the only way in is the door.
    /// </summary>
    /// <param name="storeNumber">Which store, which decides the door object.</param>
    /// <param name="blockRow">Row of the store's slot in the town's two-by-three grid.</param>
    /// <param name="blockColumn">Column of that slot.</param>
    public void BuildStore(int storeNumber, int blockRow, int blockColumn)
    {
        int centreRow = (blockRow * 10) + 5;
        int centreColumn = (blockColumn * 16) + 16;

        int top = centreRow - Rng.RandInt(3);
        int bottom = centreRow + Rng.RandInt(4);
        int left = centreColumn - Rng.RandInt(6);
        int right = centreColumn + Rng.RandInt(6);

        for (int row = top; row <= bottom; row++)
        {
            for (int column = left; column <= right; column++)
            {
                Cave[row, column].Feature = CaveFeature.BoundaryWall;
            }
        }

        // The doorway goes on one of the four walls, never a corner.
        int side = Rng.RandInt(4);
        int doorRow;
        int doorColumn;

        if (side < 3)
        {
            doorRow = Rng.RandInt(bottom - top) + top - 1;
            doorColumn = side == 1 ? left : right;
        }
        else
        {
            doorColumn = Rng.RandInt(right - left) + left - 1;
            doorRow = side == 3 ? bottom : top;
        }

        Cave[doorRow, doorColumn].Feature = CaveFeature.CorridorFloor;

        int slot = _game.Objects.Allocate();
        Cave[doorRow, doorColumn].ObjectIndex = slot;
        _game.Objects[slot].CopyFrom(StoreDoorObject + storeNumber);
    }

    /// <summary>
    /// Lays out the town. Mirrors town_gen(), less the store restocking.
    ///
    /// The whole layout is generated from the town seed rather than the running
    /// one, which is what makes the town the same place every time the player
    /// climbs out of the dungeon. The stairs are placed inside that bracket too,
    /// with the original noting why: so they do not move around.
    ///
    /// Day and night differ in more than lighting. By day the whole map is lit
    /// and few people are about; by night only the buildings are lit and twice
    /// as many are, which is what makes the town worth leaving before dark.
    ///
    /// store_maint() is not ported yet, so shop inventories are not restocked.
    /// Everything up to that point is complete.
    /// </summary>
    public void GenerateTown()
    {
        Rng.PushSeed(_game.TownSeed);

        // Six stores in a two-by-three grid, assigned to slots at random by
        // drawing from a shrinking list so no store is placed twice.
        List<int> remaining = [0, 1, 2, 3, 4, 5];
        for (int blockRow = 0; blockRow < 2; blockRow++)
        {
            for (int blockColumn = 0; blockColumn < 3; blockColumn++)
            {
                int pick = Rng.RandInt(remaining.Count) - 1;
                BuildStore(remaining[pick], blockRow, blockColumn);
                remaining.RemoveAt(pick);
            }
        }

        FillCave(CaveFeature.DarkFloor);
        PlaceBoundary();
        PlaceStairs(2, 1, 0);

        Rng.PopSeed();

        (int charRow, int charColumn) = NewSpot();
        _game.CharacterRow = charRow;
        _game.CharacterColumn = charColumn;

        // The clock runs in blocks of 5000 turns, alternating day and night.
        bool night = (1 & (_game.Turn / 5000)) != 0;

        for (int row = 0; row < Cave.Height; row++)
        {
            for (int column = 0; column < Cave.Width; column++)
            {
                // At night only the buildings stay lit; by day everything does.
                if (!night || Cave[row, column].Feature != CaveFeature.DarkFloor)
                {
                    Cave[row, column].PermanentLight = true;
                }
            }
        }

        AllocMonster(
            night ? TownMonstersNight : TownMonstersDay,
            minimumDistance: 3,
            asleep: true);
    }
}
