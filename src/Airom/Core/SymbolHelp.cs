// Ported from ident_char() in Umoria 5.6 source/help.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Saying what a character on the map stands for.
///
/// The list is the printable ASCII set in order, and the gaps in it are as
/// deliberate as the entries: a symbol nothing uses says so rather than being
/// left out. Once the symbol is named, every kind of creature drawn with it
/// that the player knows anything about is offered up in turn - so asking what
/// "d" means can walk through every dragon that has ever been met.
/// </summary>
public class SymbolHelp
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public SymbolHelp(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    /// <summary>
    /// Asks for a symbol and says what it is. Mirrors ident_char().
    /// </summary>
    public void IdentifySymbol()
    {
        if (!_display.GetCommand("Enter character to be identified :",
                                 out char symbol))
        {
            // FAITHFUL QUIRK: backing out still walks the monster memory
            // below, against whatever the last command left in the variable.
            // In the original that is uninitialised; here it is the escape,
            // which no creature is drawn with, so nothing comes of it.
            RecallEveryCreature(symbol);
            return;
        }

        _display.Print(DescribeSymbol(symbol), 0, 0);
        RecallEveryCreature(symbol);
    }

    /// <summary>
    /// What one symbol stands for. The player's own "@" is answered with their
    /// name, which is the one entry that is not a fixed string.
    /// </summary>
    private string DescribeSymbol(char symbol) => symbol switch
    {
        ' ' => "  - An open pit.",
        '!' => "! - A potion.",
        '"' => "\" - An amulet, periapt, or necklace.",
        '#' => "# - A stone wall.",
        '$' => "$ - Treasure.",

        // Veins are only drawn differently when the player has asked for it, so
        // what this symbol means depends on that option.
        '%' => _game.HighlightSeams ? "% - A magma or quartz vein." : "% - Not used.",

        '&' => "& - Treasure chest.",
        '\'' => "' - An open door.",
        '(' => "( - Soft armor.",
        ')' => ") - A shield.",
        '*' => "* - Gems.",
        '+' => "+ - A closed door.",
        ',' => ", - Food or mushroom patch.",
        '-' => "- - A wand",
        '.' => ". - Floor.",
        '/' => "/ - A pole weapon.",
        '1' => "1 - Entrance to General Store.",
        '2' => "2 - Entrance to Armory.",
        '3' => "3 - Entrance to Weaponsmith.",
        '4' => "4 - Entrance to Temple.",
        '5' => "5 - Entrance to Alchemy shop.",
        '6' => "6 - Entrance to Magic-Users store.",
        ':' => ": - Rubble.",
        ';' => "; - A loose rock.",
        '<' => "< - An up staircase.",
        '=' => "= - A ring.",
        '>' => "> - A down staircase.",
        '?' => "? - A scroll.",
        '@' => _game.Player.Name,
        'A' => "A - Giant Ant Lion.",
        'B' => "B - The Balrog.",
        'C' => "C - Gelatinous Cube.",
        'D' => "D - An Ancient Dragon (Beware).",
        'E' => "E - Elemental.",
        'F' => "F - Giant Fly.",
        'G' => "G - Ghost.",
        'H' => "H - Hobgoblin.",
        'J' => "J - Jelly.",
        'K' => "K - Killer Beetle.",
        'L' => "L - Lich.",
        'M' => "M - Mummy.",
        'O' => "O - Ooze.",
        'P' => "P - Giant humanoid.",
        'Q' => "Q - Quylthulg (Pulsing Flesh Mound).",
        'R' => "R - Reptile.",
        'S' => "S - Giant Scorpion.",
        'T' => "T - Troll.",
        'U' => "U - Umber Hulk.",
        'V' => "V - Vampire.",
        'W' => "W - Wight or Wraith.",
        'X' => "X - Xorn.",
        'Y' => "Y - Yeti.",
        '[' => "[ - Hard armor.",
        '\\' => "\\ - A hafted weapon.",
        ']' => "] - Misc. armor.",
        '^' => "^ - A trap.",
        '_' => "_ - A staff.",
        'a' => "a - Giant Ant.",
        'b' => "b - Giant Bat.",
        'c' => "c - Giant Centipede.",
        'd' => "d - Dragon.",
        'e' => "e - Floating Eye.",
        'f' => "f - Giant Frog.",
        'g' => "g - Golem.",
        'h' => "h - Harpy.",
        'i' => "i - Icky Thing.",
        'j' => "j - Jackal.",
        'k' => "k - Kobold.",
        'l' => "l - Giant Louse.",
        'm' => "m - Mold.",
        'n' => "n - Naga.",
        'o' => "o - Orc or Ogre.",
        'p' => "p - Person (Humanoid).",
        'q' => "q - Quasit.",
        'r' => "r - Rodent.",
        's' => "s - Skeleton.",
        't' => "t - Giant Tick.",
        'w' => "w - Worm or Worm Mass.",
        'y' => "y - Yeek.",
        'z' => "z - Zombie.",
        '{' => "{ - Arrow, bolt, or bullet.",
        '|' => "| - A sword or dagger.",
        '}' => "} - Bow, crossbow, or sling.",
        '~' => "~ - Miscellaneous item.",
        _ => "Not Used.",
    };

    /// <summary>
    /// Offers up the memory of every creature drawn with the symbol, newest in
    /// the table first. Asked about once; after that they simply follow one
    /// another until the player escapes or they run out.
    /// </summary>
    private void RecallEveryCreature(char symbol)
    {
        int shown = 0;

        for (int i = GameTables.CreatureList.Length - 1; i >= 0; i--)
        {
            if (GameTables.CreatureList[i].DisplayChar != symbol
                || !_loop.MonsterRecall.KnowsAnything(i))
            {
                continue;
            }

            if (shown == 0)
            {
                _display.PutBuffer("You recall those details? [y/n]", 0, 40);

                char answer = _display.ReadKey();

                if (answer != 'y' && answer != 'Y')
                {
                    break;
                }

                _display.EraseLine(0, 40);
                _display.SaveScreen();
            }

            shown++;

            char ended = _loop.MonsterRecall.Describe(i);
            _display.RestoreScreen();

            if (ended == Keys.Escape)
            {
                break;
            }
        }
    }
}
