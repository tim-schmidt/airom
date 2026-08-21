// AIrom's side of the oracle's messages and map modes.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;
using Airom.Terminal;

namespace Airom.Oracle;

public static partial class OracleDump
{
    private static readonly string[] SampleMessages =
    [
        "You feel a sudden chill.",
        "It bites you.",
        "You have a Scroll of Word of Recall (e) in your pack, and it glows faintly blue.",
        "The Giant White Louse breeds explosively and the whole corridor fills with them.",
        "You die.",
    ];

    /// <summary>
    /// The message line.
    ///
    /// Two messages run together when they both fit and prompt with -more- when
    /// they do not, so the interesting behaviour is a sequence rather than a
    /// single call. The screen is dumped after each, along with the history ring
    /// the player can review.
    /// </summary>
    public static void DumpMessages(TextWriter output, uint seed)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "messages", seed);

        var game = OracleGame();
        game.InitSeeds(seed);

        var screen = new MemoryScreen();
        var display = new Display(game, screen);

        // Spaces to answer any -more- prompt the sequence provokes.
        screen.SendKeys(new string(' ', 10));

        void DumpScreenAs(string label)
        {
            for (int row = 0; row < screen.Rows; row++)
            {
                output.Write(
                    label + " " + row.ToString(CultureInfo.InvariantCulture)
                    + " " + screen.GetRow(row).TrimEnd() + "\n");
            }
        }

        for (int i = 0; i < SampleMessages.Length; i++)
        {
            display.MessagePrint(SampleMessages[i]);
            output.Write(string.Join(
                ' ',
                "after",
                i.ToString(CultureInfo.InvariantCulture),
                "flag",
                display.MessageWaiting ? "1" : "0",
                "last",
                display.LastMessageIndex.ToString(CultureInfo.InvariantCulture)) + "\n");
            DumpScreenAs("msg");
        }

        display.MessagePrint(null);
        output.Write("after flush flag " + (display.MessageWaiting ? "1" : "0") + "\n");
        DumpScreenAs("msg");

        for (int i = 0; i < Display.SavedMessageCount; i++)
        {
            output.Write(
                "history " + i.ToString(CultureInfo.InvariantCulture)
                + " " + (display.RecentMessages[i] ?? string.Empty) + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }

    /// <summary>
    /// The whole level shrunk to one screen, as the M command shows it.
    ///
    /// Three squares by three collapse into one, so something has to win. What
    /// survives is decided by a priority table, and getting that wrong would
    /// quietly lose the stairs.
    /// </summary>
    public static void DumpMap(TextWriter output, uint seed, int level)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "map", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;

        var screen = new MemoryScreen();
        var display = new Display(game, screen);

        // The whole of generate_cave(), screen and all, so the panel is sized
        // by the level rather than by the harness.
        new DungeonGenerator(game, display).Generate();

        Cave cave = game.Cave;
        for (int row = 0; row < cave.Height; row++)
        {
            for (int column = 0; column < cave.Width; column++)
            {
                cave[row, column].PermanentLight = true;
                cave[row, column].FieldMark = true;
            }
        }

        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            game.Monsters[i].Visible = true;
        }

        cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;

        display.Panel.Follow(game.CharacterRow, game.CharacterColumn, force: true);

        // ScreenMap leaves the drawing on screen; the C only restores it after
        // the keypress, which is where the oracle snapshots it.
        display.ScreenMap();

        for (int row = 0; row < screen.Rows; row++)
        {
            output.Write(
                "map " + row.ToString(CultureInfo.InvariantCulture)
                + " " + screen.GetRow(row).TrimEnd() + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }
}
