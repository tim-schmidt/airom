// Ported from Umoria 5.6 source/main.c - the option flags, the savefile the
// game opens, the starting kit, and the loop that runs from a new character to
// a gravestone.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// What the command line asked for. Mirrors the option loop at the top of
/// main().
/// </summary>
public sealed class Options
{
    /// <summary>-n: start a new character, whatever is saved.</summary>
    public bool NewGame { get; set; }

    /// <summary>-s or -S: show the score table and stop.</summary>
    public bool ShowScores { get; set; }

    /// <summary>-S shows the whole table; -s shows only this player's part.</summary>
    public bool ScoresForPlayerOnly { get; set; }

    /// <summary>-w: ask for wizard mode, optionally with a seed after it.</summary>
    public bool WantsWizard { get; set; }

    /// <summary>The seed given after -w, or nought to take one from the clock.</summary>
    public uint Seed { get; set; }

    /// <summary>
    /// -r or -o: force the command set one way or the other. Applied after the
    /// savefile is read, since a savefile carries the choice too.
    /// </summary>
    public bool ForceCommandSet { get; set; }

    /// <summary>Which way -r/-o forced it: -r rogue-like, -o original.</summary>
    public bool ForceRogueLike { get; set; }

    /// <summary>The savefile named on the command line, if one was.</summary>
    public string? SaveFile { get; set; }

    /// <summary>Set when the arguments made no sense.</summary>
    public bool Usage { get; set; }

    /// <summary>
    /// Reads the arguments. Mirrors main()'s loop, which takes options until it
    /// meets something that does not begin with a dash and treats that as the
    /// savefile.
    /// </summary>
    public static Options Parse(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var options = new Options();
        int at = 0;

        while (at < arguments.Length && arguments[at].StartsWith('-'))
        {
            string argument = arguments[at];
            char letter = argument.Length > 1 ? argument[1] : '\0';

            switch (letter)
            {
                case 'N':
                case 'n':
                    options.NewGame = true;
                    break;

                case 'O':
                case 'o':
                    options.ForceCommandSet = true;
                    options.ForceRogueLike = false;
                    break;

                case 'R':
                case 'r':
                    options.ForceCommandSet = true;
                    options.ForceRogueLike = true;
                    break;

                case 'S':
                    options.ShowScores = true;
                    options.ScoresForPlayerOnly = true;
                    break;

                case 's':
                    options.ShowScores = true;
                    options.ScoresForPlayerOnly = false;
                    break;

                case 'W':
                case 'w':
                    options.WantsWizard = true;

                    if (argument.Length > 2
                        && uint.TryParse(argument[2..], out uint seed))
                    {
                        options.Seed = seed;
                    }

                    break;

                default:
                    options.Usage = true;
                    return options;
            }

            at++;
        }

        if (at < arguments.Length)
        {
            options.SaveFile = arguments[at];
        }

        return options;
    }
}

