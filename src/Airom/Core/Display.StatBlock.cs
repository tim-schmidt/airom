// Ported from the status sidebar in Umoria 5.6 source/misc3.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Conditions the player is under, shown along the bottom of the screen.
/// Mirrors Umoria's PY_* status bits.
/// </summary>
public static class PlayerStatus
{
    public const uint Hungry = 0x00000001;
    public const uint Weak = 0x00000002;
    public const uint Blind = 0x00000004;
    public const uint Confused = 0x00000008;
    public const uint Afraid = 0x00000010;
    public const uint Poisoned = 0x00000020;

    /// <summary>Searching as they walk, which costs a little speed.</summary>
    public const uint Searching = 0x00000100;

    public const uint Resting = 0x00000200;

    /// <summary>A spell is waiting to be learned.</summary>
    public const uint CanStudy = 0x00000400;

    /// <summary>A command is repeating.</summary>
    public const uint Repeating = 0x00200000;

    // The rest of Umoria's PY_* bits. Most are requests to recompute something
    // rather than conditions, and the sidebar ignores them.
    public const uint Hasted = 0x00000040;
    public const uint Slowed = 0x00000080;
    public const uint Invulnerable = 0x00001000;
    public const uint Heroism = 0x00002000;
    public const uint SuperHeroism = 0x00004000;
    public const uint Blessed = 0x00008000;
    public const uint DetectInvisible = 0x00010000;
    public const uint TimedInfravision = 0x00020000;
    public const uint SpeedChanged = 0x00040000;

    /// <summary>Carried weight changed, so the speed penalty needs recomputing.</summary>
    public const uint WeightChanged = 0x00080000;

    public const uint Paralysed = 0x00100000;

    /// <summary>Armour changed, so the displayed class needs recomputing.</summary>
    public const uint ArmourChanged = 0x00400000;

    public const uint Strength = 0x01000000;
    public const uint Intelligence = 0x02000000;
    public const uint Wisdom = 0x04000000;
    public const uint Dexterity = 0x08000000;
    public const uint Constitution = 0x10000000;
    public const uint Charisma = 0x20000000;
    public const uint AllStats = 0x3F000000;
    public const uint HitPointsChanged = 0x40000000;
    public const uint ManaChanged = 0x80000000;

    /// <summary>The bits the sidebar reads or writes.</summary>
    public const uint SidebarBits =
        Hungry | Weak | Blind | Confused | Afraid | Poisoned
        | Searching | Resting | CanStudy | Repeating;
}

public sealed partial class Display
{
    /// <summary>Column the sidebar starts at. Umoria's STAT_COLUMN.</summary>
    public const int StatColumn = 0;

    private static readonly string[] StatNames =
        ["STR : ", "INT : ", "WIS : ", "DEX : ", "CON : ", "CHR : "];

    /// <summary>
    /// Formats a stat for display. Mirrors cnv_stat().
    ///
    /// Values above 18 are Umoria's percentile range written as 18/nn, so a
    /// strength of 40 shows as 18/22. The full 18/100 is special-cased because
    /// it is the only three-digit remainder.
    /// </summary>
    public static string FormatStat(int stat)
    {
        if (stat <= 18)
        {
            return stat.ToString(CultureInfo.InvariantCulture).PadLeft(6);
        }

        int remainder = stat - 18;
        return remainder == 100
            ? "18/100"
            : " 18/" + remainder.ToString(CultureInfo.InvariantCulture).PadLeft(2, '0');
    }

    /// <summary>
    /// The player's rank. Mirrors title_string().
    ///
    /// Beyond the last level the title becomes king or queen, which only a
    /// winner ever sees.
    /// </summary>
    public static string TitleFor(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (player.Level < 1)
        {
            return "Babe in arms";
        }

        if (player.Level <= Player.MaxLevel)
        {
            return GameTables.ClassTitles[player.Class][player.Level - 1];
        }

        return player.Male ? "**KING**" : "**QUEEN**";
    }

    /// <summary>Writes a field, blanking the thirteen columns it occupies first.</summary>
    private void PrintField(string text, int row, int column)
    {
        PutBuffer(new string(' ', 13), row, column);
        PutBuffer(text, row, column);
    }

    /// <summary>Writes a labelled number right-aligned in six columns.</summary>
    private void PrintNumber(string label, long value, int row, int column) =>
        PutBuffer(
            label + ": " + value.ToString(CultureInfo.InvariantCulture).PadLeft(6),
            row,
            column);

    /// <summary>Writes one stat line. Mirrors prt_stat().</summary>
    public void PrintStat(Player player, int stat)
    {
        ArgumentNullException.ThrowIfNull(player);

        PutBuffer(StatNames[stat], 6 + stat, StatColumn);
        PutBuffer(FormatStat(player.UseStat[stat]), 6 + stat, StatColumn + 6);
    }

