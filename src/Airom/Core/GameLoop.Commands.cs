// Ported from the command helpers of Umoria 5.6 source/dungeon.c - go_up,
// go_down, jamdoor and refill_lamp - together with rest() and search_on() from
// source/moria1.c, scribe_object() from source/misc4.c, and the two blocks
// do_command() keeps to itself: locating the map, and toggling the search.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Data;

namespace Airom.Core;

public partial class GameLoop
{
    /// <summary>How full a lamp can be. Umoria's OBJ_LAMP_MAX.</summary>
    private const int LampMax = 15000;

    /// <summary>
    /// Climbs a staircase. Mirrors go_up().
    ///
    /// The two messages are the original's joke about how the dungeon is put
    /// together: the stairs are a maze, and the way back is a one-way door,
    /// which is why a level is never the same twice.
    /// </summary>
    private void GoUp()
    {
        CaveSquare here = _game.Cave[_game.CharacterRow, _game.CharacterColumn];

        if (here.ObjectIndex != 0
            && _game.Objects[here.ObjectIndex].TVal == ItemCategory.UpStair)
        {
            _game.DungeonLevel--;
            NewLevel = true;

            _display.MessagePrint("You enter a maze of up staircases.");
            _display.MessagePrint("You pass through a one-way door.");
            return;
        }

        _display.MessagePrint("I see no up staircase here.");
        FreeTurn = true;
    }

    /// <summary>Descends a staircase. Mirrors go_down().</summary>
    private void GoDown()
    {
        CaveSquare here = _game.Cave[_game.CharacterRow, _game.CharacterColumn];

        if (here.ObjectIndex != 0
            && _game.Objects[here.ObjectIndex].TVal == ItemCategory.DownStair)
        {
            _game.DungeonLevel++;
            NewLevel = true;

            _display.MessagePrint("You enter a maze of down staircases.");
            _display.MessagePrint("You pass through a one-way door.");
            return;
        }

        _display.MessagePrint("I see no down staircase here.");
        FreeTurn = true;
    }

    /// <summary>
    /// Spikes a door shut. Mirrors jamdoor().
    ///
    /// Each spike is worth less than the last - the series runs 20, 30, 37, 43,
    /// 48 - so a door can be made very hard to open but never impossible, and
    /// there is a point past which another spike is barely worth the weight.
    /// </summary>
    private void JamDoor()
    {
        FreeTurn = true;

        (bool taken, int direction) = ReadDirection();

        if (!taken)
        {
            return;
        }

        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;
        _game.Cave.Move(direction, ref row, ref column);

        CaveSquare square = _game.Cave[row, column];

        if (square.ObjectIndex == 0)
        {
            _display.MessagePrint("That isn't a door!");
            return;
        }

        InvenType door = _game.Objects[square.ObjectIndex];

        if (door.TVal == ItemCategory.OpenDoor)
        {
            _display.MessagePrint("The door must be closed first.");
            return;
        }

        if (door.TVal != ItemCategory.ClosedDoor)
        {
            _display.MessagePrint("That isn't a door!");
            return;
        }

        if (square.MonsterIndex != 0)
        {
            FreeTurn = false;

            _display.MessagePrint("The "
                + GameTables.CreatureList[_game.Monsters[square.MonsterIndex].CreatureIndex].Name
                + " is in your way!");

            return;
        }

        if (!_game.Inventory.FindRange(ItemCategory.Spike, ItemCategory.Never,
            out int first, out _))
        {
            _display.MessagePrint("But you have no spikes.");
            return;
        }

        FreeTurn = false;
        _display.CountMessagePrint("You jam the door with a spike.");

        // A locked door becomes a stuck one, which is the same number read the
        // other way round.
        if (door.P1 > 0)
        {
            door.P1 = (short)-door.P1;
        }

        door.P1 -= (short)(1 + (190 / (10 - door.P1)));

        InvenType spikes = _game.Inventory[first];

        if (spikes.Number > 1)
        {
            spikes.Number--;
            _game.Inventory.ReduceWeight(spikes.Weight);
        }
        else
        {
            _game.Inventory.Destroy(first);
        }
    }