/// <summary>
/// One sitting at the game, from the command line to the gravestone. Mirrors
/// main().
///
/// A session either picks a saved game up or rolls a new character, and then
/// runs one dungeon level after another until the character dies or the player
/// puts the game away. What the original does with signals, setuid and the
/// shared score file has no counterpart here and is left out; everything else
/// happens in the order it happens in the original, which matters, because the
/// seeds are set before the shops are stocked and the shops before anything is
/// generated.
/// </summary>
public class Session
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Session(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    private Player Player => _game.Player;

    /// <summary>Whether the command set starts out rogue-like. Umoria's ROGUE_LIKE.</summary>
    public const bool RogueLikeByDefault = false;

    /// <summary>
    /// Where a saved game is looked for when the command line does not say.
    /// The original consults MORIA_SAV, then HOME; here it is the player's own
    /// application data, which is the Windows answer to the same question.
    /// </summary>
    public static string DefaultSaveFile()
    {
        string? named = Environment.GetEnvironmentVariable("MORIA_SAV");

        return string.IsNullOrEmpty(named) ? SaveFile.DefaultPath : named;
    }

    /// <summary>
    /// Plays a game. Mirrors main(), less the parts that belong to a 1989 Unix
    /// machine.
    /// </summary>
    /// <returns>The process exit code.</returns>
    public int Play(Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Usage)
        {
            _display.Print("Usage: airom [-norsw] [savefile]", 0, 0);
            _display.PauseLine(23);
            return 2;
        }

        _game.RogueLikeCommands = RogueLikeByDefault;

        if (options.ShowScores)
        {
            new ScoreFile(_game, _display).Display(options.ScoresForPlayerOnly);
            return 0;
        }

        _game.Wizard = false;
        bool wantsWizard = options.WantsWizard;

        string savePath = options.SaveFile ?? DefaultSaveFile();

        // The introduction, and with it whatever the game has to say about
        // itself before anyone plays.
        ShowNews();

        // Everything the tables need, in the order main() does it: the seeds
        // first, since the shops are stocked from them.
        _game.InitSeeds(options.Seed);
        _game.Stores.Initialise();

        // A restored file may hold only the monster memory, in which case this
        // says false and a new character is rolled who remembers what the last
        // one learned. It may also resurrect a dead character, which says true
        // but leaves the level to be generated.
        bool restored = false;
        bool generate = true;

        if (!options.NewGame && File.Exists(savePath))
        {
            restored = _loop.SaveFile.Restore(savePath, out generate);
        }

        // Wizard mode is entered before the character is shown, but only after
        // the file is read, in case that was a resurrection.
        if (wantsWizard && !_loop.CharacterMaker.EnterWizardMode())
        {
            return 0;
        }

        if (restored)
        {
            _loop.CharacterMaker.ChangeName();

            // A character restored from a panic save may already be dead.
            if (Player.CurrentHitPoints < 0)
            {
                _loop.Dead = true;
            }
        }
        else
        {
            NewCharacter();
            generate = true;
        }

        if (options.ForceCommandSet)
        {
            _game.RogueLikeCommands = options.ForceRogueLike;
        }

        _game.MagicInit();

        _display.ClearScreen();
        _display.PrintStatBlock(Player);

        var builder = new DungeonGenerator(_game, _display);

        if (generate)
        {
            builder.Generate();
        }

        while (!_loop.Dead && !_loop.Leaving)
        {
            _loop.Run();

            if (!_loop.Dead && !_loop.Leaving)
            {
                builder.Generate();
            }
        }

        // A saved game has already ended itself: the turn is -1, so exit_game()
        // buries nobody, but the score table still wants its entry.
        _loop.Death.ExitGame();
        return 0;
    }

    /// <summary>
    /// Rolls someone new and hands them their belongings. Mirrors the second
    /// half of main()'s "create character" branch.
    /// </summary>
    private void NewCharacter()
    {
        _loop.CharacterMaker.Create();

        _game.BirthDate = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        _loop.CharacterMaker.GiveStartingItems();

        Player.Food = 7500;
        Player.FoodDigested = 2;

        // A spellcaster is told what they can learn before they set out. The
        // screen is cleared between the two so the list is readable, which is
        // why the clearing happens in a different place for each realm.
        switch (GameTables.Classes[Player.Class].SpellRealm)
        {
            case SpellRealm.Mage:
                _display.ClearScreen();
                _loop.Magic.CalcSpells(Stat.Intelligence);
                _loop.Magic.CalcMana(Stat.Intelligence);
                break;

            case SpellRealm.Priest:
                _loop.Magic.CalcSpells(Stat.Wisdom);
                _display.ClearScreen();
                _loop.Magic.CalcMana(Stat.Wisdom);
                break;
        }

        // From here the character is worth saving, which is what this flag
        // means to everything that might have to save one in a hurry.
        _game.CharacterGenerated = true;
    }

    /// <summary>
    /// The news file, shown before anything else. Mirrors the tail of
    /// read_times(), less the operating-hours check: this game has no closing
    /// time, having no other players to be fair to.
    /// </summary>
    private void ShowNews()
    {
        string path = Path.Combine(CharacterFile.HelpDirectory, "news");

        if (!File.Exists(path))
        {
            return;
        }

        _display.ClearScreen();

        int row = 0;

        foreach (string line in File.ReadLines(path))
        {
            if (row >= 23)
            {
                break;
            }

            _display.PutBuffer(line, row, 0);
            row++;
        }

        _display.PauseLine(23);
    }
}
