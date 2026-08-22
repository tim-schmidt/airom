// Off-screen character grid with change tracking.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Terminal;

/// <summary>
/// A grid of characters that remembers which rows changed since the last flush.
///
/// This is the part of curses AIrom actually needed: somewhere to compose a
/// frame so the terminal is written once per turn instead of once per cell. A
/// naive port that called Console.SetCursorPosition per character would issue
/// well over a thousand syscalls to paint one dungeon view.
/// </summary>
internal sealed class ScreenBuffer
{
    private readonly char[] _cells;
    private readonly bool[] _rowChanged;

    internal ScreenBuffer(int rows, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);

        Rows = rows;
        Columns = columns;
        _cells = new char[rows * columns];
        _rowChanged = new bool[rows];

        _cells.AsSpan().Fill(' ');
        _rowChanged.AsSpan().Fill(true);
    }

    internal int Rows { get; }

    internal int Columns { get; }

    internal ReadOnlySpan<char> Row(int row) => _cells.AsSpan(row * Columns, Columns);

    internal bool RowChanged(int row) => _rowChanged[row];

    internal void MarkRowClean(int row) => _rowChanged[row] = false;

    internal void MarkAllDirty() => _rowChanged.AsSpan().Fill(true);

    private bool InBounds(int row, int column) =>
        (uint)row < (uint)Rows && (uint)column < (uint)Columns;

    /// <summary>
    /// Writes one character. Out-of-range positions are dropped rather than
    /// throwing: Umoria pokes at coordinates derived from dungeon panels, and
    /// the C simply let curses clip them.
    /// </summary>
    internal void Put(int row, int column, char value)
    {
        if (!InBounds(row, column))
        {
            return;
        }

        int index = (row * Columns) + column;
        if (_cells[index] != value)
        {
            _cells[index] = value;
            _rowChanged[row] = true;
        }
    }

    /// <summary>Writes a run of characters, clipped at the right edge.</summary>
    internal void Put(int row, int column, ReadOnlySpan<char> text)
    {
        if ((uint)row >= (uint)Rows || text.IsEmpty)
        {
            return;
        }

        // Clip a start position left of the grid by dropping the hidden prefix.
        if (column < 0)
        {
            int skipped = -column;
            if (skipped >= text.Length)
            {
                return;
            }

            text = text[skipped..];
            column = 0;
        }

        int writable = Math.Min(text.Length, Columns - column);
        for (int i = 0; i < writable; i++)
        {
            Put(row, column + i, text[i]);
        }
    }

    /// <summary>Blanks from the given column to the end of the row.</summary>
    internal void EraseLine(int row, int column)
    {
        if ((uint)row >= (uint)Rows)
        {
            return;
        }

        for (int c = Math.Max(column, 0); c < Columns; c++)
        {
            Put(row, c, ' ');
        }
    }

    /// <summary>Blanks the given row and every row below it.</summary>
    internal void ClearFrom(int row)
    {
        for (int r = Math.Max(row, 0); r < Rows; r++)
        {
            EraseLine(r, 0);
        }
    }

    internal void Clear() => ClearFrom(0);

    /// <summary>Copies the grid contents into <paramref name="destination"/>.</summary>
    internal void CopyTo(char[] destination) => _cells.AsSpan().CopyTo(destination);

    /// <summary>Replaces the grid contents, marking every row for redraw.</summary>
    internal void CopyFrom(ReadOnlySpan<char> source)
    {
        source.CopyTo(_cells);
        MarkAllDirty();
    }

    internal char[] ToArray() => [.. _cells];

    /// <summary>
    /// A grid of another size holding what this one held, cut or padded at
    /// the bottom and right, with every row marked for redraw.
    /// </summary>
    internal ScreenBuffer Resized(int rows, int columns)
    {
        var resized = new ScreenBuffer(rows, columns);
        Regrid(_cells, Rows, Columns, rows, columns).CopyTo(resized._cells.AsSpan());
        return resized;
    }

    /// <summary>
    /// Lays cells stored row-major for one grid size out for another, keeping
    /// the top-left corner: rows and columns beyond the new size are dropped,
    /// and any the new size adds are blank.
    /// </summary>
    internal static char[] Regrid(
        ReadOnlySpan<char> cells,
        int rows,
        int columns,
        int newRows,
        int newColumns)
    {
        var result = new char[newRows * newColumns];
        result.AsSpan().Fill(' ');

        int sharedRows = Math.Min(rows, newRows);
        int sharedColumns = Math.Min(columns, newColumns);
        for (int row = 0; row < sharedRows; row++)
        {
            cells.Slice(row * columns, sharedColumns)
                .CopyTo(result.AsSpan(row * newColumns, sharedColumns));
        }

        return result;
    }
}
