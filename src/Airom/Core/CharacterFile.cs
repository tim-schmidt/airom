// Ported from Umoria 5.6 source/files.c - helpfile, print_objects and
// file_character.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using System.Text;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Writing things out to files, and reading the help text back in.
///
/// The character sheet written here is the same one the screen shows, laid out
/// for a page rather than a terminal - it is what a player keeps when the
/// character is gone.
/// </summary>
public class CharacterFile
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public CharacterFile(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    private Player Player => _game.Player;

    /// <summary>The form feed the original separates the pages with.</summary>
    private const char PageBreak = '\f';

    /// <summary>
    /// Where the help text lives. Beside the program, since it ships with it.
    /// </summary>
    public static string HelpDirectory { get; set; } = Path.Combine(
        AppContext.BaseDirectory, "help");

    /// <summary>
    /// Shows a help file, a screenful at a time. Mirrors helpfile().
    ///
    /// A missing file is said aloud rather than being an error: the game is
    /// perfectly playable without its help text.
    /// </summary>
    public void ShowHelp(string filename)
    {
        ArgumentNullException.ThrowIfNull(filename);

        string path = Path.Combine(HelpDirectory, filename);

        if (!File.Exists(path))
        {
            _display.Print("Can not find help file \"" + filename + "\".", 0, 0);
            return;
        }

        using StreamReader file = File.OpenText(path);

        _display.SaveScreen();

        // The end is noticed by a read that fails rather than by looking
        // ahead, which is why a file of exactly twenty-three lines to the page
        // ends on a blank one: the original does the same.
        bool ended = false;

        while (!ended)
        {
            _display.ClearScreen();

            for (int i = 0; i < 23; i++)
            {
                string? line = file.ReadLine();

                if (line is null)
                {
                    ended = true;
                    break;
                }

                _display.PutBuffer(line, i, 0);
            }

            _display.Print("[Press any key to continue.]", 23, 23);

            if (_display.ReadKey() == Keys.Escape)
            {
                break;
            }
        }

        _display.RestoreScreen();
    }

    /// <summary>
    /// Writes the character out. Mirrors file_character().
    ///
    /// Two pages: who they are and what they can do, then everything they are
    /// carrying.
    /// </summary>
    /// <returns>False when the file could not be written, so the caller asks again.</returns>
    public bool Write(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (File.Exists(path)
            && !_display.GetCheck("Replace existing file " + path + "?"))
        {
            return false;
        }

        try
        {
            File.WriteAllText(path, Compose());
        }
        catch (IOException)
        {
            _display.MessagePrint("Can't open file " + path + ":");
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            _display.MessagePrint("Can't open file " + path + ":");
            return false;
        }

        _display.Print("Writing character sheet...", 0, 0);
        _display.Print("Completed.", 0, 0);
        return true;
    }

    /// <summary>The sheet itself, as text. Split out so it can be tested.</summary>
    public string Compose()
    {
        var page = new StringBuilder();

        page.Append(PageBreak).Append("\n\n");

        const string Colon = ":";

        Row(page, " Name", Colon, Player.Name, " Age", Player.Age,
            Stat.Strength, "STR");

        Row(page, " Race", Colon, GameTables.Races[Player.Race].Name,
            " Height", Player.Height, Stat.Intelligence, "INT");

        Row(page, " Sex", Colon, Player.Male ? "Male" : "Female",
            " Weight", Player.Weight, Stat.Wisdom, "WIS");

        Row(page, " Class", Colon, GameTables.Classes[Player.Class].Title,
            " Social Class ", Player.SocialClass, Stat.Dexterity, "DEX");

        page.Append(" Title").Append(Colon.PadLeft(8)).Append(' ')
            .Append(Display.TitleFor(Player).PadRight(23))
            .Append(new string(' ', 22))
            .Append("   CON : ").Append(Display.FormatStat(Player.UseStat[Stat.Constitution]))
            .Append('\n');

        page.Append(new string(' ', 34)).Append(new string(' ', 26))
            .Append("   CHR : ").Append(Display.FormatStat(Player.UseStat[Stat.Charisma]))
            .Append("\n\n");

        page.Append(" + To Hit    : ").Append(Six(Player.DisplayedPlusToHit))
            .Append("       Level      : ").Append(Seven(Player.Level))
            .Append("    Max Hit Points : ").Append(Six(Player.MaxHitPoints)).Append('\n');

        page.Append(" + To Damage : ").Append(Six(Player.DisplayedPlusToDamage))
            .Append("       Experience : ").Append(Seven(Player.Experience))
            .Append("    Cur Hit Points : ").Append(Six(Player.CurrentHitPoints)).Append('\n');

        page.Append(" + To AC     : ").Append(Six(Player.DisplayedToArmourClass))
            .Append("       Max Exp    : ").Append(Seven(Player.MaxExperience))
            .Append("    Max Mana").Append(Colon.PadLeft(8)).Append(' ')
            .Append(Six(Player.MaxMana)).Append('\n');

        page.Append("   Total AC  : ").Append(Six(Player.DisplayedArmourClass));

        page.Append(Player.Level >= Player.MaxLevel
            ? "       Exp to Adv : *******"
            : "       Exp to Adv : " + Seven(CharacterSheet.ExperienceToAdvance(Player)));

        page.Append("    Cur Mana").Append(Colon.PadLeft(8)).Append(' ')
            .Append(Six(Player.CurrentMana)).Append('\n');

        page.Append(new string(' ', 28)).Append("Gold").Append(Colon.PadLeft(8))
            .Append(' ').Append(Seven(Player.Gold)).Append("\n\n");

        CharacterSheet.Abilities abilities = CharacterSheet.Rate(Player);

        page.Append("(Miscellaneous Abilities)\n\n");

        page.Append(" Fighting    : ")
            .Append(CharacterSheet.Likert(abilities.Fighting, 12).PadRight(10))
            .Append("   Stealth     : ")
            .Append(CharacterSheet.Likert(abilities.Stealth, 1).PadRight(10))
            .Append("   Perception  : ")
            .Append(CharacterSheet.Likert(abilities.Perception, 3)).Append('\n');

        page.Append(" Bows/Throw  : ")
            .Append(CharacterSheet.Likert(abilities.Shooting, 12).PadRight(10))
            .Append("   Disarming   : ")
            .Append(CharacterSheet.Likert(abilities.Disarming, 8).PadRight(10))
            .Append("   Searching   : ")
            .Append(CharacterSheet.Likert(abilities.Searching, 6)).Append('\n');

        page.Append(" Saving Throw: ")
            .Append(CharacterSheet.Likert(abilities.SavingThrow, 6).PadRight(10))
            .Append("   Magic Device: ")
            .Append(CharacterSheet.Likert(abilities.MagicDevice, 6).PadRight(10))
            .Append("   Infra-Vision: ")
            .Append(abilities.Infravision).Append("\n\n");

        page.Append("Character Background\n");

        foreach (string line in Player.History)
        {
            page.Append(' ').Append(line).Append('\n');
        }

        page.Append("\n  [Character's Equipment List]\n\n");

        if (_game.Inventory.EquipmentCount == 0)
        {
            page.Append("  Character has no equipment in use.\n");
        }
        else
        {
            int letter = 0;

            for (int i = Inventory.WieldSlot; i < Inventory.Size; i++)
            {
                InvenType worn = _game.Inventory[i];

                if (worn.TVal == ItemCategory.Nothing)
                {
                    continue;
                }

                page.Append("  ").Append((char)('a' + letter)).Append(") ")
                    .Append(SlotLabel(i).PadRight(19)).Append(": ")
                    .Append(_game.Names.Describe(worn, withArticle: true)).Append('\n');

                letter++;
            }
        }

        page.Append(PageBreak).Append("\n\n");
        page.Append("  [General Inventory List]\n\n");

        if (_game.Inventory.Count == 0)
        {
            page.Append("  Character has no objects in inventory.\n");
        }
        else
        {
            for (int i = 0; i < _game.Inventory.Count; i++)
            {
                page.Append((char)('a' + i)).Append(") ")
                    .Append(_game.Names.Describe(_game.Inventory[i], withArticle: true))
                    .Append('\n');
            }
        }

        page.Append(PageBreak);
        return page.ToString();
    }

    /// <summary>
    /// One of the four rows that pair a fact about the character with a number
    /// about their build and a stat.
    /// </summary>
    private void Row(
        StringBuilder page, string label, string colon, string value,
        string measure, int amount, int stat, string statLabel)
    {
        page.Append(label).Append(colon.PadLeft(14 - label.Length)).Append(' ')
            .Append(value.PadRight(23))
            .Append(measure).Append(colon.PadLeft(15 - measure.Length)).Append(' ')
            .Append(Six(amount))
            .Append("   ").Append(statLabel).Append(" : ")
            .Append(Display.FormatStat(Player.UseStat[stat]))
            .Append('\n');
    }

    /// <summary>How the written sheet names each worn slot.</summary>
    private static string SlotLabel(int slot) => slot switch
    {
        Inventory.WieldSlot => "You are wielding",
        Inventory.HeadSlot => "Worn on head",
        Inventory.NeckSlot => "Worn around neck",
        Inventory.BodySlot => "Worn on body",
        Inventory.ArmSlot => "Worn on shield arm",
        Inventory.HandsSlot => "Worn on hands",
        Inventory.RightRingSlot => "Right ring finger",
        Inventory.LeftRingSlot => "Left  ring finger",
        Inventory.FeetSlot => "Worn on feet",
        Inventory.OuterSlot => "Worn about body",
        Inventory.LightSlot => "Light source is",
        Inventory.AuxiliarySlot => "Secondary weapon",
        _ => "*Unknown value*",
    };

    /// <summary>
    /// Writes out a sample of the objects a level would produce. Mirrors
    /// print_objects(), a wizard command for looking at the tables.
    /// </summary>
    public void PrintObjects()
    {
        _display.Print("Produce objects on what level?: ", 0, 0);

        if (!_display.GetString(0, 32, 10, out string levelText))
        {
            return;
        }

        int level = ParseNumber(levelText);

        _display.Print("Produce how many objects?: ", 0, 0);

        if (!_display.GetString(0, 27, 10, out string countText))
        {
            return;
        }

        int count = ParseNumber(countText);
        bool small = _display.GetCheck("Small objects only?");

        if (count <= 0 || level < 0 || level > 1200)
        {
            return;
        }

        count = Math.Min(count, 10000);

        _display.Print("File name: ", 0, 0);

        if (!_display.GetString(0, 11, 64, out string path) || path.Length == 0)
        {
            return;
        }

        _display.Print(
            count.ToString(CultureInfo.InvariantCulture)
            + " random objects being produced...", 0, 0);

        _display.Refresh();

        var page = new StringBuilder();
        page.Append("*** Random Object Sampling:\n");
        page.Append("*** ").Append(count.ToString(CultureInfo.InvariantCulture))
            .Append(" objects\n");
        page.Append("*** For Level ").Append(level.ToString(CultureInfo.InvariantCulture))
            .Append("\n\n\n");

        var generator = new DungeonGenerator(_game);
        var sample = new InvenType();

        for (int i = 0; i < count; i++)
        {
            sample.CopyFrom(ObjectLevels.Sorted[generator.GetObjectNumber(level, small)]);
            _game.Enchantment.Apply(sample, level);

            // Priced and named as a shop would, so the list reads as what the
            // player would meet rather than as raw table entries.
            _game.Knowledge.MarkStoreBought(sample);

            if ((sample.Flags & ItemFlags.Cursed) != 0)
            {
                ItemKnowledge.AddInscription(sample, Identification.Damned);
            }

            page.Append(sample.Level.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(_game.Names.Describe(sample, withArticle: true)).Append('\n');
        }

        try
        {
            File.WriteAllText(path, page.ToString());
        }
        catch (IOException)
        {
            _display.Print("Can't open file " + path + ":", 0, 0);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            _display.Print("Can't open file " + path + ":", 0, 0);
            return;
        }

        _display.Print("Completed.", 0, 0);
    }

    private static string Six(int value) =>
        value.ToString(CultureInfo.InvariantCulture).PadLeft(6);

    private static string Seven(int value) =>
        value.ToString(CultureInfo.InvariantCulture).PadLeft(7);

    /// <summary>What atoi() makes of what was typed.</summary>
    private static int ParseNumber(string text)
    {
        int at = 0;

        while (at < text.Length && char.IsWhiteSpace(text[at]))
        {
            at++;
        }

        int start = at;

        if (at < text.Length && (text[at] == '+' || text[at] == '-'))
        {
            at++;
        }

        int digits = at;

        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            at++;
        }

        return at == digits
            ? 0
            : int.TryParse(
                text.AsSpan(start, at - start), CultureInfo.InvariantCulture,
                out int value)
                ? value
                : 0;
    }
}
