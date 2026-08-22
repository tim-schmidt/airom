// Ported from the drawing half of Umoria 5.6 source/io.c, the panel arithmetic
// in source/misc1.c, and loc_symbol()/prt_map() from the same file.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;
using Airom.Terminal;

namespace Airom.Core;

/// <summary>
/// The window onto the dungeon. Mirrors Umoria's panel globals and the
/// functions in misc1.c that maintain them.
///
/// The dungeon is larger than the screen, so the game shows one panel of it at
/// a time and scrolls when the player nears an edge. Panels overlap by half a
/// screen, which is why the map jumps rather than slides.
/// </summary>
public sealed class Panel
{
    /// <summary>
    /// Rows of dungeon a 24-row terminal shows. Umoria's SCREEN_HEIGHT.
    ///
    /// The original was written for one size of screen, so the same constant
    /// sized both the view and the dungeon: rooms are carved in a grid of
    /// half-screens, and a room is lit or darkened a block at a time. Those
    /// blocks are part of the levels themselves and stay this size however
    /// large the terminal is; <see cref="ViewRows"/> is the view.
    /// </summary>
    public const int BlockRows = 22;

    /// <summary>Columns of dungeon an 80-column terminal shows. Umoria's SCREEN_WIDTH.</summary>
    public const int BlockColumns = 66;

    /// <summary>
    /// Rows of dungeon the map area shows. Umoria's SCREEN_HEIGHT, except that
    /// the terminal decides it: everything between the message line and the
    /// status line.
    /// </summary>
    public int ViewRows { get; private set; }

    /// <summary>
    /// Columns of dungeon the map area shows. Umoria's SCREEN_WIDTH, except
    /// that the terminal decides it: everything right of the sidebar, less the
    /// last column, which the original never used either.
    /// </summary>
    public int ViewColumns { get; private set; }

    private int _caveHeight;
    private int _caveWidth;

    /// <summary>A panel the size the original had, which is what a 24x80 terminal gets.</summary>
    public Panel()
        : this(BlockRows, BlockColumns)
    {
    }

    public Panel(int viewRows, int viewColumns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(viewRows, BlockRows);
        ArgumentOutOfRangeException.ThrowIfLessThan(viewColumns, BlockColumns);

        ViewRows = viewRows;
        ViewColumns = viewColumns;
    }

    /// <summary>Which panel is showing, counted in half-screens.</summary>
    public int Row { get; private set; }

    /// <inheritdoc cref="Row"/>
    public int Column { get; private set; }

    /// <summary>
    /// Puts the panel indices out of range, so the next follow is bound to
    /// recompute. Mirrors dungeon()'s "panel_row = panel_col = -1", which is how
    /// arriving on a level guarantees the map is drawn.
    /// </summary>
    public void Invalidate()
    {
        Row = -1;
        Column = -1;

        // And the window with them. Follow() only recomputes an axis when the
        // player looks close to that edge of the window, so a window left
        // standing can answer "no need" for one axis while the other moves -
        // leaving the panel half at minus one, and the view running off the
        // side of the level. The original starts with these at nought, where
        // every position looks out of range, and this is that state.
        RowMin = 0;
        RowMax = 0;
        ColumnMin = 0;
        ColumnMax = 0;
    }

    /// <summary>Highest panel index in each direction, from the level's size.</summary>
    public int MaxRow { get; private set; }

    /// <inheritdoc cref="MaxRow"/>
    public int MaxColumn { get; private set; }

    /// <summary>First dungeon row the panel shows.</summary>
    public int RowMin { get; private set; }

    /// <summary>Last dungeon row the panel shows.</summary>
    public int RowMax { get; private set; }

    /// <inheritdoc cref="RowMin"/>
    public int ColumnMin { get; private set; }

    /// <inheritdoc cref="RowMax"/>
    public int ColumnMax { get; private set; }

    /// <summary>
    /// Subtracted from a dungeon row to get a screen row. Offset by one because
    /// the message line occupies the top of the screen.
    /// </summary>
    public int RowOffset { get; private set; }

    /// <summary>
    /// Subtracted from a dungeon column to get a screen column. Offset by
    /// thirteen, which is the width of the status sidebar.
    /// </summary>
    public int ColumnOffset { get; private set; }

