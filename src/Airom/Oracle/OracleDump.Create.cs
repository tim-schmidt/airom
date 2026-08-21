// AIrom's side of the oracle's create mode.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Oracle;

/// <summary>
/// A character maker that never asks the machine who is playing.
///
/// The harness always types a name, so this is only reached if something has
/// gone wrong - but if it ever is, the two sides must not disagree merely
/// because they are running under different accounts.
/// </summary>
internal sealed class PinnedMaker(GameState game, Display display, GameLoop loop)
    : CharacterMaker(game, display, loop)
{
    protected override string UserName() => "Player";
}

public static partial class OracleDump
{
    /// <summary>
    /// Rolling a character with someone watching, and the belongings they set
    /// out with.
    ///
    /// The arithmetic of creation was compared long ago in the character mode,
    /// which drives it without a terminal. What this compares is the asking:
    /// which letter picks which race, what the screen looks like after a
    /// reroll, which classes a race is offered and in what order, and what the
    /// starting kit is for the class that ends up chosen.
    /// </summary>
    public static void DumpCreate(TextWriter output, uint seed, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "create", seed);
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        display.MessageWaitingFlag = false;

        var keys = new System.Text.StringBuilder();

        // An invalid answer first, so the bell and the redrawn prompt are
        // compared as well as the answer that works.
        if (variation % 4 == 0)
        {
            keys.Append('9');
        }

        keys.Append((char)('a' + (variation % GameTables.Races.Length)));
        keys.Append((variation % 2) == 0 ? 'm' : 'f');

        // As many rerolls as the variation asks for, then accept.
        keys.Append(' ', variation % 3);
        keys.Append(Keys.Escape);
        keys.Append((char)('a' + (variation % 3)));
        keys.Append("Alatariel\r");

        keys.Append(' ', 599 - keys.Length);
        screen.SetKeys(keys.ToString());

        var maker = new PinnedMaker(game, display, loop);
        maker.Create();

        DumpScreenRows(output, screen, "scr");

        Player player = game.Player;

        static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

        output.Write("who [" + player.Name + "] male " + (player.Male ? "1" : "0")
            + " race " + N(player.Race) + " class " + N(player.Class) + "\n");

        output.Write("build age " + N(player.Age) + " height " + N(player.Height)
            + " weight " + N(player.Weight) + " social " + N(player.SocialClass) + "\n");

        output.Write("stats " + string.Join(
            ' ', Enumerable.Range(0, Stat.Count).Select(i => N(player.MaxStat[i]))) + "\n");

        output.Write("use " + string.Join(
            ' ', Enumerable.Range(0, Stat.Count).Select(i => N(player.UseStat[i]))) + "\n");

        output.Write("body hitdie " + N(player.HitDie) + " mhp " + N(player.MaxHitPoints)
            + " gold " + N(player.Gold) + " bth " + N(player.BaseToHit)
            + " bthb " + N(player.BaseToHitBows) + "\n");

        for (int i = 0; i < player.History.Length; i++)
        {
            output.Write("history " + N(i) + " [" + player.History[i] + "]\n");
        }

        for (int i = 0; i < Player.MaxLevel; i++)
        {
            output.Write("hp " + N(i) + " " + N(player.HitPointsByLevel[i]) + "\n");
        }

        // And what they are given to set out with.
        maker.GiveStartingItems();

        output.Write("pack " + N(game.Inventory.Count)
            + " weight " + N(game.Inventory.Weight) + "\n");

        for (int i = 0; i < game.Inventory.Count; i++)
        {
            DumpItem(output, "carried", i, game.Inventory[i]);
        }

        for (int i = 0; i < player.SpellOrder.Length; i++)
        {
            output.Write("order " + N(i) + " " + N(player.SpellOrder[i]) + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }
}
