// AIrom's side of the oracle's recall mode.
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
    /// The monster memory, written out as prose.
    ///
    /// Everything said about a creature is something the player found out, so
    /// the memory is filled in to a known depth first and the whole page
    /// compared afterwards. Four depths are used: nothing known at all, a
    /// single kill, very nearly everything, and a wizard's view - which fills
    /// the memory in, reads it out, and has to put it back exactly as it was.
    ///
    /// The character's level is varied too, since the worth of a kill is scaled
    /// by it and the sentence that says so has to get its ordinal right.
    /// </summary>
    public static void DumpRecall(
        TextWriter output, uint seed, int variation, int first, int count)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "recall", seed);
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("first " + first.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("count " + count.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.Player = new Player();

        // The depth of knowledge repeats every four, while the level keeps
        // moving - so the same four depths are read out by characters of many
        // levels, which is what the ordinal and the "a"/"an" turn on.
        game.Player.Level = 1 + ((variation * 7) % 40);
        game.Wizard = variation % 4 == 3;

        for (int which = first;
             which < first + count && which < GameTables.CreatureList.Length;
             which++)
        {
            FillMemory(game, which, variation % 4);

            var screen = new MemoryScreen { TypeAheadVisible = false };
            screen.SendKeys(new string(' ', 63));

            var display = new Display(game, screen);
            var recall = new MonsterRecall(game, display);

            string N(int value) => value.ToString(CultureInfo.InvariantCulture);

            output.Write("creature " + N(which) + " "
                + GameTables.CreatureList[which].Name + "\n");

            output.Write("  known " + (recall.KnowsAnything(which) ? "1" : "0") + "\n");
            output.Write("  answer " + N(recall.Describe(which)) + "\n");

            // And what the memory holds afterwards, which the wizard's view has
            // to have left exactly as it found it.
            MonsterMemory memory = game.Memories[which];

            output.Write(string.Join(
                ' ', "  memory",
                memory.Move.ToString(CultureInfo.InvariantCulture),
                memory.Spells.ToString(CultureInfo.InvariantCulture),
                N(memory.Kills), N(memory.Deaths), N(memory.Defense),
                N(memory.Wake), N(memory.Ignore),
                N(memory.Attacks[0]), N(memory.Attacks[1]),
                N(memory.Attacks[2]), N(memory.Attacks[3])) + "\n");

            for (int row = 0; row < screen.Rows; row++)
            {
                output.Write("scr " + N(row) + " " + screen.GetRow(row).TrimEnd() + "\n");
            }
        }

        game.Wizard = false;

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>Fills a creature's memory in to the depth the variation asks for.</summary>
    private static void FillMemory(GameState game, int which, int variation)
    {
        MonsterMemory memory = game.Memories[which];
        CreatureType creature = GameTables.CreatureList[which];

        memory.Clear();

        if (variation == 1)
        {
            memory.Kills = 1;
            memory.Attacks[0] = 1;
        }
        else if (variation == 2)
        {
            memory.Kills = 5 + (which % 50);
            memory.Deaths = which % 4;
            memory.Wake = (byte)(which % 20);
            memory.Ignore = (byte)(which % 20);
            memory.Move = creature.MoveFlags;
            memory.Defense = creature.DefenseFlags;
            memory.Spells = creature.SpellFlags;

            for (int k = 0; k < MonsterMemory.MaxAttacks; k++)
            {
                memory.Attacks[k] = (byte)(1 + ((which * (k + 1)) % 200));
            }
        }
    }
}
