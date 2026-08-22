// Ported from Umoria 5.6 source/wands.c and source/staffs.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Aiming wands and using staffs.
///
/// Both work the same way: a roll against the character's skill with magical
/// devices decides whether anything happens at all, a charge is spent, and the
/// item's bit set of effects is applied. A wand needs a direction and works on
/// what it is pointed at; a staff needs none and works on the level.
///
/// The skill roll is deliberately never hopeless - anyone gets a slim chance
/// with anything - which is what lets a warrior use a found wand at all.
/// </summary>
public sealed class Devices
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Devices(GameState game, Display display, GameLoop loop)
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

    private Spells Spells => _loop.Spells;

    private Inventory Pack => _game.Inventory;

    /// <summary>
    /// Umoria's USE_DEVICE. The number the skill roll has to reach: higher makes
    /// every device harder.
    /// </summary>
    public const int UseDevice = 3;

    /// <summary>
    /// The aim command: asks which wand and which way, then fires it. Mirrors
    /// the front of aim().
    ///
    /// The turn stops being free before the direction is asked, so backing out
    /// of the direction is what gives it back - and a wand pointed nowhere
    /// costs nothing.
    /// </summary>
    public void Aim()
    {
        _loop.FreeTurn = true;

        if (Pack.Count == 0)
        {
            _display.MessagePrint("But you are not carrying anything.");
            return;
        }

        if (!Pack.FindRange(ItemCategory.Wand, ItemCategory.Never,
                            out int first, out int last))
        {
            _display.MessagePrint("You are not carrying any wands.");
            return;
        }

        if (_loop.InventoryScreen.GetItem("Aim which wand?", first, last)
            is not int slot)
        {
            return;
        }

        _loop.FreeTurn = false;

        (bool taken, int direction) = _loop.ReadDirection();

        if (taken)
        {
            Aim(slot, direction);
        }
    }

    /// <summary>
    /// The use command: asks which staff, then uses it. Mirrors the front of
    /// use().
    /// </summary>
    public void Use()
    {
        _loop.FreeTurn = true;

        if (Pack.Count == 0)
        {
            _display.MessagePrint("But you are not carrying anything.");
            return;
        }

        if (!Pack.FindRange(ItemCategory.Staff, ItemCategory.Never,
                            out int first, out int last))
        {
            _display.MessagePrint("You are not carrying any staffs.");
            return;
        }

        if (_loop.InventoryScreen.GetItem("Use which staff?", first, last)
            is int slot)
        {
            Use(slot);
        }
    }

    /// <summary>
    /// Aims a wand in a direction. Mirrors aim(), less the prompting that picks
    /// the wand and the direction.
    /// </summary>
    public void Aim(int slot, int direction)
    {
        InvenType wand = Pack[slot];
        _loop.FreeTurn = false;

        if (Player.Confused > 0)
        {
            _display.MessagePrint("You are confused.");

            // Anywhere but where it was pointed.
            do
            {
                direction = Rng.RandInt(9);
            }
            while (direction == 5);
        }

        if (!SkillRoll(wand, penalty: 0))
        {
            _display.MessagePrint("You failed to use the wand properly.");
            return;
        }

        if (wand.P1 <= 0)
        {
            _display.MessagePrint("The wand has no charges left.");

            if (!ItemKnowledge.IsEnchantmentKnown(wand))
            {
                ItemKnowledge.AddInscription(wand, Identification.Empty);
            }

            return;
        }

        uint flags = wand.Flags;
        wand.P1--;

        bool identified = false;

        while (flags != 0)
        {
            int effect = System.Numerics.BitOperations.TrailingZeroCount(flags) + 1;
            flags &= ~(1u << (effect - 1));

            flags = AimEffect(effect, direction, flags, ref identified);
        }

        slot = Learn(slot, wand, identified);
        DescribeCharges(slot);
    }

    /// <summary>
    /// Applies one wand effect. Returns the remaining effect bits, which the
    /// wand of wonder replaces outright.
    /// </summary>
    private uint AimEffect(int effect, int direction, uint remaining, ref bool identified)
    {
        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;

        switch (effect)
        {
            case 1:
                _display.MessagePrint("A line of blue shimmering light appears.");
                Spells.LightLine(direction, row, column);
                identified = true;
                break;

            case 2:
                Spells.FireBolt(SpellElement.Lightning, direction, row, column,
                    Rng.DamRoll(4, 8), GameTables.SpellNames[8]);
                identified = true;
                break;

            case 3:
                Spells.FireBolt(SpellElement.Frost, direction, row, column,
                    Rng.DamRoll(6, 8), GameTables.SpellNames[14]);
                identified = true;
                break;

            case 4:
                Spells.FireBolt(SpellElement.Fire, direction, row, column,
                    Rng.DamRoll(9, 8), GameTables.SpellNames[22]);
                identified = true;
                break;

            case 5: identified = Spells.WallToMud(direction, row, column); break;
            case 6: identified = Spells.PolymorphMonster(direction, row, column); break;

            case 7:
                identified = Spells.WoundMonster(
                    direction, row, column, -Rng.DamRoll(4, 6));
                break;

            case 8: identified = Spells.SpeedMonster(direction, row, column, 1); break;
            case 9: identified = Spells.SpeedMonster(direction, row, column, -1); break;
            case 10: identified = Spells.ConfuseMonster(direction, row, column); break;
            case 11: identified = Spells.SleepMonster(direction, row, column); break;
            case 12: identified = Spells.DrainLife(direction, row, column); break;
            case 13: identified = Spells.DestroyDoorsAlong(direction, row, column); break;

            case 14:
                Spells.FireBolt(SpellElement.MagicMissile, direction, row, column,
                    Rng.DamRoll(2, 6), GameTables.SpellNames[0]);
                identified = true;
                break;

            case 15: identified = Spells.BuildWall(direction, row, column); break;
            case 16: identified = Spells.CloneMonster(direction, row, column); break;
            case 17: identified = Spells.TeleportMonster(direction, row, column); break;
            case 18: identified = Spells.DisarmAll(direction, row, column); break;

            case 19:
                Spells.FireBall(SpellElement.Lightning, direction, row, column,
                    32, "Lightning Ball");
                identified = true;
                break;

            case 20:
                Spells.FireBall(SpellElement.Frost, direction, row, column,
                    48, "Cold Ball");
                identified = true;
                break;

            case 21:
                Spells.FireBall(SpellElement.Fire, direction, row, column,
                    72, GameTables.SpellNames[28]);
                identified = true;
                break;

            case 22:
                Spells.FireBall(SpellElement.PoisonGas, direction, row, column,
                    12, GameTables.SpellNames[6]);
                identified = true;
                break;

            case 23:
                Spells.FireBall(SpellElement.Acid, direction, row, column,
                    60, "Acid Ball");
                identified = true;
                break;

            case 24:
                // The wand of wonder: whatever is left to do is thrown away and
                // one effect of the other twenty-three is done instead.
                return 1u << (Rng.RandInt(23) - 1);

            default:
                _display.MessagePrint("Internal error in wands()");
                break;
        }

        return remaining;
    }

    /// <summary>
    /// Uses a staff. Mirrors use(), less the prompting that picks the staff.
    /// </summary>
    public void Use(int slot)
    {
        InvenType staff = Pack[slot];
        _loop.FreeTurn = false;

        // A staff is five harder than a wand to use.
        if (!SkillRoll(staff, penalty: 5))
        {
            _display.MessagePrint("You failed to use the staff properly.");
            return;
        }

        if (staff.P1 <= 0)
        {
            _display.MessagePrint("The staff has no charges left.");

            if (!ItemKnowledge.IsEnchantmentKnown(staff))
            {
                ItemKnowledge.AddInscription(staff, Identification.Empty);
            }

            return;
        }

        uint flags = staff.Flags;
        staff.P1--;

        bool identified = false;

        while (flags != 0)
        {
            int effect = System.Numerics.BitOperations.TrailingZeroCount(flags) + 1;
            flags &= ~(1u << (effect - 1));

            UseEffect(effect, ref identified);
        }

        slot = Learn(slot, staff, identified);
        DescribeCharges(slot);
    }

    private void UseEffect(int effect, ref bool identified)
    {
        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;

        switch (effect)
        {
            case 1: identified = Spells.LightArea(row, column); break;
            case 2: identified = Spells.DetectSecretDoors(); break;
            case 3: identified = Spells.DetectTrap(); break;
            case 4: identified = Spells.DetectTreasure(); break;
            case 5: identified = Spells.DetectObject(); break;

            case 6:
                _loop.Combat.Teleport(100);
                identified = true;
                break;

            case 7:
                identified = true;
                Spells.Earthquake();
                break;

            case 8:
                identified = false;

                // FAITHFUL QUIRK: the bound is re-rolled on every pass, so how
                // many arrive is a random walk rather than one roll of four.
                var generator = new DungeonGenerator(_game, _display);

                for (int i = 0; i < Rng.RandInt(4); i++)
                {
                    int y = row;
                    int x = column;
                    identified |= generator.SummonMonster(ref y, ref x, false);
                }

                break;

            case 10:
                identified = true;
                Spells.DestroyArea(row, column);
                break;

            case 11:
                identified = true;
                Spells.Starlight(row, column);
                break;

            case 12: identified = Spells.ChangeEveryMonsterSpeed(1); break;
            case 13: identified = Spells.ChangeEveryMonsterSpeed(-1); break;
            case 14: identified = Spells.SleepEveryMonster(); break;
            case 15: identified = Spells.HealPlayer(Rng.RandInt(8)); break;
            case 16: identified = Spells.DetectInvisibleMonsters(); break;

            case 17:
                // Only noticed if it was not already in effect.
                if (Player.Hasted == 0)
                {
                    identified = true;
                }

                Player.Hasted += Rng.RandInt(30) + 15;
                break;

            case 18:
                if (Player.Slowed == 0)
                {
                    identified = true;
                }

                Player.Slowed += Rng.RandInt(30) + 15;
                break;

            case 19: identified = Spells.PolymorphEveryMonster(); break;

            case 20:
                if (Spells.RemoveCurse())
                {
                    if (Player.Blind < 1)
                    {
                        _display.MessagePrint("The staff glows blue for a moment..");
                    }

                    identified = true;
                }

                break;

            case 21: identified = Spells.DetectEvil(); break;

            case 22:
                // FAITHFUL QUIRK: short-circuiting, so a staff of curing that
                // cures blindness stops there and leaves poison and confusion
                // alone.
                if (Spells.CureBlindness() || Spells.CurePoison()
                    || Spells.CureConfusion())
                {
                    identified = true;
                }

                break;

            case 23:
                identified = Spells.DispelCreature(CreatureDefense.Evil, 60);
                break;

            case 25:
                identified = Spells.UnlightArea(row, column);
                break;

            case 32:
                // The store-bought flag, which does nothing when used.
                break;

            default:
                _display.MessagePrint("Internal error in staffs()");
                break;
        }
    }

    /// <summary>
    /// Whether the character worked the device. Mirrors the shared roll in
    /// aim() and use().
    ///
    /// Being confused halves the chance, and a deep item is harder than a
    /// shallow one - but a slim chance is always granted, so nothing is ever
    /// entirely beyond a character.
    /// </summary>
    private bool SkillRoll(InvenType device, int penalty)
    {
        int chance = Player.Save + Stats.Adjustment(Player, Stat.Intelligence)
            - device.Level - penalty
            + (GameTables.ClassLevelAdjust[Player.Class][LevelSkill.MagicDevice]
               * Player.Level / 3);

        if (Player.Confused > 0)
        {
            chance /= 2;
        }

        // Everyone gets a slight chance.
        if (chance < UseDevice && Rng.RandInt(UseDevice - chance + 1) == 1)
        {
            chance = UseDevice;
        }

        if (chance <= 0)
        {
            chance = 1;
        }

        return Rng.RandInt(chance) >= UseDevice;
    }

    /// <summary>
    /// The shared tail of aim() and use(): what working the device taught.
    /// </summary>
    private int Learn(int slot, InvenType device, bool identified)
    {
        if (identified)
        {
            if (!_game.Knowledge.IsKindKnown(device))
            {
                // Rounding the half-way case up.
                Player.Experience += (device.Level + (Player.Level >> 1)) / Player.Level;
                _loop.Levelling.PrintExperience();

                slot = Pack.Identify(slot, _display);
            }
        }
        else if (!_game.Knowledge.IsKindKnown(device))
        {
            _game.Knowledge.MarkTried(device);
        }

        return slot;
    }

    /// <summary>
    /// Says how many charges are left, if the player knows. Mirrors
    /// desc_charges().
    /// </summary>
    private void DescribeCharges(int slot)
    {
        InvenType device = Pack[slot];

        if (!ItemKnowledge.IsEnchantmentKnown(device))
        {
            return;
        }

        // FAITHFUL QUIRK: always plural, so one charge reads "1 charges".
        _display.MessagePrint("You have " + device.P1 + " charges remaining.");
    }
}
