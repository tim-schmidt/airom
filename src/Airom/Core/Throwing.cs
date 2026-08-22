// Ported from throw_object(), inven_throw(), facts() and drop_throw() in
// Umoria 5.6 source/moria4.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Throwing and firing.
///
/// Anything at all can be thrown, but a missile thrown from the weapon made for
/// it is a different proposition: the launcher's bonuses are counted twice, the
/// damage is multiplied rather than added to, and it flies two or three times as
/// far. That is the whole reason to carry a bow.
/// </summary>
public sealed class Throwing
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Throwing(GameState game, Display display, GameLoop loop)
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

    private Inventory Pack => _game.Inventory;

    /// <summary>What a thrown missile is worth, once the launcher is counted.</summary>
    private readonly record struct Missile(int BaseToHit, int PlusToHit, int Damage, int Range);

    /// <summary>
    /// Throws something across the dungeon. Mirrors throw_object().
    ///
    /// The missile is drawn a square at a time as it flies, and stops at the
    /// first thing it meets - a creature to be hit, or a wall to fall short of.
    /// </summary>
    public void ThrowObject()
    {
        if (Pack.Count == 0)
        {
            _display.MessagePrint("But you are not carrying anything.");
            _loop.FreeTurn = true;
            return;
        }

        if (_loop.InventoryScreen.GetItem("Fire/Throw which one?", 0, Pack.Count - 1)
            is not int slot)
        {
            return;
        }

        (bool taken, int direction) = _loop.ReadDirection();

        if (!taken)
        {
            return;
        }

        DescribeRemaining(slot);

        if (Player.Confused > 0)
        {
            _display.MessagePrint("You are confused.");

            do
            {
                direction = Rng.RandInt(9);
            }
            while (direction == 5);
        }

        InvenType thrown = TakeForThrowing(slot);
        Missile missile = Facts(thrown);

        Fly(thrown, missile, direction);
    }

    /// <summary>
    /// Takes one out of the pack to throw. Mirrors inven_throw().
    ///
    /// One of a pile is taken and the rest stay carried, which is what makes a
    /// quiver of arrows last.
    /// </summary>
    private InvenType TakeForThrowing(int slot)
    {
        InvenType carried = Pack[slot];
        var thrown = new InvenType();
        thrown.CopyStateFrom(carried);

        if (carried.Number > 1)
        {
            thrown.Number = 1;
            Pack.DropOne(slot);
            Player.Status |= PlayerStatus.WeightChanged;
        }
        else
        {
            Pack.Destroy(slot);
        }

        return thrown;
    }

    /// <summary>
    /// What the missile hits with, hurts with, and how far it goes. Mirrors
    /// facts().
    ///
    /// Range falls off with weight, so a heavy thing is dropped rather than
    /// thrown. The right missile in the right launcher replaces all of that
    /// with the launcher's own numbers.
    /// </summary>
    private Missile Facts(InvenType thrown)
    {
        int weight = thrown.Weight < 1 ? 1 : thrown.Weight;

        int damage = Rng.DamRoll(thrown.DamageDice, thrown.DamageSides) + thrown.ToDam;
        int baseToHit = Player.BaseToHitBows * 75 / 100;
        int plusToHit = Player.PlusToHit + thrown.ToHit;

        InvenType weapon = Pack[Inventory.WieldSlot];

        // The wielded weapon's own accuracy does not help something thrown from
        // the other hand; it is added back below if it turns out to be the
        // right launcher for this missile.
        if (weapon.TVal != ItemCategory.Nothing)
        {
            plusToHit -= weapon.ToHit;
        }

        int range = (Player.UseStat[Stat.Strength] + 20) * 10 / weight;

        if (range > 10)
        {
            range = 10;
        }

        if (weapon.TVal != ItemCategory.Bow)
        {
            return new Missile(baseToHit, plusToHit, damage, range);
        }

        // The damage is multiplied rather than added to, which is what makes a
        // matched bow and arrow worth far more than the sum of the two.
        (int wanted, int multiplier, int reach) = weapon.P1 switch
        {
            1 => (ItemCategory.SlingAmmo, 2, 20),
            2 => (ItemCategory.Arrow, 2, 25),
            3 => (ItemCategory.Arrow, 3, 30),
            4 => (ItemCategory.Arrow, 4, 35),
            5 => (ItemCategory.Bolt, 3, 25),
            6 => (ItemCategory.Bolt, 4, 35),
            _ => (ItemCategory.Nothing, 0, 0),
        };

        if (thrown.TVal != wanted)
        {
            return new Missile(baseToHit, plusToHit, damage, range);
        }

        return new Missile(
            Player.BaseToHitBows,
            plusToHit + (2 * weapon.ToHit),
            (damage + weapon.ToDam) * multiplier,
            reach);
    }

    /// <summary>
    /// Sends the missile on its way, one square at a time.
    /// </summary>
    private void Fly(InvenType thrown, Missile missile, int direction)
    {
        char symbol = thrown.DisplayChar;

        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;
        int lastRow = row;
        int lastColumn = column;
        int distance = 0;
        bool finished = false;

        do
        {
            _game.Cave.Move(direction, ref row, ref column);
            distance++;
            _loop.Lighting.LightSpot(lastRow, lastColumn);

            if (distance > missile.Range)
            {
                finished = true;
            }

            CaveSquare square = _game.Cave[row, column];

            if (square.Feature <= CaveFeature.MaxOpenSpace && !finished)
            {
                if (square.MonsterIndex > 1)
                {
                    finished = true;
                    Strike(thrown, missile, square, distance, lastRow, lastColumn);
                }
                else if (_display.Panel.Contains(row, column) && Player.Blind < 1
                         && (square.TemporaryLight || square.PermanentLight))
                {
                    // Drawn where it is now, and rubbed out next time round.
                    _display.PrintAt(symbol, row, column);
                    _display.Refresh();
                }
            }
            else
            {
                finished = true;
                DropThrown(lastRow, lastColumn, thrown);
            }

            lastRow = row;
            lastColumn = column;
        }
        while (!finished);
    }

    /// <summary>
    /// What happens when the missile reaches something. A miss drops it on the
    /// square it came from rather than the one it was aimed at.
    /// </summary>
    private void Strike(
        InvenType thrown, Missile missile, CaveSquare square,
        int distance, int lastRow, int lastColumn)
    {
        Monster monster = _game.Monsters[square.MonsterIndex];
        int baseToHit = missile.BaseToHit - distance;

        // Something the player cannot see is much harder to hit: most of the
        // bonuses come off, and what is left falls away with the distance.
        if (!monster.Visible)
        {
            baseToHit = (baseToHit / (distance + 2))
                - (Player.Level
                   * GameTables.ClassLevelAdjust[Player.Class][LevelSkill.Shooting] / 2)
                - (missile.PlusToHit * (Combat.ToHitWeight - 1));
        }

        CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

        if (!_loop.Combat.TestHit(baseToHit, Player.Level, missile.PlusToHit,
                                  creature.Ac, LevelSkill.Shooting))
        {
            DropThrown(lastRow, lastColumn, thrown);
            return;
        }

        string what = _game.Names.Describe(thrown, withArticle: false);
        bool visible = monster.Visible;

        _display.MessagePrint(visible
            ? "The " + what + " hits the " + creature.Name + "."
            : "You hear a cry as the " + what + " finds a mark.");

        int damage = _loop.Combat.TotalDamage(thrown, missile.Damage, monster.CreatureIndex);
        damage = _loop.Combat.CriticalBlow(
            thrown.Weight, missile.PlusToHit, damage, LevelSkill.Shooting);

        if (damage < 0)
        {
            damage = 0;
        }

        int killed = _loop.Combat.MonsterTakeHit(square.MonsterIndex, damage);

        if (killed >= 0)
        {
            _display.MessagePrint(visible
                ? "You have killed the " + GameTables.CreatureList[killed].Name + "."
                : "You have killed something!");

            _loop.Levelling.PrintExperience();
        }
    }

    /// <summary>
    /// Puts a thrown thing down where it came to rest. Mirrors drop_throw().
    ///
    /// One throw in ten is lost outright, and so is anything with nowhere to
    /// land: a square holds one object, so a missile that finds every square
    /// nearby occupied simply disappears.
    /// </summary>
    private void DropThrown(int row, int column, InvenType thrown)
    {
        int landingRow = row;
        int landingColumn = column;
        bool landed = false;

        if (Rng.RandInt(10) > 1)
        {
            int tries = 0;

            do
            {
                if (_game.Cave.InBounds(landingRow, landingColumn))
                {
                    CaveSquare square = _game.Cave[landingRow, landingColumn];

                    if (square.Feature <= CaveFeature.MaxOpenSpace
                        && square.ObjectIndex == 0)
                    {
                        landed = true;
                    }
                }

                if (!landed)
                {
                    landingRow = row + Rng.RandInt(3) - 2;
                    landingColumn = column + Rng.RandInt(3) - 2;
                    tries++;
                }
            }
            while (!landed && tries <= 9);
        }

        if (!landed)
        {
            _display.MessagePrint(
                "The " + _game.Names.Describe(thrown, withArticle: false)
                + " disappears.");

            return;
        }

        int index = _game.Objects.Allocate();
        _game.Cave[landingRow, landingColumn].ObjectIndex = index;
        _game.Objects[index].CopyStateFrom(thrown);
        _loop.Lighting.LightSpot(landingRow, landingColumn);
    }

    /// <summary>
    /// Says what is left of the pile. Mirrors desc_remain(), which counts it one
    /// short so that the last one reads as "no more".
    /// </summary>
    private void DescribeRemaining(int slot)
    {
        InvenType item = Pack[slot];

        item.Number--;
        string description = _game.Names.Describe(item, withArticle: true);
        item.Number++;

        _display.MessagePrint("You have " + description);
    }
}
