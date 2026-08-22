using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the view growing and shrinking with the terminal.
///
/// The original was written for one size of screen and the oracle compares at
/// that size, so nothing here can be held to the C. These pin what a larger
/// terminal gets instead: more of the dungeon, a status line still along the
/// bottom, and a redraw when the size changes under a running game.
/// </summary>
public class ResizeTests
{
    private static (GameState Game, Player Player, Display Display, MemoryScreen Screen) Level(
        int rows, int columns, int level = 1)
    {
        var game = new GameState();
        game.InitSeeds(4242);
        game.MagicInit();
        game.DungeonLevel = level;
        game.Objects.Reset();
        game.Monsters.Reset();

        Player player = new CharacterCreation(game)
            .Create(race: 0, characterClass: 0, male: true, name: "Wide");

        var screen = new MemoryScreen(rows, columns);
        var display = new Display(game, screen);
        var generator = new DungeonGenerator(game, display);
        generator.Generate();

        (int row, int column) = generator.NewSpot();
        game.CharacterRow = row;
        game.CharacterColumn = column;

        return (game, player, display, screen);
    }

    // --------------------------------------------------------------- panel

    /// <summary>
    /// The original counts panels as "(cur_height / SCREEN_HEIGHT) * 2 - 2".
    /// The port asks how many half-views it takes to reach the far edge, and
    /// must give the same answers for the sizes a level can be.
    /// </summary>
    [Theory]
    [InlineData(GameState.DungeonHeight, Panel.BlockRows)]
    [InlineData(GameState.DungeonWidth, Panel.BlockColumns)]
    [InlineData(DungeonGenerator.TownHeight, Panel.BlockRows)]
    [InlineData(DungeonGenerator.TownWidth, Panel.BlockColumns)]
    public void LastPanel_AgreesWithTheOriginalAtItsOwnSize(int cave, int view)
    {
        int original = Math.Max((cave / view * 2) - 2, 0);

        Assert.Equal(original, Panel.LastPanel(cave, view));
    }

    /// <summary>
    /// A taller, wider view scrolls in fewer, larger steps, and the last panel
    /// still reaches the far corner of the level.
    /// </summary>
    [Fact]
    public void Panel_LargerViewStillReachesTheFarCorner()
    {
        var panel = new Panel(40, 100);
        panel.Resize(GameState.DungeonHeight, GameState.DungeonWidth);

        Assert.Equal(2, panel.MaxRow);    // 66 rows, 40 shown, 20 a step
        Assert.Equal(2, panel.MaxColumn); // 198 columns, 100 shown, 50 a step

        // As arriving on a level does, so the follow lays the window out.
        panel.Invalidate();
        panel.Follow(GameState.DungeonHeight - 1, GameState.DungeonWidth - 1, force: true);

        Assert.Equal(GameState.DungeonHeight - 1, panel.RowMax);
        Assert.Equal(GameState.DungeonWidth - 1, panel.ColumnMax);
        Assert.True(panel.RowMin >= 0);
        Assert.True(panel.ColumnMin >= 0);
        Assert.Equal(40, panel.RowMax - panel.RowMin + 1);
        Assert.Equal(100, panel.ColumnMax - panel.ColumnMin + 1);
    }

    /// <summary>
    /// A view larger than the level - the town on a big terminal - stops at
    /// the level's edge rather than drawing from beyond it.
    /// </summary>
    [Fact]
    public void Panel_ViewLargerThanTheLevelStopsAtItsEdge()
    {
        var panel = new Panel(40, 100);
        panel.Resize(DungeonGenerator.TownHeight, DungeonGenerator.TownWidth);

        Assert.Equal(0, panel.MaxRow);
        Assert.Equal(0, panel.MaxColumn);

        panel.Invalidate();
        panel.Follow(5, 5, force: true);

        Assert.Equal(0, panel.RowMin);
        Assert.Equal(DungeonGenerator.TownHeight - 1, panel.RowMax);
        Assert.Equal(0, panel.ColumnMin);
        Assert.Equal(DungeonGenerator.TownWidth - 1, panel.ColumnMax);
    }

