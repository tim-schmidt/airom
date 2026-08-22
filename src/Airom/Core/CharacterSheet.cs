// Ported from the character sheet in Umoria 5.6 source/misc3.c - likert,
// put_character, put_misc1, put_stats, put_misc2, put_misc3 and display_char.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The whole character on one screen.
///
/// The eight abilities along the bottom are not numbers but words - "Fair",
/// "Superb" - because the numbers behind them mean nothing without knowing what
/// a good one would be. Each is divided by a scale of its own before the word is
/// chosen, so that the same word means roughly the same thing across all eight.
/// </summary>
public class CharacterSheet
{
    private readonly GameState _game;
    private readonly Display _display;

    public CharacterSheet(GameState game, Display display)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);

        _game = game;
        _display = display;
    }

    private Player Player => _game.Player;

    /// <summary>
    /// Turns a number into a word. Mirrors likert().
    ///
    /// The scale differs by ability, which is what makes "Good" mean the same
    /// amount of good whether it is fighting or stealth being described.
    /// </summary>
    public static string Likert(int value, int scale) => (value / scale) switch
    {
        -3 or -2 or -1 => "Very Bad",
        0 or 1 => "Bad",
        2 => "Poor",
        3 or 4 => "Fair",
        5 => "Good",
        6 => "Very Good",
        7 or 8 => "Excellent",
        _ => "Superb",
    };

    /// <summary>The whole sheet. Mirrors display_char().</summary>
    public void DisplayAll()
    {
        PutCharacter();
        PutBuild();
        PutStats();
        PutExperience();
        PutAbilities();
    }

    /// <summary>Who they are. Mirrors put_character().</summary>
    public void PutCharacter()
    {
        _display.ClearScreen();
        _display.PutBuffer("Name        :", 2, 1);
        _display.PutBuffer("Race        :", 3, 1);
        _display.PutBuffer("Sex         :", 4, 1);
        _display.PutBuffer("Class       :", 5, 1);

        if (!_game.CharacterGenerated)
        {
            return;
        }

        _display.PutBuffer(Player.Name, 2, 15);
        _display.PutBuffer(GameTables.Races[Player.Race].Name, 3, 15);
        _display.PutBuffer(Player.Male ? "Male" : "Female", 4, 15);
        _display.PutBuffer(GameTables.Classes[Player.Class].Title, 5, 15);
    }

    /// <summary>Age, height, weight and standing. Mirrors put_misc1().</summary>
    public void PutBuild()
    {
        Number("Age          ", Player.Age, 2, 38);
        Number("Height       ", Player.Height, 3, 38);
        Number("Weight       ", Player.Weight, 4, 38);
        Number("Social Class ", Player.SocialClass, 5, 38);
    }

    /// <summary>
    /// The six stats and the four combat numbers. Mirrors put_stats().
    ///
    /// A drained stat is shown twice: what it is now, and what it was, so the
    /// player can see what there is to restore.
    /// </summary>
    public void PutStats()
    {
        for (int i = 0; i < Stat.Count; i++)
        {
            _display.PutBuffer(Core.Display.StatNames[i], 2 + i, 61);
            _display.PutBuffer(Core.Display.FormatStat(Player.UseStat[i]), 2 + i, 66);

            if (Player.MaxStat[i] > Player.CurrentStat[i])
            {
                _display.PutBuffer(Core.Display.FormatStat(Player.MaxStat[i]), 2 + i, 73);
            }
        }

        Number("+ To Hit    ", Player.DisplayedPlusToHit, 9, 1);
        Number("+ To Damage ", Player.DisplayedPlusToDamage, 10, 1);
        Number("+ To AC     ", Player.DisplayedToArmourClass, 11, 1);
        Number("  Total AC  ", Player.DisplayedArmourClass, 12, 1);
    }

    /// <summary>Levels, experience, gold and health. Mirrors put_misc2().</summary>
    public void PutExperience()
    {
        LongNumber("Level      ", Player.Level, 9, 28);
        LongNumber("Experience ", Player.Experience, 10, 28);
        LongNumber("Max Exp    ", Player.MaxExperience, 11, 28);

        if (Player.Level >= Player.MaxLevel)
        {
            _display.Print("Exp to Adv.: *******", 12, 28);
        }
        else
        {
            LongNumber("Exp to Adv.", ExperienceToAdvance(Player), 12, 28);
        }

        LongNumber("Gold       ", Player.Gold, 13, 28);

        Number("Max Hit Points ", Player.MaxHitPoints, 9, 52);
        Number("Cur Hit Points ", Player.CurrentHitPoints, 10, 52);
        Number("Max Mana       ", Player.MaxMana, 11, 52);
        Number("Cur Mana       ", Player.CurrentMana, 12, 52);
    }

    /// <summary>The eight abilities, as words. Mirrors put_misc3().</summary>
    public void PutAbilities()
    {
        _display.ClearFrom(14);

        Abilities abilities = Rate(Player);

        _display.PutBuffer("(Miscellaneous Abilities)", 15, 25);

        _display.PutBuffer("Fighting    :", 16, 1);
        _display.PutBuffer(Likert(abilities.Fighting, 12), 16, 15);
        _display.PutBuffer("Bows/Throw  :", 17, 1);
        _display.PutBuffer(Likert(abilities.Shooting, 12), 17, 15);
        _display.PutBuffer("Saving Throw:", 18, 1);
        _display.PutBuffer(Likert(abilities.SavingThrow, 6), 18, 15);

        _display.PutBuffer("Stealth     :", 16, 28);
        _display.PutBuffer(Likert(abilities.Stealth, 1), 16, 42);
        _display.PutBuffer("Disarming   :", 17, 28);
        _display.PutBuffer(Likert(abilities.Disarming, 8), 17, 42);
        _display.PutBuffer("Magic Device:", 18, 28);
        _display.PutBuffer(Likert(abilities.MagicDevice, 6), 18, 42);

        _display.PutBuffer("Perception  :", 16, 55);
        _display.PutBuffer(Likert(abilities.Perception, 3), 16, 69);
        _display.PutBuffer("Searching   :", 17, 55);
        _display.PutBuffer(Likert(abilities.Searching, 6), 17, 69);
        _display.PutBuffer("Infra-Vision:", 18, 55);
        _display.PutBuffer(abilities.Infravision, 18, 69);
    }

    /// <summary>The eight numbers the words are chosen from.</summary>
    public readonly record struct Abilities(
        int Fighting, int Shooting, int Perception, int Searching, int Stealth,
        int Disarming, int SavingThrow, int MagicDevice, string Infravision);

    /// <summary>
    /// Works out the eight abilities. Shared by the screen and the written
    /// character sheet, which say the same things in a different order.
    /// </summary>
    public static Abilities Rate(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        short[] adjust = GameTables.ClassLevelAdjust[player.Class];

        int fighting = player.BaseToHit
            + (player.PlusToHit * Combat.ToHitWeight)
            + (adjust[LevelSkill.Fighting] * player.Level);

        int shooting = player.BaseToHitBows
            + (player.PlusToHit * Combat.ToHitWeight)
            + (adjust[LevelSkill.Shooting] * player.Level);

        // Turned round so that a low frequency reads as a high perception, and
        // held at nought so it never reads as worse than very bad.
        int perception = Math.Max(0, 40 - player.SearchFrequency);

        // One higher, so the range starts at nought rather than minus one.
        int stealth = player.Stealth + 1;

        int disarming = player.Disarm
            + (2 * Stats.DisarmBonus(player))
            + Stats.Adjustment(player, Stat.Intelligence)
            + (adjust[LevelSkill.Disarming] * player.Level / 3);

        int saving = player.Save
            + Stats.Adjustment(player, Stat.Wisdom)
            + (adjust[LevelSkill.SaveAndMisc] * player.Level / 3);

        // FAITHFUL QUIRK: using a magical device is worked out from the saving
        // throw rather than from anything to do with devices, which is what the
        // original does.
        int device = player.Save
            + Stats.Adjustment(player, Stat.Intelligence)
            + (adjust[LevelSkill.MagicDevice] * player.Level / 3);

        return new Abilities(
            fighting, shooting, perception, player.Search, stealth,
            disarming, saving, device,
            (player.SeeInfrared * 10).ToString(CultureInfo.InvariantCulture) + " feet");
    }

    /// <summary>What the next level costs, before the class multiplier.</summary>
    public static int ExperienceToAdvance(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        return GameTables.PlayerExperience[player.Level - 1]
            * player.ExperienceFactor / 100;
    }

    /// <summary>A labelled number in six columns. Mirrors prt_num().</summary>
    private void Number(string label, int value, int row, int column) =>
        _display.PutBuffer(
            label + ": " + value.ToString(CultureInfo.InvariantCulture).PadLeft(6),
            row, column);

    /// <summary>A labelled number in seven columns. Mirrors prt_7lnum().</summary>
    private void LongNumber(string label, int value, int row, int column) =>
        _display.PutBuffer(
            label + ": " + value.ToString(CultureInfo.InvariantCulture).PadLeft(7),
            row, column);
}
