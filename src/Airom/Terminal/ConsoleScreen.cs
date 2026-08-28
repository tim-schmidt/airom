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
///
/// The grid is the size of the console window, and follows it: while the game
/// waits for a key the window is watched, and a change of size rebuilds the
/// grid, repaints what was on it, and tells whoever is waiting through
/// <see cref="Resized"/>. Windows has no signal for this - curses had SIGWINCH,
/// and the original ignored even that - so the watching is done by looking.
/// </summary>
public sealed class ConsoleScreen : IScreen
{
    /// <summary>Smallest console Umoria will run in; io.c refuses to start below this.</summary>
    public const int MinimumRows = 24;

    /// <inheritdoc cref="MinimumRows"/>
    public const int MinimumColumns = 80;

    /// <summary>
    /// How long to wait between looks at the keyboard and the window while a
    /// key is awaited. Short enough that neither a keypress nor a resize is
    /// felt to lag; long enough that an idle game costs nothing to speak of.
    /// </summary>
    private const int PollMilliseconds = 15;

    private ScreenBuffer _buffer;
    private char[] _onScreen;

    /// <summary>
    /// The size the game is told, which is the grid's size except for the
    /// moment in <see cref="FitToWindow"/> when the grid is being changed:
    /// then the grid is as large as both the old size and the new, so that
    /// whoever is told of the change can move what is on it before anything
    /// is cut, and these already answer for the new size.
    /// </summary>
    private int _rows;

    /// <inheritdoc cref="_rows"/>
    private int _columns;

    /// <summary>True while <see cref="Resized"/> is being called; painting waits.</summary>
    private bool _resizing;
    private readonly StringBuilder _pending = new(1024);
    private char[]? _saved;
    private int _cursorRow;
    private int _cursorColumn;

    /// <summary>
    /// The console window's size as last seen. The grid is never smaller than
    /// the minimum, so a window shrunk below it shows the grid's top-left
    /// corner and the rest is cut; these say where the cut falls.
    /// </summary>
    private int _windowRows;

    /// <inheritdoc cref="_windowRows"/>
    private int _windowColumns;

    /// <summary>
    /// True when output is redirected, as under a test runner. Positioning is
    /// then skipped so the game can be driven headlessly.
    /// </summary>
    private readonly bool _headless;

    public ConsoleScreen(int rows = MinimumRows, int columns = MinimumColumns)
    {
        _windowRows = rows;
        _windowColumns = columns;
        _buffer = new ScreenBuffer(Math.Max(rows, MinimumRows), Math.Max(columns, MinimumColumns));
        _rows = _buffer.Rows;
        _columns = _buffer.Columns;
        _onScreen = new char[_buffer.Rows * _buffer.Columns];
        // Nothing has been painted yet, so every cell must count as different.
        _onScreen.AsSpan().Clear();

        _headless = Console.IsOutputRedirected;
    }

    public int Rows => _rows;

    public int Columns => _columns;

    public Action? Resized { get; set; }

