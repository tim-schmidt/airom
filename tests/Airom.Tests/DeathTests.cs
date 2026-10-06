using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the end of the game: the gravestone, the score arithmetic, the
/// encoding of a score record and the table those records go into.
///
/// The stone, the character sheet, the written character file and the record
/// encoding are all diffed against the C oracle - 160 death and sheet runs and
/// 18 score runs, the last of which compares every byte. The table itself is
/// the one part that was rewritten rather than ported, since the original's
/// shared setuid file has no meaning for one player on one machine, so it
/// is these tests that hold it up.
/// </summary>
[Collection("game files")]
public class DeathTests
{
    private static (GameState, Display, GameLoop, MemoryScreen) Game(uint seed = 1)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.Player = new Player();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SetKeys(new string(' ', 200));
        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        return (game, display, loop, screen);
    }

    /// <summary>
    /// Thirty-one columns, with the odd space going on the right - which is
    /// what puts an even-length name half a column left of centre on the stone.
    /// </summary>
    [Theory]
    [InlineData("", 31)]
    [InlineData("Alatariel", 31)]
    [InlineData("Bob", 31)]
    public void Centre_FillsThirtyOneColumns(string name, int width)
    {
        Assert.Equal(width, Death.Centre(name).Length);
        Assert.Equal(name, Death.Centre(name).Trim());
    }

    [Fact]
    public void Centre_PutsTheOddSpaceOnTheRight()
    {
        // Four characters: 13 spaces before, 14 after.
        string centred = Death.Centre("Fred");

        Assert.Equal(13, centred.Length - centred.TrimStart().Length);
        Assert.Equal(14, centred.Length - centred.TrimEnd().Length);
    }

    /// <summary>A score never falls: a saved game keeps whatever it had.</summary>
    [Fact]
    public void TotalPoints_NeverFallsBelowTheScoreAlreadyEarned()
    {
        (GameState game, Display display, GameLoop loop, _) = Game();

        game.Player.MaxExperience = 100;
        game.Player.MaxDungeonLevel = 1;
        game.MaxScore = 50000;

        Assert.Equal(50000, new Death(game, display, loop).TotalPoints());
    }

    /// <summary>
    /// Depth is counted twice over: once for the deepest level ever reached and
    /// once for the level died on.
    /// </summary>
    [Fact]
    public void TotalPoints_CountsBothDepths()
    {
        (GameState game, Display display, GameLoop loop, _) = Game();

        game.Player.MaxExperience = 1000;
        game.Player.MaxDungeonLevel = 10;
        game.Player.Gold = 550;
        game.DungeonLevel = 4;

        // 1000 experience + 10 levels at a hundred + 5 for the gold + 4 at fifty.
        Assert.Equal(1000 + 1000 + 5 + 200, new Death(game, display, loop).TotalPoints());
    }

    /// <summary>
    /// The winner is taken out of the dungeon, given a fortune and killed off by
    /// old age, which is the only way to leave the game having won it.
    /// </summary>
    [Fact]
    public void Crown_MakesTheWinnerRichAndOld()
    {
        (GameState game, Display display, GameLoop loop, MemoryScreen screen) = Game();

        game.Player.Level = 30;
        game.Player.Gold = 100;
        game.Player.MaxExperience = 1000;

        // Already at full experience, so the restoration Crown does first has
        // nothing to give back and the level only grows by the constant.
        game.Player.Experience = 1000;
        game.DungeonLevel = 50;

        new Death(game, display, loop).Crown();

        Assert.Equal(0, game.DungeonLevel);
        Assert.Equal("Ripe Old Age", game.DiedFrom);
        Assert.Equal(30 + Player.MaxLevel, game.Player.Level);
        Assert.Equal(250100, game.Player.Gold);
        Assert.Equal(5001000, game.Player.MaxExperience);
        Assert.Equal(game.Player.MaxExperience, game.Player.Experience);
    }

    // ------------------------------------------------------------ the cipher

    /// <summary>
    /// Every byte is exclusive-ored with the one before it, so a record cannot
    /// be read except from its own beginning.
    /// </summary>
    [Fact]
    public void SaveCipher_ChainsEachByteOnTheLast()
    {
        using var bytes = new MemoryStream();
        var writer = new SaveCipher(bytes) { Key = 0 };

        writer.WriteByte(0);
        writer.WriteByte(0);
        writer.WriteByte(0);

        // With a key of nought the chain is transparent; with anything else it
        // is not, and the same input gives different bytes.
        Assert.Equal([0, 0, 0], bytes.ToArray());

        using var scrambled = new MemoryStream();
        var second = new SaveCipher(scrambled) { Key = 0x9F };

        second.WriteByte(0);
        second.WriteByte(0);

        Assert.NotEqual<byte[]>([0, 0], scrambled.ToArray());
    }

    [Fact]
    public void SaveCipher_ReadsBackWhatItWrote()
    {
        using var bytes = new MemoryStream();
        var writer = new SaveCipher(bytes) { Key = 0x5A };

        writer.WriteByte(200);
        writer.WriteShort(40000);
        writer.WriteLong(3000000000u);
        writer.WriteFixed("Alatariel", 27);

        bytes.Position = 0;
        var reader = new SaveCipher(bytes) { Key = 0x5A };

        Assert.Equal(200, reader.ReadByte());
        Assert.Equal(40000, reader.ReadShort());
        Assert.Equal(3000000000u, reader.ReadLong());
        Assert.Equal("Alatariel", reader.ReadFixed(27));
    }

    /// <summary>
    /// A record is fixed width, so the table can be read straight through and a
    /// short file can be told from a truncated one.
    /// </summary>
    [Fact]
    public void WriteHighScore_IsAlwaysTheSameLength()
    {
        using var bytes = new MemoryStream();
        var writer = new SaveCipher(bytes);

        writer.WriteHighScore(Score(1000, "Alatariel", "a Giant Rat"));
        long first = bytes.Length;

        writer.WriteHighScore(Score(1, "A", "z"));

        Assert.Equal(first, bytes.Length - first);
        Assert.Equal(SaveCipher.HighScoreLength, first);
    }

    [Fact]
    public void ReadHighScore_ReportsTheEndOfTheFile()
    {
        using var bytes = new MemoryStream();
        var writer = new SaveCipher(bytes);

        writer.WriteHighScore(Score(1000, "Alatariel", "a Giant Rat"));

        bytes.Position = 0;
        var reader = new SaveCipher(bytes);

        HighScore back = reader.ReadHighScore();

        Assert.False(reader.AtEnd);
        Assert.Equal("Alatariel", back.Name);
        Assert.Equal("a Giant Rat", back.DiedFrom);

        reader.ReadHighScore();
        Assert.True(reader.AtEnd);
    }

    // ------------------------------------------------------------ the table

    /// <summary>
    /// Runs a body with the score file pointed at a directory of its own, so a
    /// test never touches the player's real table.
    /// </summary>
    private static void WithScoreFile(Action<string> body)
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "airom-scores-" + Guid.NewGuid().ToString("N"));

        string was = ScoreFile.DefaultPath;
        ScoreFile.DefaultPath = Path.Combine(directory, "scores.dat");

        try
        {
            body(ScoreFile.DefaultPath);
        }
        finally
        {
            ScoreFile.DefaultPath = was;

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static ScoreFile Table(GameState game, Display display, int birth, string name)
    {
        game.BirthDate = birth;
        game.Player.Name = name;
        game.Player.MaxHitPoints = 100;
        game.Player.CurrentHitPoints = 50;
        game.Player.Level = 10;
        game.Player.MaxDungeonLevel = 20;
        game.DungeonLevel = 15;

        return new ScoreFile(game, display);
    }

    [Fact]
    public void Record_KeepsTheTableInOrderOfScore()
    {
        WithScoreFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            Table(game, display, 1, "Least").Record(100, "a Giant Rat");
            Table(game, display, 2, "Most").Record(9000, "an Ancient Dragon");
            Table(game, display, 3, "Middle").Record(500, "a Kobold");

            List<HighScore> entries = new ScoreFile(game, display).Read()!;

            Assert.Equal(["Most", "Middle", "Least"], entries.Select(e => e.Name));
        });
    }

    /// <summary>
    /// The article is dropped, so the table reads "killed by Giant Rat" in a
    /// column that has no room for the rest.
    ///
    /// FAITHFUL QUIRK: only "a" and "an" are dropped. "the Balrog" keeps its
    /// article, and always has.
    /// </summary>
    [Fact]
    public void Record_DropsTheArticleFromWhatKilledThem()
    {
        WithScoreFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            Table(game, display, 1, "One").Record(100, "a Giant Rat");
            Table(game, display, 2, "Two").Record(90, "an Ancient Dragon");
            Table(game, display, 3, "Three").Record(80, "the Balrog");
            Table(game, display, 4, "Four").Record(70, "Quitting");

            List<HighScore> entries = new ScoreFile(game, display).Read()!;

            Assert.Equal(
                ["Giant Rat", "Ancient Dragon", "the Balrog", "Quitting"],
                entries.Select(e => e.DiedFrom));
        });
    }

    /// <summary>
    /// One entry per character. With no user ids to tell players apart it is the
    /// birth date that does it, which is what stops a character saved over and
    /// over from filling the table.
    /// </summary>
    [Fact]
    public void Record_ReplacesAnEarlierSaveOfTheSameCharacter()
    {
        WithScoreFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            Table(game, display, 700, "Alatariel").Record(100, "(saved)");
            Table(game, display, 700, "Alatariel").Record(500, "(saved)");
            Table(game, display, 700, "Alatariel").Record(900, "a Giant Rat");

            List<HighScore> entries = new ScoreFile(game, display).Read()!;

            Assert.Single(entries);
            Assert.Equal(900, entries[0].Points);
            Assert.Equal("Giant Rat", entries[0].DiedFrom);
        });
    }

    /// <summary>A death is final: it is never replaced by a later entry.</summary>
    [Fact]
    public void Record_KeepsEveryDeathOfTheSameCharacter()
    {
        WithScoreFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            Table(game, display, 700, "Alatariel").Record(900, "a Giant Rat");
            Table(game, display, 700, "Alatariel").Record(100, "a Kobold");

            Assert.Equal(2, new ScoreFile(game, display).Read()!.Count);
        });
    }

    [Fact]
    public void IsDuplicate_FindsACharacterAlreadyInTheTable()
    {
        WithScoreFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            Assert.False(Table(game, display, 700, "Alatariel").IsDuplicate());

            Table(game, display, 700, "Alatariel").Record(900, "a Giant Rat");

            Assert.True(Table(game, display, 700, "Alatariel").IsDuplicate());
            Assert.False(Table(game, display, 701, "Someone Else").IsDuplicate());
        });
    }

    /// <summary>A saved game is not a death, so it does not count as one.</summary>
    [Fact]
    public void IsDuplicate_IgnoresASavedGame()
    {
        WithScoreFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            Table(game, display, 700, "Alatariel").Record(900, "(saved)");

            Assert.False(Table(game, display, 700, "Alatariel").IsDuplicate());
        });
    }

    /// <summary>
    /// A game restored from a panic save is not scored, and neither is one
    /// played in wizard mode.
    /// </summary>
    [Fact]
    public void Record_RefusesAPanicSaveOrAWizard()
    {
        WithScoreFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            game.PanicSaved = true;
            Table(game, display, 700, "Alatariel").Record(900, "a Giant Rat");

            game.PanicSaved = false;
            game.NoScore = 1;
            Table(game, display, 701, "Wizard").Record(900, "a Giant Rat");

            Assert.Empty(new ScoreFile(game, display).Read()!);
        });
    }

    /// <summary>A file from another version of the game is refused, not read.</summary>
    [Fact]
    public void Read_RefusesAFileFromAnotherVersion()
    {
        WithScoreFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [4, 0, 0]);

            Assert.Null(new ScoreFile(game, display).Read());
        });
    }

    [Fact]
    public void Read_TreatsAMissingFileAsAnEmptyTable()
    {
        WithScoreFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            Assert.Empty(new ScoreFile(game, display).Read()!);
        });
    }

    // ------------------------------------------------------------- the files

    /// <summary>
    /// Runs a body with the help directory pointed at one of its own, so a test
    /// never depends on what shipped beside the program.
    /// </summary>
    private static void WithHelpFile(string name, string[] lines, Action<GameState, Display, GameLoop, MemoryScreen> body)
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "airom-help-" + Guid.NewGuid().ToString("N"));

        string was = CharacterFile.HelpDirectory;
        Directory.CreateDirectory(directory);
        CharacterFile.HelpDirectory = directory;

        try
        {
            File.WriteAllLines(Path.Combine(directory, name), lines);

            (GameState game, Display display, GameLoop loop, MemoryScreen screen) = Game();
            body(game, display, loop, screen);
        }
        finally
        {
            CharacterFile.HelpDirectory = was;
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Help is shown twenty-three lines at a time.</summary>
    [Fact]
    public void ShowHelp_ShowsAPageAtATime()
    {
        string[] lines = Enumerable.Range(0, 30)
            .Select(i => "line " + i.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        WithHelpFile("test.hlp", lines, (game, display, loop, screen) =>
        {
            // One key turns the first page; the second ends it.
            screen.SetKeys("  ");
            loop.CharacterFile.ShowHelp("test.hlp");

            // The screen is put back the way it was found.
            Assert.Equal(string.Empty, screen.GetRow(0).Trim());
        });
    }

    /// <summary>
    /// FAITHFUL QUIRK: the end of the file is noticed by a read that fails, not
    /// by looking ahead, so a file that fills its last page exactly is followed
    /// by a blank one.
    /// </summary>
    [Fact]
    public void ShowHelp_AsksForOneKeyPerPageAndOneMore()
    {
        string[] lines = Enumerable.Range(0, 23)
            .Select(i => "line " + i.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        WithHelpFile("test.hlp", lines, (game, display, loop, screen) =>
        {
            // Two keys: one for the full page, one for the blank page after it.
            screen.SetKeys("  ");
            loop.CharacterFile.ShowHelp("test.hlp");
        });
    }

    /// <summary>Escape leaves the help without reading the rest of it.</summary>
    [Fact]
    public void ShowHelp_StopsOnEscape()
    {
        string[] lines = Enumerable.Range(0, 100)
            .Select(i => "line " + i.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        WithHelpFile("test.hlp", lines, (game, display, loop, screen) =>
        {
            screen.SetKeys(Keys.Escape.ToString());
            loop.CharacterFile.ShowHelp("test.hlp");
        });
    }

    /// <summary>
    /// A missing help file is said aloud rather than being an error: the game
    /// is perfectly playable without its help text.
    /// </summary>
    [Fact]
    public void ShowHelp_SaysSoWhenTheFileIsMissing()
    {
        WithHelpFile("test.hlp", ["nothing"], (game, display, loop, screen) =>
        {
            loop.CharacterFile.ShowHelp("absent.hlp");

            Assert.Contains("Can not find help file", screen.GetRow(0));
        });
    }

    /// <summary>
    /// Every name a help key can pass to ShowHelp, and the news file the game
    /// shows before anything else.
    ///
    /// These are the strings the commands hold, not a listing of the folder:
    /// the point is to catch a file that was renamed, dropped, or never copied
    /// out of the source tree, any of which would leave the reader itself
    /// working perfectly and every help key in the game saying "Can not find".
    /// </summary>
    private static readonly string[] ShippedHelp =
    [
        "welcome.hlp",  // ? at any of the three creation prompts
        "origcmds.hlp", // ? with the original keys
        "roglcmds.hlp", // ? with the rogue-like keys
        "owizcmds.hlp", // backslash, wizard, original keys
        "rwizcmds.hlp", // backslash, wizard, rogue-like keys
        "version.hlp",  // v
        "COPYING",      // control-V
        "news",         // shown once, before the game starts
    ];

    /// <summary>Where the help text ships: beside the program.</summary>
    private static string ShippedHelpDirectory =>
        Path.Combine(AppContext.BaseDirectory, "help");

    /// <summary>
    /// Every help file the game can ask for is where it looks for it, and has
    /// something in it.
    /// </summary>
    [Fact]
    public void ShippedHelp_IsBesideTheProgram()
    {
        foreach (string name in ShippedHelp)
        {
            string path = Path.Combine(ShippedHelpDirectory, name);

            Assert.True(File.Exists(path), name + " does not ship beside the program");
            Assert.NotEmpty(File.ReadAllText(path));
        }
    }

    /// <summary>
    /// Each of them reads back onto the screen.
    ///
    /// The other help tests write a file of their own, which proves the reader
    /// and proves nothing about what shipped. This one opens the real thing and
    /// looks at the page while the key is being asked for - after the call the
    /// screen has been restored, so what the player saw is gone.
    /// </summary>
    [Fact]
    public void ShippedHelp_ReadsOntoTheScreen()
    {
        string was = CharacterFile.HelpDirectory;
        CharacterFile.HelpDirectory = ShippedHelpDirectory;

        try
        {
            foreach (string name in ShippedHelp)
            {
                (_, _, GameLoop loop, MemoryScreen screen) = Game();
                screen.SetKeys(new string(Keys.Escape, 200));

                string[] lines = File.ReadAllLines(
                    Path.Combine(ShippedHelpDirectory, name));

                int first = Array.FindIndex(lines, line => line.Trim().Length > 0);
                Assert.True(first >= 0, name + " has no text in it");

                string? shown = null;
                screen.BeforeReadKey = () => shown ??= screen.GetRow(first);

                loop.CharacterFile.ShowHelp(name);

                Assert.DoesNotContain("Can not find", screen.GetRow(0));
                Assert.NotNull(shown);

                // Trimmed the way put_buffer trims it: the screen is 80 columns
                // and the last one is left alone.
                string expected = lines[first].Length > 79
                    ? lines[first][..79]
                    : lines[first];

                Assert.Equal(expected.TrimEnd(), shown!.TrimEnd());
            }
        }
        finally
        {
            CharacterFile.HelpDirectory = was;
        }
    }

    /// <summary>
    /// The written character starts and ends with a page break, so it prints as
    /// three pages on a printer that honours them.
    /// </summary>
    [Fact]
    public void Compose_IsThreePages()
    {
        (GameState game, _, GameLoop loop, _) = Game();

        game.Player.Name = "Alatariel";

        string sheet = loop.CharacterFile.Compose();

        Assert.StartsWith("", sheet);
        Assert.EndsWith("", sheet);
        Assert.Equal(3, sheet.Count(c => c == ''));
        Assert.Contains(" Name        : Alatariel", sheet);
    }

    /// <summary>Writing over a file asks first, and takes no for an answer.</summary>
    [Fact]
    public void Write_AsksBeforeReplacingAFile()
    {
        (GameState game, _, GameLoop loop, MemoryScreen screen) = Game();

        game.Player.Name = "Alatariel";

        string path = Path.Combine(
            Path.GetTempPath(), "airom-sheet-" + Guid.NewGuid().ToString("N") + ".txt");

        try
        {
            File.WriteAllText(path, "not a character sheet");

            screen.SetKeys("n");
            Assert.False(loop.CharacterFile.Write(path));
            Assert.Equal("not a character sheet", File.ReadAllText(path));

            screen.SetKeys("y");
            Assert.True(loop.CharacterFile.Write(path));
            Assert.Contains("Alatariel", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static HighScore Score(int points, string name, string diedFrom) => new()
    {
        Points = points,
        BirthDate = 700000000,
        Uid = 0,
        MaxHitPoints = 100,
        CurrentHitPoints = 50,
        DungeonLevel = 10,
        Level = 5,
        MaxDungeonLevel = 12,
        Sex = 'M',
        Race = 0,
        Class = 0,
        Name = name,
        DiedFrom = diedFrom,
    };
}
