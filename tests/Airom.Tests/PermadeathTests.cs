using Airom.Core;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks that death stays permanent.
///
/// Umoria does not delete the savefile when a character dies - it writes over
/// it with the dead character. The file that is left holds everything the
/// character learned and no level to come back to, so the next game reads it
/// for its monster memory and then rolls somebody new. Skip that write and the
/// last living save is still sitting there, and the dead walk again.
/// </summary>
[Collection("game files")]
public class PermadeathTests
{
    private static (GameState, Display, GameLoop, MemoryScreen) Game(uint seed = 1)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SetKeys(new string(Keys.Escape, 600));

        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        game.Player.Name = "Testor";
        game.Player.MaxHitPoints = 30;
        game.Player.CurrentHitPoints = 30;
        game.Player.Level = 5;
        game.DungeonLevel = 3;
        game.Turn = 900;
        game.CharacterGenerated = true;
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);

        return (game, display, loop, screen);
    }

    private static void WithSaveFile(Action<string> body)
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "airom-dead-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        string wasScores = ScoreFile.DefaultPath;
        ScoreFile.DefaultPath = Path.Combine(directory, "scores.dat");

        try
        {
            body(Path.Combine(directory, "game.sav"));
        }
        finally
        {
            ScoreFile.DefaultPath = wasScores;
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A character who saved, played on and then died leaves a dead character
    /// in the file - not the living one they saved earlier.
    /// </summary>
    [Fact]
    public void Dying_WritesOverTheLivingSave()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            // Saved while alive, as a player does before something dangerous.
            loop.SaveFile.CurrentPath = path;
            Assert.True(loop.SaveFile.Save(path));

            // And then played on, and died.
            game.Turn = 950;
            game.CharacterSaved = false;
            loop.Dead = true;
            game.DiedFrom = "a Giant Rat";
            game.Player.CurrentHitPoints = -1;

            loop.Death.ExitGame();

            // What is on disk is the dead one: restoring gives back the memory
            // and says so, rather than handing the character over.
            (GameState back, Display backDisplay, GameLoop backLoop, _) = Game();

            // Somebody else entirely, so that anything the restore hands over
            // is visible.
            back.Turn = -1;
            back.CharacterGenerated = false;
            back.Player.Name = "Nobody";
            back.Player.Level = 1;

            Assert.False(new SaveFile(back, backDisplay, backLoop)
                .Restore(path, out bool generate));

            Assert.True(generate, "a dead character brought a level back with them");

            // The character themselves was never read: only what they learned.
            Assert.Equal("Nobody", back.Player.Name);
            Assert.Equal(1, back.Player.Level);
            Assert.True(back.Turn < 0, "a dead character brought their turn back");
        });
    }

    /// <summary>
    /// What the dead leave behind is what they learned. A new character starts
    /// knowing it.
    /// </summary>
    [Fact]
    public void Dying_LeavesTheMonsterMemoryBehind()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            game.Memories[17].Kills = 9;
            game.Memories[17].Deaths = 1;
            game.HighlightSeams = true;

            loop.SaveFile.CurrentPath = path;
            loop.Dead = true;
            game.DiedFrom = "an Ancient Dragon";

            loop.Death.ExitGame();

            (GameState back, Display backDisplay, GameLoop backLoop, _) = Game();
            back.Turn = -1;

            new SaveFile(back, backDisplay, backLoop).Restore(path, out _);

            Assert.Equal(9, back.Memories[17].Kills);
            Assert.Equal(1, back.Memories[17].Deaths);
            Assert.True(back.HighlightSeams);
        });
    }

    /// <summary>
    /// The whole of it, through the session: roll somebody, save, play on, die,
    /// and start again. The second game must ask who the player is rather than
    /// handing the dead character back.
    /// </summary>
    [Fact]
    public void StartingAgainAfterDying_AsksForANewCharacter()
    {
        WithSaveFile(path =>
        {
            string help = CharacterFile.HelpDirectory;

            // No news file, so nothing pauses before the questions.
            CharacterFile.HelpDirectory = Path.Combine(
                Path.GetTempPath(), "airom-no-help-" + Guid.NewGuid().ToString("N"));

            try
            {
                // A human warrior called Testor, who quits - which is a death
                // as far as the game is concerned.
                (GameState first, _, GameLoop firstLoop, MemoryScreen firstScreen) = Game();

                firstScreen.SetKeys(
                    "am" + Keys.Escape + "aTestor\r"
                    + string.Concat(Enumerable.Repeat(" " + Keys.Control('K') + " y", 60))
                    + new string(Keys.Escape, 200));

                new Session(first, new Display(first, firstScreen), firstLoop)
                    .Play(new Options { NewGame = true, SaveFile = path });

                Assert.True(firstLoop.Dead, "the first character did not die");
                Assert.Equal("Testor", first.Player.Name);
                Assert.True(File.Exists(path), "dying left no file behind");

                // And now somebody else sits down at the same save slot.
                (GameState second, _, GameLoop secondLoop, MemoryScreen secondScreen) =
                    Game();

                // The leading space is for the -more- that "Restoring Memory
                // of a departed spirit..." leaves waiting: the first question
                // written over the message line flushes it, and the flush takes
                // a key with it.
                secondScreen.SetKeys(
                    " am" + Keys.Escape + "aSecond\r"
                    + string.Concat(Enumerable.Repeat(" " + Keys.Control('K') + " y", 60))
                    + new string(Keys.Escape, 200));

                new Session(second, new Display(second, secondScreen), secondLoop)
                    .Play(new Options { SaveFile = path });

                // The questions were asked, which is the whole point: a dead
                // character does not come back to be played.
                Assert.Equal("Second", second.Player.Name);
            }
            finally
            {
                CharacterFile.HelpDirectory = help;
            }
        });
    }

    /// <summary>
    /// A character who saved and quit is not written over again: they are still
    /// alive, and the file already holds them.
    /// </summary>
    [Fact]
    public void LeavingAfterASave_DoesNotWriteAgain()
    {
        WithSaveFile(path =>
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            loop.SaveFile.CurrentPath = path;
            Assert.True(loop.SaveFile.Save(path));

            byte[] saved = File.ReadAllBytes(path);

            // Saving set the turn to -1 and marked the character saved, which
            // is the state exit_game() is reached in after a quit.
            loop.Death.ExitGame();

            Assert.Equal(saved, File.ReadAllBytes(path));
        });
    }
}
