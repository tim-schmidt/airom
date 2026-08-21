using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the saved game.
///
/// The file itself is diffed against the C oracle - 180 runs, every byte of
/// every file, written, read back and written again - so what these hold up is
/// the behaviour around it: what a save does to the game, what a refused file
/// does, and which files this will and will not open.
/// </summary>
[Collection("game files")]
public class SaveFileTests
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

        game.Player.Name = "Alatariel";
        game.Player.MaxHitPoints = 20;
        game.Player.CurrentHitPoints = 20;
        game.Player.Level = 3;
        game.Turn = 700;
        game.DungeonLevel = 4;
        game.CharacterGenerated = true;
        game.DiedFrom = "a Giant Rat";

        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);

        return (game, display, loop, screen);
    }

    /// <summary>Runs a body with a save file of its own.</summary>
    private static void WithSaveFile(Action<string> body)
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "airom-save-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        try
        {
            body(Path.Combine(directory, "game.sav"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Saving ends the session: the turn goes to -1, which is what stops the
    /// gravestone being printed for someone who merely stopped playing.
    /// </summary>
    [Fact]
    public void Save_EndsTheSession()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            Assert.True(new SaveFile(game, display, loop).Save(path));

            Assert.True(File.Exists(path));
            Assert.True(game.CharacterSaved);
            Assert.Equal(-1, game.Turn);
        });
    }

    /// <summary>
    /// The pack's penalty is taken off before the file is written, so a
    /// restored character does not carry it twice.
    /// </summary>
    [Fact]
    public void Save_PutsTheSpeedBackBeforeWriting()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            game.Inventory.PackBurden = 3;
            game.Player.Speed = 3;

            new SaveFile(game, display, loop).Save(path);

            Assert.Equal(0, game.Inventory.PackBurden);
            Assert.Equal(0, game.Player.Speed);
        });
    }

    /// <summary>A game already saved is not written twice.</summary>
    [Fact]
    public void Save_DoesNothingWhenAlreadySaved()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            game.CharacterSaved = true;

            Assert.True(new SaveFile(game, display, loop).Save(path));
            Assert.False(File.Exists(path));
        });
    }

    /// <summary>
    /// The three version bytes are written from a cleared chain, so they can be
    /// read without knowing the key.
    /// </summary>
    [Fact]
    public void Save_StartsWithTheVersionInPlainBytes()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            new SaveFile(game, display, loop).Save(path);

            byte[] bytes = File.ReadAllBytes(path);

            Assert.Equal(SaveFile.VersionMajor, bytes[0]);
            Assert.Equal(SaveFile.VersionMinor, bytes[1]);
            Assert.Equal(SaveFile.PatchLevel, bytes[2]);
        });
    }

    /// <summary>The whole game comes back, down to the level being stood on.</summary>
    [Fact]
    public void Restore_BringsTheGameBack()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            game.Player.Gold = 4321;
            game.Player.MaxDungeonLevel = 9;
            game.RogueLikeCommands = true;
            game.Cave[10, 10].Feature = CaveFeature.LightFloor;
            game.Memories[7].Kills = 5;
            game.CharacterRow = 10;
            game.CharacterColumn = 10;

            new SaveFile(game, display, loop).Save(path);

            (GameState back, Display backDisplay, GameLoop backLoop, _) = Game();
            back.Turn = -1;

            Assert.True(new SaveFile(back, backDisplay, backLoop)
                .Restore(path, out bool generate));

            Assert.False(generate);
            Assert.Equal("Alatariel", back.Player.Name);
            Assert.Equal(4321, back.Player.Gold);
            Assert.Equal(9, back.Player.MaxDungeonLevel);
            Assert.Equal(700, back.Turn);
            Assert.Equal(4, back.DungeonLevel);
            Assert.True(back.RogueLikeCommands);
            Assert.Equal(CaveFeature.LightFloor, back.Cave[10, 10].Feature);
            Assert.Equal(5, back.Memories[7].Kills);
            Assert.True(back.CharacterGenerated);
        });
    }

    /// <summary>
    /// A restored character is alive and well until something says otherwise -
    /// the killed-by string is overwritten for anyone still standing.
    /// </summary>
    [Fact]
    public void Restore_ClearsWhatKilledThem()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            new SaveFile(game, display, loop).Save(path);

            (GameState back, Display backDisplay, GameLoop backLoop, _) = Game();
            back.Turn = -1;

            new SaveFile(back, backDisplay, backLoop).Restore(path, out _);

            Assert.Equal("(alive and well)", back.DiedFrom);
        });
    }

    /// <summary>
    /// A dead character's file stops after the shops. Restoring it gives back
    /// the monster memory and the options and nothing else, and says so by
    /// returning false: the caller rolls a new character who remembers what the
    /// last one learned.
    /// </summary>
    [Fact]
    public void Restore_OfADeadCharacterKeepsOnlyTheMemory()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            loop.Dead = true;
            game.Memories[11].Kills = 3;
            game.HighlightSeams = true;

            new SaveFile(game, display, loop).Save(path);

            (GameState back, Display backDisplay, GameLoop backLoop, _) = Game();
            back.Turn = -1;

            Assert.False(new SaveFile(back, backDisplay, backLoop)
                .Restore(path, out bool generate));

            Assert.True(generate);
            Assert.Equal(3, back.Memories[11].Kills);
            Assert.True(back.HighlightSeams);

            // The character themselves was never read.
            Assert.NotEqual(700, back.Turn);
        });
    }

    /// <summary>
    /// A wizard may bring a dead character back, which is the only way the rest
    /// of a dead character's file is ever used.
    /// </summary>
    [Fact]
    public void Restore_LetsAWizardResurrect()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            loop.Dead = true;
            game.Player.CurrentHitPoints = -5;
            game.Player.Food = -20;
            game.Player.Poisoned = 40;

            new SaveFile(game, display, loop).Save(path);

            (GameState back, Display backDisplay, GameLoop backLoop, MemoryScreen screen) =
                Game();

            back.Turn = -1;
            back.Wizard = true;
            screen.SetKeys("y" + new string(' ', 40));

            Assert.True(new SaveFile(back, backDisplay, backLoop)
                .Restore(path, out _));

            // Brought back on the town level, not starving and not about to be
            // poisoned to death again.
            Assert.Equal(0, back.DungeonLevel);
            Assert.Equal(0, back.Player.CurrentHitPoints);
            Assert.Equal(0, back.Player.Food);
            Assert.Equal(1, back.Player.Poisoned);

            // And recorded as a resurrection rather than a wizard game.
            Assert.False(back.Wizard);
            Assert.Equal(0x1, back.NoScore & 0x1);
        });
    }

    /// <summary>A refused resurrection leaves the character dead.</summary>
    [Fact]
    public void Restore_TakesNoForAnAnswerAboutResurrection()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            loop.Dead = true;
            new SaveFile(game, display, loop).Save(path);

            (GameState back, Display backDisplay, GameLoop backLoop, MemoryScreen screen) =
                Game();

            back.Turn = -1;
            back.Wizard = true;
            screen.SetKeys("n" + new string(' ', 40));

            Assert.False(new SaveFile(back, backDisplay, backLoop).Restore(path, out _));
        });
    }

    /// <summary>
    /// A character who won is out of the game for good: their level is past
    /// what the tables allow, so bringing them back would break things.
    /// </summary>
    [Fact]
    public void Restore_RefusesToResurrectAWinner()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            loop.Dead = true;
            game.TotalWinner = true;

            new SaveFile(game, display, loop).Save(path);

            (GameState back, Display backDisplay, GameLoop backLoop, MemoryScreen screen) =
                Game();

            back.Turn = -1;
            back.Wizard = true;
            screen.SetKeys("y" + new string(' ', 40));

            Assert.False(new SaveFile(back, backDisplay, backLoop).Restore(path, out _));
        });
    }

    [Fact]
    public void Restore_SaysSoWhenThereIsNoFile()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, MemoryScreen screen) = Game();
            game.Turn = -1;

            Assert.False(new SaveFile(game, display, loop).Restore(path, out bool generate));

            Assert.True(generate);
            Assert.Contains("Savefile does not exist", screen.GetRow(0));
        });
    }

    /// <summary>Umoria 4 wrote a different format, and this will not read it.</summary>
    [Fact]
    public void Restore_RefusesAFileFromAnotherVersion()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, MemoryScreen screen) = Game();
            game.Turn = -1;

            File.WriteAllBytes(path, [4, 0, 0, 0, 0, 0]);

            Assert.False(new SaveFile(game, display, loop).Restore(path, out _));
            Assert.Contains("different version", screen.GetRow(2));
        });
    }

    /// <summary>
    /// Files from 5.0.14 on are read, since the format was frozen at 5.2.2 -
    /// which is what lets a game saved by the original be picked up here. A
    /// file older than that is refused.
    /// </summary>
    [Fact]
    public void Restore_RefusesAFileOlderThanTheFormat()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, MemoryScreen screen) = Game();
            game.Turn = -1;

            File.WriteAllBytes(path, [5, 0, 13, 0, 0, 0]);

            Assert.False(new SaveFile(game, display, loop).Restore(path, out _));
            Assert.Contains("different version", screen.GetRow(2));
        });
    }

    /// <summary>A file cut short is not half-believed.</summary>
    [Fact]
    public void Restore_RefusesATruncatedFile()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, MemoryScreen screen) = Game();

            new SaveFile(game, display, loop).Save(path);

            byte[] bytes = File.ReadAllBytes(path);
            File.WriteAllBytes(path, bytes[..(bytes.Length / 2)]);

            (GameState back, Display backDisplay, GameLoop backLoop, _) = Game();
            back.Turn = -1;

            Assert.False(new SaveFile(back, backDisplay, backLoop).Restore(path, out _));
        });
    }

    /// <summary>
    /// Restoring while still alive is impossible, and is refused rather than
    /// being allowed to overwrite the game in progress.
    /// </summary>
    [Fact]
    public void Restore_RefusesWhileStillAlive()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, MemoryScreen screen) = Game();

            new SaveFile(game, display, loop).Save(path);

            (GameState back, Display backDisplay, GameLoop backLoop, _) = Game();
            back.Turn = 100;

            Assert.False(new SaveFile(back, backDisplay, backLoop).Restore(path, out _));
        });
    }

    /// <summary>A save file whose clock says what the test wants it to say.</summary>
    private sealed class PinnedSave(GameState game, Display display, GameLoop loop, uint when)
        : SaveFile(game, display, loop)
    {
        protected override uint Now() => when;
    }

    /// <summary>
    /// The shops restock for every day the game was put away, rounded to the
    /// nearest, and never more than ten days' worth.
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(86400 * 3, 3)]
    [InlineData(86400 * 40, 10)]
    public void Restore_RestocksTheShopsForTheDaysThatPassed(uint away, int expected)
    {
        WithSaveFile(path =>
        {
            const uint Written = 1000000;

            (GameState game, Display display, GameLoop loop, _) = Game();

            game.Stores.Initialise();

            new PinnedSave(game, display, loop, Written) { StartTime = Written }
                .Save(path);

            (GameState back, Display backDisplay, GameLoop backLoop, _) = Game();
            back.Turn = -1;

            new PinnedSave(back, backDisplay, backLoop, Written + away)
                .Restore(path, out _);

            // What the same number of restocking rounds does to the same shops,
            // starting from the same file.
            (GameState alone, Display aloneDisplay, GameLoop aloneLoop, _) = Game();
            alone.Turn = -1;

            new PinnedSave(alone, aloneDisplay, aloneLoop, Written).Restore(path, out _);

            for (int i = 0; i < expected; i++)
            {
                alone.Stores.Maintain();
            }

            Assert.Equal(
                alone.Stores.All.Select(store => store.StockCount),
                back.Stores.All.Select(store => store.StockCount));
        });
    }

    /// <summary>
    /// The town uses one corner of the grid, but the whole grid is written out -
    /// the original's array is a fixed size and the file follows it.
    /// </summary>
    [Fact]
    public void Save_WritesTheWholeGridEvenForTheTown()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            game.DungeonLevel = 0;
            new DungeonGenerator(game, display).Generate();
            game.CharacterRow = 3;
            game.CharacterColumn = 3;

            Assert.True(new SaveFile(game, display, loop).Save(path));

            (GameState back, Display backDisplay, GameLoop backLoop, _) = Game();
            back.Turn = -1;

            Assert.True(new SaveFile(back, backDisplay, backLoop).Restore(path, out _));

            Assert.Equal(game.Cave.Height, back.Cave.Height);
            Assert.Equal(game.Cave.Width, back.Cave.Width);
        });
    }
}
