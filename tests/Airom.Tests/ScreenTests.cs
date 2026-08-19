using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the terminal layer that replaces Umoria's curses use.
///
/// The clipping behaviour matters more than it looks. Umoria derives screen
/// coordinates from dungeon panel offsets and cheerfully hands curses positions
/// that fall outside the window, relying on it to discard them. The port has to
/// do the same rather than throw.
/// </summary>
public class ScreenTests
{
    private static MemoryScreen NewScreen() => new(rows: 24, columns: 80);

    [Fact]
    public void NewScreen_IsBlankAndTheRightSize()
    {
        MemoryScreen screen = NewScreen();

        Assert.Equal(24, screen.Rows);
        Assert.Equal(80, screen.Columns);
        Assert.Equal(new string(' ', 80), screen.GetRow(0));
        Assert.Equal(string.Empty, screen.GetText().Trim());
    }

    [Fact]
    public void Put_WritesCharactersAndStrings()
    {
        MemoryScreen screen = NewScreen();

        screen.Put(0, 0, '@');
        screen.Put(1, 3, "Welcome to AIrom");

        Assert.StartsWith("@", screen.GetRow(0), StringComparison.Ordinal);
        Assert.Equal("   Welcome to AIrom", screen.GetRow(1).TrimEnd());
    }

    [Fact]
    public void Put_ClipsAtTheRightEdgeInsteadOfThrowing()
    {
        MemoryScreen screen = NewScreen();

        screen.Put(0, 76, "ABCDEFGH");

        Assert.Equal("ABCD", screen.GetRow(0)[76..]);
    }

    /// <summary>
    /// A string starting left of the grid keeps only the part that lands on it,
    /// which is how curses handled a panel scrolled past the origin.
    /// </summary>
    [Fact]
    public void Put_DropsThePrefixThatFallsOffTheLeftEdge()
    {
        MemoryScreen screen = NewScreen();

        screen.Put(0, -3, "ABCDEF");

        Assert.Equal("DEF", screen.GetRow(0).TrimEnd());
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(24, 0)]
    [InlineData(0, 80)]
    [InlineData(999, 999)]
    public void Put_IgnoresPositionsOffTheGrid(int row, int column)
    {
        MemoryScreen screen = NewScreen();

        screen.Put(row, column, '@');

        Assert.Equal(string.Empty, screen.GetText().Trim());
    }

    [Fact]
    public void Put_WhollyOffTheLeftEdge_WritesNothing()
    {
        MemoryScreen screen = NewScreen();

        screen.Put(0, -10, "ABC");

        Assert.Equal(string.Empty, screen.GetText().Trim());
    }

    [Fact]
    public void EraseLine_BlanksFromTheColumnToTheEnd()
    {
        MemoryScreen screen = NewScreen();
        screen.Put(2, 0, "keep this and drop the rest");

        screen.EraseLine(2, 9);

        Assert.Equal("keep this", screen.GetRow(2).TrimEnd());
    }

    [Fact]
    public void ClearFrom_BlanksTheRowAndEverythingBelow()
    {
        MemoryScreen screen = NewScreen();
        screen.Put(0, 0, "header");
        screen.Put(5, 0, "middle");
        screen.Put(23, 0, "footer");

        screen.ClearFrom(5);

        Assert.Equal("header", screen.GetRow(0).TrimEnd());
        Assert.Equal(string.Empty, screen.GetRow(5).Trim());
        Assert.Equal(string.Empty, screen.GetRow(23).Trim());
    }

    [Fact]
    public void Clear_BlanksEverything()
    {
        MemoryScreen screen = NewScreen();
        screen.Put(0, 0, "something");
        screen.Put(23, 40, "else");

        screen.Clear();

        Assert.Equal(string.Empty, screen.GetText().Trim());
    }

