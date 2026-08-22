// AIrom's side of the oracle's symbol and wizard modes.
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
    /// What a character on the map stands for.
    ///
    /// Every printable symbol is asked about in turn, and the answer compared.
    /// The monster memory is filled in for half of them first, so the offer to
    /// recall what is drawn with that symbol is taken up as well as declined -
    /// and an "n" answer, a "y" answer and an escape part-way through are all
    /// covered by feeding a different reply each time.
    /// </summary>
    public static void DumpSymbol(
        TextWriter output, uint seed, int variation, int first, int count)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "symbol", seed);
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("first " + first.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("count " + count.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();
        game.Player = new Player { Name = "Oracle the Bold" };
        game.Player.Level = 1 + ((variation * 5) % 30);

        // Veins are picked out on the odd variations, which is the one answer
        // that turns on a player option.
        game.HighlightSeams = variation % 2 == 1;

        for (int which = first; which < first + count && which < 95; which++)
        {
            char symbol = (char)(' ' + which);

            // Nothing known at all, then a little known about everything - so
            // the recall is both declined and offered.
            for (int i = 0; i < game.Memories.Count; i++)
            {
                game.Memories[i].Clear();

                if (variation >= 2)
                {
                    game.Memories[i].Kills = 1 + (i % 7);
                    game.Memories[i].Attacks[0] = 1;
                }
            }

            var screen = new MemoryScreen { TypeAheadVisible = false };

            // How the offer to recall is answered: declined, taken and read to
            // the end, or taken and escaped out of part-way.
            string answer = (variation % 6) switch
            {
                0 or 1 => "n",
                2 or 3 => new string('y', 40),
                _ => "y" + (char)27,
            };

            string keys = symbol + answer;
            screen.SendKeys(keys + new string(' ', 599 - keys.Length));

            var display = new Display(game, screen);
            var loop = new GameLoop(game, display);

            Action stopLogging = LogKeys(output, screen);
            loop.SymbolHelp.IdentifySymbol();
            stopLogging();

            output.Write("symbol " + which.ToString(CultureInfo.InvariantCulture)
                + " [" + symbol + "]\n");

            for (int row = 0; row < screen.Rows; row++)
            {
                output.Write("scr " + row.ToString(CultureInfo.InvariantCulture)
                    + " " + screen.GetRow(row).TrimEnd() + "\n");
            }
        }

        game.HighlightSeams = false;

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>The scripts the character editor runs.</summary>
    private static readonly string[] WizardScripts =
    [
        "",                                       // backed out of at once
        "18\r18\r18\r18\r18\r18\r",               // the stats, then out
        "18\r18\r18\r18\r18\r18\r250\r99\r",      // and the hit points and mana
        "3\r118\r50\r2\r119\r18\r1\r0\r",         // the edges of every bound
        "18\r18\r18\r18\r18\r18\r250\r99\r5000\r100\r9\r100\r80\r120\r90\r180\r+++-\r",
        "\r\r\r\r\r\r\r\r\r\r\r\r\r\r\r\r",       // nothing typed anywhere
    ];

    /// <summary>
    /// The scripts the item builder runs. Each opens with a space: the builder
    /// announces itself before its first question, and writing that question
    /// over the message line flushes it through a -more- that takes a key.
    /// </summary>
    private static readonly string[] CreateScripts =
    [
        " ",                                                             // backed out
        " 23\r|\r1\r100\r1\r2\r6\r5\r3\r0\r0\r0\r0\r100\r5\ry",           // a sword
        " 23\r|\r1\r100\r1\r2\r6\r5\r3\r0\r0\r0\r1f\r100\r5\ry",          // with flags
        " 75\r!\r64\r4\r3\r0\r0\r0\r0\r0\r0\r500\r0\r400\r10\ry",         // a potion
        " 23\r|\r1\r100\r1\r2\r6\r5\r3\r0\r0\r0\r0\r100\r5\rn",           // abandoned
        " 23\r|\r1\r100\r1\r2\r6\r5\r3\r0\r",                            // backed out
    ];

    /// <summary>
    /// The debugging commands.
    ///
    /// Lighting the level is compared as a whole map. Editing the character and
    /// building an item by hand are both a run of typed answers, so the scripts
    /// are typed exactly as a player would type them - including the ones that
    /// back out part-way, since backing out of any question abandons the rest.
    /// </summary>
    public static void DumpWizard(TextWriter output, uint seed, int level, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "wizard", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;
        game.Player = new Player { MaxDungeonLevel = level };
        game.Knowledge.Reset();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        var display = new Display(game, screen);

        // Generated with the screen in hand, so the panel is sized by the
        // arrival rather than by the harness afterwards.
        new DungeonGenerator(game, display).Generate();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;

        var loop = new GameLoop(game, display);
        Player player = game.Player;

        player.Level = 20;
        player.ExperienceFactor = 100;
        player.Gold = 500;
        player.Search = 20;
        player.Stealth = 3;
        player.Disarm = 30;
        player.Save = 40;
        player.BaseToHit = 50;
        player.BaseToHitBows = 45;
        player.Weight = 150;
        player.Food = 5000;

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 16;
            player.CurrentStat[i] = 12;
            player.ModStat[i] = 0;
            loop.Stats.SetUseStat(i);
        }

        loop.Equipment.Recalculate();

        player.MaxHitPoints = 100;
        player.CurrentHitPoints = 60;
        player.MaxMana = 20;
        player.CurrentMana = 10;

        LightTheLamp(game, 400);
        loop.EnterLevel();

        display.MessageWaitingFlag = false;

        string N(int value) => value.ToString(CultureInfo.InvariantCulture);

        // Lighting the level is the same command whichever script follows, and
        // it toggles, so it is run twice - and once more to leave it as found.
        loop.WizardCommands.LightLevel();
        output.Write("lit-first " + N(CountLit(game)) + "\n");
        loop.WizardCommands.LightLevel();
        output.Write("lit-second " + N(CountLit(game)) + "\n");
        loop.WizardCommands.LightLevel();

        bool editing = variation % 2 == 0;

        string script = editing
            ? WizardScripts[(variation / 2) % WizardScripts.Length]
            : CreateScripts[(variation / 2) % CreateScripts.Length];

        screen.SetKeys(script + new string((char)27, 599 - script.Length));

        if (editing)
        {
            loop.WizardCommands.ChangeCharacter();
        }
        else
        {
            Action stopCreateLogging = LogKeys(output, screen);
            loop.WizardCommands.CreateObject();
            stopCreateLogging();
        }

        output.Write("stats " + string.Join(' ',
            player.CurrentStat.Select(N)) + "\n");

        output.Write("max " + string.Join(' ',
            player.MaxStat.Select(N)) + "\n");

        output.Write(string.Join(
            ' ', "misc",
            N(player.MaxHitPoints), N(player.CurrentHitPoints),
            N(player.MaxMana), N(player.CurrentMana), N(player.Gold),
            N(player.Search), N(player.Stealth), N(player.Disarm),
            N(player.Save), N(player.BaseToHit), N(player.BaseToHitBows),
            N(player.Weight), N(player.Speed)) + "\n");

        CaveSquare square = game.Cave[game.CharacterRow, game.CharacterColumn];

        if (square.ObjectIndex != 0)
        {
            InvenType item = game.Objects[square.ObjectIndex];

            output.Write(string.Join(
                ' ', "floor",
                N(item.Index), N(item.TVal), N(item.SubVal), N(item.Number),
                N(item.Weight), N(item.ToHit), N(item.ToDam), N(item.Ac),
                N(item.ToAc), N(item.Cost),
                item.Flags.ToString(CultureInfo.InvariantCulture),
                N(item.Level),
                game.Names.Describe(item, withArticle: true)) + "\n");
        }
        else
        {
            output.Write("floor none\n");
        }

        for (int row = 0; row < screen.Rows; row++)
        {
            output.Write("scr " + N(row) + " " + screen.GetRow(row).TrimEnd() + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>How much of the level is lit, which is what the command changes.</summary>
    private static int CountLit(GameState game)
    {
        int lit = 0;

        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                if (game.Cave[row, column].PermanentLight)
                {
                    lit++;
                }
            }
        }

        return lit;
    }
}
