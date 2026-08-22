// AIrom's side of the oracle's fight and traps modes.
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
    /// Sets up a level with the monsters and the traps taken off it, and a
    /// rolled character standing on it.
    /// </summary>
    private static (GameState Game, Display Display, MemoryScreen Screen, GameLoop Loop)
        FightingLevel(uint seed, int level, bool rollCharacter)
    {
        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();
        game.Turn = 0;
        game.DungeonLevel = level;

        if (rollCharacter)
        {
            game.Player = new CharacterCreation(game)
                .Create(race: 0, characterClass: 0, male: true, name: "Oracle");
        }
        else
        {
            game.Player.Level = 1;
        }

        game.Player.MaxDungeonLevel = level;
        game.Player.Food = 7500;
        game.Player.FoodDigested = 2;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        var display = new Display(game, screen);

        // Generated with the screen in hand, so the panel is sized by the
        // arrival rather than by the harness afterwards.
        new DungeonGenerator(game, display).Generate();

        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                CaveSquare square = game.Cave[row, column];
                square.MonsterIndex = 0;

                if (square.ObjectIndex != 0)
                {
                    int category = game.Objects[square.ObjectIndex].TVal;
                    if (category is ItemCategory.InvisibleTrap or ItemCategory.VisibleTrap
                        or ItemCategory.StoreDoor)
                    {
                        square.ObjectIndex = 0;
                    }
                }
            }
        }

        game.Monsters.Reset();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;
        LightTheLamp(game, 400);

        screen.SendKeys(new string(' ', 3999));

        var loop = new GameLoop(game, display);

        // The rolling leaves its own labels on the screen; what is compared is
        // what the fight draws.
        display.ClearScreen();
        loop.EnterLevel();

        return (game, display, screen, loop);
    }

    /// <summary>
    /// Hitting things until they stop moving.
    ///
    /// A monster is put beside the player and attacked over and over. Every blow
    /// is three rolls - whether it lands, how hard, and whether it was good
    /// enough to count for extra - and a kill runs monster_death() as well,
    /// which rolls again for what was being carried.
    /// </summary>
    public static void DumpFight(TextWriter output, uint seed, int level, int creature, int rounds)
    {
        // Said rather than thrown: the creature table has no bounds check of
        // its own, and an index past it produces a stack trace that says
        // nothing about which argument was wrong.
        if (creature < 0 || creature >= GameTables.CreatureList.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(creature), creature,
                "creature is outside 0.."
                    + (GameTables.CreatureList.Length - 1)
                        .ToString(CultureInfo.InvariantCulture));
        }

        ArgumentNullException.ThrowIfNull(output);

        Header(output, "fight", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("creature " + creature.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("rounds " + rounds.ToString(CultureInfo.InvariantCulture) + "\n");

        (GameState game, _, MemoryScreen screen, GameLoop loop) =
            FightingLevel(seed, level, rollCharacter: true);

        // A weapon worth swinging: a long sword that slays dragons, so tot_dam
        // has something to multiply and the monster memory has something to
        // learn.
        InvenType weapon = game.Inventory[Inventory.WieldSlot];
        weapon.CopyFrom(34);
        weapon.Flags |= ItemFlags.SlayDragon;
        weapon.ToHit = 3;
        weapon.ToDam = 2;
        game.Knowledge.LearnEnchantment(weapon);
        game.Inventory.EquipWielded(weapon.Weight);
        loop.Equipment.ApplyItem(weapon, 1);
        loop.Equipment.Recalculate();

        var generator = new DungeonGenerator(game);

        for (int round = 0; round < rounds; round++)
        {
            bool placed = false;

            // Put one beside the player, in the first open square.
            for (int direction = 1; direction <= 9 && !placed; direction++)
            {
                if (direction == 5)
                {
                    continue;
                }

                int row = game.CharacterRow;
                int column = game.CharacterColumn;

                if (!game.Cave.Move(direction, ref row, ref column))
                {
                    continue;
                }

                CaveSquare square = game.Cave[row, column];

                if (square.Feature > CaveFeature.MaxOpenSpace || square.MonsterIndex != 0)
                {
                    continue;
                }

                if (generator.PlaceMonster(row, column, creature, asleep: false))
                {
                    game.Monsters[square.MonsterIndex].Visible = true;
                    loop.Combat.Attack(row, column);
                    placed = true;
                }
            }

            output.Write(string.Join(
                ' ',
                "round",
                round.ToString(CultureInfo.InvariantCulture),
                "placed",
                placed ? "1" : "0",
                "exp",
                game.Player.Experience.ToString(CultureInfo.InvariantCulture),
                "lev",
                game.Player.Level.ToString(CultureInfo.InvariantCulture),
                "mfptr",
                (game.Monsters.Count - MonsterPool.FirstIndex)
                    .ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        output.Write(string.Join(
            ' ',
            "chp",
            game.Player.CurrentHitPoints.ToString(CultureInfo.InvariantCulture),
            "mhp",
            game.Player.MaxHitPoints.ToString(CultureInfo.InvariantCulture),
            "gold",
            game.Player.Gold.ToString(CultureInfo.InvariantCulture)) + "\n");

        MonsterMemory memory = game.Memories[creature];
        output.Write(string.Join(
            ' ',
            "memory kills",
            memory.Kills.ToString(CultureInfo.InvariantCulture),
            "cmove",
            memory.Move.ToString(CultureInfo.InvariantCulture),
            "cdefense",
            ((int)memory.Defense).ToString(CultureInfo.InvariantCulture)) + "\n");

        int objectCount = game.Objects.Count - ObjectPool.FirstIndex;
        output.Write("objects " + objectCount.ToString(CultureInfo.InvariantCulture) + "\n");

        for (int i = ObjectPool.FirstIndex; i < game.Objects.Count; i++)
        {
            output.Write(
                "dropped " + i.ToString(CultureInfo.InvariantCulture)
                + " " + game.Names.Describe(game.Objects[i], withArticle: true) + "\n");
        }

        for (int row = 0; row < screen.Rows; row++)
        {
            output.Write(
                "fight " + row.ToString(CultureInfo.InvariantCulture)
                + " " + screen.GetRow(row).TrimEnd() + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>
    /// Standing on things that bite.
    ///
    /// Every trap in the table is sprung in turn, on a fresh character each
    /// time, so one that kills does not stop the rest. What is compared is the
    /// damage, the conditions it left behind, and what it did to the level
    /// around it.
    /// </summary>
    public static void DumpTraps(TextWriter output, uint seed, int level, int first, int count)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "traps", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("first " + first.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("count " + count.ToString(CultureInfo.InvariantCulture) + "\n");


        uint state = 0;

        for (int which = first; which < first + count && which < TrapCount; which++)
        {
            (GameState game, _, MemoryScreen screen, GameLoop loop) =
                FightingLevel(seed, level, rollCharacter: false);

            Player player = game.Player;
            player.CurrentHitPoints = 200;
            player.MaxHitPoints = 200;
            player.Food = 7500;

            // The trap goes under the player, which is where a sprung one always
            // is.
            int slot = game.Objects.Allocate();
            game.Objects[slot].CopyFrom(TrapListStart + which);
            game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = slot;

            loop.Traps.HitTrap(game.CharacterRow, game.CharacterColumn);

            output.Write(string.Join(
                ' ',
                "trap",
                which.ToString(CultureInfo.InvariantCulture),
                "chp",
                player.CurrentHitPoints.ToString(CultureInfo.InvariantCulture),
                "dun",
                game.DungeonLevel.ToString(CultureInfo.InvariantCulture),
                "newlevel",
                loop.NewLevel ? "1" : "0",
                "teleport",
                loop.Teleporting ? "1" : "0") + "\n");

            output.Write(string.Join(
                ' ',
                "  blind",
                player.Blind.ToString(CultureInfo.InvariantCulture),
                "confused",
                player.Confused.ToString(CultureInfo.InvariantCulture),
                "poisoned",
                player.Poisoned.ToString(CultureInfo.InvariantCulture),
                "paralysis",
                player.Paralysis.ToString(CultureInfo.InvariantCulture),
                "slow",
                player.Slowed.ToString(CultureInfo.InvariantCulture)) + "\n");

            output.Write(string.Join(
                ' ',
                "  str",
                ((int)player.CurrentStat[Stat.Strength]).ToString(CultureInfo.InvariantCulture),
                "con",
                ((int)player.CurrentStat[Stat.Constitution])
                    .ToString(CultureInfo.InvariantCulture),
                "objects",
                (game.Objects.Count - ObjectPool.FirstIndex)
                    .ToString(CultureInfo.InvariantCulture),
                "monsters",
                (game.Monsters.Count - MonsterPool.FirstIndex)
                    .ToString(CultureInfo.InvariantCulture)) + "\n");

            output.Write("  message " + screen.GetRow(0).TrimEnd() + "\n");

            state = game.Rng.State;

        }

        // Each trap gets a game of its own, so the last one's generator is the
        // one the original would be holding at the end. Unlike the item modes
        // this loop has nothing to skip: every trap in range is sprung.
        Line(output, "final-state", state);
    }

    /// <summary>How many traps there are in the table. Umoria's MAX_TRAP.</summary>
    private const int TrapCount = 18;
}
