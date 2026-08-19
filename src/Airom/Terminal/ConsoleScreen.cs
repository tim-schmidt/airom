// System.Console implementation of the terminal surface.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Text;

namespace Airom.Terminal;

/// <summary>
/// Draws AIrom on a Windows console.
///
/// Deliberately built on System.Console with no third-party terminal library.
/// The game needs cursor positioning, character output, clearing and raw key
/// input - nothing a widget framework provides is useful when the game paints
/// every cell itself, and staying dependency-free keeps the single-file publish
/// clean and the NativeAOT path open.
///
/// Output is batched. Each <see cref="Refresh"/> compares the composed frame
/// against what the console last received and writes only the runs that
/// differ, one positioning call per run. Painting a fresh dungeon view costs a
/// few dozen console calls rather than the ~1,450 a per-cell port would.
/// </summary>
public sealed class ConsoleScreen : IScreen
{
    /// <summary>Smallest console Umoria will run in; io.c refuses to start below this.</summary>
    public const int MinimumRows = 24;

    /// <inheritdoc cref="MinimumRows"/>
    public const int MinimumColumns = 80;

    private readonly ScreenBuffer _buffer;
    private readonly char[] _onScreen;
    private readonly StringBuilder _pending = new(1024);
    private char[]? _saved;
    private int _cursorRow;
    private int _cursorColumn;

    /// <summary>
    /// True when output is redirected, as under a test runner. Positioning is
    /// then skipped so the game can be driven headlessly.
    /// </summary>
    private readonly bool _headless;

    public ConsoleScreen(int rows = MinimumRows, int columns = MinimumColumns)
    {
        _buffer = new ScreenBuffer(rows, columns);
        _onScreen = new char[rows * columns];
        // Nothing has been painted yet, so every cell must count as different.
        _onScreen.AsSpan().Clear();

        _headless = Console.IsOutputRedirected;
    }

    public int Rows => _buffer.Rows;

    public int Columns => _buffer.Columns;

    /// <summary>
    /// Prepares the real console: no echo, no line buffering, control keys
    /// delivered to the game. Mirrors what init_curses() and moriaterm() did
    /// with cbreak(), noecho() and nonl().
    /// </summary>
    public static ConsoleScreen Create()
    {
        if (!Console.IsOutputRedirected)
        {
            if (Console.WindowHeight < MinimumRows || Console.WindowWidth < MinimumColumns)
            {
                throw new InvalidOperationException(
                    $"AIrom needs a console of at least {MinimumColumns}x{MinimumRows}; "
                    + $"this one is {Console.WindowWidth}x{Console.WindowHeight}.");
            }

            // Umoria binds several control characters as commands - ^X to save,
            // ^P for message history - so Ctrl+C must arrive as input, not as a
            // signal that kills the process mid-turn.
            Console.TreatControlCAsInput = true;
            Console.CursorVisible = true;
            Console.Clear();
        }

        return new ConsoleScreen();
    }

    /// <summary>Puts the console back the way it was found. Mirrors restore_term().</summary>
    public static void Restore()
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        Console.TreatControlCAsInput = false;
        Console.CursorVisible = true;
        Console.ResetColor();
        Console.SetCursorPosition(0, Math.Max(Console.WindowHeight - 1, 0));
        Console.WriteLine();
    }

    public void Put(int row, int column, char value) => _buffer.Put(row, column, value);

    public void Put(int row, int column, ReadOnlySpan<char> text) =>
        _buffer.Put(row, column, text);

    public void EraseLine(int row, int column) => _buffer.EraseLine(row, column);

    public void ClearFrom(int row) => _buffer.ClearFrom(row);

    public void Clear() => _buffer.Clear();

    public void MoveCursor(int row, int column)
    {
        _cursorRow = Math.Clamp(row, 0, Rows - 1);
        _cursorColumn = Math.Clamp(column, 0, Columns - 1);
    }

    public void Refresh()
    {
        if (_headless)
        {
            // Still reconcile, so tests observe the same buffer the console would.
            for (int row = 0; row < Rows; row++)
            {
                _buffer.Row(row).CopyTo(_onScreen.AsSpan(row * Columns, Columns));
                _buffer.MarkRowClean(row);
            }

            return;
        }

        for (int row = 0; row < Rows; row++)
        {
            if (!_buffer.RowChanged(row))
            {
                continue;
            }

            WriteChangedRuns(row);
            _buffer.Row(row).CopyTo(_onScreen.AsSpan(row * Columns, Columns));
            _buffer.MarkRowClean(row);
        }

        Console.SetCursorPosition(_cursorColumn, _cursorRow);
        Console.Out.Flush();
    }

    /// <summary>
    /// Short gaps of unchanged cells are written through rather than split into
    /// two runs, because another positioning call costs more than a handful of
    /// redundant characters.
    /// </summary>
    internal const int GapTolerance = 4;

    /// <summary>
    /// Works out which stretches of a row have to be rewritten.
    ///
    /// Separated from the console call so it can be tested: this is the only
    /// non-trivial logic in the terminal layer, and getting it wrong shows up as
    /// corrupted output rather than an exception.
    /// </summary>
    /// <returns>(start column, length) pairs, left to right and non-overlapping.</returns>
    internal static List<(int Start, int Length)> ComputeRuns(
        ReadOnlySpan<char> next,
        ReadOnlySpan<char> shown,
        int gapTolerance = GapTolerance)
    {
        List<(int, int)> runs = [];
        int column = 0;

        while (column < next.Length)
        {
            if (next[column] == shown[column])
            {
                column++;
                continue;
            }

            int start = column;
            int lastDifferent = column;
            while (column < next.Length && column - lastDifferent <= gapTolerance)
            {
                if (next[column] != shown[column])
                {
                    lastDifferent = column;
                }

                column++;
            }

            runs.Add((start, lastDifferent - start + 1));
        }

        return runs;
    }

    private void WriteChangedRuns(int row)
    {
        ReadOnlySpan<char> next = _buffer.Row(row);
        ReadOnlySpan<char> shown = _onScreen.AsSpan(row * Columns, Columns);

        foreach ((int start, int length) in ComputeRuns(next, shown))
        {
            _pending.Clear();
            _pending.Append(next.Slice(start, length));

            Console.SetCursorPosition(start, row);
            Console.Out.Write(_pending);
        }
    }

    public void SaveScreen()
    {
        _saved ??= new char[Rows * Columns];
        _buffer.CopyTo(_saved);
    }

    public void RestoreScreen()
    {
        if (_saved is null)
        {
            return;
        }

        _buffer.CopyFrom(_saved);
    }

    public bool KeyAvailable => !Console.IsInputRedirected && Console.KeyAvailable;

    public char ReadKey()
    {
        ConsoleKeyInfo key = Console.ReadKey(intercept: true);

        // Umoria's inkey() deals in plain characters. Keys that produce none -
        // arrows, function keys - come back as '\0' for the caller to map.
        return key.KeyChar;
    }

    public void FlushInput()
    {
        while (KeyAvailable)
        {
            Console.ReadKey(intercept: true);
        }
    }

    public void Bell()
    {
        if (!_headless)
        {
            Console.Out.Write('\a');
        }
    }
}
