// Ported from the prompting half of Umoria 5.6 source/create.c - choose_race,
// get_sex, get_class and create_character - together with get_name and
// change_name from source/misc3.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Rolling a character, with the player watching.
///
/// The arithmetic all belongs to <see cref="CharacterCreation"/>, which was
/// ported first so it could be compared without a terminal. This is the part
/// that asks: race, then sex, then as many rolls of the stats as the player
/// cares to sit through, then class, and finally a name.
///
/// The order matters and is not the obvious one. The race is chosen before
/// anything is rolled because it decides which history charts are used; the
/// class is chosen after the stats are settled because it adjusts them and
/// then works out hit points from what is left.
/// </summary>
public class CharacterMaker
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public CharacterMaker(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    private Player Player => _game.Player;

    /// <summary>
    /// How long the player is made to wait at the end. Umoria's
    /// PLAYER_EXIT_PAUSE, which the original explains as discouraging players
    /// from rolling characters all afternoon - "which can be VERY expensive CPU
    /// wise" on a shared 1989 machine.
    /// </summary>
    public const int ExitPause = 0;

    /// <summary>
    /// Rolls a character from beginning to end. Mirrors create_character().
    /// </summary>
    /// <returns>
    /// False when the player answered the last prompt with Q, which is the one
    /// chance to walk away from a character rather than play them.
    /// </returns>
    public bool Create()
    {
        _game.Player = new Player();
        var creation = new CharacterCreation(_game);

        _loop.CharacterSheet.PutCharacter();

        ChooseRace();
        ChooseSex();

        Roll(creation);

        // The prompt is printed once and then left standing: rerolling redraws
        // the character above it without touching this line.
        _display.ClearFrom(20);
        _display.PutBuffer(
            "Hit space to reroll or ESC to accept characteristics: ", 20, 2);

        while (true)
        {
            _display.MoveCursor(20, 56);
            char key = _display.ReadKey();

            if (key == Keys.Escape)
            {
                break;
            }

            if (key == ' ')
            {
                Roll(creation);
            }
            else
            {
                _display.Bell();
            }
        }

        ChooseClass(creation);
        creation.RollMoney(Player);

        _loop.CharacterSheet.PutStats();
        _loop.CharacterSheet.PutExperience();
        _loop.CharacterSheet.PutAbilities();

        GetName();

        // The original waits PLAYER_EXIT_PAUSE seconds before going, and says
        // in the same breath that machines slow enough to need the delay
        // should skip it. Every machine is now that machine.
        return !_display.PauseExit(23);
    }

    /// <summary>
    /// One roll of everything the player is being offered: the stats with the
    /// race already in them, a history, and a build.
    /// </summary>
    private void Roll(CharacterCreation creation)
    {
        creation.RollForRace(Player, Player.Race);
        creation.RollHistory(Player);
        creation.RollAgeHeightWeight(Player);

        PrintHistory();
        _loop.CharacterSheet.PutBuild();
        _loop.CharacterSheet.PutStats();
    }

    /// <summary>The race, which decides what histories and classes follow.</summary>
    private void ChooseRace()
    {
        _display.ClearFrom(20);
        _display.PutBuffer("Choose a race (? for Help):", 20, 2);

        int column = 2;
        int row = 21;

        for (int i = 0; i < GameTables.Races.Length; i++)
        {
            _display.PutBuffer(
                (char)('a' + i) + ") " + GameTables.Races[i].Name, row, column);

            column += 15;

            if (column > 70)
            {
                column = 2;
                row++;
            }
        }

        while (true)
        {
            _display.MoveCursor(20, 30);
            char key = _display.ReadKey();
            int chosen = key - 'a';

            if (chosen >= 0 && chosen < GameTables.Races.Length)
            {
                Player.Race = chosen;
                _display.PutBuffer(GameTables.Races[chosen].Name, 3, 15);
                return;
            }

            if (key == '?')
            {
                _loop.CharacterFile.ShowHelp("welcome.hlp");
            }
            else
            {
                _display.Bell();
            }
        }
    }

    private void ChooseSex()
    {
        _display.ClearFrom(20);
        _display.PutBuffer("Choose a sex (? for Help):", 20, 2);
        _display.PutBuffer("m) Male       f) Female", 21, 2);

        while (true)
        {
            _display.MoveCursor(20, 29);
            char key = _display.ReadKey();

            if (key is 'f' or 'F')
            {
                Player.Male = false;
                _display.PutBuffer("Female", 4, 15);
                return;
            }

            if (key is 'm' or 'M')
            {
                Player.Male = true;
                _display.PutBuffer("Male", 4, 15);
                return;
            }

            if (key == '?')
            {
                _loop.CharacterFile.ShowHelp("welcome.hlp");
            }
            else
            {
                _display.Bell();
            }
        }
    }

    /// <summary>
    /// The class, from the ones this race is allowed. The letters run over the
    /// allowed classes rather than over all of them, so what "c" means depends
    /// on the race chosen a moment ago.
    /// </summary>
    private void ChooseClass(CharacterCreation creation)
    {
        IReadOnlyList<int> allowed = CharacterCreation.AllowedClasses(Player.Race);

        _display.ClearFrom(20);
        _display.PutBuffer("Choose a class (? for Help):", 20, 2);

        int column = 2;
        int row = 21;

        for (int i = 0; i < allowed.Count; i++)
        {
            _display.PutBuffer(
                (char)('a' + i) + ") " + GameTables.Classes[allowed[i]].Title,
                row, column);

            column += 15;

            if (column > 70)
            {
                column = 2;
                row++;
            }
        }

        Player.Class = 0;

        while (true)
        {
            _display.MoveCursor(20, 31);
            char key = _display.ReadKey();
            int chosen = key - 'a';

            if (chosen >= 0 && chosen < allowed.Count)
            {
                _display.ClearFrom(20);
                _display.PutBuffer(GameTables.Classes[allowed[chosen]].Title, 5, 15);

                creation.ApplyClass(Player, allowed[chosen]);

                // Settling the stats again through the game's own path rather
                // than the creation code's, because this one has consequences:
                // a spellcaster is told here what they can learn.
                for (int i = 0; i < Stat.Count; i++)
                {
                    _loop.Stats.SetUseStat(i);
                }

                return;
            }

            if (key == '?')
            {
                _loop.CharacterFile.ShowHelp("welcome.hlp");
            }
            else
            {
                _display.Bell();
            }
        }
    }

    /// <summary>The four lines of background. Mirrors print_history().</summary>
    private void PrintHistory()
    {
        _display.PutBuffer("Character Background", 14, 27);

        for (int i = 0; i < Player.History.Length; i++)
        {
            _display.Print(Player.History[i], i + 15, 10);
        }
    }

    /// <summary>
    /// Asks for a name. Mirrors get_name().
    ///
    /// An empty answer is taken as a request for the name the machine knows the
    /// player by, which is whoever is logged in.
    /// </summary>
    public void GetName()
    {
        _display.Print(
            "Enter your player's name  [press <RETURN> when finished]", 21, 2);

        _display.PutBuffer(new string(' ', 23), 2, 15);

        if (!_display.GetString(2, 15, 23, out string name) || name.Length == 0)
        {
            name = UserName();
            _display.PutBuffer(name, 2, 15);
        }

        Player.Name = name;
        _display.ClearFrom(20);
    }

    /// <summary>
    /// Whoever is logged in, cut to what the name field holds. Mirrors
    /// user_name(), which reads the password file; here it is the login .NET
    /// reports, which on macOS and Linux comes from that same file.
    /// </summary>
    protected virtual string UserName()
    {
        string name = Environment.UserName;

        if (name.Length == 0)
        {
            name = "Player";
        }

        return name.Length > 22 ? name[..22] : name;
    }

    /// <summary>
    /// The character sheet, with the two things a player may do to it: write it
    /// out, or change the name on it. Mirrors change_name().
    /// </summary>
    public void ChangeName()
    {
        using IDisposable centred = _display.Centred();

        _loop.CharacterSheet.DisplayAll();

        while (true)
        {
            _display.Print(
                "<f>ile character description. <c>hange character name.", 21, 2);

            char key = _display.ReadKey();

            switch (key)
            {
                case 'c':
                    GetName();
                    return;

                case 'f':
                    _display.Print("File name:", 0, 0);

                    if (_display.GetString(0, 10, 60, out string name)
                        && name.Length > 0
                        && _loop.CharacterFile.Write(name))
                    {
                        return;
                    }

                    break;

                case Keys.Escape:
                case ' ':
                case '\n':
                case '\r':
                    return;

                default:
                    _display.Bell();
                    break;
            }
        }
    }

    /// <summary>
    /// Turns wizard mode on, once the player has agreed to give up their score.
    /// Mirrors enter_wiz_mode().
    /// </summary>
    public bool EnterWizardMode()
    {
        bool answer = false;

        if (_game.NoScore == 0)
        {
            _display.MessagePrint("Wizard mode is for debugging and experimenting.");

            answer = _display.GetCheck(
                "The game will not be scored if you enter wizard mode. Are you sure?");
        }

        if (_game.NoScore != 0 || answer)
        {
            _game.NoScore |= 0x2;
            _game.Wizard = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// The options screen. Mirrors set_options().
    ///
    /// One line per option with the cursor parked in the answer column: y and n
    /// set it and move on, return moves on without changing anything, and the
    /// list wraps rather than stopping at the end.
    /// </summary>
    public void SetOptions()
    {
        (string Prompt, Func<bool> Get, Action<bool> Set)[] options =
        [
            ("Running: cut known corners",
                () => _game.CutCorners, value => _game.CutCorners = value),
            ("Running: examine potential corners",
                () => _game.ExamineCorners, value => _game.ExamineCorners = value),
            ("Running: print self during run",
                () => _game.ShowSelfWhileRunning,
                value => _game.ShowSelfWhileRunning = value),
            ("Running: stop when map sector changes",
                () => _game.StopAtLevelBounds, value => _game.StopAtLevelBounds = value),
            ("Running: run through open doors",
                () => _game.IgnoreDoorsWhileRunning,
                value => _game.IgnoreDoorsWhileRunning = value),
            ("Prompt to pick up objects",
                () => _game.PromptBeforeCarrying,
                value => _game.PromptBeforeCarrying = value),
            ("Rogue like commands",
                () => _game.RogueLikeCommands, value => _game.RogueLikeCommands = value),
            ("Show weights in inventory",
                () => _game.ShowWeights, value => _game.ShowWeights = value),
            ("Highlight and notice mineral seams",
                () => _game.HighlightSeams, value => _game.HighlightSeams = value),
            ("Beep for invalid character",
                () => _game.SoundEnabled, value => _game.SoundEnabled = value),
            ("Display rest/repeat counts",
                () => _game.DisplayCounts, value => _game.DisplayCounts = value),
        ];

        _display.Print(
            "  ESC when finished, y/n to set options, <return> or - to move cursor",
            0, 0);

        for (int i = 0; i < options.Length; i++)
        {
            _display.Print(
                options[i].Prompt.PadRight(38) + ": " + (options[i].Get() ? "yes" : "no "),
                i + 1, 0);
        }

        _display.EraseLine(options.Length + 1, 0);

        int at = 0;

        while (true)
        {
            _display.MoveCursor(at + 1, 40);

            switch (_display.ReadKey())
            {
                case Keys.Escape:
                    return;

                case '-':
                    at = at > 0 ? at - 1 : options.Length - 1;
                    break;

                case ' ':
                case '\n':
                case '\r':
                    at = at + 1 < options.Length ? at + 1 : 0;
                    break;

                case 'y':
                case 'Y':
                    _display.PutBuffer("yes", at + 1, 40);
                    options[at].Set(true);
                    at = at + 1 < options.Length ? at + 1 : 0;
                    break;

                case 'n':
                case 'N':
                    _display.PutBuffer("no ", at + 1, 40);
                    options[at].Set(false);
                    at = at + 1 < options.Length ? at + 1 : 0;
                    break;

                default:
                    _display.Bell();
                    break;
            }
        }
    }

    /// <summary>
    /// What a new character is given. Mirrors char_inven_init().
    ///
    /// Everything is known from the moment it is handed over, since it came
    /// from a shop rather than out of the dungeon. A sword is also marked to
    /// show its numbers, which is what makes the starting stiletto read as a
    /// weapon rather than as an unidentified one.
    /// </summary>
    public void GiveStartingItems()
    {
        for (int i = 0; i < Inventory.Size; i++)
        {
            _game.Inventory[i].Clear();
        }

        _game.Inventory.Reset();

        foreach (int kind in GameTables.StartingItems[Player.Class])
        {
            var item = new InvenType();
            item.CopyFrom(kind);

            _game.Knowledge.MarkStoreBought(item);

            if (item.TVal == ItemCategory.Sword)
            {
                item.Identification |= Identification.ShowHitDam;
            }

            _game.Inventory.Carry(item);
        }

        // An odd place for it, as the original says, but no worse a place than
        // any other: nothing is known until something is learned.
        for (int i = 0; i < Player.SpellOrder.Length; i++)
        {
            Player.SpellOrder[i] = Player.NoSpell;
        }
    }
}