    /// <summary>
    /// Prepares the real console: no echo, no line buffering, control keys
    /// delivered to the game. Mirrors what init_curses() and moriaterm() did
    /// with cbreak(), noecho() and nonl().
    ///
    /// The grid is cut to the window as found. A bigger window gets a bigger
    /// grid, which the game fills with more of the dungeon; a smaller one is
    /// refused, as the original refused it.
    /// </summary>
    public static ConsoleScreen Create()
    {
        if (Console.IsOutputRedirected)
        {
            return new ConsoleScreen();
        }

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
        Console.Write(UnderlineCursor);

        // The classic console does not read that sequence but has a knob of
        // its own, and a terminal that has neither simply keeps its own
        // cursor.
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Console.CursorSize = 20;
            }
        }
        catch (PlatformNotSupportedException)
        {
            // Nothing to put right: the shape is a courtesy, not a feature.
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        Console.Clear();

        return new ConsoleScreen(Console.WindowHeight, Console.WindowWidth);
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
        Console.Write(DefaultCursor);
        Console.ResetColor();

        try
        {
            Console.SetCursorPosition(0, Math.Max(Console.WindowHeight - 1, 0));
        }
        catch (ArgumentOutOfRangeException)
        {
            // A window being resized at the very moment of leaving. The
            // prompt comes back wherever the cursor is, which is fine.
        }

        Console.WriteLine();
    }

    /// <summary>
    /// A blinking underline, which is what the cursor looked like on the
    /// terminals Umoria was written for.
    ///
    /// The game leaves the cursor standing on the player, as curses does, so
    /// its shape decides how that reads. A modern terminal defaults to a bar
    /// drawn down the left edge of the cell, which puts it beside the player
    /// rather than under them and looks like a fault. An underline sits below
    /// the character it is on, where it belongs.
    ///
    /// Terminals that do not understand the sequence ignore it, and the game
    /// looks the way it did before.
    /// </summary>
    private const string UnderlineCursor = "\u001b[3 q";

    /// <summary>Hands the cursor's shape back to the terminal.</summary>
    private const string DefaultCursor = "\u001b[0 q";

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
        if (_resizing)
        {
            // The grid is mid-change; everything is painted once it is done.
            return;
        }

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

        try
        {
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

            // The cursor stays inside the window even when the grid does not.
            Console.SetCursorPosition(
                Math.Min(_cursorColumn, _windowColumns - 1),
                Math.Min(_cursorRow, _windowRows - 1));
            Console.Out.Flush();
        }
        catch (Exception error) when (error is ArgumentOutOfRangeException or IOException)
        {
            // The window shrank between being measured and being written to -
            // a drag of its edge is many sizes in quick succession, and the
            // console refuses a position outside whatever it is at the moment.
            // Nothing is lost: forgetting the measured size makes the next look
            // at the window find it changed, rebuild for the size it has then,
            // and paint everything again.
            ForgetWindow();
        }
    }

    /// <summary>
    /// Marks the window's size as unknown, so <see cref="FitToWindow"/> is
    /// bound to measure it afresh and repaint.
    /// </summary>
    private void ForgetWindow()
    {
        _windowRows = 0;
        _windowColumns = 0;
        _buffer.MarkAllDirty();
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
        // Rows below the window cannot be positioned to; they are left for
        // the day the window grows back.
        if (row >= _windowRows)
        {
            return;
        }

        ReadOnlySpan<char> next = _buffer.Row(row);
        ReadOnlySpan<char> shown = _onScreen.AsSpan(row * Columns, Columns);

        // Likewise columns past the window's right edge. The last cell of the
        // last row is never written either: the console answers a character
        // there by scrolling everything up a line. The original never put
        // anything in that corner, and the layout here keeps it free too, so
        // this guard is only against a window narrowed below the minimum.
        int limit = row == _windowRows - 1 ? _windowColumns - 1 : _windowColumns;

        foreach ((int start, int length) in ComputeRuns(next, shown))
        {
            int writable = Math.Min(length, limit - start);
            if (writable <= 0)
            {
                continue;
            }

            _pending.Clear();
            _pending.Append(next.Slice(start, writable));

            Console.SetCursorPosition(start, row);
            Console.Out.Write(_pending);
        }
    }

    public void SaveScreen()
    {
        _saved ??= new char[_buffer.Rows * _buffer.Columns];
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

    public bool KeyAvailable
    {
        get
        {
            if (Console.IsInputRedirected)
            {
                return false;
            }

            // A run or a rest asks this every step instead of reading a key,
            // so it is also where a resize is noticed during one.
            FitToWindow();
            return Console.KeyAvailable;
        }
    }

    public char ReadKey()
    {
        // A redirected input has no key events to read, only bytes. Umoria can
        // be fed a script the same way, and treats the end of one as a hangup
        // worth saving the game over; here the end simply answers escape,
        // which backs out of whatever was being asked.
        if (Console.IsInputRedirected)
        {
            int typed = Console.In.Read();
            return typed < 0 ? Airom.Core.Keys.Escape : (char)typed;
        }

        // Waiting is done by looking rather than blocking, so the window can
        // be watched while the player makes up their mind.
        while (!Console.KeyAvailable)
        {
            FitToWindow();
            Thread.Sleep(PollMilliseconds);
        }

        ConsoleKeyInfo key = Console.ReadKey(intercept: true);

        char keypad = TranslateKeypad(key);
        if (keypad != '\0')
        {
            return keypad;
        }

        // Umoria's inkey() deals in plain characters. Keys that produce none -
        // arrows, function keys - come back as '\0' for the caller to map.
        return key.KeyChar;
    }

    public bool RogueLikeKeypad { get; set; }

    /// <summary>
    /// Turns an arrow or keypad key into the command character the keyset
    /// binds to that direction, or '\0' for any other key. Mirrors the two
    /// tables in the MSDOS build's bios_getch() (ibmpc/ms_misc.c).
    ///
    /// The DOS tables had three columns - plain, shift and NumLock - with
    /// NumLock holding the control characters that tunnel. A modern NumLock
    /// puts digits on the keypad before the game ever sees them, which is the
    /// original keyset's own walking commands, so here the third column is
    /// reached with Ctrl instead: plain walks, Shift runs, Ctrl tunnels. The
    /// original keyset's columns were all the same, so its modifiers change
    /// nothing, exactly as on DOS.
    ///
    /// Add and Subtract are the keypad's own + and - keys; the row's + and -
    /// arrive as OemPlus and OemMinus and pass through untranslated.
    /// </summary>
    private char TranslateKeypad(ConsoleKeyInfo key)
    {
        char walk = key.Key switch
        {
            ConsoleKey.Home => RogueLikeKeypad ? 'y' : '7',
            ConsoleKey.UpArrow => RogueLikeKeypad ? 'k' : '8',
            ConsoleKey.PageUp => RogueLikeKeypad ? 'u' : '9',
            ConsoleKey.LeftArrow => RogueLikeKeypad ? 'h' : '4',

            // The keypad's 5 with NumLock off, which both tables bind to
            // standing still.
            ConsoleKey.Clear => RogueLikeKeypad ? '.' : '5',
            ConsoleKey.RightArrow => RogueLikeKeypad ? 'l' : '6',
            ConsoleKey.End => RogueLikeKeypad ? 'b' : '1',
            ConsoleKey.DownArrow => RogueLikeKeypad ? 'j' : '2',
            ConsoleKey.PageDown => RogueLikeKeypad ? 'n' : '3',
            ConsoleKey.Insert => 'i',
            ConsoleKey.Delete => '.',
            ConsoleKey.Subtract => RogueLikeKeypad ? '.' : '-',
            ConsoleKey.Add => RogueLikeKeypad
                ? Airom.Core.Keys.Control('P')
                : Airom.Core.Keys.Return,
            _ => '\0',
        };

        if (RogueLikeKeypad && char.IsAsciiLetterLower(walk) && walk != 'i')
        {
            if ((key.Modifiers & ConsoleModifiers.Control) != 0)
            {
                return Airom.Core.Keys.Control(char.ToUpperInvariant(walk));
            }

            if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
            {
                return char.ToUpperInvariant(walk);
            }
        }

        return walk;
    }

    /// <summary>
    /// Brings the grid into line with the console window, if the window has
    /// changed size since it was last looked at, and tells <see cref="Resized"/>.
    /// What the grid held is kept and painted again - the console reflows or
    /// loses it when the window changes, and until the game draws for the
    /// new size it is still the screen the player is looking at.
    ///
    /// When the grid shrinks, whatever lies beyond the new size has to go -
    /// but not before <see cref="Resized"/> has had the chance to move it.
    /// So the grid is first made large enough for both sizes, the new size
    /// is reported, the call is made, and only then is the grid cut.
    /// </summary>
    /// <returns>Whether the window had changed.</returns>
    private bool FitToWindow()
    {
        if (_headless)
        {
            return false;
        }

        // The window's size, or the buffer's if that is somehow smaller: a
        // position is refused outside the buffer, whatever the window says.
        int rows;
        int columns;
        try
        {
            rows = Math.Min(Console.WindowHeight, Console.BufferHeight);
            columns = Math.Min(Console.WindowWidth, Console.BufferWidth);
        }
        catch (IOException)
        {
            return false;
        }

        if (rows < 1 || columns < 1 || (rows == _windowRows && columns == _windowColumns))
        {
            return false;
        }

        _windowRows = rows;
        _windowColumns = columns;

        int gridRows = Math.Max(rows, MinimumRows);
        int gridColumns = Math.Max(columns, MinimumColumns);
        if (gridRows != Rows || gridColumns != Columns)
        {
            Regrid(Math.Max(gridRows, _buffer.Rows), Math.Max(gridColumns, _buffer.Columns));
            _rows = gridRows;
            _columns = gridColumns;

            _resizing = true;
            try
            {
                Resized?.Invoke();
            }
            finally
            {
                _resizing = false;
            }

            Regrid(gridRows, gridColumns);
            MoveCursor(_cursorRow, _cursorColumn);
        }
        else
        {
            Resized?.Invoke();
        }

        // Whatever the console made of the old contents, none of it is trusted.
        try
        {
            Console.Clear();
        }
        catch (IOException)
        {
            ForgetWindow();
            return false;
        }

        _onScreen.AsSpan().Clear();
        _buffer.MarkAllDirty();
        Refresh();
        return true;
    }

    /// <summary>Gives the grid another size, keeping what it held, top-left anchored.</summary>
    private void Regrid(int rows, int columns)
    {
        if (rows == _buffer.Rows && columns == _buffer.Columns)
        {
            return;
        }

        if (_saved is not null)
        {
            _saved = ScreenBuffer.Regrid(_saved, _buffer.Rows, _buffer.Columns, rows, columns);
        }

        _buffer = _buffer.Resized(rows, columns);
        _onScreen = new char[rows * columns];
    }

    public void MoveBlock(int fromRow, int fromColumn, int rows, int columns, int toRow, int toColumn)
    {
        _buffer.MoveBlock(fromRow, fromColumn, rows, columns, toRow, toColumn);

        // The cursor goes with the block if it was inside it.
        if (_cursorRow >= fromRow && _cursorRow < fromRow + rows
            && _cursorColumn >= fromColumn && _cursorColumn < fromColumn + columns)
        {
            MoveCursor(_cursorRow - fromRow + toRow, _cursorColumn - fromColumn + toColumn);
        }
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