    /// <summary>
    /// A saved game records how far its own view could scroll. Restored under
    /// a larger terminal, that figure is recounted for the view it now has.
    /// </summary>
    [Fact]
    public void RestoreBounds_RecountsForALargerView()
    {
        var panel = new Panel(40, 100);
        panel.RestoreBounds(4, 4, GameState.DungeonHeight, GameState.DungeonWidth);

        Assert.Equal(2, panel.MaxRow);
        Assert.Equal(2, panel.MaxColumn);
    }

    /// <summary>
    /// Whatever the view, a savefile records the original's count, so a real
    /// Umoria - or this port under another terminal - reads it as its own.
    /// </summary>
    [Fact]
    public void SavedMax_IsAlwaysTheOriginalsCount()
    {
        var wide = new Panel(40, 100);
        wide.Resize(GameState.DungeonHeight, GameState.DungeonWidth);

        Assert.Equal(2, wide.MaxRow);
        Assert.Equal(4, wide.SavedMaxRow);
        Assert.Equal(4, wide.SavedMaxColumn);

        var original = new Panel();
        original.RestoreBounds(3, 2, GameState.DungeonHeight, GameState.DungeonWidth);

        Assert.Equal(3, original.SavedMaxRow);
        Assert.Equal(2, original.SavedMaxColumn);
    }

    /// <summary>
    /// Changing the view re-counts the grid for the level the panel was last
    /// sized to, and forgets the window so the next follow lays it out afresh.
    /// </summary>
    [Fact]
    public void SetView_RecountsTheGridAndForgetsTheWindow()
    {
        var panel = new Panel();
        panel.Resize(GameState.DungeonHeight, GameState.DungeonWidth);
        panel.Follow(33, 99, force: true);

        panel.SetView(40, 100);

        Assert.Equal(2, panel.MaxRow);
        Assert.Equal(2, panel.MaxColumn);
        Assert.Equal(-1, panel.Row);
        Assert.Equal(0, panel.RowMax);
    }

    /// <summary>
    /// Rooms are carved and lit in blocks of half the original screen, whatever
    /// the view is now. A bigger view must not move the lighting grid.
    /// </summary>
    [Fact]
    public void Blocks_StayTheOriginalSize()
    {
        Assert.Equal(22, Panel.BlockRows);
        Assert.Equal(66, Panel.BlockColumns);
        Assert.Equal(Panel.BlockRows, new Panel().ViewRows);
        Assert.Equal(Panel.BlockColumns, new Panel().ViewColumns);
    }

    // ------------------------------------------------------------- display

    /// <summary>
    /// The map fills everything between the message line and the status line,
    /// and everything right of the sidebar but the last column - which on a
    /// 24x80 terminal is the original's 22x66.
    /// </summary>
    [Theory]
    [InlineData(24, 80, 22, 66)]
    [InlineData(30, 100, 28, 86)]
    [InlineData(50, 200, 48, 186)]
    public void Display_SizesThePanelToTheScreen(int rows, int columns, int viewRows, int viewColumns)
    {
        var display = new Display(new GameState(), new MemoryScreen(rows, columns));

        Assert.Equal(viewRows, display.Panel.ViewRows);
        Assert.Equal(viewColumns, display.Panel.ViewColumns);
        Assert.False(display.ScreenSizeChanged);
    }

    /// <summary>
    /// The status line keeps to the bottom row and the depth to the right
    /// edge, wherever those are.
    /// </summary>
    [Fact]
    public void StatusLine_SitsOnTheBottomRow()
    {
        (_, Player player, Display display, MemoryScreen screen) = Level(30, 100);
        player.Status |= PlayerStatus.Hungry;

        display.PrintHunger(player);
        display.PrintDepth();

        Assert.Equal("Hungry", screen.GetRow(29)[..6]);
        Assert.Equal("50 feet", screen.GetRow(29)[85..].TrimEnd());
        Assert.Equal(string.Empty, screen.GetRow(23).Trim());
    }

