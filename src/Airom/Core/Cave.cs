// Ported from cave_type in Umoria 5.6 source/types.h and the cave helpers in
// source/misc1.c and source/misc3.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// One dungeon square. Mirrors Umoria's cave_type.
///
/// The C packs this into four bytes - two 8-bit list indices, an 8-bit terrain
/// value and four bitfields - because in 1989 a 66x198 grid of them mattered.
/// The indices are widened to int here: at 13,000 squares the saving is
/// meaningless, and the byte width is what caps a level at 255 monsters and
/// 255 objects.
/// </summary>
public sealed class CaveSquare
{
    /// <summary>Index into the monster list, or 0 for none. 1 means the player.</summary>
    public int MonsterIndex { get; set; }

    /// <summary>Index into the object list, or 0 for none.</summary>
    public int ObjectIndex { get; set; }

    /// <summary>Terrain value; one of the <see cref="CaveFeature"/> constants.</summary>
    public byte Feature { get; set; }

    /// <summary>
    /// Part of a room that should be permanently lit once entered. Walls with
    /// this set stay lit after being tunnelled out.
    /// </summary>
    public bool LitRoom { get; set; }

    /// <summary>
    /// Field mark: the player has seen what is here. Traps, doors and stairs
    /// stay hidden until this is set.
    /// </summary>
    public bool FieldMark { get; set; }

    /// <summary>Permanently lit - walls and lit rooms.</summary>
    public bool PermanentLight { get; set; }

    /// <summary>Temporarily lit, by the player's lamp or a spell.</summary>
    public bool TemporaryLight { get; set; }

    /// <summary>Resets to the blank state blank_cave() produces.</summary>
    public void Clear()
    {
        MonsterIndex = 0;
        ObjectIndex = 0;
        Feature = CaveFeature.NullWall;
        LitRoom = false;
        FieldMark = false;
        PermanentLight = false;
        TemporaryLight = false;
    }
}

/// <summary>
/// The dungeon grid for the level being played.
///
/// Umoria declares this as a fixed cave[MAX_HEIGHT][MAX_WIDTH] and tracks the
/// part in use with cur_height and cur_width, which differ by level - the town
/// is 22x66 while a dungeon level is 66x198. Here the grid is simply allocated
/// at the size in use, which is the same thing without a compile-time ceiling.
/// </summary>
public sealed class Cave
{
    private CaveSquare[,] _squares = new CaveSquare[0, 0];

    public Cave(int height, int width) => Resize(height, width);

    /// <summary>Rows in use. Umoria's cur_height.</summary>
    public int Height { get; private set; }

    /// <summary>Columns in use. Umoria's cur_width.</summary>
    public int Width { get; private set; }

    public CaveSquare this[int row, int column] => _squares[row, column];

    /// <summary>
    /// Reallocates the grid for a level of a different size and blanks it.
    /// Mirrors setting cur_height/cur_width and calling blank_cave().
    /// </summary>
    public void Resize(int height, int width)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);

        if (Height != height || Width != width)
        {
            Height = height;
            Width = width;
            _squares = new CaveSquare[height, width];
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    _squares[row, column] = new CaveSquare();
                }
            }

            return;
        }

        Blank();
    }

    /// <summary>Clears every square. Mirrors blank_cave().</summary>
    public void Blank()
    {
        for (int row = 0; row < Height; row++)
        {
            for (int column = 0; column < Width; column++)
            {
                _squares[row, column].Clear();
            }
        }
    }

    /// <summary>
    /// Whether a position is inside the dungeon proper - that is, not on the
    /// boundary wall. Mirrors in_bounds(), which excludes the outer ring rather
    /// than merely checking the array range.
    /// </summary>
    public bool InBounds(int row, int column) =>
        row > 0 && row < Height - 1 && column > 0 && column < Width - 1;

    /// <summary>
    /// Steps one square in a numeric-keypad direction, 1 to 9. Mirrors mmove().
    /// </summary>
    /// <returns>
    /// False if the step would leave the grid, in which case the position is
    /// left untouched. Note this is the full array range, not
    /// <see cref="InBounds"/> - the streamer generator relies on walking onto
    /// the boundary ring to stop.
    /// </returns>
    public bool Move(int direction, ref int row, ref int column)
    {
        int newRow = direction switch
        {
            1 or 2 or 3 => row + 1,
            4 or 5 or 6 => row,
            7 or 8 or 9 => row - 1,
            _ => row,
        };

        int newColumn = direction switch
        {
            1 or 4 or 7 => column - 1,
            2 or 5 or 8 => column,
            3 or 6 or 9 => column + 1,
            _ => column,
        };

        if (newRow < 0 || newRow >= Height || newColumn < 0 || newColumn >= Width)
        {
            return false;
        }

        row = newRow;
        column = newColumn;
        return true;
    }
}
