using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the outermost layer: what the command line asks for, what a new
/// character is handed, and the screens that stand between a player and a
/// game.
///
/// The rolling and the prompting are both diffed against the C oracle - 64
/// runs of the create mode, screen and character together - so these hold up
/// the parts with no counterpart to compare against: the arguments, which the
/// original reads from a 1989 Unix command line, and the options screen.
/// </summary>
[Collection("game files")]
public class SessionTests
{
    private static (GameState, Display, GameLoop, MemoryScreen) Game(uint seed = 1)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SetKeys(new string(' ', 200));

        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        return (game, display, loop, screen);
    }

    [Fact]
    public void Parse_TakesNothingAsAnOrdinaryGame()
    {
        Options options = Options.Parse([]);

        Assert.False(options.NewGame);
        Assert.False(options.WantsWizard);
        Assert.False(options.ShowScores);
        Assert.False(options.ForceCommandSet);
        Assert.False(options.Usage);
        Assert.Null(options.SaveFile);
    }

    [Theory]
    [InlineData("-n")]
    [InlineData("-N")]
    public void Parse_TakesNewGame(string argument)
    {
        Assert.True(Options.Parse([argument]).NewGame);
    }

    /// <summary>
    /// The command set can be forced either way, and is applied after the
    /// savefile is read - a savefile carries the choice too, and the flag is
    /// meant to override it.
    /// </summary>
    [Theory]
    [InlineData("-r", true)]
    [InlineData("-R", true)]
    [InlineData("-o", false)]
    [InlineData("-O", false)]
    public void Parse_ForcesTheCommandSet(string argument, bool rogueLike)
    {
        Options options = Options.Parse([argument]);

        Assert.True(options.ForceCommandSet);
        Assert.Equal(rogueLike, options.ForceRogueLike);
    }

    /// <summary>A wizard may name the seed, which is the point of the flag.</summary>
    [Fact]
    public void Parse_TakesASeedAfterTheWizardFlag()
    {
        Options options = Options.Parse(["-w12345"]);

        Assert.True(options.WantsWizard);
        Assert.Equal(12345u, options.Seed);
    }

    [Fact]
    public void Parse_TakesTheWizardFlagWithoutASeed()
    {
        Options options = Options.Parse(["-w"]);

        Assert.True(options.WantsWizard);
        Assert.Equal(0u, options.Seed);
    }

    /// <summary>-S is the whole table; -s is only this player's part.</summary>
    [Theory]
    [InlineData("-S", true)]
    [InlineData("-s", false)]
    public void Parse_TakesTheScoreFlags(string argument, bool playerOnly)
    {
        Options options = Options.Parse([argument]);

        Assert.True(options.ShowScores);
        Assert.Equal(playerOnly, options.ScoresForPlayerOnly);
    }

    /// <summary>
    /// Options stop at the first argument that does not begin with a dash, and
    /// that argument is the savefile.
    /// </summary>
    [Fact]
    public void Parse_TakesASaveFileAfterTheOptions()
    {
        Options options = Options.Parse(["-n", "-w", @"C:\games\moria.sav"]);

        Assert.True(options.NewGame);
        Assert.True(options.WantsWizard);
        Assert.Equal(@"C:\games\moria.sav", options.SaveFile);
    }

    [Fact]
    public void Parse_ReportsAnUnknownFlag()
    {
        Assert.True(Options.Parse(["-q"]).Usage);
    }

    /// <summary>The original's MORIA_SAV is honoured, as a path in its own right.</summary>
    [Fact]
    public void DefaultSaveFile_PrefersTheEnvironment()
    {
        string? was = Environment.GetEnvironmentVariable("MORIA_SAV");

        try
        {
            Environment.SetEnvironmentVariable("MORIA_SAV", @"C:\games\named.sav");
            Assert.Equal(@"C:\games\named.sav", Session.DefaultSaveFile());

            Environment.SetEnvironmentVariable("MORIA_SAV", null);
            Assert.Equal(SaveFile.DefaultPath, Session.DefaultSaveFile());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MORIA_SAV", was);
        }
    }

    // ------------------------------------------------------------ the outfit

    /// <summary>
    /// Everything a character sets out with is known from the moment it is
    /// handed over: it came from a shop, not out of the dungeon.
    /// </summary>
    [Fact]
    public void GiveStartingItems_HandsOverFiveKnownThings()
    {
        for (int characterClass = 0; characterClass < GameTables.Classes.Length;
            characterClass++)
        {
            (GameState game, _, GameLoop loop, _) = Game();

            game.Player.Class = characterClass;
            loop.CharacterMaker.GiveStartingItems();

            Assert.Equal(5, game.Inventory.Count);

            for (int i = 0; i < game.Inventory.Count; i++)
            {
                InvenType item = game.Inventory[i];

                Assert.NotEqual(ItemCategory.Nothing, item.TVal);
                Assert.NotEqual(0, item.Identification & Identification.StoreBought);
            }
        }
    }

    /// <summary>
    /// A sword is marked to show its numbers, which is what makes the starting
    /// stiletto read as a weapon rather than as something unidentified.
    /// </summary>
    [Fact]
    public void GiveStartingItems_ShowsTheNumbersOnAStartingSword()
    {
        (GameState game, _, GameLoop loop, _) = Game();

        // The warrior, whose kit includes a weapon.
        game.Player.Class = 0;
        loop.CharacterMaker.GiveStartingItems();

        InvenType? sword = Enumerable.Range(0, game.Inventory.Count)
            .Select(i => game.Inventory[i])
            .FirstOrDefault(item => item.TVal == ItemCategory.Sword);

        Assert.NotNull(sword);
        Assert.NotEqual(0, sword.Identification & Identification.ShowHitDam);
    }

    /// <summary>Nothing is known until something is learned.</summary>
    [Fact]
    public void GiveStartingItems_LeavesTheSpellOrderEmpty()
    {
        (GameState game, _, GameLoop loop, _) = Game();

        game.Player.SpellOrder[0] = 3;
        loop.CharacterMaker.GiveStartingItems();

        Assert.All(game.Player.SpellOrder, entry => Assert.Equal(Player.NoSpell, entry));
    }

    // ----------------------------------------------------------- the screens

    /// <summary>
    /// Wizard mode costs the player their score, and asks before taking it.
    /// </summary>
    [Fact]
    public void EnterWizardMode_AsksBeforeSpoilingTheScore()
    {
        (GameState game, _, GameLoop loop, MemoryScreen screen) = Game();

        // Space then the answer, over and over: the space flushes the -more-
        // that the warning leaves waiting, and the answer is read past any
        // spaces after it.
        screen.SetKeys(string.Concat(Enumerable.Repeat(" n", 50)));
        Assert.False(loop.CharacterMaker.EnterWizardMode());
        Assert.False(game.Wizard);
        Assert.Equal(0, game.NoScore);

        screen.SetKeys(string.Concat(Enumerable.Repeat(" y", 50)));
        Assert.True(loop.CharacterMaker.EnterWizardMode());
        Assert.True(game.Wizard);
        Assert.Equal(0x2, game.NoScore & 0x2);
    }

    /// <summary>
    /// A game already unscored does not ask again: there is nothing left to
    /// give up.
    /// </summary>
    [Fact]
    public void EnterWizardMode_DoesNotAskTwice()
    {
        (GameState game, _, GameLoop loop, MemoryScreen screen) = Game();

        game.NoScore = 0x1;
        screen.SetKeys(string.Empty);

        Assert.True(loop.CharacterMaker.EnterWizardMode());
        Assert.True(game.Wizard);
    }

    /// <summary>
    /// The options screen sets one option per keypress and moves the cursor on,
    /// wrapping round at the end of the list.
    /// </summary>
    [Fact]
    public void SetOptions_SetsEachOptionInTurn()
    {
        (GameState game, _, GameLoop loop, MemoryScreen screen) = Game();

        game.CutCorners = false;
        game.ExamineCorners = false;
        game.ShowSelfWhileRunning = true;

        // Yes, yes, no, then escape.
        screen.SetKeys("yyn" + Keys.Escape);
        loop.CharacterMaker.SetOptions();

        Assert.True(game.CutCorners);
        Assert.True(game.ExamineCorners);
        Assert.False(game.ShowSelfWhileRunning);
    }

    /// <summary>Return moves the cursor on without changing anything.</summary>
    [Fact]
    public void SetOptions_LeavesAnOptionAloneOnReturn()
    {
        (GameState game, _, GameLoop loop, MemoryScreen screen) = Game();

        game.CutCorners = true;
        game.ExamineCorners = false;

        screen.SetKeys("\rn" + Keys.Escape);
        loop.CharacterMaker.SetOptions();

        Assert.True(game.CutCorners);
        Assert.False(game.ExamineCorners);
    }

    /// <summary>
    /// The name is asked for, and an empty answer is taken as a request for
    /// whoever the machine knows the player as.
    /// </summary>
    [Fact]
    public void GetName_TakesATypedName()
    {
        (GameState game, _, GameLoop loop, MemoryScreen screen) = Game();

        game.Player = new Player();
        screen.SetKeys("Alatariel\r" + new string(' ', 20));

        loop.CharacterMaker.GetName();

        Assert.Equal("Alatariel", game.Player.Name);
    }

    // ------------------------------------------------------- a whole sitting

    /// <summary>
    /// The whole thing, driven by a script: roll a human warrior, arrive in the
    /// town, and quit.
    ///
    /// This is the one test that runs main() from end to end, so it is the one
    /// that would notice the pieces having come apart - a character rolled but
    /// never given a level to stand on, or a quit that never reaches the score
    /// table.
    /// </summary>
    [Fact]
    public void Play_RollsACharacterAndReachesTheTown()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "airom-play-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        string wasScores = ScoreFile.DefaultPath;
        string wasHelp = CharacterFile.HelpDirectory;

        ScoreFile.DefaultPath = Path.Combine(directory, "scores.dat");
        CharacterFile.HelpDirectory = Path.Combine(directory, "help");

        try
        {
            (GameState game, _, GameLoop loop, MemoryScreen screen) = Game();

            // Race a, sex m, accept the roll, class a, a name, then quit and
            // agree to it. Quitting is control-K rather than Q: with the
            // original command set, Q is not a command at all. The escapes at
            // the end walk out of the gravestone and the score table.
            screen.SetKeys(
                "am" + Keys.Escape + "aTester\r"
                + string.Concat(Enumerable.Repeat(" " + Keys.Control('K') + " y", 100))
                + new string(Keys.Escape, 200));

            int result = new Session(game, new Display(game, screen), loop)
                .Play(new Options { NewGame = true, SaveFile = Path.Combine(directory, "game.sav") });

            Assert.Equal(0, result);

            // A character was rolled, given a kit, and put in the town.
            Assert.True(game.CharacterGenerated);
            Assert.Equal("Tester", game.Player.Name);
            Assert.Equal(5, game.Inventory.Count);
            // Fed for a while: the belly starts at 7500 and the turns that
            // passed have taken a little off it.
            Assert.InRange(game.Player.Food, 7000, 7500);
            Assert.Equal(0, game.DungeonLevel);

            // And they quit, which is a death like any other as far as the
            // score table is concerned.
            Assert.True(loop.Dead);
            Assert.Equal("Quitting", game.DiedFrom);
            Assert.NotEmpty(new ScoreFile(game, new Display(game, screen)).Read()!);
        }
        finally
        {
            ScoreFile.DefaultPath = wasScores;
            CharacterFile.HelpDirectory = wasHelp;
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The last prompt of character creation offers a way out, and taking it
    /// leaves rather than starting the game.
    ///
    /// Nothing has been generated at that point - no level, no score, no save -
    /// so leaving costs nothing and records nothing. It is the one chance to
    /// look at a rolled character and decide not to play them.
    /// </summary>
    [Fact]
    public void Play_LeavesWhenTheLastPromptIsAnsweredWithQ()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "airom-quit-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        string wasScores = ScoreFile.DefaultPath;
        string wasHelp = CharacterFile.HelpDirectory;

        ScoreFile.DefaultPath = Path.Combine(directory, "scores.dat");
        CharacterFile.HelpDirectory = Path.Combine(directory, "help");

        try
        {
            (GameState game, _, GameLoop loop, MemoryScreen screen) = Game();

            string save = Path.Combine(directory, "game.sav");

            // Race a, sex m, accept the roll, class a, a name - and then Q at
            // the pause that ends creation.
            screen.SetKeys("am" + Keys.Escape + "aQuitter\r" + "Q"
                + new string(Keys.Escape, 100));

            int result = new Session(game, new Display(game, screen), loop)
                .Play(new Options { NewGame = true, SaveFile = save });

            Assert.Equal(0, result);

            // The character was rolled but never became a game: no level, no
            // file, and nothing on the board.
            Assert.False(game.CharacterGenerated, "the game started anyway");
            Assert.False(File.Exists(save), "leaving wrote a savefile");
            Assert.Empty(new ScoreFile(game, new Display(game, screen)).Read()!);
        }
        finally
        {
            ScoreFile.DefaultPath = wasScores;
            CharacterFile.HelpDirectory = wasHelp;
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Any other key carries on into the game.</summary>
    [Fact]
    public void Play_CarriesOnWhenTheLastPromptIsAnsweredWithAnythingElse()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "airom-carry-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        string wasScores = ScoreFile.DefaultPath;
        string wasHelp = CharacterFile.HelpDirectory;

        ScoreFile.DefaultPath = Path.Combine(directory, "scores.dat");
        CharacterFile.HelpDirectory = Path.Combine(directory, "help");

        try
        {
            (GameState game, _, GameLoop loop, MemoryScreen screen) = Game();

            screen.SetKeys("am" + Keys.Escape + "aPlayer\r" + " "
                + string.Concat(Enumerable.Repeat(" " + Keys.Control('K') + " y", 60))
                + new string(Keys.Escape, 200));

            new Session(game, new Display(game, screen), loop)
                .Play(new Options
                {
                    NewGame = true,
                    SaveFile = Path.Combine(directory, "game.sav"),
                });

            Assert.True(game.CharacterGenerated, "the game never started");
            Assert.Equal("Player", game.Player.Name);
        }
        finally
        {
            ScoreFile.DefaultPath = wasScores;
            CharacterFile.HelpDirectory = wasHelp;
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetName_FallsBackToTheMachinesName()
    {
        (GameState game, _, GameLoop loop, MemoryScreen screen) = Game();

        game.Player = new Player();
        screen.SetKeys("\r" + new string(' ', 20));

        loop.CharacterMaker.GetName();

        Assert.NotEqual(string.Empty, game.Player.Name);
    }
}
