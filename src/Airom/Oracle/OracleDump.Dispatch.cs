// AIrom's side of the oracle's dispatch mode.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;
using Airom.Terminal;

namespace Airom.Oracle;

public static partial class OracleDump
{
    /// <summary>
    /// Every key, pressed.
    ///
    /// The commands mode compares the table that turns one command set into
    /// the other, which is worth having but says nothing about what the
    /// dispatch then does with the answer - and a command bound to the wrong
    /// key passes that table happily. This drives the dispatch itself, once per
    /// key, and compares what each one said, whether it took a turn, and what
    /// it changed.
    ///
    /// Every prompt a command raises is answered with escape, so what is
    /// compared is the command reaching the right place rather than what it
    /// does once it is there - which the other modes cover a command at a time.
    /// </summary>
    public static void DumpDispatch(
        TextWriter output, uint seed, int level, int rogue, int first, int count)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "dispatch", seed);

        static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

        output.Write("level " + N(level) + "\n");
        output.Write("rogue " + N(rogue) + "\n");
        output.Write("first " + N(first) + " count " + N(count) + "\n");

        uint state = 0;

        for (int which = first; which < first + count; which++)
        {
            // Not control-X: saving the game ends the original outright, and
            // would take the rest of the run with it.
            if (which is < 0 or > 127 or 24)
            {
                continue;
            }

            // A level of its own for every key, so that one command cannot
            // leave the next one standing somewhere different.
            (GameState game, MemoryScreen screen, Display display, GameLoop loop) =
                DispatchSetup(seed, level, rogue != 0);

            int row = game.CharacterRow;
            int column = game.CharacterColumn;

            // Escapes all the way down: every prompt this can raise is answered
            // by backing out of it.
            screen.SetKeys(new string(Keys.Escape, 599));

            loop.DispatchForOracle((char)which);

            output.Write("key " + N(which)
                + " free " + (loop.FreeTurn ? "1" : "0")
                + " new-level " + (loop.NewLevel ? "1" : "0")
                + " moved " + N(game.CharacterRow - row)
                + " " + N(game.CharacterColumn - column)
                + " level " + N(game.DungeonLevel)
                + " turn " + N(game.Turn) + "\n");

            output.Write("said " + N(which) + " ["
                + (SaysNothingComparable(which) ? "not compared" : screen.GetRow(0).TrimEnd())
                + "]\n");

            state = game.Rng.State;
            output.Write("state " + N(which) + " "
                + state.ToString(CultureInfo.InvariantCulture) + "\n");
        }

        Line(output, "final-state", state);
    }

    /// <summary>
    /// Five keys whose message cannot be compared, for reasons that have
    /// nothing to do with which command they reach.
    ///
    /// Three of them open a help file, and the original was compiled with the
    /// author's own home directory baked into the name, so the two sides
    /// disagree about a path rather than about a command. One saves the game,
    /// Two ask for a shell, which Windows does not have and the original
    /// refuses anyway. What each key did is still compared - the turn, the
    /// move, the level - only what it said is not.
    /// </summary>
    private static bool SaysNothingComparable(int command) =>
        command is 22 or 33 or 36 or 63 or 118;

    /// <summary>
    /// A character standing on a freshly built level, set up the way dungeon()
    /// leaves things just before it asks for a command.
    /// </summary>
    private static (GameState, MemoryScreen, Display, GameLoop) DispatchSetup(
        uint seed, int level, bool rogue)
    {
        (GameState game, MemoryScreen screen, Display display, GameLoop loop) =
            DeathSetup(seed, 0);

        game.Stores.Initialise();

        // The same help files the C side is pointed at, by the same relative
        // name, so a command that opens one is compared by which it asked for.
        CharacterFile.HelpDirectory = "help";

        game.RogueLikeCommands = rogue;
        display.MessageWaitingFlag = false;

        game.DungeonLevel = level;
        new DungeonGenerator(game, display).Generate();

        for (int i = 0; i < game.Player.SpellOrder.Length; i++)
        {
            game.Player.SpellOrder[i] = Player.NoSpell;
        }

        game.Turn = 100;
        game.CharacterGenerated = true;
        game.CharacterSaved = false;
        loop.Dead = false;
        game.DiedFrom = "(alive and well)";

        game.Player.Food = 5000;
        game.Player.FoodDigested = 2;
        game.Player.CurrentHitPoints = game.Player.MaxHitPoints;
        game.Player.CurrentMana = game.Player.MaxMana;

        loop.FreeTurn = false;
        loop.NewLevel = false;
        loop.Running = false;
        display.CommandCount = 0;

        // What dungeon() does before it asks for anything: put the view where
        // the player is and draw it. Several commands read the panel.
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);
        display.Panel.Invalidate();
        display.Panel.Follow(game.CharacterRow, game.CharacterColumn, force: true);
        display.PrintMap();

        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;

        return (game, screen, display, loop);
    }
}