    /// <summary>
    /// A wider screen lets a message run further before " -more-" is needed,
    /// and a " [y/n]" goes at the same far column.
    /// </summary>
    [Fact]
    public void GetCheck_PutsTheAnswerAtTheWiderEdge()
    {
        (_, _, Display display, MemoryScreen screen) = Level(30, 100);
        string? asked = null;
        screen.BeforeReadKey = () => asked = screen.GetRow(0);
        screen.SendKeys('y');

        Assert.True(display.GetCheck(new string('q', 98)));
        Assert.NotNull(asked);
        Assert.Equal(" [y/n]", asked[93..99]);
        Assert.Equal('q', asked[92]);
    }

    /// <summary>
    /// Written text is clipped at the screen's own right edge, not column 79.
    /// </summary>
    [Fact]
    public void PutBuffer_ClipsAtTheScreensOwnEdge()
    {
        var screen = new MemoryScreen(24, 100);
        var display = new Display(new GameState(), screen);

        display.PutBuffer(new string('x', 100), 5, 90);

        Assert.Equal(new string('x', 9), screen.GetRow(5)[90..].TrimEnd());
        Assert.Equal(' ', screen.GetRow(5)[99]);
    }

    /// <summary>
    /// The map drawn on a bigger screen really is bigger: rows of dungeon
    /// appear below the original's last map row.
    /// </summary>
    [Fact]
    public void PrintMap_FillsTheLargerScreen()
    {
        (GameState game, _, Display display, MemoryScreen screen) = Level(40, 120);

        display.Panel.Follow(game.CharacterRow, game.CharacterColumn, force: true);

        // Light the level so there is something to see everywhere.
        for (int row = 0; row < game.Cave.Height; row++)
        {
            for (int column = 0; column < game.Cave.Width; column++)
            {
                game.Cave[row, column].PermanentLight = true;
            }
        }

        display.PrintMap();

        Assert.Equal(38, display.Panel.RowMax - display.Panel.RowMin + 1);
        Assert.Equal(106, display.Panel.ColumnMax - display.Panel.ColumnMin + 1);
        Assert.NotEqual(string.Empty, screen.GetRow(38).Trim());
        Assert.NotEqual(string.Empty, screen.GetRow(30)[80..].Trim());
        Assert.Equal(string.Empty, screen.GetRow(39).Trim());
        Assert.Equal(' ', screen.GetRow(20)[119]);
    }

    // ------------------------------------------------------------- resizing

    /// <summary>
    /// The screen keeps what it held across a resize, cut or padded at the
    /// bottom and right, so whatever was up stays up until the game redraws.
    /// </summary>
    [Fact]
    public void Screen_KeepsItsContentsAcrossAResize()
    {
        var screen = new MemoryScreen(24, 80);
        screen.Put(3, 5, "hello");
        screen.Put(23, 70, "bottom");
        screen.SaveScreen();

        screen.Resize(30, 100);

        Assert.Equal(30, screen.Rows);
        Assert.Equal(100, screen.Columns);
        Assert.Equal("hello", screen.GetRow(3)[5..10]);
        Assert.Equal("bottom", screen.GetRow(23)[70..76]);
        Assert.Equal(string.Empty, screen.GetRow(29).Trim());

        screen.Resize(10, 8);

        Assert.Equal("hel", screen.GetRow(3)[5..]);

        // The saved copy follows the grid, so an overlay's restore still fits.
        screen.Clear();
        screen.RestoreScreen();
        Assert.Equal("hel", screen.GetRow(3)[5..]);
    }

    [Fact]
    public void Regrid_KeepsTheTopLeftCorner()
    {
        char[] cells = "abcdefghi".ToCharArray(); // 3x3

        char[] grown = ScreenBuffer.Regrid(cells, 3, 3, 4, 5);
        Assert.Equal("abc  def  ghi       ", new string(grown));

        char[] shrunk = ScreenBuffer.Regrid(cells, 3, 3, 2, 2);
        Assert.Equal("abde", new string(shrunk));
    }