    /// <summary>
    /// Sizes the panel grid for a level. Mirrors the setup in generate_cave().
    ///
    /// FAITHFUL QUIRK - do not add a Bounds() call here. generate_cave sets the
    /// panel indices without recomputing the window, and get_panel only
    /// recomputes when the panel actually changes. So a player who happens to
    /// start in the panel the indices already name leaves the window at
    /// whatever the previous level set - zero on the first level of a session.
    /// Recomputing here looks like an obvious fix and shifts the drawn map.
    /// </summary>
    public void Resize(int caveHeight, int caveWidth)
    {
        _caveHeight = caveHeight;
        _caveWidth = caveWidth;
        MaxRow = LastPanel(caveHeight, ViewRows);
        MaxColumn = LastPanel(caveWidth, ViewColumns);
        Row = MaxRow;
        Column = MaxColumn;
    }

    /// <summary>
    /// How many half-screens the view can scroll along one axis of a level.
    ///
    /// The original writes this as "(cur_height / SCREEN_HEIGHT) * 2 - 2", which
    /// is the same number for the two heights and two widths a level can have
    /// when the view is the size it assumes, and wrong for any other: a view
    /// taller than half a level would be told there was nothing to scroll to.
    /// This asks the question directly - how many steps of half a view until
    /// the last panel reaches the far edge - and the unit test holds it to the
    /// original's answers.
    ///
    /// A level no larger than the view gives no scrolling at all, which is
    /// what the town relies on.
    /// </summary>
    internal static int LastPanel(int caveExtent, int viewExtent)
    {
        int step = viewExtent / 2;
        int beyond = caveExtent - viewExtent;
        return beyond <= 0 ? 0 : (beyond + step - 1) / step;
    }

    /// <summary>
    /// Gives the panel a new view size, as when the terminal has been resized.
    /// The grid is re-counted for the level it was last sized to and the
    /// window forgotten, so the next <see cref="Follow"/> lays it out afresh.
    /// </summary>
    public void SetView(int viewRows, int viewColumns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(viewRows, BlockRows);
        ArgumentOutOfRangeException.ThrowIfLessThan(viewColumns, BlockColumns);

        ViewRows = viewRows;
        ViewColumns = viewColumns;

        if (_caveHeight > 0)
        {
            MaxRow = LastPanel(_caveHeight, ViewRows);
            MaxColumn = LastPanel(_caveWidth, ViewColumns);
        }

        Invalidate();
    }

    /// <summary>Recomputes the visible window. Mirrors panel_bounds().</summary>
    public void Bounds()
    {
        RowMin = Row * (ViewRows / 2);
        ColumnMin = Column * (ViewColumns / 2);

        // Two things the original's arithmetic never had to meet, because at
        // its size a level is always a whole number of half-screens and the
        // last panel ends exactly on the level's edge. A view the level is
        // not a whole number of half-views larger than would have its last
        // panel hang past the edge, showing a sliver of level and a lot of
        // nothing; it is pulled back to end on the edge instead. And a view
        // larger than the level - the town on a tall terminal - stops at the
        // level's edge rather than looking beyond it. Neither moves anything
        // at the original's size.
        if (_caveHeight > 0 && Row >= 0)
        {
            RowMin = Math.Min(RowMin, Math.Max(_caveHeight - ViewRows, 0));
        }

        if (_caveWidth > 0 && Column >= 0)
        {
            ColumnMin = Math.Min(ColumnMin, Math.Max(_caveWidth - ViewColumns, 0));
        }

        RowMax = RowMin + ViewRows - 1;
        RowOffset = RowMin - 1;
        ColumnMax = ColumnMin + ViewColumns - 1;
        ColumnOffset = ColumnMin - Display.SidebarWidth;

        if (_caveHeight > 0)
        {
            RowMax = Math.Min(RowMax, _caveHeight - 1);
            ColumnMax = Math.Min(ColumnMax, _caveWidth - 1);

            // A level smaller than the view is drawn in the middle of it
            // rather than tucked into the top-left corner. The offsets are
            // what place a square on the screen, so this is all it takes.
            RowOffset -= Math.Max(ViewRows - _caveHeight, 0) / 2;
            ColumnOffset -= Math.Max(ViewColumns - _caveWidth, 0) / 2;
        }
    }

