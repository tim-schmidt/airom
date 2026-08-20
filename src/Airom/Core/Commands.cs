// Ported from the command translation in Umoria 5.6 source/dungeon.c and the
// direction reading in source/moria1.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Core;

/// <summary>
/// The two command sets and the translation between them.
///
/// Umoria accepts either the original keys or the rogue-like ones, and settles
/// the difference immediately: every command is converted to its rogue-like form
/// before anything looks at it, so only one set reaches the dispatch. That is
/// why the original set can map two keys onto one command and why some keys mean
/// nothing at all in one set and a command in the other.
/// </summary>
public static class Commands
{
    /// <summary>Anything illegal, which the dispatch reports rather than ignores.</summary>
    public const char Illegal = '~';

    /// <summary>Do nothing, and do not use up a turn.</summary>
    public const char Nothing = ' ';

    /// <summary>
    /// Turns a key from the original set into its rogue-like equivalent. Mirrors
    /// original_commands().
    ///
    /// Two of them - run and tunnel - need a direction before they can be
    /// translated at all, since the rogue-like set spells the direction into the
    /// command letter. <paramref name="readDirection"/> supplies it, and
    /// returning false there means the player abandoned the command.
    /// </summary>
    public static char ToRogueLike(char command, Func<(bool Taken, int Direction)> readDirection)
    {
        ArgumentNullException.ThrowIfNull(readDirection);

        switch (command)
        {
            case var _ when command == Keys.Control('K'): // exit
                return 'Q';

            case var _ when command == Keys.Control('J'):
            case var _ when command == Keys.Control('M'):
                return '+';

            // Already the same in both sets.
            case var _ when command == Keys.Control('P'): // repeat message
            case var _ when command == Keys.Control('W'): // wizard mode
            case var _ when command == Keys.Control('X'): // save and exit
            case var _ when command == Keys.Control('V'): // view licence
            case ' ':
            case '!':
            case '$':
            case '/':
            case '<':
            case '>':
            case '-':
            case '=':
            case '{':
            case '?':
            case 'A':
            case 'C':
            case 'D':
            case 'E':
            case 'F':
            case 'G':
            case 'M':
            case 'R':
            case 'V':
            case 'c':
            case 'd':
            case 'e':
            case 'i':
            case 'm':
            case 'o':
            case 'p':
            case 'q':
            case 'r':
            case 's':
            case 'v':
            case 'w':
                return command;

            case '.': // run in a direction
                return RunKey(readDirection);

            case 'T': // tunnel in a direction
                return TunnelKey(readDirection);

            // The number pad walks.
            case '1': return 'b';
            case '2': return 'j';
            case '3': return 'n';
            case '4': return 'h';
            case '5': return '.'; // rest one turn
            case '6': return 'l';
            case '7': return 'y';
            case '8': return 'k';
            case '9': return 'u';

            case 'B': return 'f'; // bash
            case 'L': return 'W'; // locate on the map
            case 'S': return '#'; // search mode
            case 'a': return 'z'; // aim a wand
            case 'b': return 'P'; // browse a book
            case 'f': return 't'; // fire
            case 'h': return '?'; // help
            case 'j': return 'S'; // jam a door
            case 'l': return 'x'; // look
            case 't': return 'T'; // take off
            case 'u': return 'Z'; // use a staff
            case 'x': return 'X'; // exchange weapons

            // Wizard commands.
            case var _ when command == Keys.Control('A'): // cure all
            case var _ when command == Keys.Control('D'): // jump levels
            case var _ when command == Keys.Control('I'): // identify
            case var _ when command == Keys.Control('T'): // teleport
            case var _ when command == Keys.Control('E'): // edit character
            case var _ when command == Keys.Control('F'): // genocide
            case var _ when command == Keys.Control('G'): // treasure
            case ':':
            case '@':
            case '+':
                return command;

            case var _ when command == Keys.Control('B'): return Keys.Control('O'); // objects
            case var _ when command == Keys.Control('H'): return '\\';              // wizard help
            case var _ when command == Keys.Control('L'): return '*';               // light the level
            case var _ when command == Keys.Control('U'): return '&';               // summon

            default:
                return Illegal;
        }
    }

    /// <summary>
    /// The run commands are the walk letters in upper case, which is why "." in
    /// the original set becomes a run rather than a step.
    /// </summary>
    private static char RunKey(Func<(bool Taken, int Direction)> readDirection)
    {
        (bool taken, int direction) = readDirection();
        if (!taken)
        {
            return Nothing;
        }

        return direction switch
        {
            1 => 'B',
            2 => 'J',
            3 => 'N',
            4 => 'H',
            6 => 'L',
            7 => 'Y',
            8 => 'K',
            9 => 'U',
            _ => Nothing,
        };
    }

    private static char TunnelKey(Func<(bool Taken, int Direction)> readDirection)
    {
        (bool taken, int direction) = readDirection();
        if (!taken)
        {
            return Nothing;
        }

        return direction switch
        {
            1 => Keys.Control('B'),
            2 => Keys.Control('J'),
            3 => Keys.Control('N'),
            4 => Keys.Control('H'),
            6 => Keys.Control('L'),
            7 => Keys.Control('Y'),
            8 => Keys.Control('K'),
            9 => Keys.Control('U'),
            _ => Nothing,
        };
    }

    /// <summary>
    /// Turns a rogue-like movement key into its number-pad digit. Mirrors
    /// map_roguedir().
    /// </summary>
    public static char MapRogueDirection(char command) => command switch
    {
        'h' => '4',
        'y' => '7',
        'k' => '8',
        'u' => '9',
        'l' => '6',
        'n' => '3',
        'j' => '2',
        'b' => '1',
        '.' => '5',
        _ => command,
    };

    /// <summary>
    /// Whether a command may be given a repeat count. Mirrors
    /// valid_countcommand().
    ///
    /// Only commands that make sense to do over and over take one: walking,
    /// tunnelling, searching, resting, opening. Anything that prompts, spends an
    /// item or leaves the level does not, so a mistyped count cannot quaff
    /// ninety-nine potions.
    /// </summary>
    public static bool AllowsCount(char command) => command switch
    {
        var _ when command == Keys.Control('P') => true,
        Keys.Escape => true,
        ' ' => true,
        '-' => true,
        'b' or 'f' or 'j' or 'n' or 'h' or 'l' or 'y' or 'k' or 'u' or '.' => true,
        'B' or 'J' or 'N' or 'H' or 'L' or 'Y' or 'K' or 'U' => true,
        'D' or 'R' or 'S' or 'o' or 's' or '+' => true,
        var _ when command == Keys.Control('Y') => true,
        var _ when command == Keys.Control('K') => true,
        var _ when command == Keys.Control('U') => true,
        var _ when command == Keys.Control('L') => true,
        var _ when command == Keys.Control('N') => true,
        var _ when command == Keys.Control('J') => true,
        var _ when command == Keys.Control('B') => true,
        var _ when command == Keys.Control('H') => true,
        var _ when command == Keys.Control('D') => true,
        var _ when command == Keys.Control('G') => true,
        _ => false,
    };
}