    /// <summary>
    /// Pours a flask of oil into a lamp. Mirrors refill_lamp().
    ///
    /// A lamp holds fifteen thousand turns of light and no more; what will not
    /// fit is spilled, which is the game's way of saying not to carry oil
    /// around for nothing.
    /// </summary>
    private void RefillLamp()
    {
        FreeTurn = true;

        if (_game.Inventory[Inventory.LightSlot].SubVal != 0)
        {
            _display.MessagePrint("But you are not using a lamp.");
            return;
        }

        if (!_game.Inventory.FindRange(ItemCategory.Flask, ItemCategory.Never,
            out int first, out _))
        {
            _display.MessagePrint("You have no oil.");
            return;
        }

        FreeTurn = false;

        InvenType lamp = _game.Inventory[Inventory.LightSlot];
        lamp.P1 += _game.Inventory[first].P1;

        if (lamp.P1 > LampMax)
        {
            lamp.P1 = LampMax;
            _display.MessagePrint("Your lamp overflows, spilling oil on the ground.");
            _display.MessagePrint("Your lamp is full.");
        }
        else if (lamp.P1 > LampMax / 2)
        {
            _display.MessagePrint("Your lamp is more than half full.");
        }
        else if (lamp.P1 == LampMax / 2)
        {
            _display.MessagePrint("Your lamp is half full.");
        }
        else
        {
            _display.MessagePrint("Your lamp is less than half full.");
        }

        _inventoryScreen.DescribeRemaining(first);
        _game.Inventory.Destroy(first);
    }

    /// <summary>
    /// Rests for a while. Mirrors rest().
    ///
    /// A count typed before the command is taken as the answer; otherwise it is
    /// asked for, and a star means "until something happens". Resting is
    /// interrupted by anything at all, which is what makes it safe to ask for
    /// thousands of turns.
    /// </summary>
    private void Rest()
    {
        int turns;

        if (_display.CommandCount > 0)
        {
            turns = _display.CommandCount;
            _display.CommandCount = 0;
        }
        else
        {
            _display.Print("Rest for how long? ", 0, 0);
            turns = 0;

            if (_display.GetString(0, 19, 5, out string answer))
            {
                turns = answer.StartsWith('*')
                    ? -MaxShort
                    : ParseCount(answer);
            }
        }

        if (turns == -MaxShort || (turns > 0 && turns < MaxShort))
        {
            if ((Player.Status & PlayerStatus.Searching) != 0)
            {
                SearchOff();
            }

            Player.Rest = turns;
            Player.Status |= PlayerStatus.Resting;

            _display.PrintState(Player);

            // Resting is cheaper than moving about, which is the whole point of
            // doing it rather than walking in circles.
            Player.FoodDigested--;

            _display.Print("Press any key to stop resting...", 0, 0);
            _display.Refresh();
            return;
        }

        if (turns != 0)
        {
            _display.MessagePrint("Invalid rest count.");
        }

        _display.EraseLine(Display.MessageLine, 0);
        FreeTurn = true;
    }

    /// <summary>
    /// Reads a number the way the original's atoi() does: as much of the front
    /// of the answer as looks like one, and nought if none of it does.
    /// </summary>
    private static int ParseCount(string text)
    {
        int at = 0;

        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            at++;
        }

