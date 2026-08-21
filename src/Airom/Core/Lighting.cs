// Ported from the lighting half of Umoria 5.6 source/moria1.c, with
// check_view() from source/misc4.c that drives it.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// What the player can see, and what the screen is told about it.
///
/// Umoria keeps three separate ideas about a square and this is where they are
/// set. Permanent light means the square stays drawn once seen - a wall, or a
/// lit room. Temporary light is the lamp the player carries, and travels with
/// them. A field mark is an object the player has noticed, which stays on the
/// map after the light moves on.
///
/// Only the squares that changed are redrawn. A step lights the three-by-three
/// block the player moves into, unlights the one they left, and repaints the
/// rectangle covering both - never the whole screen.
/// </summary>
public sealed class Lighting
{
    private readonly GameState _game;
    private readonly Display _display;

    public Lighting(GameState game, Display display)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);

        _game = game;
        _display = display;
    }

    /// <summary>
    /// Raised when the view has scrolled to a new sector. The command loop
    /// hangs a run-stopper on it; nothing else listens.
    /// </summary>
    public Action? PanelMoved { get; set; }

    private Player Player => _game.Player;

    /// <summary>
    /// Whether the square the player stands on is dark. Mirrors no_light().
    /// </summary>
    public bool NoLight()
    {
        CaveSquare square = _game.Cave[_game.CharacterRow, _game.CharacterColumn];
        return !square.TemporaryLight && !square.PermanentLight;
    }

    /// <summary>
    /// Moves whatever occupies one square to another. Mirrors move_rec().
    ///
    /// The value is copied out before the source is cleared, which is what makes
    /// moving a square onto itself harmless.
    /// </summary>
    public void MoveRecord(int fromRow, int fromColumn, int toRow, int toColumn)
    {
        int occupant = _game.Cave[fromRow, fromColumn].MonsterIndex;
        _game.Cave[fromRow, fromColumn].MonsterIndex = 0;
        _game.Cave[toRow, toColumn].MonsterIndex = occupant;
    }

    /// <summary>
    /// Lights the room the given square belongs to. Mirrors light_room().
    ///
    /// A room is lit in one go rather than square by square, which is what makes
    /// stepping through a door reveal the whole room at once. The block searched
    /// is a quarter of a screen, aligned to a grid: rooms are carved inside those
    /// blocks, so a room never straddles two.
    ///
    /// Objects lying in the room are noticed as it lights, but only the ones
    /// worth remembering - an invisible trap stays unnoticed.
    /// </summary>
    public void LightRoom(int row, int column)
    {
        int blockHeight = Panel.ViewRows / 2;
        int blockWidth = Panel.ViewColumns / 2;

        int startRow = row / blockHeight * blockHeight;
        int startColumn = column / blockWidth * blockWidth;
        int endRow = startRow + blockHeight - 1;
        int endColumn = startColumn + blockWidth - 1;

        for (int y = startRow; y <= endRow; y++)
        {
            for (int x = startColumn; x <= endColumn; x++)
            {
                CaveSquare square = _game.Cave[y, x];
                if (!square.LitRoom || square.PermanentLight)
                {
                    continue;
                }

                square.PermanentLight = true;

                if (square.Feature == CaveFeature.DarkFloor)
                {
                    square.Feature = CaveFeature.LightFloor;
                }

                if (!square.FieldMark && square.ObjectIndex != 0)
                {
                    int category = _game.Objects[square.ObjectIndex].TVal;
                    if (category is >= ItemCategory.MinVisible and <= ItemCategory.MaxVisible)
                    {
                        square.FieldMark = true;
                    }
                }

                _display.PrintAt(_display.SymbolAt(y, x), y, x);
            }
        }
    }

    /// <summary>
    /// Redraws one square, if it is on screen. Mirrors lite_spot().
    /// </summary>
    public void LightSpot(int row, int column)
    {
        if (_display.Panel.Contains(row, column))
        {
            _display.PrintAt(_display.SymbolAt(row, column), row, column);
        }
    }

    /// <summary>
    /// Moves the player's light from one square to another. Mirrors
    /// move_light().
    ///
    /// Which of the two halves runs depends on whether there is any light to
    /// move: a blind player, or one whose lamp has gone out, only moves their
    /// own symbol.
    /// </summary>
    public void MoveLight(int fromRow, int fromColumn, int toRow, int toColumn)
    {
        if (Player.Blind > 0 || !_game.PlayerLight)
        {
            MoveInTheDark(fromRow, fromColumn, toRow, toColumn);
        }
        else
        {
            MoveWithLamp(fromRow, fromColumn, toRow, toColumn);
        }
    }

    /// <summary>
    /// A step taken by lamplight. Mirrors sub1_move_light().
    ///
    /// The old three-by-three block is unlit and the new one lit, and everything
    /// from the topmost to the bottom-most line the player touched is redrawn.
    /// Walls become permanently known as soon as they are lit, because a wall
    /// does not change; floors do not, so an unlit floor goes back to darkness
    /// behind the player.
    ///
    /// While running, the lamp is switched off entirely unless the player has
    /// asked to be drawn, which is what keeps a long run from flickering.
    /// </summary>
    private void MoveWithLamp(int fromRow, int fromColumn, int toRow, int toColumn)
    {
        if (_game.TemporaryLightOn)
        {
            for (int y = fromRow - 1; y <= fromRow + 1; y++)
            {
                for (int x = fromColumn - 1; x <= fromColumn + 1; x++)
                {
                    _game.Cave[y, x].TemporaryLight = false;
                }
            }

            if (_game.Running && !_game.ShowSelfWhileRunning)
            {
                _game.TemporaryLightOn = false;
            }
        }
        else if (!_game.Running || _game.ShowSelfWhileRunning)
        {
            _game.TemporaryLightOn = true;
        }

        for (int y = toRow - 1; y <= toRow + 1; y++)
        {
            for (int x = toColumn - 1; x <= toColumn + 1; x++)
            {
                CaveSquare square = _game.Cave[y, x];

                // Only a normal step lights anything up.
                if (_game.TemporaryLightOn)
                {
                    square.TemporaryLight = true;
                }

                if (square.Feature >= CaveFeature.MinCaveWall)
                {
                    square.PermanentLight = true;
                }
                else if (!square.FieldMark && square.ObjectIndex != 0)
                {
                    int category = _game.Objects[square.ObjectIndex].TVal;
                    if (category is >= ItemCategory.MinVisible and <= ItemCategory.MaxVisible)
                    {
                        square.FieldMark = true;
                    }
                }
            }
        }

        // The rectangle covering both blocks, from the uppermost line the player
        // was on to the bottom-most.
        (int top, int bottom) = fromRow < toRow
            ? (fromRow - 1, toRow + 1)
            : (toRow - 1, fromRow + 1);

        (int left, int right) = fromColumn < toColumn
            ? (fromColumn - 1, toColumn + 1)
            : (toColumn - 1, fromColumn + 1);

        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                _display.PrintAt(_display.SymbolAt(y, x), y, x);
            }
        }
    }

    /// <summary>
    /// A step taken blind, or with no lamp. Mirrors sub3_move_light().
    ///
    /// Nothing new is revealed, so only the player's own symbol moves. The lamp
    /// is put out on the way, which is what makes walking out of light darken
    /// the squares behind properly.
    /// </summary>
    private void MoveInTheDark(int fromRow, int fromColumn, int toRow, int toColumn)
    {
        if (_game.TemporaryLightOn)
        {
            for (int y = fromRow - 1; y <= fromRow + 1; y++)
            {
                for (int x = fromColumn - 1; x <= fromColumn + 1; x++)
                {
                    _game.Cave[y, x].TemporaryLight = false;
                    _display.PrintAt(_display.SymbolAt(y, x), y, x);
                }
            }

            _game.TemporaryLightOn = false;
        }
        else if (!_game.Running || _game.ShowSelfWhileRunning)
        {
            _display.PrintAt(_display.SymbolAt(fromRow, fromColumn), fromRow, fromColumn);
        }

        if (!_game.Running || _game.ShowSelfWhileRunning)
        {
            _display.PrintAt('@', toRow, toColumn);
        }
    }

    /// <summary>
    /// Brings the view up to date around the player. Mirrors check_view().
    ///
    /// The map is only repainted when the view moved to another panel, which is
    /// what keeps a step inside the same screenful cheap. Standing on a lit floor
    /// lights the room; standing in its doorway lights it too, which is why a
    /// room is revealed before the player walks in.
    /// </summary>
    public void CheckView()
    {
        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;

        if (_display.Panel.Follow(row, column, force: false))
        {
            _display.PrintMap();

            // A player who asked for it is told the map has moved by having
            // their run stopped. Mirrors get_panel()'s end_find().
            if (_game.StopAtLevelBounds)
            {
                PanelMoved?.Invoke();
            }
        }

        MoveLight(row, column, row, column);

        CaveSquare here = _game.Cave[row, column];

        if (here.Feature == CaveFeature.LightFloor)
        {
            if (Player.Blind < 1 && !here.PermanentLight)
            {
                LightRoom(row, column);
            }
        }
        else if (here.LitRoom && Player.Blind < 1)
        {
            // In the doorway of a lit room: light whichever of the neighbouring
            // rooms are lit.
            for (int y = row - 1; y <= row + 1; y++)
            {
                for (int x = column - 1; x <= column + 1; x++)
                {
                    CaveSquare square = _game.Cave[y, x];
                    if (square.Feature == CaveFeature.LightFloor && !square.PermanentLight)
                    {
                        LightRoom(y, x);
                    }
                }
            }
        }
    }
}
