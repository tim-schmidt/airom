// Ported from the score-file half of Umoria 5.6 source/death.c -
// display_scores, duplicate_character and highscores.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The table of who has died and how well.
///
/// The original shares one score file between every player on a Unix machine,
/// and goes to some trouble over it: the game runs setuid, the file is locked
/// while it is written, and each entry carries the user id of whoever earned
/// it. None of that applies here - this is one person's game on one machine -
/// so the user id is nought throughout, which is the case the original already
/// handles: with no user ids to tell players apart it falls back to the
/// character's birth date, and that is what stops one character filling the
/// table with entries as it is saved over and over.
///
/// The file itself is the original's, byte for byte: three version numbers
/// followed by a run of records in the save file's own encoding, ordered best
/// first. A new score is inserted by pushing everything below it down one.
/// </summary>
public class ScoreFile
{
    private readonly GameState _game;
    private readonly Display _display;

    public ScoreFile(GameState game, Display display)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);

        _game = game;
        _display = display;
    }

    private Player Player => _game.Player;

    /// <summary>How many entries the file will hold. Umoria's SCOREFILE_SIZE.</summary>
    public const int MaxEntries = 1000;

    /// <summary>How many fit on one page of the table.</summary>
    private const int PageSize = 20;

    /// <summary>
    /// Where the score file lives. A file beside the player's own data rather
    /// than a shared one under the game, since there is nobody to share with.
    /// </summary>
    public static string DefaultPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AIrom", "scores.dat");

    /// <summary>The version this game writes. Mirrors CUR_VERSION_MAJ and friends.</summary>
    private static readonly byte[] Version = [5, 6, 0];

    /// <summary>
    /// Reads the whole table. An empty or missing file is an empty table, and
    /// one from a different version of the game is refused with a message.
    /// </summary>
    /// <returns>Every entry, best first, or nothing when the file was refused.</returns>
    public List<HighScore>? Read()
    {
        if (!File.Exists(DefaultPath))
        {
            return [];
        }

        using FileStream file = File.OpenRead(DefaultPath);
        var reader = new SaveCipher(file);

        int major = file.ReadByte();
        int minor = file.ReadByte();
        int patch = file.ReadByte();

        if (major < 0)
        {
            // An empty file.
            return [];
        }

        if (!IsSupported(major, minor, patch))
        {
            _display.MessagePrint(
                "Sorry. This scorefile is from a different version of umoria.");

            _display.MessagePrint(null);
            return null;
        }

        var entries = new List<HighScore>();

        while (entries.Count < MaxEntries)
        {
            HighScore score = reader.ReadHighScore();

            if (reader.AtEnd)
            {
                break;
            }

            entries.Add(score);
        }

        return entries;
    }

    /// <summary>
    /// Which score files this game will read. Anything from 5.2.2 to now, as
    /// the original allows.
    /// </summary>
    private static bool IsSupported(int major, int minor, int patch) =>
        major == Version[0]
        && minor <= Version[1]
        && !(minor == Version[1] && patch > Version[2])
        && !(minor == 2 && patch < 2)
        && minor >= 2;

    /// <summary>Writes the whole table back, best first.</summary>
    private static void Write(List<HighScore> entries)
    {
        string? directory = Path.GetDirectoryName(DefaultPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream file = File.Create(DefaultPath);

        file.WriteByte(Version[0]);
        file.WriteByte(Version[1]);
        file.WriteByte(Version[2]);

        var writer = new SaveCipher(file);

        foreach (HighScore entry in entries)
        {
            writer.WriteHighScore(entry);
        }
    }

    /// <summary>
    /// Shows the table, twenty to a page. Mirrors display_scores().
    /// </summary>
    /// <param name="playerOnly">
    /// Whether to show only this player's entries. It means nothing here - on a
    /// single-user game every entry is theirs - but it is what the original
    /// asks for and it is carried through rather than dropped.
    /// </param>
    public void Display(bool playerOnly)
    {
        List<HighScore>? entries = Read();

        if (entries is null)
        {
            return;
        }

        int rank = 1;
        int at = 0;

        do
        {
            _display.ClearScreen();

            int line = 1;

            while (at < entries.Count && line < 21)
            {
                HighScore score = entries[at];

                // Every entry belongs to the player, so there is nothing for
                // playerOnly to leave out.
                if (!playerOnly || score.Uid == 0)
                {
                    line++;

                    _display.Print(string.Concat(
                        rank.ToString(CultureInfo.InvariantCulture).PadRight(4),
                        score.Points.ToString(CultureInfo.InvariantCulture).PadLeft(8),
                        " ", Fit(score.Name, 19).PadRight(19),
                        " ", score.Sex.ToString(),
                        " ", Fit(RaceName(score.Race), 10).PadRight(10),
                        " ", Fit(ClassName(score.Class), 7).PadRight(7),
                        score.Level.ToString(CultureInfo.InvariantCulture).PadLeft(3),
                        " ", Fit(score.DiedFrom, 22)), line, 0);
                }

                rank++;
                at++;
            }

            _display.Print(
                "Rank  Points Name              Sex Race       Class  Lvl Killed By",
                0, 0);

            _display.EraseLine(1, 0);
            _display.Print("[Press any key to continue.]", 23, 23);

            if (_display.ReadKey() == Keys.Escape)
            {
                break;
            }
        }
        while (at < entries.Count);
    }

    /// <summary>
    /// Whether this character is already in the table under a name it saved
    /// with. Mirrors duplicate_character().
    ///
    /// With no user ids, a character is recognised by when it was rolled
    /// together with its sex, race and class - which is what stops a saved game
    /// being scored twice.
    /// </summary>
    public bool IsDuplicate()
    {
        List<HighScore>? entries = Read();

        if (entries is null)
        {
            return false;
        }

        char sex = Player.Male ? 'M' : 'F';

        foreach (HighScore score in entries)
        {
            if (score.Uid == 0
                && score.BirthDate == _game.BirthDate
                && score.Class == Player.Class
                && score.Race == Player.Race
                && score.Sex == sex
                && score.DiedFrom != "(saved)")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Puts this character into the table. Mirrors highscores().
    ///
    /// A game restored from a panic save is not scored, and neither is one
    /// played in wizard mode - both of which are decided before anything is
    /// read.
    /// </summary>
    public void Record(int points, string diedFrom)
    {
        ArgumentNullException.ThrowIfNull(diedFrom);

        _display.ClearScreen();

        if (_game.NoScore != 0)
        {
            return;
        }

        if (_game.PanicSaved)
        {
            _display.MessagePrint(
                "Sorry, scores for games restored from panic save files are not saved.");

            return;
        }

        List<HighScore>? entries = Read();

        if (entries is null)
        {
            return;
        }

        var entry = new HighScore
        {
            Points = points,
            BirthDate = _game.BirthDate,
            Uid = 0,
            MaxHitPoints = Player.MaxHitPoints,
            CurrentHitPoints = Player.CurrentHitPoints,
            DungeonLevel = _game.DungeonLevel,
            Level = Player.Level,
            MaxDungeonLevel = Player.MaxDungeonLevel,
            Sex = Player.Male ? 'M' : 'F',
            Race = Player.Race,
            Class = Player.Class,
            Name = Fit(Player.Name, HighScore.NameLength - 1),
            DiedFrom = Fit(WithoutArticle(diedFrom), HighScore.DiedFromLength - 1),
        };

        int at = 0;

        while (at < entries.Count && entry.Points < entries[at].Points)
        {
            // One entry per character: a saved game already in the table is
            // replaced rather than added to.
            if (IsSameCharacter(entry, entries[at]))
            {
                return;
            }

            at++;

            if (at >= MaxEntries)
            {
                return;
            }
        }

        entries.Insert(at, entry);

        // And anything below it that is the same character is dropped, which is
        // what the original's rewrite-downwards loop amounts to.
        for (int i = at + 1; i < entries.Count; i++)
        {
            if (IsSameCharacter(entry, entries[i]))
            {
                entries.RemoveAt(i);
                break;
            }
        }

        if (entries.Count > MaxEntries)
        {
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }

        Write(entries);
    }

    /// <summary>
    /// Whether two entries are the same character. With no user ids this is the
    /// birth date and the build, and only against an entry that was saved
    /// rather than died.
    /// </summary>
    private static bool IsSameCharacter(HighScore entry, HighScore other) =>
        other.DiedFrom == "(saved)"
        && entry.BirthDate == other.BirthDate
        && entry.Sex == other.Sex
        && entry.Race == other.Race
        && entry.Class == other.Class;

    /// <summary>
    /// Strips a leading "a" or "an" from what killed the player, so the table
    /// reads "Giant Rat" rather than "a Giant Rat".
    /// </summary>
    private static string WithoutArticle(string diedFrom)
    {
        if (!diedFrom.StartsWith('a'))
        {
            return diedFrom;
        }

        int at = 1;

        if (at < diedFrom.Length && diedFrom[at] == 'n')
        {
            at++;
        }

        while (at < diedFrom.Length && char.IsWhiteSpace(diedFrom[at]))
        {
            at++;
        }

        return diedFrom[at..];
    }

    private static string Fit(string text, int width) =>
        text.Length > width ? text[..width] : text;

    private static string RaceName(int race) =>
        race >= 0 && race < GameTables.Races.Length
            ? GameTables.Races[race].Name
            : "?";

    private static string ClassName(int characterClass) =>
        characterClass >= 0 && characterClass < GameTables.Classes.Length
            ? GameTables.Classes[characterClass].Title
            : "?";
}