        return at == 0
            ? 0
            : int.Parse(text[..at], CultureInfo.InvariantCulture);
    }

    /// <summary>Starts searching. Mirrors search_on().</summary>
    public void SearchOn()
    {
        ChangeSpeed(1);
        Player.Status |= PlayerStatus.Searching;

        _display.PrintState(Player);
        _display.PrintSpeed(Player);

        Player.FoodDigested++;
    }

    /// <summary>
    /// Writes a note on something. Mirrors scribe_object().
    ///
    /// The inscription shares the line with the item's own name, so a long name
    /// leaves less room to write - never more than twelve characters, and less
    /// if the thing is called something elaborate.
    /// </summary>
    private void ScribeObject()
    {
        if (_game.Inventory.Count == 0 && _game.Inventory.EquipmentCount == 0)
        {
            _display.MessagePrint("You are not carrying anything to inscribe.");
            return;
        }

        if (_inventoryScreen.GetItem("Which one? ", 0, Inventory.Size - 1) is not int slot)
        {
            return;
        }

        InvenType item = _game.Inventory[slot];
        string described = _game.Names.Describe(item, withArticle: true);

        _display.MessagePrint("Inscribing " + described);

        string prompt = item.Inscription.Length > 0
            ? "Replace " + item.Inscription + " New inscription:"
            : "Inscription: ";

        int room = Math.Min(78 - described.Length, 12);

        _display.Print(prompt, 0, 0);

        if (_display.GetString(0, prompt.Length, room, out string note))
        {
            item.Inscription = note;
        }
    }

    /// <summary>
    /// Walks the map without walking the player. Mirrors the "W" block of
    /// do_command().
    ///
    /// The view is moved a half-screen at a time in whatever direction is
    /// asked for, and each sector is named by where it lies from the one the
    /// player is standing in, so a player can find their way about a level they
    /// have already explored. The view is put back at the end.
    /// </summary>
    private void LocateOnMap()
    {
        FreeTurn = true;

        if (Player.Blind > 0 || _lighting.NoLight())
        {
            _display.MessagePrint("You can't see your map.");
            return;
        }

        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;

        if (_display.Panel.Follow(row, column, force: true))
        {
            _display.PrintMap();
        }

        int fromRow = _display.Panel.Row;
        int fromColumn = _display.Panel.Column;

        while (true)
        {
            int atRow = _display.Panel.Row;
            int atColumn = _display.Panel.Column;

            string where = atRow == fromRow && atColumn == fromColumn
                ? string.Empty
                : (atRow < fromRow ? " North" : atRow > fromRow ? " South" : string.Empty)
                    + (atColumn < fromColumn ? " West"
                        : atColumn > fromColumn ? " East" : string.Empty)
                    + " of";

            string prompt = "Map sector ["
                + atRow.ToString(CultureInfo.InvariantCulture) + ","
                + atColumn.ToString(CultureInfo.InvariantCulture)
                + "], which is" + where + " your sector. Look which direction?";

            (bool taken, int direction) = ReadDirection(prompt);

            if (!taken)
            {
                break;
            }

            // Straight to the same place in the next sector, rather than
            // walking there - the original says as much, and apologises for
            // how the arithmetic reads.
            while (true)
            {
                column += (((direction - 1) % 3) - 1) * (_display.Panel.ViewColumns / 2);
                row -= (((direction - 1) / 3) - 1) * (_display.Panel.ViewRows / 2);

                // FAITHFUL QUIRK: the width is checked against the width and
                // the height against the width as well. On a level twice as
                // wide as it is tall that lets the view walk off the bottom,
                // where get_panel simply refuses to move and the loop spins
                // until the player presses escape.
                if (column < 0 || row < 0
                    || column >= _game.Cave.Width || row >= _game.Cave.Width)
                {
                    _display.MessagePrint("You've gone past the end of your map.");

                    column -= (((direction - 1) % 3) - 1) * (_display.Panel.ViewColumns / 2);
                    row += (((direction - 1) / 3) - 1) * (_display.Panel.ViewRows / 2);
                    break;
                }

                if (_display.Panel.Follow(row, column, force: true))
                {
                    _display.PrintMap();
                    break;
                }
            }
        }

        // Back to where the player actually is, but only if the view moved.
        if (_display.Panel.Follow(_game.CharacterRow, _game.CharacterColumn, force: false))
        {
            _display.PrintMap();
        }
    }

    /// <summary>
    /// Shows the score table. Mirrors the "V" block of do_command(), which
    /// shows the whole table the first time and only this player's part of it
    /// when asked twice running.
    /// </summary>
    private void ViewScores()
    {
        bool playerOnly = LastCommand == 'V';

        _display.SaveScreen();
        new ScoreFile(_game, _display).Display(playerOnly);
        _display.RestoreScreen();

        FreeTurn = true;
    }
}