    /// <summary>
    /// Scrolls the view if the player has come within two rows or three columns
    /// of an edge. Mirrors get_panel().
    /// </summary>
    /// <param name="force">Recompute even if the player is nowhere near an edge.</param>
    /// <returns>Whether the view moved.</returns>
    public bool Follow(int row, int column, bool force)
    {
        int wantRow = Row;
        int wantColumn = Column;

        if (force || row < RowMin + 2 || row > RowMax - 2)
        {
            wantRow = (row - (ViewRows / 4)) / (ViewRows / 2);
            wantRow = Math.Clamp(wantRow, 0, MaxRow);
        }

        if (force || column < ColumnMin + 3 || column > ColumnMax - 3)
        {
            wantColumn = (column - (ViewColumns / 4)) / (ViewColumns / 2);
            wantColumn = Math.Clamp(wantColumn, 0, MaxColumn);
        }

        if (wantRow == Row && wantColumn == Column)
        {
            return false;
        }

        Row = wantRow;
        Column = wantColumn;
        Bounds();
        return true;
    }

    /// <summary>
    /// Puts the panel grid back the size a saved game says it was, without
    /// touching which panel is showing.
    /// </summary>
    public void RestoreBounds(int maxRow, int maxColumn, int caveHeight, int caveWidth)
    {
        _caveHeight = caveHeight;
        _caveWidth = caveWidth;
        MaxRow = maxRow;
        MaxColumn = maxColumn;

        // The saved figures describe the view the game was saved under. A
        // terminal of another size needs them counted again for its own view,
        // which for the original's size is the number that was saved.
        if (ViewRows != BlockRows || ViewColumns != BlockColumns)
        {
            MaxRow = LastPanel(caveHeight, ViewRows);
            MaxColumn = LastPanel(caveWidth, ViewColumns);
        }

        // The window is deliberately not recomputed. get_char() restores how
        // far the view may scroll and nothing else, leaving the window where a
        // fresh game leaves it, and the first look at the level settles it.
        // Computing it here instead puts a plausible window on a level the
        // player is not standing in, which is worse than none at all.
    }

    /// <summary>
    /// What a savefile records for how far the view scrolls: the original's
    /// count for the original's view, so a game saved here reads back into a
    /// real Umoria - or into this port under a terminal of another size - the
    /// way one saved there would. The same as <see cref="MaxRow"/> when the
    /// view is the original's size, and that includes whatever a restored
    /// game brought with it.
    /// </summary>
    public int SavedMaxRow =>
        ViewRows == BlockRows && ViewColumns == BlockColumns
            ? MaxRow
            : LastPanel(_caveHeight, BlockRows);

    /// <inheritdoc cref="SavedMaxRow"/>
    public int SavedMaxColumn =>
        ViewRows == BlockRows && ViewColumns == BlockColumns
            ? MaxColumn
            : LastPanel(_caveWidth, BlockColumns);

    /// <summary>Whether a dungeon square is inside the visible window. Mirrors panel_contains().</summary>
    public bool Contains(int row, int column) =>
        row >= RowMin && row <= RowMax && column >= ColumnMin && column <= ColumnMax;
}

/// <summary>
/// Drawing the game onto the screen. Mirrors the output half of io.c together
/// with prt_map() and loc_symbol().
///
/// Everything here writes into an <see cref="IScreen"/> rather than a terminal,
/// so the composed frame can be read back and compared against the original's.
/// </summary>
public sealed partial class Display(GameState game, IScreen screen)
{
    /// <summary>The row messages appear on. Umoria's MSG_LINE.</summary>
    public const int MessageLine = 0;

    private readonly GameState _game = game;
    private readonly IScreen _screen = screen;

    /// <summary>Width of the status sidebar, which is where the map begins.</summary>
    public const int SidebarWidth = 13;

    /// <summary>
    /// The window onto the dungeon, sized to the screen: the rows between the
    /// message line and the status line, and the columns right of the sidebar
    /// less the last one. On a 24x80 terminal that is the original's 22x66.
    /// </summary>
    public Panel Panel { get; } = new(ViewRowsFor(screen.Rows), ViewColumnsFor(screen.Columns));

    /// <summary>Rows of map a screen of the given height shows.</summary>
    public static int ViewRowsFor(int screenRows) => screenRows - 2;

    /// <summary>Columns of map a screen of the given width shows.</summary>
    public static int ViewColumnsFor(int screenColumns) => screenColumns - SidebarWidth - 1;

