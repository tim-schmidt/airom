// AIrom's side of the oracle's moria4 and look modes.
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
    /// <summary>How many times each action is repeated.</summary>
    private const int Moria4Rounds = 24;

    /// <summary>
    /// Digging, disarming, bashing and throwing.
    ///
    /// None of these can be compared on a level as it was generated: whether
    /// there is rubble next to the player is a matter of luck, and a run that
    /// finds none would prove nothing. So the eight squares around the player
    /// are set to a known arrangement first - a wall of each kind, rubble, a
    /// secret door, a locked door, a chest and a trap - and each variation then
    /// aims its action at whichever of them it is about.
    /// </summary>
    public static void DumpMoria4(TextWriter output, uint seed, int level, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "moria4", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();

        game.InitSeeds(seed);
        game.Turn = 0;
        game.DungeonLevel = level;
        game.Player = new Player { MaxDungeonLevel = level };
        game.Knowledge.Reset();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        var display = new Display(game, screen);

        new DungeonGenerator(game).Generate();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);

        var loop = new GameLoop(game, display);
        Player player = game.Player;

        player.Level = 20;
        player.ExperienceFactor = 100;
        player.HitDie = 10;
        player.Save = 40;
        player.Weight = 150;
        player.Food = 5000;

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 18;
            player.CurrentStat[i] = 18;
            player.ModStat[i] = 0;
            loop.Stats.SetUseStat(i);
        }

        Moria4Wield(game, variation);
        loop.Equipment.Recalculate();

        player.MaxHitPoints = 2000;
        player.CurrentHitPoints = 2000;

        Moria4Pack(game);
        Moria4Surround(game);

        if (variation is 7 or 8 or 9)
        {
            Moria4Target(game, adjacent: variation == 9);
        }

        game.PlayerLight = true;
        display.Panel.Invalidate();
        loop.Lighting.CheckView();

        for (int round = 0; round < Moria4Rounds; round++)
        {
            // Cleared each round, so that a message left over from the last one
            // does not turn this one's prompt into a -more- that eats a key.
            display.MessageWaitingFlag = false;
            loop.FreeTurn = false;
            loop.NewLevel = false;
            display.CommandCount = 0;

            RunMoria4Round(loop, screen, variation, round);

            string N(int value) => value.ToString(CultureInfo.InvariantCulture);

            output.Write(string.Join(
                ' ', "round", N(round),
                "free", loop.FreeTurn ? "1" : "0",
                "chp", N(player.CurrentHitPoints),
                "exp", N(player.Experience),
                "at", N(game.CharacterRow), N(game.CharacterColumn)) + "\n");

            output.Write(string.Join(
                ' ',
                "  str", N(player.CurrentStat[Stat.Strength]),
                "con", N(player.CurrentStat[Stat.Constitution]),
                "dex", N(player.CurrentStat[Stat.Dexterity]),
                "para", N(player.Paralysis),
                "conf", N(player.Confused)) + "\n");

            int monsterHitPoints = 0;

            for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
            {
                monsterHitPoints += game.Monsters[i].HitPoints;
            }

            output.Write(string.Join(
                ' ',
                "  packed", N(game.Inventory.Count),
                "weight", N(game.Inventory.Weight),
                "objects", N(game.Objects.Count - ObjectPool.FirstIndex),
                "monsters", N(game.Monsters.Count - MonsterPool.FirstIndex),
                "monhp", N(monsterHitPoints)) + "\n");

            for (int i = 0; i < 9; i++)
            {
                int row = game.CharacterRow + (i / 3) - 1;
                int column = game.CharacterColumn + (i % 3) - 1;

                if (!game.Cave.InBounds(row, column))
                {
                    continue;
                }

                CaveSquare square = game.Cave[row, column];
                InvenType? lying = square.ObjectIndex != 0
                    ? game.Objects[square.ObjectIndex]
                    : null;

                output.Write(string.Join(
                    ' ',
                    "  around", N(i),
                    "fval", N(square.Feature),
                    "tptr", N(square.ObjectIndex),
                    "tval", N(lying?.TVal ?? 0),
                    "p1", N(lying?.P1 ?? 0),
                    "flags", (lying?.Flags ?? 0u).ToString(CultureInfo.InvariantCulture))
                    + "\n");
            }

            output.Write("  message " + screen.GetRow(0).TrimEnd() + "\n");
            output.Write("  state "
                + game.Rng.State.ToString(CultureInfo.InvariantCulture) + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>One round of whichever action the variation is about.</summary>
    private static void RunMoria4Round(
        GameLoop loop, MemoryScreen screen, int variation, int round)
    {
        switch (variation)
        {
            case 0:
            case 1:
            case 2:
                // Dig at each of the sides and corners that hold something.
                Moria4Keys(screen, string.Empty);
                loop.Tunnelling.Tunnel("8264793"[round % 7] - '0');
                break;

            case 3:
                // The trap in the floor is to the south west.
                Moria4Keys(screen, "1");
                loop.Traps.DisarmTrap();
                break;

            case 4:
                // The chest is to the south east.
                Moria4Keys(screen, "3");
                loop.Traps.DisarmTrap();
                break;

            case 5:
                // The door is to the north west.
                Moria4Keys(screen, "7");
                loop.Doors.Bash();
                break;

            case 6:
                Moria4Keys(screen, "3");   // the chest
                loop.Doors.Bash();
                break;

            case 7:
            case 8:
                Moria4Keys(screen, "a6");
                loop.Throwing.ThrowObject();
                break;

            case 9:
                // Something alive, one square east.
                Moria4Keys(screen, "6");
                loop.Doors.Bash();
                break;

            default:
                Moria4Keys(screen, string.Empty);
                break;
        }
    }

    /// <summary>
    /// The keys one round needs, padded with escapes. Set fresh each round so
    /// that nothing one action leaves behind is read by the next.
    /// </summary>
    private static void Moria4Keys(MemoryScreen screen, string keys) =>
        screen.SetKeys(keys + new string((char)27, 599 - keys.Length));

    /// <summary>What goes on each of the eight squares around the player.</summary>
    private static void Moria4Surround(GameState game)
    {
        int row = game.CharacterRow;
        int column = game.CharacterColumn;

        // Three kinds of rock to dig, and rubble.
        game.Cave[row - 1, column].Feature = CaveFeature.GraniteWall;
        game.Cave[row + 1, column].Feature = CaveFeature.MagmaWall;
        game.Cave[row, column + 1].Feature = CaveFeature.QuartzWall;

        game.Cave[row, column - 1].Feature = CaveFeature.CorridorFloor;
        PlaceFixed(game, row, column - 1, RubbleObject);

        // A secret door, which digs like the granite it is hiding in.
        game.Cave[row - 1, column + 1].Feature = CaveFeature.GraniteWall;
        PlaceFixed(game, row - 1, column + 1, SecretDoorObject);

        // A door to bash, locked hard enough to take a few tries.
        game.Cave[row - 1, column - 1].Feature = CaveFeature.CorridorFloor;
        InvenType door = PlaceFixed(game, row - 1, column - 1, ClosedDoorObject);
        door.P1 = 8;

        // A chest, locked and trapped, to disarm or to smash.
        game.Cave[row + 1, column + 1].Feature = CaveFeature.CorridorFloor;
        InvenType chest = PlaceFixed(game, row + 1, column + 1, SmallChestObject);
        chest.Flags = ChestFlags.Locked | ChestFlags.LoseStrength | ChestFlags.Poison;
        chest.Level = 10;

        // Known, since a chest's trap has to be seen before it can be worked on.
        game.Knowledge.LearnEnchantment(chest);

        // And a trap in the floor, found - as a trap has to be before it can be
        // disarmed.
        game.Cave[row + 1, column - 1].Feature = CaveFeature.CorridorFloor;
        InvenType trap = PlaceFixed(game, row + 1, column - 1, TrapListObject + 1);
        trap.TVal = ItemCategory.VisibleTrap;
        trap.DisplayChar = '^';

        // Nothing is standing on any of them.
        for (int i = 0; i < 9; i++)
        {
            if (i != 4)
            {
                game.Cave[row + (i / 3) - 1, column + (i % 3) - 1].MonsterIndex = 0;
            }
        }
    }

    /// <summary>
    /// A lane to the east with something in it, so that a thrown thing has
    /// somewhere to fly and something to hit.
    /// </summary>
    private static void Moria4Target(GameState game, bool adjacent)
    {
        int row = game.CharacterRow;
        int column = game.CharacterColumn;

        for (int j = 1; j <= 8; j++)
        {
            if (!game.Cave.InBounds(row, column + j))
            {
                continue;
            }

            game.Cave[row, column + j].Feature = CaveFeature.CorridorFloor;
            game.Cave[row, column + j].ObjectIndex = 0;
            game.Cave[row, column + j].MonsterIndex = 0;
        }

        int distance = adjacent ? 1 : 5;

        if (game.Cave.InBounds(row, column + distance))
        {
            new DungeonGenerator(game)
                .PlaceMonster(row, column + distance, 20, false);
        }
    }

    private static InvenType PlaceFixed(GameState game, int row, int column, int which)
    {
        int index = game.Objects.Allocate();
        game.Objects[index].CopyFrom(which);
        game.Cave[row, column].ObjectIndex = index;
        return game.Objects[index];
    }

    /// <summary>
    /// What the player is holding, which decides how well they dig and throw.
    /// </summary>
    private static void Moria4Wield(GameState game, int variation)
    {
        game.Inventory.Reset();

        game.Inventory[Inventory.BodySlot].CopyFrom(103);   // soft leather armor
        game.Inventory[Inventory.ArmSlot].CopyFrom(111);    // a shield, which a bash hits with
        game.Inventory[Inventory.LightSlot].CopyFrom(365);  // a wooden torch
        game.Inventory[Inventory.LightSlot].P1 = 5000;
        game.Inventory.EquipmentCount = 3;

        if (variation == 1)
        {
            // A shovel, whose digging plus is worth far more than any weapon.
            game.Inventory[Inventory.WieldSlot]
                .CopyFrom(FirstOfCategory(ItemCategory.Digging));
            game.Inventory[Inventory.WieldSlot].P1 = 2;
            game.Inventory.EquipmentCount++;
        }
        else if (variation == 2)
        {
            // Bare hands, which dig nothing at all.
        }
        else if (variation == 8)
        {
            // A bow, so that the arrows are fired rather than thrown.
            game.Inventory[Inventory.WieldSlot]
                .CopyFrom(FirstOfCategory(ItemCategory.Bow));

            // A short bow, which is what an arrow is made for.
            game.Inventory[Inventory.WieldSlot].P1 = 2;
            game.Inventory.EquipmentCount++;
        }
        else
        {
            game.Inventory[Inventory.WieldSlot].CopyFrom(30);   // a stiletto
            game.Inventory.EquipmentCount++;
        }
    }

    /// <summary>A pack of things to throw: one of each of a few kinds, and a quiver.</summary>
    private static void Moria4Pack(GameState game)
    {
        int[] kinds =
        [
            ItemCategory.Arrow, ItemCategory.Flask, ItemCategory.Potion1,
            ItemCategory.Food, ItemCategory.Hafted,
        ];

        foreach (int kind in kinds)
        {
            var held = new InvenType();
            held.CopyFrom(FirstOfCategory(kind));

            if (kind == ItemCategory.Arrow)
            {
                held.Number = 20;
            }

            game.Inventory.Carry(held);
        }
    }

    private const int TrapListObject = 378;
    private const int RubbleObject = 396;
    private const int SmallChestObject = 326;
    private const int ClosedDoorObject = 368;

    /// <summary>
    /// A known arrangement around the player, so that every direction the cone
    /// is pointed has something in it worth describing.
    ///
    /// A level as generated is mostly corridor, whose walls are all granite -
    /// and granite is only described when it has something in it, so a look
    /// down a corridor finds nothing at all and proves nothing.
    /// </summary>
    private static void LookArena(GameState game)
    {
        int[][] offsets =
        [
            [-1, 0], [1, 0], [0, -1], [0, 1], [-1, -1], [-1, 1], [1, -1], [1, 1],
        ];

        int centreRow = game.CharacterRow;
        int centreColumn = game.CharacterColumn;

        // An open floor to look across.
        for (int i = -5; i <= 5; i++)
        {
            for (int j = -7; j <= 7; j++)
            {
                int row = centreRow + i;
                int column = centreColumn + j;

                if (!game.Cave.InBounds(row, column))
                {
                    continue;
                }

                game.Cave[row, column].Feature = CaveFeature.CorridorFloor;
                game.Cave[row, column].ObjectIndex = 0;

                if (i != 0 || j != 0)
                {
                    game.Cave[row, column].MonsterIndex = 0;
                }
            }
        }

        // Something to see two squares out in each of the eight directions, and
        // something else four squares out.
        for (int k = 0; k < 8; k++)
        {
            for (int i = 2; i <= 4; i += 2)
            {
                int row = centreRow + (offsets[k][0] * i);
                int column = centreColumn + (offsets[k][1] * i);

                if (!game.Cave.InBounds(row, column))
                {
                    continue;
                }

                PlaceFixed(game, row, column, 30 + (k * 3) + (i / 2));
            }
        }

        // Mineral veins five out, which only the second pass describes.
        for (int k = 0; k < 8; k++)
        {
            int row = centreRow + (offsets[k][0] * 5);
            int column = centreColumn + (offsets[k][1] * 5);

            if (game.Cave.InBounds(row, column))
            {
                game.Cave[row, column].Feature = (k & 1) != 0
                    ? CaveFeature.MagmaWall
                    : CaveFeature.QuartzWall;
            }
        }

        // And something alive, three squares to the east.
        if (game.Cave.InBounds(centreRow, centreColumn + 3))
        {
            new DungeonGenerator(game)
                .PlaceMonster(centreRow, centreColumn + 3, 20, false);
        }
    }

    /// <summary>
    /// The enhanced look, with its cone of peripheral vision.
    ///
    /// The level is left exactly as it was generated, and the whole of it is lit
    /// and remembered with every creature on show, so that whatever the cone
    /// reaches is worth describing. Every key the look asks for is answered with
    /// a space, which steps on to the next thing.
    /// </summary>
    public static void DumpLook(TextWriter output, uint seed, int level, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "look", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();

        game.InitSeeds(seed);
        game.Turn = 0;
        game.DungeonLevel = level;
        game.Player = new Player { MaxDungeonLevel = level };
        game.Knowledge.Reset();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        var display = new Display(game, screen);

        new DungeonGenerator(game).Generate();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);

        var loop = new GameLoop(game, display);
        Player player = game.Player;

        player.Level = 20;
        player.ExperienceFactor = 100;
        player.Food = 5000;

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 18;
            player.CurrentStat[i] = 18;
            player.ModStat[i] = 0;
            loop.Stats.SetUseStat(i);
        }

        player.MaxHitPoints = 500;
        player.CurrentHitPoints = 500;

        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                game.Cave[row, column].PermanentLight = true;
                game.Cave[row, column].FieldMark = true;
            }
        }

        LookArena(game);

        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            game.Monsters[i].Visible = true;
        }

        game.PlayerLight = true;
        display.Panel.Invalidate();
        loop.Lighting.CheckView();

        // Mineral veins are picked out on the odd variations, which is what
        // makes look take a second pass over the rock.
        game.HighlightSeams = variation % 2 == 1;

        display.MessageWaitingFlag = false;

        // Directions 1 to 9, with 5 meaning every way at once. The variations
        // past eighteen run the same looks again, but answer the first thing
        // described with an "r" - which recalls the creature, if it was one,
        // and so puts the monster memory up in the middle of a look and takes
        // it down again.
        int direction = ((variation % 18) / 2) + 1;

        // Every description answered with an "r" rather than a space, so
        // whichever of them are creatures are recalled.
        string keys = direction.ToString(CultureInfo.InvariantCulture);

        screen.SetKeys(keys
            + new string(variation >= 18 ? 'r' : ' ', 4000 - keys.Length));

        // Every description is overwritten by the next, so the only record of
        // what the cone actually found is what was on the message line each
        // time it stopped to ask.
        int asked = 0;

        screen.BeforeReadKey = () =>
        {
            output.Write(string.Join(
                ' ', "ask", asked.ToString(CultureInfo.InvariantCulture),
                "at", screen.CursorRow.ToString(CultureInfo.InvariantCulture),
                screen.CursorColumn.ToString(CultureInfo.InvariantCulture),
                screen.GetRow(0).TrimEnd()) + "\n");

            asked++;
        };

        loop.Looking.Look();
        screen.BeforeReadKey = null;

        output.Write("direction " + direction.ToString(CultureInfo.InvariantCulture)
            + " seams " + (game.HighlightSeams ? "1" : "0")
            + " recall " + (variation >= 18 ? "1" : "0") + "\n");

        for (int row = 0; row < screen.Rows; row++)
        {
            output.Write(
                "scr " + row.ToString(CultureInfo.InvariantCulture)
                + " " + screen.GetRow(row).TrimEnd() + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }
}
