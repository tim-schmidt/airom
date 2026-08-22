// Ported from look(), look_ray() and look_see() in Umoria 5.6 source/moria4.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Looking around, with peripheral vision. Mirrors the look half of moria4.c.
///
/// Each square is treated as a diamond just touching its four neighbours, and a
/// square is visible if any part of its diamond can be reached by a line from
/// the player that crosses no opaque diamond. So a square partly behind a pillar
/// is still seen, and one squarely behind it is not.
///
/// The work is done by <see cref="LookRay"/>, which sets up a frame of its own
/// with the direction as its x axis and looks at everything between two angles.
/// It recurses: each call sweeps one line parallel to the centre line, a fixed
/// distance out, and calls itself for the next line along wherever it finds a
/// gap. Looking all eight ways sees everything that ought to be visible.
/// </summary>
public class Looking
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Looking(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    private Player Player => _game.Player;

    /// <summary>
    /// Any sufficiently large number: gradients are held as y over x times
    /// twice this, so that they can be compared as integers. Umoria's GRADF.
    /// </summary>
    private const int Gradient = 10000;

    // The frame the ray works in. A dungeon position is
    //   row    = character row    + _rowFromX * x + _rowFromY * y
    //   column = character column + _colFromX * x + _colFromY * y
    // so the same sweep serves all eight directions by changing these four.
    private int _colFromX;
    private int _colFromY;
    private int _rowFromX;
    private int _rowFromY;

    /// <summary>How many things have been shown, which decides the closing message.</summary>
    private int _seen;

    /// <summary>
    /// Set while a square is only being tested for opacity rather than looked
    /// at, so that a direct line of sight is not described twice.
    /// </summary>
    private bool _quiet;

    /// <summary>
    /// Which pass this is: the first looks at creatures and objects, the second
    /// at the rock itself. The rock is only worth a second pass when the player
    /// has asked for mineral veins to be picked out.
    /// </summary>
    private int _pass;

    // Indexed by direction / 2, and so only meaningful for the four straight
    // directions. The diagonals borrow two of them.
    private static readonly int[] ColumnFromY = [0, 1, 0, 0, -1];
    private static readonly int[] ColumnFromX = [0, 0, -1, 1, 0];
    private static readonly int[] RowFromY = [0, 0, 1, -1, 0];
    private static readonly int[] RowFromX = [0, 1, 0, 0, -1];

    /// <summary>Maps a diagonal to the two straight directions either side of it.</summary>
    private static readonly int[] FirstDiagonal = [1, 3, 0, 2, 4];
    private static readonly int[] SecondDiagonal = [2, 1, 0, 4, 3];

    /// <summary>
    /// Recalls what is known about a kind of creature. Mirrors the
    /// roff_recall() call. Left overridable so a harness can answer without a
    /// terminal.
    /// </summary>
    /// <returns>The key that ended the recall, so escape still aborts the look.</returns>
    protected virtual char RecallCreature(int creatureIndex) =>
        _loop.MonsterRecall.Describe(creatureIndex);

    /// <summary>
    /// Looks in a direction, or in all of them. Mirrors look().
    ///
    /// Everything in the cone is shown one at a time, with the cursor parked on
    /// it and a key waited for. Escape gives up on the whole thing.
    /// </summary>
    public void Look()
    {
        if (Player.Blind > 0)
        {
            _display.MessagePrint("You can't see a damn thing!");
            return;
        }

        if (Player.Hallucinating > 0)
        {
            _display.MessagePrint(
                "You can't believe what you are seeing! It's like a dream!");
            return;
        }

        if (!ReadAnyDirection("Look which direction?", out int direction))
        {
            return;
        }

        _seen = 0;
        _pass = 0;
        _quiet = false;

        // The square the player is standing on, which is described first.
        if (LookSee(0, 0, out _))
        {
            _display.MessagePrint("--Aborting look--");
            return;
        }

        bool aborted;

        do
        {
            aborted = SweepAll(direction);
        }
        while (!aborted && _game.HighlightSeams && ++_pass < 2);

        if (aborted)
        {
            _display.MessagePrint("--Aborting look--");
            return;
        }

        if (_seen > 0)
        {
            _display.MessagePrint(direction == 5
                ? "That's all you see."
                : "That's all you see in that direction.");
        }
        else
        {
            _display.MessagePrint(direction == 5
                ? "You see nothing of interest."
                : "You see nothing of interest in that direction.");
        }
    }

    /// <summary>
    /// One pass over whichever cone the direction asks for.
    /// </summary>
    /// <returns>Whether the player gave up part-way.</returns>
    private bool SweepAll(int direction)
    {
        if (direction == 5)
        {
            // All four straight cones, each swept twice - once either side of
            // its centre line - which between them covers everything.
            for (int i = 1; i <= 4; i++)
            {
                SetFrame(i, flipped: false);

                if (LookRay(0, (2 * Gradient) - 1, 1))
                {
                    return true;
                }

                _colFromY = -_colFromY;
                _rowFromY = -_rowFromY;

                if (LookRay(0, 2 * Gradient, 2))
                {
                    return true;
                }
            }

            return false;
        }

        if ((direction & 1) == 0)
        {
            // A straight direction: one sweep either side of the centre line.
            SetFrame(direction >> 1, flipped: false);

            if (LookRay(0, Gradient, 1))
            {
                return true;
            }

            _colFromY = -_colFromY;
            _rowFromY = -_rowFromY;
            return LookRay(0, Gradient, 2);
        }

        // A diagonal, which borrows the two straight directions either side of
        // it and takes a wider cone from each.
        SetFrame(FirstDiagonal[direction >> 1], flipped: true);

        if (LookRay(1, 2 * Gradient, Gradient))
        {
            return true;
        }

        SetFrame(SecondDiagonal[direction >> 1], flipped: false);
        return LookRay(1, (2 * Gradient) - 1, Gradient);
    }

    /// <summary>Points the ray frame down one of the four straight directions.</summary>
    private void SetFrame(int half, bool flipped)
    {
        _colFromX = ColumnFromX[half];
        _rowFromX = RowFromX[half];
        _colFromY = flipped ? -ColumnFromY[half] : ColumnFromY[half];
        _rowFromY = flipped ? -RowFromY[half] : RowFromY[half];
    }

    /// <summary>
    /// Looks at everything in the cone between two rays, on or beyond the line
    /// <paramref name="y"/> squares out from the centre line. Mirrors
    /// look_ray().
    ///
    /// The rays are gradients, y over x, times twice <see cref="Gradient"/>, and
    /// this is only ever called with gradients between forty-five degrees and
    /// almost horizontal. It walks the line looking for windows - runs of
    /// transparent squares - and recurses into each one to sweep the next line
    /// out.
    /// </summary>
    /// <param name="from">The wider angle of the cone; the sweep runs inwards.</param>
    /// <param name="to">The narrower angle.</param>
    /// <returns>Whether the player gave up.</returns>
    private bool LookRay(int y, int from, int to)
    {
        // If from is the smaller, the cone has closed and there is nothing left.
        if (from <= to || y > GameLoop.MaxSight)
        {
            return false;
        }

        // The first square on this line that any part of the cone reaches:
        // the least x with (2x-1)/x < from/Gradient. Called with y of zero it
        // works out as zero, which has to be nudged up to one.
        int x = (int)(((long)Gradient * ((2 * y) - 1) / from) + 1);

        if (x <= 0)
        {
            x = 1;
        }

        // And the last: the greatest x with (2x+1)/x > to/Gradient.
        int maxX = (int)((((long)Gradient * ((2 * y) + 1)) - 1) / to);

        if (maxX > GameLoop.MaxSight)
        {
            maxX = GameLoop.MaxSight;
        }

        if (maxX < x)
        {
            return false;
        }

        // Squares straight down the line of sight are looked at by the caller,
        // so here they are only tested for whether they block the view.
        _quiet = (y == 0 && to > 1) || (y == x && from < Gradient * 2);

        if (LookSee(x, y, out bool transparent))
        {
            return true;
        }

        if (y == x)
        {
            _quiet = false;
        }

        // The original jumps straight into the second loop when the first
        // square is already transparent; the flag stands in for that jump.
        bool alreadyInWindow = transparent;

        while (true)
        {
            if (!alreadyInWindow)
            {
                // Sweep the next line out, through the window just found.
                if (LookRay(y + 1, from, (int)(((2 * y) + 1) * (long)Gradient / x)))
                {
                    return true;
                }

                // Then walk on to the start of the next window.
                do
                {
                    if (x == maxX)
                    {
                        return false;
                    }

                    // Whether what was just passed closes the cone entirely.
                    // With y of zero it always does.
                    from = (int)(((2 * y) - 1) * (long)Gradient / x);

                    if (from <= to)
                    {
                        return false;
                    }

                    x++;

                    if (LookSee(x, y, out transparent))
                    {
                        return true;
                    }
                }
                while (!transparent);
            }

            alreadyInWindow = false;

            // And on to the far side of this window.
            do
            {
                if (x == maxX)
                {
                    // The window is trimmed by a limit found earlier.
                    return LookRay(y + 1, from, to);
                }

                x++;

                if (LookSee(x, y, out transparent))
                {
                    return true;
                }
            }
            while (transparent);
        }
    }

    /// <summary>
    /// Looks at one square of the ray frame, and says whether it can be seen
    /// through. Mirrors look_see().
    /// </summary>
    /// <returns>Whether the player pressed escape.</returns>
    private bool LookSee(int x, int y, out bool transparent)
    {
        string leadIn = x == 0 && y == 0 ? "You are on" : "You see";

        int column = _game.CharacterColumn + (_colFromX * x) + (_colFromY * y);
        int row = _game.CharacterRow + (_rowFromX * x) + (_rowFromY * y);

        if (!_display.Panel.Contains(row, column))
        {
            transparent = false;
            return false;
        }

        CaveSquare square = _game.Cave[row, column];
        transparent = square.Feature <= CaveFeature.MaxOpenSpace;

        if (_quiet)
        {
            return false;
        }

        bool described = false;
        char answer = ' ';

        if (_pass == 0 && square.MonsterIndex > 1
            && _game.Monsters[square.MonsterIndex].Visible)
        {
            int kind = _game.Monsters[square.MonsterIndex].CreatureIndex;
            string name = GameTables.CreatureList[kind].Name;

            _display.Print(
                leadIn + " " + (IsVowel(name[0]) ? "an" : "a") + " " + name
                + ". [(r)ecall]", 0, 0);

            leadIn = "It is on";
            described = true;

            _display.MoveCursorRelative(row, column);
            answer = _display.ReadKey();

            if (answer is 'r' or 'R')
            {
                _display.SaveScreen();
                answer = RecallCreature(kind);
                _display.RestoreScreen();
            }
        }

        if (square.TemporaryLight || square.PermanentLight || square.FieldMark)
        {
            bool asRock = false;

            if (square.ObjectIndex != 0)
            {
                InvenType item = _game.Objects[square.ObjectIndex];

                if (item.TVal == ItemCategory.SecretDoor)
                {
                    // A secret door looks like the granite it is hiding in.
                    asRock = true;
                }
                else if (_pass == 0 && item.TVal != ItemCategory.InvisibleTrap)
                {
                    _display.Print(
                        leadIn + " " + _game.Names.Describe(item, withArticle: true)
                        + " ---pause---", 0, 0);

                    leadIn = "It is in";
                    described = true;

                    _display.MoveCursorRelative(row, column);
                    answer = _display.ReadKey();
                }
            }

            if ((_pass > 0 || described) && square.Feature >= CaveFeature.MinClosedSpace)
            {
                string? rock = DescribeRock(square.Feature, asRock, described);

                if (rock is not null)
                {
                    _display.Print(leadIn + " " + rock + " ---pause---", 0, 0);
                    described = true;

                    _display.MoveCursorRelative(row, column);
                    answer = _display.ReadKey();
                }
            }
        }

        if (described)
        {
            _seen++;

            if (answer == Keys.Escape)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What the rock itself is worth saying, if anything. Plain granite is only
    /// interesting when it has something in it - a secret door, or an object
    /// already described - so on its own it is passed over in silence.
    /// </summary>
    private static string? DescribeRock(byte feature, bool asRock, bool described)
    {
        if (asRock || feature is CaveFeature.BoundaryWall or CaveFeature.GraniteWall)
        {
            return described ? "a granite wall" : null;
        }

        return feature switch
        {
            CaveFeature.MagmaWall => "some dark rock",
            CaveFeature.QuartzWall => "a quartz vein",
            _ => null,
        };
    }

    /// <summary>
    /// Asks for a direction, allowing the five that means every direction at
    /// once. Mirrors get_alldir().
    /// </summary>
    private bool ReadAnyDirection(string prompt, out int direction)
    {
        while (true)
        {
            if (!_display.GetCommand(prompt, out char command))
            {
                _loop.FreeTurn = true;
                direction = 0;
                return false;
            }

            if (_game.RogueLikeCommands)
            {
                command = Commands.MapRogueDirection(command);
            }

            if (command is >= '1' and <= '9')
            {
                direction = command - '0';
                return true;
            }

            _display.Bell();
        }
    }

    private static bool IsVowel(char letter) =>
        letter is 'a' or 'e' or 'i' or 'o' or 'u'
            or 'A' or 'E' or 'I' or 'O' or 'U';
}