    /// <summary>
    /// Once the screen changes size the display knows, and the next full
    /// redraw lays the panel out for the new size and puts the status line on
    /// the new bottom row.
    /// </summary>
    [Fact]
    public void DrawCave_RefitsThePanelAfterAResize()
    {
        (GameState game, Player player, Display display, MemoryScreen screen) = Level(24, 80);
        display.Panel.Follow(game.CharacterRow, game.CharacterColumn, force: true);
        display.DrawCave(player);

        Assert.False(display.ScreenSizeChanged);

        screen.Resize(40, 120);

        Assert.True(display.ScreenSizeChanged);

        display.DrawCave(player);

        Assert.False(display.ScreenSizeChanged);
        Assert.Equal(38, display.Panel.ViewRows);
        Assert.Equal(106, display.Panel.ViewColumns);
        Assert.True(display.Panel.Contains(game.CharacterRow, game.CharacterColumn));
        Assert.Equal("50 feet", screen.GetRow(39)[105..].TrimEnd());
        Assert.Equal(string.Empty, screen.GetRow(23)[65..].Trim());

        // And back down again.
        screen.Resize(24, 80);
        display.DrawCave(player);

        Assert.Equal(22, display.Panel.ViewRows);
        Assert.Equal(66, display.Panel.ViewColumns);
        Assert.True(display.Panel.Contains(game.CharacterRow, game.CharacterColumn));
        Assert.Equal("50 feet", screen.GetRow(23)[65..].TrimEnd());
    }

    /// <summary>
    /// A resize while the game waits at the command prompt is drawn for on
    /// the spot, through the action the prompt passes in; any other wait
    /// leaves the screen alone.
    /// </summary>
    [Fact]
    public void ReadKey_DrawsForTheNewSizeWhileWaiting()
    {
        (_, _, Display display, MemoryScreen screen) = Level(24, 80);
        int redrawn = 0;

        screen.BeforeReadKey = () =>
        {
            screen.BeforeReadKey = null;
            screen.Resize(30, 100);
        };
        screen.SendKeys('x');

        Assert.Equal('x', display.ReadKey(whenResized: () => redrawn++));
        Assert.Equal(1, redrawn);
        Assert.Null(screen.Resized);

        screen.BeforeReadKey = () =>
        {
            screen.BeforeReadKey = null;
            screen.Resize(24, 80);
        };
        screen.SendKeys('y');

        Assert.Equal('y', display.ReadKey());
        Assert.Equal(1, redrawn);
    }

    /// <summary>
    /// The loop itself notices a resize that happened somewhere it could not
    /// redraw - inside a prompt, say - the next time it comes round to ask for
    /// a command, and draws for the new size before asking.
    /// </summary>
    [Fact]
    public void Loop_RedrawsForANewSizeBeforeTheNextCommand()
    {
        (GameState game, Player player, Display display, MemoryScreen screen) = Level(24, 80);
        game.Player = player;
        player.Food = 5000;
        var loop = new GameLoop(game, display);

        screen.TypeAheadVisible = false;
        screen.Resize(30, 100);
        Assert.True(display.ScreenSizeChanged);

        // Quit at the first command prompt, saying yes when asked. The level
        // is laid out for the new size before that prompt is put up.
        screen.SetKeys(Keys.Control('K') + "y");
        loop.Run();

        Assert.True(loop.Dead);
        Assert.False(display.ScreenSizeChanged);
        Assert.Equal(28, display.Panel.ViewRows);
        Assert.Equal("50 feet", screen.GetRow(29)[85..].TrimEnd());
    }
    // ------------------------------------------------------ edges and centring

    /// <summary>
    /// A room straddling the panel's edge lights only the part inside it.
    /// With panel edges that no longer line up with the lighting blocks, a
    /// room the player stands in can straddle an edge, and the far side used
    /// to spill onto the status line and stay there.
    /// </summary>
    [Fact]
    public void LightRoom_DrawsNothingOutsideThePanel()
    {
        (GameState game, Player player, Display display, MemoryScreen screen) = Level(30, 100);
        display.Panel.Follow(33, 99, force: true);
        display.DrawCave(player);
        Panel panel = display.Panel;

        // The view is 28 rows, so the panel ends at row 41 - inside the
        // 11-row block that runs from 33 to 43.
        Assert.Equal(41, panel.RowMax);
        int column = panel.ColumnMin + 10;
        for (int row = 33; row <= 43; row++)
        {
            CaveSquare square = game.Cave[row, column];
            square.LitRoom = true;
            square.PermanentLight = false;
            square.Feature = CaveFeature.DarkFloor;
        }

        string statusBefore = screen.GetRow(29);
        var lighting = new Lighting(game, display);

        lighting.LightRoom(41, column);

        Assert.Equal('.', screen.GetRow(28)[Display.SidebarWidth + 10]);
        Assert.Equal(statusBefore, screen.GetRow(29));
        Assert.True(game.Cave[43, column].PermanentLight);
    }

