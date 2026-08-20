// Ported from openobject, closeobject and twall in Umoria 5.6 source/moria3.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Opening and closing what is in the way.
///
/// A door and a chest are opened by the same command and the same skill: it is
/// the lock that is being picked either way. What differs is what is behind it -
/// a door has a corridor, and a chest may have a needle.
/// </summary>
public sealed class Doors
{
    /// <summary>The open door an opened door becomes. Umoria's OBJ_OPEN_DOOR.</summary>
    private const int OpenDoorObject = 367;

    /// <summary>The closed door a closed door becomes. Umoria's OBJ_CLOSED_DOOR.</summary>
    private const int ClosedDoorObject = 368;

    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Doors(GameState game, Display display, GameLoop loop)
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
    /// How good the player is at picking a lock. Mirrors the expression
    /// openobject() works out twice.
    /// </summary>
    private int LockPickingSkill() =>
        Player.Disarm
        + (2 * Stats.DisarmBonus(Player))
        + Stats.Adjustment(Player, Stat.Intelligence)
        + (GameTables.ClassLevelAdjust[Player.Class][LevelSkill.Disarming] * Player.Level / 3);

    /// <summary>
    /// Opens a door or a chest. Mirrors openobject().
    ///
    /// A locked door is picked against its lock, and picking one is worth a
    /// point of experience - the only experience in the game that comes from
    /// something other than a monster or a spell.
    /// </summary>
    public void OpenObject()
    {
        (bool taken, int direction) = _loop.ReadDirection();
        if (!taken)
        {
            return;
        }

        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;
        _game.Cave.Move(direction, ref row, ref column);

        CaveSquare square = _game.Cave[row, column];

        if (square.MonsterIndex > 1 && square.ObjectIndex != 0
            && (_game.Objects[square.ObjectIndex].TVal == ItemCategory.ClosedDoor
                || _game.Objects[square.ObjectIndex].TVal == ItemCategory.Chest))
        {
            _display.MessagePrint(InTheWay(square.MonsterIndex));
            return;
        }

        if (square.ObjectIndex == 0)
        {
            NothingToOpen();
            return;
        }

        InvenType item = _game.Objects[square.ObjectIndex];

        if (item.TVal == ItemCategory.ClosedDoor)
        {
            OpenDoor(item, square, row, column);
        }
        else if (item.TVal == ItemCategory.Chest)
        {
            OpenChest(item, square, row, column);
        }
        else
        {
            NothingToOpen();
        }
    }

    private void OpenDoor(InvenType door, CaveSquare square, int row, int column)
    {
        if (door.P1 > 0)
        {
            // Locked.
            if (Player.Confused > 0)
            {
                _display.MessagePrint("You are too confused to pick the lock.");
            }
            else if (LockPickingSkill() - door.P1 > _game.Rng.RandInt(100))
            {
                _display.MessagePrint("You have picked the lock.");
                Player.Experience++;
                _loop.Levelling.PrintExperience();
                door.P1 = 0;
            }
            else
            {
                _display.CountMessagePrint("You failed to pick the lock.");
            }
        }
        else if (door.P1 < 0)
        {
            // Stuck, which no amount of picking will help.
            _display.MessagePrint("It appears to be stuck.");
        }

        if (door.P1 == 0)
        {
            door.CopyFrom(OpenDoorObject);
            square.Feature = CaveFeature.CorridorFloor;
            _loop.Lighting.LightSpot(row, column);
            _display.CommandCount = 0;
        }
    }