    /// <summary>
    /// Draws the whole sidebar. Mirrors prt_stat_block().
    ///
    /// The layout is fixed by row: identity at the top, the six stats, then the
    /// numbers, with the condition indicators along the bottom two lines. Each
    /// condition owns a column range, which is why they can be redrawn
    /// independently as they come and go.
    /// </summary>
    public void PrintStatBlock(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        PrintField(GameTables.Races[player.Race].Name, 2, StatColumn);
        PrintField(GameTables.Classes[player.Class].Title, 3, StatColumn);
        PrintField(TitleFor(player), 4, StatColumn);

        for (int i = 0; i < Stat.Count; i++)
        {
            PrintStat(player, i);
        }

        PrintNumber("LEV ", player.Level, 13, StatColumn);
        PrintNumber("EXP ", player.Experience, 14, StatColumn);
        PrintNumber("MANA", player.CurrentMana, 15, StatColumn);
        PrintNumber("MHP ", player.MaxHitPoints, 16, StatColumn);
        PrintNumber("CHP ", player.CurrentHitPoints, 17, StatColumn);
        PrintNumber("AC  ", player.DisplayedArmourClass, 19, StatColumn);
        PrintNumber("GOLD", player.Gold, 20, StatColumn);

        PrintWinner(player);

        uint status = player.Status;

        if ((status & (PlayerStatus.Hungry | PlayerStatus.Weak)) != 0)
        {
            PrintHunger(player);
        }

        if ((status & PlayerStatus.Blind) != 0)
        {
            PrintBlind(player);
        }

        if ((status & PlayerStatus.Confused) != 0)
        {
            PrintConfused(player);
        }

        if ((status & PlayerStatus.Afraid) != 0)
        {
            PrintAfraid(player);
        }

        if ((status & PlayerStatus.Poisoned) != 0)
        {
            PrintPoisoned(player);
        }

        if ((status & (PlayerStatus.Searching | PlayerStatus.Resting)) != 0)
        {
            PrintState(player);
        }

        // Searching costs a point of speed, so it is discounted before deciding
        // whether there is anything to report.
        if (player.Speed - (int)((status & PlayerStatus.Searching) >> 8) != 0)
        {
            PrintSpeed(player);
        }

        PrintStudy(player);
    }

    /// <summary>Mirrors prt_hunger(). Weak outranks merely hungry.</summary>
    public void PrintHunger(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        string text = (player.Status & PlayerStatus.Weak) != 0 ? "Weak  "
            : (player.Status & PlayerStatus.Hungry) != 0 ? "Hungry"
            : "      ";
        PutBuffer(text, 23, 0);
    }

    /// <summary>Mirrors prt_blind().</summary>
    public void PrintBlind(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        PutBuffer((player.Status & PlayerStatus.Blind) != 0 ? "Blind" : "     ", 23, 7);
    }

    /// <summary>Mirrors prt_confused().</summary>
    public void PrintConfused(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        PutBuffer(
            (player.Status & PlayerStatus.Confused) != 0 ? "Confused" : "        ", 23, 13);
    }

    /// <summary>Mirrors prt_afraid().</summary>
    public void PrintAfraid(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        PutBuffer((player.Status & PlayerStatus.Afraid) != 0 ? "Afraid" : "      ", 23, 22);
    }

    /// <summary>Mirrors prt_poisoned().</summary>
    public void PrintPoisoned(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        PutBuffer(
            (player.Status & PlayerStatus.Poisoned) != 0 ? "Poisoned" : "        ", 23, 29);
    }

    /// <summary>
    /// What the player is busy doing. Mirrors prt_state().
    ///
    /// Paralysis outranks everything, then resting, then a repeating command,
    /// then searching. The repeat flag is cleared and set again here, which is
    /// how the rest of the game learns a count is running.
    /// </summary>
    public void PrintState(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        player.Status &= ~PlayerStatus.Repeating;

        string text;

        if (player.Paralysis > 1)
        {
            text = "Paralysed";
        }
        else if ((player.Status & PlayerStatus.Resting) != 0)
        {
            text = player.Rest < 0 ? "Rest *"
                : _game.DisplayCounts
                    ? "Rest " + player.Rest.ToString(CultureInfo.InvariantCulture).PadRight(5)
                    : "Rest";
        }
        else if (CommandCount > 0)
        {
            text = _game.DisplayCounts
                ? "Repeat " + CommandCount.ToString(CultureInfo.InvariantCulture).PadRight(3)
                : "Repeat";

            player.Status |= PlayerStatus.Repeating;
            PutBuffer(text, 23, 38);

            // Searching overwrites the repeat count when both apply.
            if ((player.Status & PlayerStatus.Searching) != 0)
            {
                PutBuffer("Search", 23, 38);
            }

            return;
        }
        else if ((player.Status & PlayerStatus.Searching) != 0)
        {
            text = "Searching";
        }
        else
        {
            // Wide enough for "Repeat 999".
            text = new string(' ', 10);
        }

        PutBuffer(text, 23, 38);
    }

    /// <summary>
    /// How fast the player is moving. Mirrors prt_speed().
    ///
    /// Searching costs a point, so it is discounted first - which is why
    /// searching at normal pace reports nothing rather than "Slow".
    /// </summary>
    public void PrintSpeed(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        int speed = player.Speed;
        if ((player.Status & PlayerStatus.Searching) != 0)
        {
            speed--;
        }

        string text = speed switch
        {
            > 1 => "Very Slow",
            1 => "Slow     ",
            0 => "         ",
            -1 => "Fast     ",
            _ => "Very Fast",
        };

        PutBuffer(text, 23, 49);
    }

    /// <summary>Mirrors prt_study().</summary>
    public void PrintStudy(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        player.Status &= ~PlayerStatus.CanStudy;
        PutBuffer(player.NewSpells == 0 ? "     " : "Study", 23, 59);
    }

    /// <summary>
    /// Anything that disqualifies the player from the score list, or marks them
    /// a winner. Mirrors prt_winner().
    /// </summary>
    public void PrintWinner(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        if ((_game.NoScore & 0x2) != 0)
        {
            PutBuffer(_game.Wizard ? "Is wizard  " : "Was wizard ", 22, 0);
        }
        else if ((_game.NoScore & 0x1) != 0)
        {
            PutBuffer("Resurrected", 22, 0);
        }
        else if ((_game.NoScore & 0x4) != 0)
        {
            PutBuffer("Duplicate", 22, 0);
        }
        else if (_game.TotalWinner)
        {
            PutBuffer("*Winner*   ", 22, 0);
        }
    }
}
