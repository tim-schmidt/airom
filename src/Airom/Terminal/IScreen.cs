// The terminal surface AIrom draws on. Replaces the curses layer that Umoria
// 5.6 used in source/io.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Terminal;

/// <summary>
/// A character grid with deferred output, matching how Umoria's io.c drives
/// curses: drawing calls modify an off-screen buffer and nothing reaches the
/// terminal until <see cref="Refresh"/>.
///
/// The game is strictly monochrome. Umoria 5.6's portable build makes no
/// attribute calls at all - the only standout handling in io.c is inside the
/// Mac branch, which encoded it in a character's sign bit - so there is
/// deliberately no colour or emphasis in this interface.
///
/// Positions are row-then-column and zero-based, matching the C.
/// </summary>
public interface IScreen
{
    /// <summary>Rows available. Umoria requires at least 24.</summary>
    int Rows { get; }

    /// <summary>Columns available. Umoria requires at least 80.</summary>
    int Columns { get; }

    /// <summary>Writes one character. Out-of-range positions are ignored.</summary>
    void Put(int row, int column, char value);

    /// <summary>Writes a string, clipped at the right edge.</summary>
    void Put(int row, int column, ReadOnlySpan<char> text);

    /// <summary>Blanks from the given column to the end of the row. Mirrors erase_line().</summary>
    void EraseLine(int row, int column);

    /// <summary>Blanks the given row and everything below it. Mirrors clear_from().</summary>
    void ClearFrom(int row);

    /// <summary>Blanks the whole grid. Mirrors clear_screen().</summary>
    void Clear();

    /// <summary>
    /// Moves a rectangle of the grid to another position, blanking everything
    /// else, and takes the cursor along if it was inside. What a full-screen
    /// layout needs when the terminal changes size under it: the screen
    /// already holds exactly what should be shown, only somewhere else.
    /// </summary>
    void MoveBlock(int fromRow, int fromColumn, int rows, int columns, int toRow, int toColumn);

    /// <summary>Parks the cursor. Mirrors move_cursor().</summary>
    void MoveCursor(int row, int column);

    /// <summary>Sends pending changes to the terminal. Mirrors put_qio() and curses refresh().</summary>
    void Refresh();

    /// <summary>
    /// Snapshots the grid so an overlay can be drawn over it. Mirrors
    /// save_screen(), which copied stdscr into a spare full-screen window.
    /// Umoria never nests these, so a single slot is enough.
    /// </summary>
    void SaveScreen();

    /// <summary>Puts back what <see cref="SaveScreen"/> captured. Mirrors restore_screen().</summary>
    void RestoreScreen();

    /// <summary>
    /// Blocks for one keypress and returns it, without echoing. Control keys
    /// arrive as their control characters, since Umoria binds several of them
    /// as commands.
    /// </summary>
    char ReadKey();

    /// <summary>
    /// Which keyset the keypad and the arrow keys spell their directions in.
    /// The MSDOS build's bios_getch() translated keypad scan codes through one
    /// of two tables chosen by rogue_like_commands; the game keeps this flag
    /// in line with that option so <see cref="ReadKey"/> can do the same.
    /// </summary>
    bool RogueLikeKeypad { get; set; }

    /// <summary>Whether <see cref="ReadKey"/> would return without blocking.</summary>
    bool KeyAvailable { get; }

    /// <summary>
    /// Called from inside <see cref="ReadKey"/> if the terminal changes size
    /// while a key is awaited. By then <see cref="Rows"/> and
    /// <see cref="Columns"/> answer for the new size and what the grid held
    /// has been painted again within it; the call is the chance to draw for
    /// the new size instead. The game sets it only where it knows how to draw
    /// everything on the screen, and a screen that cannot change size never
    /// calls it.
    /// </summary>
    Action? Resized { get; set; }

    /// <summary>Discards type-ahead. Mirrors flush().</summary>
    void FlushInput();

    /// <summary>Rings the terminal bell. Mirrors bell().</summary>
    void Bell();
}
