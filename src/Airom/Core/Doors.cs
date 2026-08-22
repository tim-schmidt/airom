// Ported from openobject, closeobject and twall in Umoria 5.6 source/moria3.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
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

    private Rng Rng => _game.Rng;

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
    /// Throws a shoulder at whatever is in the way. Mirrors bash().
    ///
    /// A door bashed open is faster than a door unlocked, and the character
    /// carries on through it - but half the time it breaks, and every failure
    /// risks losing their footing. A chest bashed is mostly ruined along with
    /// everything in it.
    /// </summary>
    public void Bash()
    {
        (bool taken, int direction) = _loop.ReadDirection();

        if (!taken)
        {
            return;
        }

        if (Player.Confused > 0)
        {
            _display.MessagePrint("You are confused.");

            do
            {
                direction = Rng.RandInt(9);
            }
            while (direction == 5);
        }

        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;
        _game.Cave.Move(direction, ref row, ref column);

        CaveSquare square = _game.Cave[row, column];

        if (square.MonsterIndex > 1)
        {
            if (Player.Afraid > 0)
            {
                _display.MessagePrint("You are afraid!");
            }
            else
            {
                BashMonster(row, column);
            }

            return;
        }

        if (square.ObjectIndex == 0)
        {
            // Nothing there. A wall gets the same answer as a secret door, so
            // that bashing around cannot be used to find one.
            _display.MessagePrint(square.Feature < CaveFeature.MinCaveWall
                ? "You bash at empty space."
                : "You bash it, but nothing interesting happens.");

            return;
        }

        InvenType obstacle = _game.Objects[square.ObjectIndex];

        if (obstacle.TVal == ItemCategory.ClosedDoor)
        {
            BashDoor(obstacle, square, direction, row, column);
        }
        else if (obstacle.TVal == ItemCategory.Chest)
        {
            BashChest(obstacle);
        }
        else
        {
            // No free turn here on purpose: with one, a player could bash in
            // every direction until they found an invisible creature.
            _display.MessagePrint("You bash it, but nothing interesting happens.");
        }
    }

    /// <summary>
    /// A closed door. Whether it gives is weighed the same way a monster's
    /// shoulder is, against how firmly it is locked or jammed.
    /// </summary>
    private void BashDoor(
        InvenType door, CaveSquare square, int direction, int row, int column)
    {
        _display.CountMessagePrint("You smash into the door!");

        int force = Player.UseStat[Stat.Strength] + (Player.Weight / 2);
        int fastening = Math.Abs(door.P1);

        if (Rng.RandInt(force * (20 + fastening)) < 10 * (force - fastening))
        {
            _display.MessagePrint("The door crashes open!");
            door.CopyFrom(OpenDoorObject);

            // Half the time the door is broken rather than merely open, which
            // is what a p1 of one means for an open door.
            door.P1 = (short)(1 - Rng.RandInt(2));
            square.Feature = CaveFeature.CorridorFloor;

            if (Player.Confused == 0)
            {
                _loop.Movement.MoveChar(direction, false);
            }
            else
            {
                _loop.Lighting.LightSpot(row, column);
            }

            return;
        }

        if (Rng.RandInt(150) > Player.UseStat[Stat.Dexterity])
        {
            _display.MessagePrint("You are off-balance.");
            Player.Paralysis = 1 + Rng.RandInt(2);
            return;
        }

        // Said once rather than every turn of a counted bash.
        if (_display.CommandCount == 0)
        {
            _display.MessagePrint("The door holds firm.");
        }
    }

    /// <summary>
    /// A chest, which mostly means ruining it. Breaking the lock open is the
    /// lucky outcome; destroying the contents is the likely one.
    /// </summary>
    private void BashChest(InvenType chest)
    {
        if (Rng.RandInt(10) == 1)
        {
            _display.MessagePrint("You have destroyed the chest.");
            _display.MessagePrint("and its contents!");
            chest.Index = RuinedChestObject;
            chest.Flags = 0;
            return;
        }

        if ((chest.Flags & ChestFlags.Locked) != 0 && Rng.RandInt(10) == 1)
        {
            _display.MessagePrint("The lock breaks open!");
            chest.Flags &= ~ChestFlags.Locked;
            return;
        }

        _display.CountMessagePrint("The chest holds firm.");
    }

    /// <summary>
    /// A shoulder charge at a creature. Mirrors py_bash().
    ///
    /// It is the shield that does the damage, so someone carrying none is
    /// bashing with nothing at all. What it is really for is the stunning:
    /// something dazed by a bash cannot fight back for a few turns.
    /// </summary>
    private void BashMonster(int row, int column)
    {
        int index = _game.Cave[row, column].MonsterIndex;
        Monster monster = _game.Monsters[index];
        CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];
        InvenType shield = _game.Inventory[Inventory.ArmSlot];

        monster.Sleep = 0;

        string name = monster.Visible
            ? "the " + creature.Name
            : "it";

        int toHit = Player.UseStat[Stat.Strength] + (shield.Weight / 2)
            + (Player.Weight / 10);

        // Something the player cannot see is much harder to shoulder squarely.
        if (!monster.Visible)
        {
            toHit = (toHit / 2)
                - (Player.UseStat[Stat.Dexterity] * (Combat.ToHitWeight - 1))
                - (Player.Level
                   * GameTables.ClassLevelAdjust[Player.Class][LevelSkill.Fighting] / 2);
        }

        if (!_loop.Combat.TestHit(toHit, Player.Level, Player.UseStat[Stat.Dexterity],
                                  creature.Ac, LevelSkill.Fighting))
        {
            _display.MessagePrint("You miss " + name + ".");
            OffBalance();
            return;
        }

        _display.MessagePrint("You hit " + name + ".");

        int damage = Rng.DamRoll(shield.DamageDice, shield.DamageSides);
        damage = _loop.Combat.CriticalBlow(
            (shield.Weight / 4) + Player.UseStat[Stat.Strength], 0, damage,
            LevelSkill.Fighting);

        damage += (Player.Weight / 60) + 3;

        if (damage < 0)
        {
            damage = 0;
        }

        if (_loop.Combat.MonsterTakeHit(index, damage) >= 0)
        {
            _display.MessagePrint("You have slain " + name + ".");
            _loop.Levelling.PrintExperience();
            OffBalance();
            return;
        }

        string capitalised = char.ToUpper(name[0], CultureInfo.InvariantCulture)
            + name[1..];

        // A Balrog cannot be stunned: its hit points are fixed at the maximum,
        // so the roll is against a number nothing can beat.
        int averageHitPoints = (creature.DefenseFlags & CreatureDefense.MaxHitPoints) != 0
            ? creature.HitDiceCount * creature.HitDiceSides
            : (creature.HitDiceCount * (creature.HitDiceSides + 1)) >> 1;

        if (100 + Rng.RandInt(400) + Rng.RandInt(400)
            > monster.HitPoints + averageHitPoints)
        {
            monster.Stunned += Rng.RandInt(3) + 1;

            if (monster.Stunned > 24)
            {
                monster.Stunned = 24;
            }

            _display.MessagePrint(capitalised + " appears stunned!");
        }
        else
        {
            _display.MessagePrint(capitalised + " ignores your bash!");
        }

        OffBalance();
    }

    /// <summary>
    /// The price of throwing your weight about: a clumsy character spends a
    /// turn or two on the floor.
    /// </summary>
    private void OffBalance()
    {
        if (Rng.RandInt(150) > Player.UseStat[Stat.Dexterity])
        {
            _display.MessagePrint("You are off balance.");
            Player.Paralysis = 1 + Rng.RandInt(2);
        }
    }

    /// <summary>What a chest becomes when it is smashed. Umoria's OBJ_RUINED_CHEST.</summary>
    private const int RuinedChestObject = 418;

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
