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
    /// <summary>Rows of dungeon the map area shows. Umoria's SCREEN_HEIGHT.</summary>
    public const int ViewRows = 22;

    /// <summary>Columns of dungeon the map area shows. Umoria's SCREEN_WIDTH.</summary>
    public const int ViewColumns = 66;

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
        // A level exactly one screen tall gives no scrolling at all, which is
        // what the town relies on. The clamp keeps that from going negative.
        MaxRow = Math.Max((caveHeight / ViewRows * 2) - 2, 0);
        MaxColumn = Math.Max((caveWidth / ViewColumns * 2) - 2, 0);
        Row = MaxRow;
        Column = MaxColumn;
    }

    /// <summary>Recomputes the visible window. Mirrors panel_bounds().</summary>
    public void Bounds()
    {
        RowMin = Row * (ViewRows / 2);
        RowMax = RowMin + ViewRows - 1;
        RowOffset = RowMin - 1;

        ColumnMin = Column * (ViewColumns / 2);
        ColumnMax = ColumnMin + ViewColumns - 1;
        ColumnOffset = ColumnMin - 13;
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

    /// <summary>The window onto the dungeon.</summary>
    public Panel Panel { get; } = new();

    /// <summary>
    /// Writes text at a screen position, clipped at the right edge. Mirrors
    /// put_buffer().
    /// </summary>
    public void PutBuffer(string text, int row, int column)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (column > 79)
        {
            column = 79;
        }

        int room = 79 - column;
        _screen.Put(row, column, text.Length > room ? text[..room] : text);
    }

    /// <summary>Blanks from a column to the end of its row. Mirrors erase_line().</summary>
    public void EraseLine(int row, int column) => _screen.EraseLine(row, column);

    /// <summary>Blanks the whole screen. Mirrors clear_screen().</summary>
    public void ClearScreen() => _screen.Clear();

    /// <summary>Blanks a row and everything below it. Mirrors clear_from().</summary>
    public void ClearFrom(int row) => _screen.ClearFrom(row);

    /// <summary>
    /// Writes a line, clearing whatever was there first. Mirrors prt().
    /// </summary>
    public void Print(string text, int row, int column)
    {
        _screen.EraseLine(row, column);
        PutBuffer(text, row, column);
    }

    /// <summary>
    /// Draws one character at a dungeon position, translated to the screen.
    /// Mirrors print().
    /// </summary>
    public void PrintAt(char symbol, int row, int column) =>
        _screen.Put(row - Panel.RowOffset, column - Panel.ColumnOffset, symbol);

    /// <summary>
    /// Parks the cursor at a dungeon position. Mirrors move_cursor_relative().
    /// </summary>
    public void MoveCursorRelative(int row, int column) =>
        _screen.MoveCursor(row - Panel.RowOffset, column - Panel.ColumnOffset);

    /// <summary>Parks the cursor at a screen position. Mirrors move_cursor().</summary>
    public void MoveCursor(int row, int column) => _screen.MoveCursor(row, column);

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
    /// Blank squares are skipped rather than written, because the row was
    /// cleared first - which is also why the sidebar survives: the clear starts
    /// at column thirteen.
    /// </summary>
    public void PrintMap()
    {
        int screenRow = 0;

        for (int row = Panel.RowMin; row <= Panel.RowMax; row++)
        {
            screenRow++;
            _screen.EraseLine(screenRow, 13);

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
