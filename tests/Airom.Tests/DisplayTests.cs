using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the display layer: the panel window and the drawn map.
///
/// 45 drawn maps are diffed against the C oracle, which now links the real io.c
/// against a curses that records into a grid. These pin the properties.
/// </summary>
public class DisplayTests
{
    private static (GameState Game, Display Display, MemoryScreen Screen) Level(uint seed, int level)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = level;
        game.Objects.Reset();
        game.Monsters.Reset();
        game.Cave.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        game.Cave.Blank();

        new DungeonGenerator(game).GenerateCave();

        var screen = new MemoryScreen();
        var display = new Display(game, screen);
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);

        return (game, display, screen);
    }

    // --------------------------------------------------------------- panel

    /// <summary>
    /// A dungeon level is three screens across each way, and panels overlap by
    /// half a screen - which is why the view jumps rather than sliding.
    /// </summary>
    [Fact]
    public void Panel_SizesItselfFromTheLevel()
    {
        var panel = new Panel();
        panel.Resize(GameState.DungeonHeight, GameState.DungeonWidth);

        Assert.Equal(4, panel.MaxRow);    // 66/22*2 - 2
        Assert.Equal(4, panel.MaxColumn); // 198/66*2 - 2
    }

    /// <summary>
    /// The town is exactly one screen, so there is nowhere to scroll to. The
    /// arithmetic would go negative without the clamp.
    /// </summary>
    [Fact]
    public void Panel_HasNowhereToScrollInTheTown()
    {
        var panel = new Panel();
        panel.Resize(DungeonGenerator.TownHeight, DungeonGenerator.TownWidth);

        Assert.Equal(0, panel.MaxRow);
        Assert.Equal(0, panel.MaxColumn);
    }

    [Fact]
    public void Panel_BoundsCoverExactlyOneScreen()
    {
        var panel = new Panel();
        panel.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        panel.Follow(33, 99, force: true);

        Assert.Equal(Panel.ViewRows, panel.RowMax - panel.RowMin + 1);
        Assert.Equal(Panel.ViewColumns, panel.ColumnMax - panel.ColumnMin + 1);
    }

    /// <summary>
    /// The offsets place the map below the message line and to the right of the
    /// status sidebar, which is what keeps the drawing from overwriting either.
    /// </summary>
    [Fact]
    public void Panel_OffsetsLeaveRoomForTheMessageLineAndSidebar()
    {
        var panel = new Panel();
        panel.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        panel.Follow(0, 0, force: true);

        Assert.Equal(panel.RowMin - 1, panel.RowOffset);
        Assert.Equal(panel.ColumnMin - 13, panel.ColumnOffset);
    }

    /// <summary>
    /// The view follows the player only once they come within two rows or three
    /// columns of an edge, which is what stops it twitching on every step.
    /// </summary>
    [Fact]
    public void Panel_FollowsOnlyNearAnEdge()
    {
        var panel = new Panel();
        panel.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        panel.Follow(33, 99, force: true);

        int row = panel.Row;
        int column = panel.Column;

        // Well inside the current window: nothing moves.
        Assert.False(panel.Follow(panel.RowMin + 10, panel.ColumnMin + 30, force: false));
        Assert.Equal(row, panel.Row);
        Assert.Equal(column, panel.Column);

        // Right at the top edge: it scrolls.
        Assert.True(panel.Follow(panel.RowMin, panel.ColumnMin, force: false));
    }

    [Fact]
    public void Panel_ContainsMatchesItsBounds()
    {
        var panel = new Panel();
        panel.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        panel.Follow(33, 99, force: true);

        Assert.True(panel.Contains(panel.RowMin, panel.ColumnMin));
        Assert.True(panel.Contains(panel.RowMax, panel.ColumnMax));
        Assert.False(panel.Contains(panel.RowMin - 1, panel.ColumnMin));
        Assert.False(panel.Contains(panel.RowMax, panel.ColumnMax + 1));
    }

    /// <summary>
    /// REGRESSION: generate_cave sets the panel indices without recomputing the
    /// window, and get_panel only recomputes when the panel changes. A player
    /// starting in the panel the indices already name therefore leaves the
    /// window stale. Recomputing in Resize looks like an obvious fix and shifts
    /// the drawn map away from the original's.
    /// </summary>
    [Fact]
    public void Panel_ResizeDoesNotRecomputeTheWindow()
    {
        var panel = new Panel();
        panel.Resize(GameState.DungeonHeight, GameState.DungeonWidth);

        Assert.Equal(4, panel.Row);
        Assert.Equal(0, panel.RowMin);
        Assert.Equal(0, panel.ColumnMin);
    }

    // ------------------------------------------------------------ drawing

    [Fact]
    public void PutBuffer_ClipsAtTheRightEdge()
    {
        var game = new GameState();
        var screen = new MemoryScreen();
        var display = new Display(game, screen);

        display.PutBuffer(new string('x', 100), 5, 70);

        // 79 - 70 leaves room for nine characters, and column 79 stays clear.
        Assert.Equal(new string('x', 9), screen.GetRow(5)[70..].TrimEnd());
        Assert.Equal(' ', screen.GetRow(5)[79]);
    }

    [Fact]
    public void Print_ClearsTheRestOfTheLineFirst()
    {
        var game = new GameState();
        var screen = new MemoryScreen();
        var display = new Display(game, screen);

        display.PutBuffer("this should all be replaced", 3, 0);
        display.Print("short", 3, 0);

        Assert.Equal("short", screen.GetRow(3).TrimEnd());
    }

    /// <summary>
    /// A dungeon position becomes a screen position by subtracting the panel
    /// offsets. This is the arithmetic the whole map rests on.
    /// </summary>
    [Fact]
    public void PrintAt_TranslatesDungeonCoordinatesToScreen()
    {
        (_, Display display, MemoryScreen screen) = Level(12345, 20);
        display.Panel.Follow(33, 99, force: true);

        int row = display.Panel.RowMin + 5;
        int column = display.Panel.ColumnMin + 7;

        display.PrintAt('@', row, column);

        Assert.Equal('@', screen.GetRow(row - display.Panel.RowOffset)[
            column - display.Panel.ColumnOffset]);
    }

    /// <summary>
    /// The map is drawn from column thirteen rightwards, so the status sidebar
    /// survives a redraw.
    /// </summary>
    [Fact]
    public void PrintMap_LeavesTheSidebarAlone()
    {
        (GameState game, Display display, MemoryScreen screen) = Level(12345, 20);
        LightEverything(game);
        display.Panel.Follow(game.CharacterRow, game.CharacterColumn, force: true);

        display.PutBuffer("SIDEBAR", 5, 0);
        display.PrintMap();

        Assert.StartsWith("SIDEBAR", screen.GetRow(5), StringComparison.Ordinal);
    }

    [Fact]
    public void PrintMap_DrawsThePlayerAndSomeTerrain()
    {
        (GameState game, Display display, MemoryScreen screen) = Level(12345, 20);
        LightEverything(game);
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;
        display.Panel.Follow(game.CharacterRow, game.CharacterColumn, force: true);

        display.PrintMap();

        string frame = screen.GetText();
        Assert.Contains('@', frame);
        Assert.Contains('#', frame);
        Assert.Contains('.', frame);
    }

    /// <summary>
    /// An unseen, unlit square shows nothing at all - which is what makes
    /// exploring worth doing.
    /// </summary>
    [Fact]
    public void SymbolAt_HidesWhatHasNotBeenSeen()
    {
        (GameState game, Display display, _) = Level(999, 20);

        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                CaveSquare square = game.Cave[row, column];
                if (!square.PermanentLight && !square.TemporaryLight && !square.FieldMark
                    && square.MonsterIndex == 0)
                {
                    Assert.Equal(' ', display.SymbolAt(row, column));
                    return;
                }
            }
        }
    }

    /// <summary>
    /// The player outranks everything else on their square, and a visible
    /// monster outranks the object and terrain beneath it.
    /// </summary>
    [Fact]
    public void SymbolAt_DrawsInPriorityOrder()
    {
        (GameState game, Display display, _) = Level(4242, 20);
        LightEverything(game);

        game.Cave[10, 10].MonsterIndex = 1;
        Assert.Equal('@', display.SymbolAt(10, 10));

        // A visible monster beats the floor under it.
        int slot = MonsterPool.FirstIndex;
        game.Monsters[slot].Visible = true;
        game.Monsters[slot].CreatureIndex = 0;
        game.Cave[11, 11].MonsterIndex = slot;
        Assert.Equal(
            GameTables.CreatureList[0].DisplayChar,
            display.SymbolAt(11, 11));
    }

    /// <summary>
    /// Mineral veins are drawn as plain rock unless the player turns the option
    /// on, which is what makes tunnelling for treasure a deliberate choice.
    /// </summary>
    [Fact]
    public void SymbolAt_HidesMineralVeinsUnlessAsked()
    {
        (GameState game, Display display, _) = Level(7, 20);
        LightEverything(game);
        game.Cave[20, 20].Feature = CaveFeature.MagmaWall;
        game.Cave[20, 20].ObjectIndex = 0;

        Assert.False(game.HighlightSeams);
        Assert.Equal('#', display.SymbolAt(20, 20));

        game.HighlightSeams = true;
        Assert.Equal('%', display.SymbolAt(20, 20));
    }

    private static void LightEverything(GameState game)
    {
        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                game.Cave[row, column].PermanentLight = true;
                game.Cave[row, column].FieldMark = true;
            }
        }
    }
}