    private void OpenChest(InvenType chest, CaveSquare square, int row, int column)
    {
        bool opened = true;

        if ((chest.Flags & ChestFlags.Locked) != 0)
        {
            opened = false;

            if (Player.Confused > 0)
            {
                _display.MessagePrint("You are too confused to pick the lock.");
            }
            else if (LockPickingSkill() - chest.Level > _game.Rng.RandInt(100))
            {
                _display.MessagePrint("You have picked the lock.");
                opened = true;
                Player.Experience += chest.Level;
                _loop.Levelling.PrintExperience();
            }
            else
            {
                _display.CountMessagePrint("You failed to pick the lock.");
            }
        }

        if (opened)
        {
            chest.Flags &= ~ChestFlags.Locked;
            chest.SpecialName = SpecialName.Empty;
            _game.Knowledge.LearnEnchantment(chest);
            chest.Cost = 0;
        }

        // Still trapped? The lock and the trap are separate things, so a picked
        // lock does not make a chest safe.
        bool holdsTreasure = false;

        if ((chest.Flags & ChestFlags.Locked) == 0)
        {
            _loop.Traps.ChestTrap(row, column);

            if (square.ObjectIndex != 0)
            {
                holdsTreasure = true;
            }
        }

        if (!holdsTreasure)
        {
            return;
        }

        // The contents are handed out as though a monster had died holding them.
        InvenType remaining = _game.Objects[square.ObjectIndex];

        // The curse is cleared first, so nobody wins the game by opening a
        // cursed chest.
        remaining.Flags &= ~ItemFlags.Cursed;
        _loop.Combat.MonsterDeath(row, column, remaining.Flags);
        remaining.Flags = 0;
    }

    /// <summary>
    /// Closes an open door. Mirrors closeobject().
    ///
    /// A broken door cannot be closed, and neither can one with something
    /// standing in it.
    /// </summary>
    public void CloseObject()
    {
        (bool taken, int direction) = _loop.ReadDirection();
        if (!taken)
        {
            return;
        }

        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;
        _game.Cave.Move(direction, ref row, ref column);

        CaveSquare square = _game.Cave[row, column];

        if (square.ObjectIndex == 0
            || _game.Objects[square.ObjectIndex].TVal != ItemCategory.OpenDoor)
        {
            _display.MessagePrint("I do not see anything you can close there.");
            _loop.FreeTurn = true;
            return;
        }

        if (square.MonsterIndex != 0)
        {
            _display.MessagePrint(InTheWay(square.MonsterIndex));
            return;
        }

        InvenType door = _game.Objects[square.ObjectIndex];

        if (door.P1 != 0)
        {
            _display.MessagePrint("The door appears to be broken.");
            return;
        }

        door.CopyFrom(ClosedDoorObject);
        square.Feature = CaveFeature.BlockedFloor;
        _loop.Lighting.LightSpot(row, column);
    }

    /// <summary>
    /// Turns a wall into floor. Mirrors twall().
    ///
    /// What it becomes depends on where it was: a wall that belonged to a room
    /// takes on the floor and the lighting of its neighbours, and one that did
    /// not becomes plain corridor. Digging into a room from outside therefore
    /// leaves a lit square rather than a dark hole in the middle of the floor.
    /// </summary>
    /// <returns>False when the digging failed, which the caller reports.</returns>
    public bool TunnelWall(int row, int column, int digging, int hardness)
    {
        if (digging <= hardness)
        {
            return false;
        }

        CaveSquare square = _game.Cave[row, column];

        if (square.LitRoom)
        {
            bool found = false;

            for (int y = row - 1; y <= row + 1 && !found; y++)
            {
                for (int x = column - 1; x <= column + 1; x++)
                {
                    if (_game.Cave[y, x].Feature <= CaveFeature.MaxCaveRoom)
                    {
                        square.Feature = _game.Cave[y, x].Feature;
                        square.PermanentLight = _game.Cave[y, x].PermanentLight;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                square.Feature = CaveFeature.CorridorFloor;
                square.PermanentLight = false;
            }
        }
        else
        {
            square.Feature = CaveFeature.CorridorFloor;
            square.PermanentLight = false;
        }

        square.FieldMark = false;

        if (_display.Panel.Contains(row, column)
            && (square.TemporaryLight || square.PermanentLight)
            && square.ObjectIndex != 0)
        {
            _display.MessagePrint("You have found something!");
        }

        _loop.Lighting.LightSpot(row, column);
        return true;
    }

    private void NothingToOpen()
    {
        _display.MessagePrint("I do not see anything you can open there.");
        _loop.FreeTurn = true;
    }

    /// <summary>
    /// Whatever is standing in the way, named only if the player can see it.
    /// </summary>
    private string InTheWay(int monsterIndex)
    {
        Monster monster = _game.Monsters[monsterIndex];

        string name = monster.Visible
            ? "The " + GameTables.CreatureList[monster.CreatureIndex].Name
            : "Something";

        return name + " is in your way!";
    }
}
