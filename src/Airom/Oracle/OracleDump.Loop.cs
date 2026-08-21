// AIrom's side of the oracle's commands and regen modes.
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
    /// Writes a key the way the dump names it: printable characters as
    /// themselves, everything else by its number, since control keys are
    /// commands here.
    /// </summary>
    private static string KeyText(string label, int key) =>
        key is > 32 and < 127
            ? label + " '" + (char)key + "'"
            : label + " " + key.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The command tables: every key through the original-to-rogue translation
    /// and the count validity test.
    ///
    /// Two of the translations - walk and tunnel - read a direction before they
    /// can answer, because the rogue-like set spells the direction into the
    /// command letter. Each key is therefore tried twice: once with a direction
    /// waiting, and once with an escape, which is the player abandoning the
    /// command.
    /// </summary>
    public static void DumpCommands(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.Write("mode commands\n");

        var game = OracleGame();
        var screen = new MemoryScreen();
        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        for (int key = 0; key < 128; key++)
        {
            screen.SendKeys("4");
            loop.FreeTurn = false;
            char answered = Commands.ToRogueLike((char)key, loop.ReadDirection);

            screen.SendKeys(Keys.Escape.ToString());
            loop.FreeTurn = false;
            char abandoned = Commands.ToRogueLike((char)key, loop.ReadDirection);

            output.Write(
                KeyText("key", key)
                + KeyText(" answered", answered)
                + KeyText(" abandoned", abandoned)
                + " count " + (Commands.AllowsCount((char)key) ? "1" : "0") + "\n");
        }
    }

    /// <summary>
    /// Hit point and mana regeneration.
    ///
    /// Both carry a fraction in 1/65536ths between turns, because a character
    /// regenerates far less than a point a turn. The interesting cases are the
    /// carry, the clamp at full, and the overflow guard that a very high maximum
    /// would otherwise walk into.
    /// </summary>
    public static void DumpRegen(TextWriter output, uint seed, int turns)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "regen", seed);
        output.Write("turns " + turns.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = OracleGame();
        var screen = new MemoryScreen();
        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        Player player = game.Player;
        player.MaxHitPoints = 250;
        player.CurrentHitPoints = 1;
        player.HitPointFraction = 0;
        player.MaxMana = 90;
        player.CurrentMana = 0;
        player.ManaFraction = 0;

        for (int i = 0; i < turns; i++)
        {
            // The three food bands and the doubled resting rate, so every
            // regeneration factor the loop can pass in is covered.
            int percent = (i % 4) switch
            {
                0 => GameLoop.RegenNormal,
                1 => GameLoop.RegenWeak,
                2 => GameLoop.RegenFainting,
                _ => GameLoop.RegenNormal * 2,
            };

            loop.RegenerateHitPoints(percent);
            loop.RegenerateMana(percent);

            output.Write(string.Join(
                ' ',
                "turn",
                i.ToString(CultureInfo.InvariantCulture),
                "percent",
                percent.ToString(CultureInfo.InvariantCulture),
                "chp",
                player.CurrentHitPoints.ToString(CultureInfo.InvariantCulture),
                "frac",
                player.HitPointFraction.ToString(CultureInfo.InvariantCulture),
                "cmana",
                player.CurrentMana.ToString(CultureInfo.InvariantCulture),
                "frac",
                player.ManaFraction.ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        // The overflow guard: a maximum beyond a signed short saturates rather
        // than wrapping negative.
        player.MaxHitPoints = GameLoop.MaxShort;
        player.CurrentHitPoints = GameLoop.MaxShort - 1;
        player.HitPointFraction = 0;
        loop.RegenerateHitPoints(GameLoop.RegenNormal * 2);

        output.Write(
            "clamped chp " + player.CurrentHitPoints.ToString(CultureInfo.InvariantCulture)
            + " frac " + player.HitPointFraction.ToString(CultureInfo.InvariantCulture) + "\n");
    }
}