    /// <summary>The row the status line sits on. Row 23 in the original.</summary>
    public int StatusLine => _screen.Rows - 1;

    /// <summary>The last column text may reach. Column 79 in the original.</summary>
    private int LastColumn => _screen.Columns - 1;

    /// <summary>
    /// Where row 0, column 0 of a full-screen layout falls on the screen. Zero
    /// except inside <see cref="Centred"/>, when the original's 24x80 layouts
    /// - the news, the character sheet, the tomb, the scores - are set in the
    /// middle of a bigger terminal instead of its top-left corner.
    /// </summary>
    private int _originRow;

    /// <inheritdoc cref="_originRow"/>
    private int _originColumn;

    /// <summary>
    /// Until the returned scope is disposed, text written by row and column
    /// is placed as if the screen were the original's 24 by 80, set in the
    /// middle of the terminal. For a terminal of that size nothing moves.
    ///
    /// Only the drawing that goes by screen position moves: <see cref="PutBuffer"/>,
    /// <see cref="Print"/>, <see cref="EraseLine"/>, <see cref="ClearFrom"/>,
    /// <see cref="MoveCursor"/> and <see cref="GetString"/>. The map and the
    /// cursor on it are placed by the panel and are not affected, which is
    /// why the scope must be left before the playing screen is drawn again.
    /// Scopes nest, each setting the same origin, so a help screen shown from
    /// inside the character sheet lands where the sheet did.
    /// </summary>
    public IDisposable Centred()
    {
        var scope = new OriginScope(this, _originRow, _originColumn);
        _originRow = Math.Max((_screen.Rows - ConsoleScreen.MinimumRows) / 2, 0);
        _originColumn = Math.Max((_screen.Columns - ConsoleScreen.MinimumColumns) / 2, 0);
        return scope;
    }

    private sealed class OriginScope(Display display, int row, int column) : IDisposable
    {
        public void Dispose()
        {
            display._originRow = row;
            display._originColumn = column;
        }
    }

    /// <summary>
    /// Whether the screen has changed size since the panel was laid out for
    /// it. The terminal can be resized at any moment; the game notices here,
    /// the next time it goes to draw the whole screen.
    /// </summary>
    public bool ScreenSizeChanged =>
        Panel.ViewRows != ViewRowsFor(_screen.Rows)
        || Panel.ViewColumns != ViewColumnsFor(_screen.Columns);

    /// <summary>
    /// Writes text at a screen position, clipped at the right edge. Mirrors
    /// put_buffer().
    /// </summary>
    public void PutBuffer(string text, int row, int column)
    {
        ArgumentNullException.ThrowIfNull(text);

        row += _originRow;
        column += _originColumn;

        if (column > LastColumn)
        {
            column = LastColumn;
        }

        int room = LastColumn - column;
        string written = text.Length > room ? text[..room] : text;

        _screen.Put(row, column, written);

        // Where curses leaves the cursor: writing carries it along, so it ends
        // just past what was written. get_check() reads it back to decide
        // whether a long prompt has pushed its answer off the screen.
        _screen.MoveCursor(row, column + written.Length);
    }

    /// <summary>Blanks from a column to the end of its row. Mirrors erase_line().</summary>
    public void EraseLine(int row, int column)
    {
        // Wiping the message line has to let the player read what is on it
        // first, or a message could vanish before it was ever seen.
        if (row == MessageLine && MessageWaiting)
        {
            MessagePrint(null);
        }

        row += _originRow;
        column += _originColumn;

        _screen.MoveCursor(row, column);
        _screen.EraseLine(row, column);
    }

    /// <summary>Blanks the whole screen. Mirrors clear_screen().</summary>
    public void ClearScreen()
    {
        if (MessageWaiting)
        {
            MessagePrint(null);
        }

        _screen.Clear();
    }

    /// <summary>Blanks a row and everything below it. Mirrors clear_from().</summary>
    public void ClearFrom(int row) => _screen.ClearFrom(row + _originRow);

    /// <summary>
    /// Writes a line, clearing whatever was there first. Mirrors prt().
    /// </summary>
    public void Print(string text, int row, int column)
    {
        EraseLine(row, column);
        PutBuffer(text, row, column);
    }

