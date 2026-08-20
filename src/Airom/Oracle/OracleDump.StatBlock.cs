// AIrom's side of the oracle's statblock mode.
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
    /// The status sidebar, for a rolled character under a set of conditions.
    ///
    /// Each condition owns a column range along the bottom two lines, so they
    /// can be redrawn as they come and go. The interesting parts are the ones
    /// that interact: weak outranks hungry, paralysis outranks resting,
    /// searching overwrites a repeat count, and searching is discounted from the
    /// speed before deciding whether there is anything to say about it.
    /// </summary>
    public static void DumpStatBlock(TextWriter output, uint seed, int variation)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "statblock", seed);
        output.Write("variation " + variation.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();

        // A human warrior, so the character is the same in every variation.
        Player player = new CharacterCreation(game)
            .Create(race: 0, characterClass: 0, male: true, name: "Oracle");

        player.Experience = 12345;
        player.CurrentMana = 7;
        player.DisplayedArmourClass = 14;
        player.Gold = 4321;

        var screen = new MemoryScreen();
        var display = new Display(game, screen);

        switch (variation)
        {
            case 0:
                break;
            case 1:
                player.Status |= PlayerStatus.Hungry;
                break;
            case 2:
                player.Status |= PlayerStatus.Weak | PlayerStatus.Hungry;
                break;
            case 3:
                player.Status |= PlayerStatus.Blind | PlayerStatus.Confused
                    | PlayerStatus.Afraid | PlayerStatus.Poisoned;
                break;
            case 4:
                player.Status |= PlayerStatus.Searching;
                break;
            case 5:
                player.Status |= PlayerStatus.Resting;
                player.Rest = 42;
                break;
            case 6:
                player.Status |= PlayerStatus.Resting;
                player.Rest = -1;
                break;
            case 7:
                player.Paralysis = 5;
                player.Status |= PlayerStatus.Resting;
                break;
            case 8:
                display.CommandCount = 17;
                break;
            case 9:
                display.CommandCount = 17;
                player.Status |= PlayerStatus.Searching;
                break;
            case 10:
                player.Speed = 2;
                break;
            case 11:
                player.Speed = -3;
                break;
            case 12:
                player.Speed = 1;
                player.Status |= PlayerStatus.Searching;
                break;
            case 13:
                player.NewSpells = 2;
                break;
            case 14:
                game.TotalWinner = true;
                break;
            case 15:
                game.NoScore |= 0x2;
                game.Wizard = true;
                break;
            case 16:
                // The whole range FormatStat has to write, including the 18/100
                // that is the only three digit remainder.
                player.UseStat[0] = 3;
                player.UseStat[1] = 18;
                player.UseStat[2] = 19;
                player.UseStat[3] = 18 + 22;
                player.UseStat[4] = 18 + 99;
                player.UseStat[5] = 18 + 100;
                break;
            case 17:
                player.Level = 0;
                break;
            case 18:
                player.Level = Player.MaxLevel;
                break;
            case 19:
                player.Level = Player.MaxLevel + 1;
                break;
            default:
                player.Level = Player.MaxLevel + 1;
                player.Male = false;
                break;
        }

        display.ClearScreen();
        display.PrintStatBlock(player);

        for (int row = 0; row < screen.Rows; row++)
        {
            output.Write(
                "stat " + row.ToString(CultureInfo.InvariantCulture)
                + " " + screen.GetRow(row).TrimEnd() + "\n");
        }

        output.Write(
            "status " + player.Status.ToString(CultureInfo.InvariantCulture) + "\n");
        Line(output, "final-state", game.Rng.State);
    }
}
