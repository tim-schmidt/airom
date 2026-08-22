// AIrom's side of the oracle's compact mode.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Oracle;

public static partial class OracleDump
{
    /// <summary>
    /// Filling a level until it can hold no more.
    ///
    /// A level may hold 175 objects and 125 monsters. Reaching either is rare
    /// in play and certain here: this packs the level and then asks for one
    /// more, which is what sends the allocation to the compaction. What is
    /// compared is which of them survived, and the generator state afterwards -
    /// compaction rolls for every candidate, so a difference of one draw moves
    /// everything after it.
    /// </summary>
    public static void DumpCompact(TextWriter output, uint seed, int level, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "compact", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
        static string L(long value) => value.ToString(CultureInfo.InvariantCulture);

        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SetKeys(new string(' ', 599));

        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        display.MessageWaitingFlag = false;

        game.DungeonLevel = level;
        var builder = new DungeonGenerator(game, display);
        builder.Generate();

        output.Write("generated objects " + N(game.Objects.Count)
            + " monsters " + N(game.Monsters.Count)
            + " at " + N(game.CharacterRow) + " " + N(game.CharacterColumn) + "\n");

        // Pack the level with objects, walking the floor in order so that both
        // sides fill the same squares.
        int placed = 0;

        for (int row = 1; row < game.Cave.Height - 1
            && game.Objects.Count < ObjectPool.Capacity; row++)
        {
            for (int column = 1; column < game.Cave.Width - 1
                && game.Objects.Count < ObjectPool.Capacity; column++)
            {
                CaveSquare square = game.Cave[row, column];

                if (square.Feature <= CaveFeature.MaxOpenSpace
                    && square.ObjectIndex == 0 && square.MonsterIndex == 0)
                {
                    int slot = game.Objects.Allocate();
                    square.ObjectIndex = slot;
                    game.Objects[slot].CopyFrom(ObjectLevels.Sorted[(placed + variation) % 100]);
                    placed++;
                }
            }
        }

        output.Write("packed objects " + N(game.Objects.Count)
            + " placed " + N(placed) + "\n");

        // And one more, which has nowhere to go until something is thrown away.
        int before = game.Objects.Count;
        int freed = game.Objects.Allocate();

        output.Write("after-compacting objects " + N(game.Objects.Count)
            + " slot " + N(freed) + " freed " + N(before - freed) + "\n");

        // Undo the allocation so the counts below describe the level rather
        // than the harness.
        game.Objects.SetCount(game.Objects.Count - 1);

        int remaining = 0;
        long where = 0;

        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                if (game.Cave[row, column].ObjectIndex != 0)
                {
                    remaining++;
                    where += (long)(row + 1) * (column + 1);
                }
            }
        }

        output.Write("objects-left " + N(remaining) + " where " + L(where) + "\n");
        Line(output, "state-after-objects", game.Rng.State);

        // Now the monsters. Placing them one at a time from the creature table
        // keeps both sides drawing the same numbers.
        placed = 0;

        for (int row = 1; row < game.Cave.Height - 1
            && game.Monsters.Count < MonsterPool.Capacity; row++)
        {
            for (int column = 1; column < game.Cave.Width - 1
                && game.Monsters.Count < MonsterPool.Capacity; column++)
            {
                CaveSquare square = game.Cave[row, column];

                if (square.Feature <= CaveFeature.MaxOpenSpace
                    && square.MonsterIndex == 0
                    && Cave.Distance(row, column, game.CharacterRow, game.CharacterColumn) > 2
                    && builder.PlaceMonster(
                        row, column,
                        (placed + variation) % (GameTables.CreatureList.Length - 30),
                        asleep: false))
                {
                    placed++;
                }
            }
        }

        output.Write("packed monsters " + N(game.Monsters.Count)
            + " placed " + N(placed) + "\n");

        before = game.Monsters.Count;
        freed = game.Monsters.Allocate();

        output.Write("after-compacting monsters " + N(game.Monsters.Count)
            + " slot " + N(freed)
            + " freed " + N(freed < 0 ? -1 : before - freed) + "\n");

        if (freed >= 0)
        {
            game.Monsters.SetCount(game.Monsters.Count - 1);
        }

        remaining = 0;
        where = 0;

        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            remaining++;
            where += (long)(i + 1) * (game.Monsters[i].CreatureIndex + 1);
        }

        output.Write("monsters-left " + N(remaining) + " where " + L(where) + "\n");

        for (int i = MonsterPool.FirstIndex;
            i < game.Monsters.Count && i < MonsterPool.FirstIndex + 40; i++)
        {
            Monster monster = game.Monsters[i];

            output.Write("monster " + N(i) + " index " + N(monster.CreatureIndex)
                + " at " + N(monster.Row) + " " + N(monster.Column)
                + " distance " + N(monster.DistanceToPlayer)
                + " hp " + N(monster.HitPoints) + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }
}