    /// <summary>
    /// The town is smaller than a big view, so it sits in the middle of the
    /// map area, and the whole area is cleared first: the last level's map
    /// does not show around it.
    /// </summary>
    [Fact]
    public void PrintMap_CentresASmallLevelAndClearsAroundIt()
    {
        (GameState game, Player player, Display display, MemoryScreen screen) = Level(40, 120, level: 0);

        // Something left over from a bigger level, all over the map area.
        for (int row = 1; row <= 38; row++)
        {
            screen.Put(row, Display.SidebarWidth, new string('#', 106));
        }

        display.Panel.Invalidate();
        display.Panel.Follow(game.CharacterRow, game.CharacterColumn, force: true);
        display.PrintMap();

        Panel panel = display.Panel;
        Assert.Equal(0, panel.RowMin);
        Assert.Equal(DungeonGenerator.TownHeight - 1, panel.RowMax);

        // 38 rows of view, 22 of town: eight blank above, eight below.
        int topRow = 0 - panel.RowOffset;
        int leftColumn = 0 - panel.ColumnOffset;
        Assert.Equal(1 + 8, topRow);
        Assert.Equal(Display.SidebarWidth + 20, leftColumn);

        for (int row = 1; row <= 38; row++)
        {
            string shown = screen.GetRow(row);
            Assert.DoesNotContain('#', shown[Display.SidebarWidth..leftColumn]);
            Assert.DoesNotContain('#', shown[(leftColumn + DungeonGenerator.TownWidth)..]);
            if (row < topRow || row >= topRow + DungeonGenerator.TownHeight)
            {
                Assert.Equal(string.Empty, shown[Display.SidebarWidth..].Trim());
            }
        }

        // The town's own boundary wall is there, in the middle.
        Assert.Equal('#', screen.GetRow(topRow)[leftColumn]);
        _ = player;
    }

    /// <summary>
    /// The original's full-screen layouts - news, character sheet, tomb,
    /// scores - are set in the middle of a bigger terminal, and nothing moves
    /// on a 24x80 one.
    /// </summary>
    [Theory]
    [InlineData(24, 80, 0, 0)]
    [InlineData(30, 100, 3, 10)]
    [InlineData(50, 200, 13, 60)]
    public void Centred_OffsetsFullScreenLayouts(int rows, int columns, int rowOrigin, int columnOrigin)
    {
        var screen = new MemoryScreen(rows, columns);
        var display = new Display(new GameState(), screen);

        using (display.Centred())
        {
            display.Print("hello", 0, 0);
            display.PutBuffer("[Press any key to continue.]", 23, 23);
            display.MoveCursor(5, 7);
        }

        Assert.Equal("hello", screen.GetRow(rowOrigin)[columnOrigin..(columnOrigin + 5)]);
        Assert.Equal("[Press any key", screen.GetRow(rowOrigin + 23)[(columnOrigin + 23)..(columnOrigin + 37)]);
        Assert.Equal(rowOrigin + 5, screen.CursorRow);
        Assert.Equal(columnOrigin + 7, screen.CursorColumn);

        // Out of the scope, the origin is back at the corner.
        display.Print("plain", 1, 0);
        Assert.Equal("plain", screen.GetRow(1)[..5]);
    }

    /// <summary>Scopes nest without compounding, and restore what they found.</summary>
    [Fact]
    public void Centred_NestsWithoutCompounding()
    {
        var screen = new MemoryScreen(30, 100);
        var display = new Display(new GameState(), screen);

        using (display.Centred())
        {
            using (display.Centred())
            {
                display.PutBuffer("x", 0, 0);
            }

            display.PutBuffer("y", 1, 0);
        }

        display.PutBuffer("z", 2, 0);

        Assert.Equal('x', screen.GetRow(3)[10]);
        Assert.Equal('y', screen.GetRow(4)[10]);
        Assert.Equal('z', screen.GetRow(2)[0]);
    }
}
