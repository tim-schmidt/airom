// Ported from los() in Umoria 5.6 source/misc1.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Whether one square can see another. Mirrors los().
///
/// The line between two squares is walked a tile at a time, and any closed space
/// along the way blocks it. The awkward part is that the line rarely runs
/// through tile centres, so the offset is tracked as a fraction - and to keep
/// the whole thing in integers, everything is multiplied by twice the product of
/// the two deltas. That scale factor is why the arithmetic below looks stranger
/// than the geometry it describes.
/// </summary>
public static class LineOfSight
{
    /// <summary>
    /// Whether the second square is visible from the first.
    /// </summary>
    public static bool Between(Cave cave, int fromRow, int fromColumn, int toRow, int toColumn)
    {
        ArgumentNullException.ThrowIfNull(cave);

        int deltaColumn = toColumn - fromColumn;
        int deltaRow = toRow - fromRow;

        // Touching squares always see each other.
        if (deltaColumn is < 2 and > -2 && deltaRow is < 2 and > -2)
        {
            return true;
        }

        if (deltaColumn == 0)
        {
            if (deltaRow < 0)
            {
                (fromRow, toRow) = (toRow, fromRow);
            }

            for (int row = fromRow + 1; row < toRow; row++)
            {
                if (cave[row, fromColumn].Feature >= CaveFeature.MinClosedSpace)
                {
                    return false;
                }
            }

            return true;
        }

        if (deltaRow == 0)
        {
            if (deltaColumn < 0)
            {
                (fromColumn, toColumn) = (toColumn, fromColumn);
            }

            for (int column = fromColumn + 1; column < toColumn; column++)
            {
                if (cave[fromRow, column].Feature >= CaveFeature.MinClosedSpace)
                {
                    return false;
                }
            }

            return true;
        }

        // Neither straight nor adjacent: walk the line along its longer axis.
        int halfScale = Math.Abs(deltaColumn * deltaRow);
        int scale = halfScale << 1;
        int columnSign = deltaColumn < 0 ? -1 : 1;
        int rowSign = deltaRow < 0 ? -1 : 1;

        if (Math.Abs(deltaColumn) >= Math.Abs(deltaRow))
        {
            // Start at the border between the first tile and the second, where
            // the offset is half the slope - scaled, so twice the square of the
            // row delta.
            int offset = deltaRow * deltaRow;
            int step = offset << 1;
            int column = fromColumn + columnSign;
            int row;

            if (offset == halfScale)
            {
                // The special case of a slope of exactly one.
                row = fromRow + rowSign;
                offset -= scale;
            }
            else
            {
                row = fromRow;
            }

            while (toColumn != column)
            {
                if (cave[row, column].Feature >= CaveFeature.MinClosedSpace)
                {
                    return false;
                }

                offset += step;

                if (offset < halfScale)
                {
                    column += columnSign;
                }
                else if (offset > halfScale)
                {
                    row += rowSign;

                    if (cave[row, column].Feature >= CaveFeature.MinClosedSpace)
                    {
                        return false;
                    }

                    column += columnSign;
                    offset -= scale;
                }
                else
                {
                    // The line passes exactly through the corner of a tile.
                    column += columnSign;
                    row += rowSign;
                    offset -= scale;
                }
            }

            return true;
        }
        else
        {
            int offset = deltaColumn * deltaColumn;
            int step = offset << 1;
            int row = fromRow + rowSign;
            int column;

            if (offset == halfScale)
            {
                column = fromColumn + columnSign;
                offset -= scale;
            }
            else
            {
                column = fromColumn;
            }

            while (toRow != row)
            {
                if (cave[row, column].Feature >= CaveFeature.MinClosedSpace)
                {
                    return false;
                }

                offset += step;

                if (offset < halfScale)
                {
                    row += rowSign;
                }
                else if (offset > halfScale)
                {
                    column += columnSign;

                    if (cave[row, column].Feature >= CaveFeature.MinClosedSpace)
                    {
                        return false;
                    }

                    row += rowSign;
                    offset -= scale;
                }
                else
                {
                    column += columnSign;
                    row += rowSign;
                    offset -= scale;
                }
            }

            return true;
        }
    }
}