    /// <summary>
    /// Draws one character at a dungeon position, translated to the screen.
    /// Mirrors print().
    /// </summary>
    public void PrintAt(char symbol, int row, int column)
    {
        int screenRow = row - Panel.RowOffset;
        int screenColumn = column - Panel.ColumnOffset;

        // Drawn wherever the arithmetic says, clipped only at the screen's
        // edge, as curses clipped for the original. Keeping a square off the
        // message line, the status line and the sidebar is the caller's job,
        // as it was there - see the note on Lighting.LightRoom.
        _screen.Put(screenRow, screenColumn, symbol);

        // A single character does carry the cursor along with it, which is the
        // one place writing and moving differ.
        _screen.MoveCursor(screenRow, screenColumn + 1);
    }

    /// <summary>
    /// Parks the cursor at a dungeon position. Mirrors move_cursor_relative().
    /// </summary>
    public void MoveCursorRelative(int row, int column) =>
        _screen.MoveCursor(row - Panel.RowOffset, column - Panel.ColumnOffset);

    /// <summary>Parks the cursor at a screen position. Mirrors move_cursor().</summary>
    public void MoveCursor(int row, int column) =>
        _screen.MoveCursor(row + _originRow, column + _originColumn);

    /// <summary>
    /// What a dungeon square looks like from where the player stands. Mirrors
    /// loc_symbol().
    ///
    /// The order of the tests is the priority: the player first, then blindness
    /// and hallucination, then monsters, then whether the square has been seen
    /// at all, then objects, then the terrain underneath.
    /// </summary>
    public char SymbolAt(int row, int column)
    {
        CaveSquare square = _game.Cave[row, column];

        // The player is left out of a run unless they ask to be drawn, which
        // makes a long run less flickery.
        if (square.MonsterIndex == 1 && (!_game.Running || _game.ShowSelfWhileRunning))
        {
            return '@';
        }

        if ((_game.Player.Status & PlayerStatus.Blind) != 0)
        {
            return ' ';
        }

        // Hallucinating turns one square in twelve into something else
        // entirely, redrawn differently every time the map is repainted.
        if (_game.Player.Hallucinating > 0 && _game.Rng.RandInt(12) == 1)
        {
            return (char)(_game.Rng.RandInt(95) + 31);
        }

        if (square.MonsterIndex > 1 && _game.Monsters[square.MonsterIndex].Visible)
        {
            return GameTables.CreatureList[
                _game.Monsters[square.MonsterIndex].CreatureIndex].DisplayChar;
        }

        // Never seen and not currently lit: the player has no idea.
        if (!square.PermanentLight && !square.TemporaryLight && !square.FieldMark)
        {
            return ' ';
        }

        if (square.ObjectIndex != 0
            && _game.Objects[square.ObjectIndex].TVal != ItemCategory.InvisibleTrap)
        {
            return _game.Objects[square.ObjectIndex].DisplayChar;
        }

        if (square.Feature <= CaveFeature.MaxCaveFloor)
        {
            return '.';
        }

        // Mineral veins are drawn as plain wall unless the player has asked for
        // them to be picked out.
        if (square.Feature is CaveFeature.GraniteWall or CaveFeature.BoundaryWall
            || !_game.HighlightSeams)
        {
            return '#';
        }

        return square.Feature switch
        {
            CaveFeature.MagmaWall => '%',
            CaveFeature.QuartzWall => '*',
            _ => '#',
        };
    }

    /// <summary>
    /// Draws the visible part of the level. Mirrors prt_map().
    ///
    /// Blank squares are skipped rather than written, because the map area was
    /// cleared first - which is also why the sidebar survives: the clear starts
    /// at column thirteen. The whole area is cleared, not only the rows the
    /// panel covers: the original's panel always covered it all, but a level
    /// smaller than the view - the town - covers only the middle, and what
    /// the last level left around it has to go.
    /// </summary>
    public void PrintMap()
    {
        for (int screenRow = 1; screenRow <= Panel.ViewRows; screenRow++)
        {
            _screen.EraseLine(screenRow, SidebarWidth);
        }

        for (int row = Panel.RowMin; row <= Panel.RowMax; row++)
        {
            for (int column = Panel.ColumnMin; column <= Panel.ColumnMax; column++)
            {
                char symbol = SymbolAt(row, column);
                if (symbol != ' ')
                {
                    PrintAt(symbol, row, column);
                }
            }
        }
    }
}