    /// <summary>
    /// save_screen()/restore_screen() bracket every overlay Umoria draws - the
    /// inventory list, the help screens - so the round trip has to be exact.
    /// </summary>
    [Fact]
    public void SaveAndRestoreScreen_RoundTripsExactly()
    {
        MemoryScreen screen = NewScreen();
        screen.Put(3, 10, "the dungeon behind the overlay");
        screen.Put(20, 0, "status line");
        string before = screen.GetText();

        screen.SaveScreen();
        screen.Clear();
        screen.Put(0, 0, "an inventory overlay");
        Assert.NotEqual(before, screen.GetText());

        screen.RestoreScreen();

        Assert.Equal(before, screen.GetText());
    }

    [Fact]
    public void RestoreScreen_WithoutASave_LeavesTheScreenAlone()
    {
        MemoryScreen screen = NewScreen();
        screen.Put(0, 0, "untouched");

        screen.RestoreScreen();

        Assert.Equal("untouched", screen.GetRow(0).TrimEnd());
    }

    [Fact]
    public void MoveCursor_ClampsToTheGrid()
    {
        MemoryScreen screen = NewScreen();

        screen.MoveCursor(100, 100);
        Assert.Equal(23, screen.CursorRow);
        Assert.Equal(79, screen.CursorColumn);

        screen.MoveCursor(-5, -5);
        Assert.Equal(0, screen.CursorRow);
        Assert.Equal(0, screen.CursorColumn);
    }

    // ------------------------------------------------------------ input

    [Fact]
    public void ReadKey_ReturnsScriptedKeysInOrder()
    {
        MemoryScreen screen = NewScreen();
        screen.SendKeys("hjkl");

        Assert.Equal('h', screen.ReadKey());
        Assert.Equal('j', screen.ReadKey());
        Assert.Equal('k', screen.ReadKey());
        Assert.Equal('l', screen.ReadKey());
    }

    [Fact]
    public void KeyAvailable_TracksTheScript()
    {
        MemoryScreen screen = NewScreen();
        Assert.False(screen.KeyAvailable);

        screen.SendKeys('x');
        Assert.True(screen.KeyAvailable);

        screen.ReadKey();
        Assert.False(screen.KeyAvailable);
    }

    /// <summary>
    /// Running out of scripted input must fail loudly. A headless run that
    /// blocked waiting for a key would hang rather than report anything.
    /// </summary>
    [Fact]
    public void ReadKey_ThrowsWhenTheScriptRunsOut()
    {
        MemoryScreen screen = NewScreen();

        Assert.Throws<InvalidOperationException>(() => screen.ReadKey());
    }

    [Fact]
    public void FlushInput_DiscardsTypeAhead()
    {
        MemoryScreen screen = NewScreen();
        screen.SendKeys("aaaaa");

        screen.FlushInput();

        Assert.False(screen.KeyAvailable);
    }

    /// <summary>
    /// Umoria binds control characters as commands, so they have to survive as
    /// ordinary input rather than being swallowed.
    /// </summary>
    [Fact]
    public void ControlCharacters_PassThroughUnchanged()
    {
        MemoryScreen screen = NewScreen();
        screen.SendKeys('\x18', '\x10', '\x16'); // ^X save, ^P messages, ^V licence

        Assert.Equal('\x18', screen.ReadKey());
        Assert.Equal('\x10', screen.ReadKey());
        Assert.Equal('\x16', screen.ReadKey());
    }

    [Fact]
    public void Bell_IsCounted()
    {
        MemoryScreen screen = NewScreen();

        screen.Bell();
        screen.Bell();

        Assert.Equal(2, screen.BellCount);
    }

    // ------------------------------------------------------------ buffering

    /// <summary>
    /// Nothing should reach the terminal until Refresh, matching how Umoria
    /// composes a frame and then calls put_qio().
    /// </summary>
    [Fact]
    public void Refresh_IsCountedSeparatelyFromDrawing()
    {
        MemoryScreen screen = NewScreen();

        screen.Put(0, 0, "drawn but not shown");
        Assert.Equal(0, screen.RefreshCount);

        screen.Refresh();
        Assert.Equal(1, screen.RefreshCount);
    }

