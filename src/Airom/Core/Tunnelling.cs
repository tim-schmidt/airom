// Ported from tunnel() in Umoria 5.6 source/moria4.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Digging through rock and rubble.
///
/// How fast it goes is a digging ability worked out from strength and whatever
/// is in hand: a shovel or a pick counts for a great deal, an ordinary weapon
/// for half of what it would do as a weapon, and bare hands for nothing at all.
/// That number is then rolled against the hardness of what is being dug, which
/// is why granite can take a very long time and a quartz vein rarely does.
/// </summary>
public sealed class Tunnelling
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Tunnelling(GameState game, Display display, GameLoop loop)
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

    private Cave Cave => _game.Cave;

    /// <summary>
    /// Digs in a direction. Mirrors tunnel().
    ///
    /// Confusion sends the digging somewhere else three times in four, which is
    /// why a confused character can knock a hole in the wrong wall.
    /// </summary>
    public void Tunnel(int direction)
    {
        if (Player.Confused > 0 && Rng.RandInt(4) > 1)
        {
            direction = Rng.RandInt(9);
        }

        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;
        Cave.Move(direction, ref row, ref column);

        CaveSquare square = Cave[row, column];
        InvenType weapon = _game.Inventory[Inventory.WieldSlot];

        // Nothing may be dug that is not diggable. The check is here rather
        // than further down so that digging at empty air cannot be used to get
        // a free attack on whatever happens to be standing in it.
        if (square.Feature < CaveFeature.MinCaveWall
            && (square.ObjectIndex == 0
                || (_game.Objects[square.ObjectIndex].TVal != ItemCategory.Rubble
                    && _game.Objects[square.ObjectIndex].TVal != ItemCategory.SecretDoor)))
        {
            _display.MessagePrint(square.ObjectIndex == 0
                ? "Tunnel through what?  Empty air?!?"
                : "You can't tunnel through that.");

            _loop.FreeTurn = true;
            return;
        }

        if (square.MonsterIndex > 1)
        {
            Monster monster = _game.Monsters[square.MonsterIndex];

            string name = monster.Visible
                ? "The " + GameTables.CreatureList[monster.CreatureIndex].Name
                : "Something";

            _display.MessagePrint(name + " is in your way!");

            if (Player.Afraid < 1)
            {
                _loop.Combat.Attack(row, column);
            }
            else
            {
                _display.MessagePrint("You are too afraid!");
            }

            return;
        }

        if (weapon.TVal == ItemCategory.Nothing)
        {
            _display.MessagePrint("You dig with your hands, making no progress.");
            return;
        }

        DigWith(weapon, row, column, square);
    }

    /// <summary>
    /// One turn of digging with whatever is in hand, against whatever is in the
    /// way.
    /// </summary>
    private void DigWith(InvenType weapon, int row, int column, CaveSquare square)
    {
        int ability = Player.UseStat[Stat.Strength];

        if ((weapon.Flags & ItemFlags.Tunnel) != 0)
        {
            // A digger, whose plus is worth far more than any weapon's.
            ability += 25 + (weapon.P1 * 50);
        }
        else
        {
            ability += (weapon.DamageDice * weapon.DamageSides)
                + weapon.ToHit + weapon.ToDam;

            // Halved, so that digging without a shovel is not too easy.
            ability >>= 1;
        }

        // A weapon too heavy to swing properly is also awkward to dig with.
        if (_game.Inventory.WeaponTooHeavy)
        {
            ability += (Player.UseStat[Stat.Strength] * 15) - weapon.Weight;

            if (ability < 0)
            {
                ability = 0;
            }
        }

        switch (square.Feature)
        {
            case CaveFeature.GraniteWall:
                Dig(row, column, ability, Rng.RandInt(1200) + 80,
                    "You tunnel into the granite wall.");
                break;

            case CaveFeature.MagmaWall:
                Dig(row, column, ability, Rng.RandInt(600) + 10,
                    "You tunnel into the magma intrusion.");
                break;

            case CaveFeature.QuartzWall:
                Dig(row, column, ability, Rng.RandInt(400) + 10,
                    "You tunnel into the quartz vein.");
                break;

            case CaveFeature.BoundaryWall:
                _display.MessagePrint("This seems to be permanent rock.");
                break;

            default:
                // Whatever is in the way is an object rather than the rock
                // itself: rubble, or a secret door standing in a wall.
                DigObject(row, column, square, ability);
                break;
        }
    }

    /// <summary>
    /// One turn against solid rock. The message that it is still going is a
    /// counted one, so a long dig says so once rather than every turn.
    /// </summary>
    private void Dig(int row, int column, int ability, int hardness, string progress)
    {
        if (_loop.Doors.TunnelWall(row, column, ability, hardness))
        {
            _display.MessagePrint("You have finished the tunnel.");
        }
        else
        {
            _display.CountMessagePrint(progress);
        }
    }

    /// <summary>
    /// Digging at rubble, or at a secret door that has not been found yet.
    ///
    /// Rubble sometimes has something under it. A secret door is dug at as
    /// though it were the wall it looks like, and the digging searches - which
    /// is how tunnelling into a blank wall finds the door in it.
    /// </summary>
    private void DigObject(int row, int column, CaveSquare square, int ability)
    {
        InvenType blocking = _game.Objects[square.ObjectIndex];

        if (blocking.TVal == ItemCategory.Rubble)
        {
            if (ability > Rng.RandInt(180))
            {
                _loop.Movement.DeleteObject(row, column);
                _display.MessagePrint("You have removed the rubble.");

                if (Rng.RandInt(10) == 1)
                {
                    new DungeonGenerator(_game, _display).PlaceObject(row, column, false);

                    if (IsSeen(row, column))
                    {
                        _display.MessagePrint("You have found something!");
                    }
                }

                _loop.Lighting.LightSpot(row, column);
            }
            else
            {
                _display.CountMessagePrint("You dig in the rubble.");
            }

            return;
        }

        if (blocking.TVal == ItemCategory.SecretDoor)
        {
            _display.CountMessagePrint("You tunnel into the granite wall.");
            _loop.Movement.Search(_game.CharacterRow, _game.CharacterColumn, Player.Search);
            return;
        }

        // The original calls abort() here: the guard at the top of tunnel()
        // means nothing else can reach this. Reaching it is a fault in this
        // port rather than something the player did.
        throw new InvalidOperationException(
            "tunnelled into an object that is neither rubble nor a secret door");
    }

    /// <summary>Whether a square is lit or remembered. Mirrors test_light().</summary>
    private bool IsSeen(int row, int column)
    {
        CaveSquare square = Cave[row, column];
        return square.PermanentLight || square.TemporaryLight || square.FieldMark;
    }
}
