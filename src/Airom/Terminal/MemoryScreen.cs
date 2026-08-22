// Headless terminal surface.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Terminal;

/// <summary>
/// An <see cref="IScreen"/> that renders into memory and reads keys from a
/// script.
///
/// This is not only test scaffolding. Verifying the port means driving AIrom
/// and the original C from the same seed and comparing what each draws, which
/// needs a way to run the game with no console attached and read the frame back
/// as text.
/// </summary>
public sealed class MemoryScreen : IScreen
{
    private ScreenBuffer _buffer;
    private readonly Queue<char> _input = new();
    private char[]? _saved;

    public MemoryScreen(
        int rows = ConsoleScreen.MinimumRows,
        int columns = ConsoleScreen.MinimumColumns)
    {
        _buffer = new ScreenBuffer(rows, columns);
    }

    public int Rows => _buffer.Rows;

    public int Columns => _buffer.Columns;

    public Action? Resized { get; set; }

    /// <summary>
    /// Changes the grid's size the way a console screen does when its window
    /// is resized: what it held is kept, cut or padded at the bottom and
    /// right, and whoever is waiting on <see cref="Resized"/> is told. Lets a
    /// test resize the terminal under the game.
    /// </summary>
    public void Resize(int rows, int columns)
    {
        if (_saved is not null)
        {
            _saved = ScreenBuffer.Regrid(_saved, Rows, Columns, rows, columns);
        }

        _buffer = _buffer.Resized(rows, columns);
        MoveCursor(CursorRow, CursorColumn);
        Resized?.Invoke();
    }

    /// <summary>How many times <see cref="Refresh"/> has been called.</summary>
    public int RefreshCount { get; private set; }

    /// <summary>Cursor row as last set by <see cref="MoveCursor"/>.</summary>
    public int CursorRow { get; private set; }

    /// <summary>Cursor column as last set by <see cref="MoveCursor"/>.</summary>
    public int CursorColumn { get; private set; }

    /// <summary>How many times <see cref="Bell"/> has been called.</summary>
    public int BellCount { get; private set; }

    /// <summary>Queues keypresses for <see cref="ReadKey"/> to return in order.</summary>
    public void SendKeys(params char[] keys)
    {
        foreach (char key in keys)
        {
            _input.Enqueue(key);
        }
    }

    /// <inheritdoc cref="SendKeys(char[])"/>
    public void SendKeys(string keys) => SendKeys(keys.ToCharArray());

    /// <summary>
    /// Replaces the waiting keys rather than adding to them, so a harness can
    /// give one action its own script without whatever the last one left behind.
    /// </summary>
    public void SetKeys(string keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _input.Clear();
        SendKeys(keys);
    }

    /// <summary>One row of the composed frame, as text.</summary>
    public string GetRow(int row) => new(_buffer.Row(row));

    /// <summary>The whole frame, one line per row, trailing blanks trimmed.</summary>
    public string GetText() =>
        string.Join(
            Environment.NewLine,
            Enumerable.Range(0, Rows).Select(r => GetRow(r).TrimEnd()));

    public void Put(int row, int column, char value) => _buffer.Put(row, column, value);

    public void Put(int row, int column, ReadOnlySpan<char> text) =>
        _buffer.Put(row, column, text);

    public void EraseLine(int row, int column) => _buffer.EraseLine(row, column);

    public void ClearFrom(int row) => _buffer.ClearFrom(row);

    public void Clear() => _buffer.Clear();

    public void MoveCursor(int row, int column)
    {
        CursorRow = Math.Clamp(row, 0, Rows - 1);
        CursorColumn = Math.Clamp(column, 0, Columns - 1);
    }

    public void Refresh()
    {
        RefreshCount++;
        for (int row = 0; row < Rows; row++)
        {
            _buffer.MarkRowClean(row);
        }
    }

    public void SaveScreen()
    {
        _saved ??= new char[Rows * Columns];
        _buffer.CopyTo(_saved);
    }

    public void RestoreScreen()
    {
        if (_saved is not null)
        {
            _buffer.CopyFrom(_saved);
        }
    }

    public bool KeyAvailable => TypeAheadVisible && _input.Count > 0;

    /// <summary>
    /// Whether queued keys count as type-ahead. The oracle turns this off: its
    /// key script is the player typing on cue, not keys already waiting, and the
    /// C harness reports no type-ahead at all.
    /// </summary>
    public bool TypeAheadVisible { get; set; } = true;

    /// <summary>
    /// Called just before each key is handed over, so a harness can record what
    /// was on the screen at the moment the game stopped to ask. Some screens
    /// overwrite each thing they say with the next, and this is the only record
    /// that survives.
    /// </summary>
    public Action? BeforeReadKey { get; set; }

    /// <summary>
    /// Returns the next scripted key. Throws when the script runs dry, because
    /// a headless run that blocks for input would otherwise hang a test.
    /// </summary>
    public char ReadKey()
    {
        BeforeReadKey?.Invoke();

        return _input.Count > 0
            ? _input.Dequeue()
            : throw new InvalidOperationException(
                "MemoryScreen ran out of scripted input while the game asked for a key.");
    }

    /// <summary>
    /// Discards type-ahead. A script that is not type-ahead survives, which is
    /// what lets the oracle leave a command waiting for later in the run.
    /// </summary>
    public void FlushInput()
    {
        if (TypeAheadVisible)
        {
            _input.Clear();
        }
    }

    public void Bell() => BellCount++;
}