    [Fact]
    public void ScreenBuffer_MarksOnlyTouchedRowsAsChanged()
    {
        var buffer = new ScreenBuffer(24, 80);
        for (int row = 0; row < 24; row++)
        {
            buffer.MarkRowClean(row);
        }

        buffer.Put(7, 3, 'x');

        Assert.True(buffer.RowChanged(7));
        Assert.False(buffer.RowChanged(6));
        Assert.False(buffer.RowChanged(8));
    }

    /// <summary>
    /// Writing the value already present is not a change. Umoria repaints the
    /// map every turn, so most cells are rewritten identically and must not
    /// force the row out to the console again.
    /// </summary>
    [Fact]
    public void ScreenBuffer_IgnoresWritesThatChangeNothing()
    {
        var buffer = new ScreenBuffer(24, 80);
        buffer.Put(4, 4, '#');
        for (int row = 0; row < 24; row++)
        {
            buffer.MarkRowClean(row);
        }

        buffer.Put(4, 4, '#');

        Assert.False(buffer.RowChanged(4));
    }

    [Fact]
    public void ScreenBuffer_RejectsNonsenseDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenBuffer(0, 80));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenBuffer(24, 0));
    }
}

/// <summary>
/// Tests for the run-diffing that decides what actually reaches the console.
/// A mistake here corrupts the display rather than throwing, so it is worth
/// covering directly.
/// </summary>
public class ConsoleScreenRunTests
{
    private static List<(int Start, int Length)> Runs(string next, string shown, int gap = 4) =>
        ConsoleScreen.ComputeRuns(next.AsSpan(), shown.AsSpan(), gap);

    [Fact]
    public void IdenticalRows_ProduceNoOutput() =>
        Assert.Empty(Runs("hello world", "hello world"));

    [Fact]
    public void AWhollyDifferentRow_IsOneRun()
    {
        var runs = Runs("abcdef", "......");

        (int start, int length) = Assert.Single(runs);
        Assert.Equal(0, start);
        Assert.Equal(6, length);
    }

    [Fact]
    public void ASingleChangedCell_IsAOneCharacterRun()
    {
        var runs = Runs("a@c", "abc");

        Assert.Equal([(1, 1)], runs);
    }

    /// <summary>
    /// A short unchanged gap is written through, because one extra positioning
    /// call costs more than repainting a few identical characters.
    /// </summary>
    [Fact]
    public void ShortGaps_AreBridgedIntoOneRun()
    {
        // Two changes three cells apart, inside the tolerance of four.
        var runs = Runs("X...Y", ".....");

        Assert.Equal([(0, 5)], runs);
    }

    [Fact]
    public void LongGaps_SplitIntoSeparateRuns()
    {
        // Ten unchanged cells between the changes, well past the tolerance.
        var runs = Runs("X..........Y", "............");

        Assert.Equal([(0, 1), (11, 1)], runs);
    }

    [Fact]
    public void RunsAreOrderedAndDoNotOverlap()
    {
        var runs = Runs("A.........B.........C", ".....................");

        Assert.Equal([(0, 1), (10, 1), (20, 1)], runs);

        for (int i = 1; i < runs.Count; i++)
        {
            Assert.True(runs[i].Start >= runs[i - 1].Start + runs[i - 1].Length);
        }
    }

    [Fact]
    public void EveryDifferingCell_FallsInsideSomeRun()
    {
        const string Shown = "the quick brown fox jumps over the lazy dog";
        const string Next  = "the QUICK brown fox JUMPS over the lazy DOG";

        var runs = Runs(Next, Shown);

        for (int i = 0; i < Next.Length; i++)
        {
            if (Next[i] == Shown[i])
            {
                continue;
            }

            Assert.Contains(runs, r => i >= r.Start && i < r.Start + r.Length);
        }
    }

    [Fact]
    public void ChangeAtTheLastColumn_IsCaptured()
    {
        var runs = Runs("abcX", "abc.");

        Assert.Equal([(3, 1)], runs);
    }
}
