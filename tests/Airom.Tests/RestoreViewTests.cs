using Airom.Core;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the view a restored game arrives with.
///
/// Restoring is the one time the game finds itself on a level it did not just
/// build, with a view left over from somewhere else. Getting that wrong sent
/// the map off the side of the level.
/// </summary>
[Collection("game files")]
public class RestoreViewTests
{
    private static (GameState, Display, GameLoop, MemoryScreen) Game(uint seed = 1)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SetKeys(new string(' ', 400));

        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        return (game, display, loop, screen);
    }

    /// <summary>
    /// A game saved deep in a level and picked up again draws its first map
    /// without running off the edge.
    ///
    /// The panel scrolls in half-screens, and the view is only recomputed for
    /// an axis the player looks close to the edge of. A window left over from
    /// somewhere else can therefore answer "no need to move" for one axis while
    /// the other moves - leaving the panel half unset, at minus one, and the
    /// map drawn from thirty-three columns before the level starts.
    /// </summary>
    [Fact]
    public void Restore_DrawsTheFirstMapWithoutRunningOffTheLevel()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "airom-view-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "game.sav");

        try
        {
            (GameState game, Display display, GameLoop loop, _) = Game();

            game.Player.Name = "Deep";
            game.Player.MaxHitPoints = 50;
            game.Player.CurrentHitPoints = 50;
            game.DungeonLevel = 3;
            game.Turn = 500;
            game.CharacterGenerated = true;

            new DungeonGenerator(game, display).Generate();

            // Far down the level and near the left of it: the row is outside a
            // fresh window and the column is inside one, which is the pairing
            // that used to leave the panel half unset.
            game.CharacterRow = game.Cave.Height - 15;
            game.CharacterColumn = 40;

            Assert.True(new SaveFile(game, display, loop).Save(path));

            (GameState back, Display backDisplay, GameLoop backLoop, MemoryScreen screen) =
                Game();

            back.Turn = -1;
            Assert.True(new SaveFile(back, backDisplay, backLoop).Restore(path, out _));

            // What the loop does on arrival, which is where the map is drawn.
            backDisplay.Panel.Invalidate();
            backLoop.CheckViewForTest();

            Panel panel = backDisplay.Panel;

            Assert.InRange(panel.RowMin, 0, back.Cave.Height - 1);
            Assert.InRange(panel.RowMax, 0, back.Cave.Height - 1);
            Assert.InRange(panel.ColumnMin, 0, back.Cave.Width - 1);
            Assert.InRange(panel.ColumnMax, 0, back.Cave.Width - 1);

            // And the player is inside the window, which is the point of it.
            Assert.True(panel.Contains(back.CharacterRow, back.CharacterColumn),
                "the restored view does not show the player");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Invalidating the panel clears the window with it, so the next look
    /// settles both directions rather than trusting half of an old one.
    /// </summary>
    [Fact]
    public void Invalidate_ClearsTheWindowAsWellAsThePanel()
    {
        (GameState game, Display display, _, _) = Game();

        display.Panel.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        display.Panel.Bounds();

        Assert.NotEqual(0, display.Panel.RowMin);

        display.Panel.Invalidate();

        Assert.Equal(0, display.Panel.RowMin);
        Assert.Equal(0, display.Panel.RowMax);
        Assert.Equal(0, display.Panel.ColumnMin);
        Assert.Equal(0, display.Panel.ColumnMax);
    }

    /// <summary>
    /// Restoring says how far the view may scroll and nothing more: the window
    /// itself is left for the first look at the level to settle.
    /// </summary>
    [Fact]
    public void RestoreBounds_LeavesTheWindowAlone()
    {
        (GameState game, Display display, _, _) = Game();

        display.Panel.Invalidate();
        display.Panel.RestoreBounds(4, 4);

        Assert.Equal(4, display.Panel.MaxRow);
        Assert.Equal(4, display.Panel.MaxColumn);
        Assert.Equal(0, display.Panel.RowMin);
        Assert.Equal(0, display.Panel.ColumnMin);
    }
}
