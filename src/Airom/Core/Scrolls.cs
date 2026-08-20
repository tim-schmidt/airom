// Ported from Umoria 5.6 source/scrolls.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Reading scrolls.
///
/// A scroll is a bit set of effects like a potion, but two things make reading
/// one different: it can fail to be used up at all (a cancelled identify leaves
/// the scroll in the pack), and several of its effects ask the player a question
/// part-way through. The questions are seams here - see <see cref="ChooseItem"/>
/// and <see cref="ChooseSymbol"/> - because get_item() and get_com() belong to
/// the part of misc3.c that is not ported yet.
/// </summary>
public class Scrolls
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Scrolls(GameState game, Display display, GameLoop loop)
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
    /// The armour slots a scroll of enchantment can reach, in the order the
    /// original gathers them for the roll.
    /// </summary>
    private static readonly int[] ArmourSlots =
    [
        Inventory.BodySlot,
        Inventory.ArmSlot,
        Inventory.OuterSlot,
        Inventory.HandsSlot,
        Inventory.HeadSlot,
        Inventory.FeetSlot,
    ];

    /// <summary>
    /// The same slots in the order the original tests them one at a time - for
    /// the cursed override, and for the cursing scrolls. The head and the hands
    /// are the other way round from <see cref="ArmourSlots"/>; nothing seems to
    /// turn on it, but it is reproduced rather than tidied.
    /// </summary>
    private static readonly int[] ArmourSlotsInOrder =
    [
        Inventory.BodySlot,
        Inventory.ArmSlot,
        Inventory.OuterSlot,
        Inventory.HeadSlot,
        Inventory.HandsSlot,
        Inventory.FeetSlot,
    ];

    /// <summary>
    /// Asks which item to work on. Pending: get_item() from misc3.c.
    ///
    /// Returning nothing means the player cancelled, which for a scroll of
    /// identify or of recharging leaves the scroll unused.
    /// </summary>
    protected virtual int? ChooseItem(string prompt, int first, int last) => null;

    /// <summary>
    /// Asks for a single letter. Pending: get_com() from io.c. Used only by the
    /// scroll of genocide.
    /// </summary>
    protected virtual char? ChooseSymbol(string prompt) => null;

    /// <summary>
    /// Reads the scroll in a pack slot and applies everything it does. Mirrors
    /// read_scroll(), less the prompting that picks the scroll.
    ///
    /// The whole tail of the original is here as well: the experience for
    /// learning what a scroll was, the naming, and the using up.
    /// </summary>
    public void Read(int slot)
    {
        _loop.FreeTurn = false;

        InvenType scroll = Pack[slot];
        uint flags = scroll.Flags;
        bool secondTable = scroll.TVal == ItemCategory.Scroll2;

        bool identified = false;
        bool usedUp = true;

        while (flags != 0)
        {
            int effect = System.Numerics.BitOperations.TrailingZeroCount(flags) + 1;
            flags &= ~(1u << (effect - 1));

            if (secondTable)
            {
                effect += 32;
            }

            slot = Apply(effect, slot, ref identified, ref usedUp);
        }

        Finish(slot, identified, usedUp);
    }

    /// <summary>
    /// Applies one effect. Returns the slot the scroll is in afterwards, which
    /// only the identify scroll can change.
    /// </summary>
    private int Apply(int effect, int slot, ref bool identified, ref bool usedUp)
    {
        switch (effect)
        {
            case 1:
                identified |= EnchantWeapon(weaponToHit: true, weaponToDamage: false);
                break;

            case 2:
                identified |= EnchantWeapon(weaponToHit: false, weaponToDamage: true);
                break;

            case 3:
                identified |= EnchantArmour(greater: false);
                break;

            case 4:
                _display.MessagePrint("This is an identify scroll.");
                identified = true;
                usedUp = IdentifySomething();

                // Identifying can merge two piles, which moves this scroll down
                // the pack - arbitrarily far, if an identify scroll was used on
                // another identify scroll, but always down.
                while (Pack[slot].TVal != ItemCategory.Scroll1
                       || Pack[slot].Flags != IdentifyScrollFlag)
                {
                    slot--;
                }

                break;

            case 5:
                if (Spells.RemoveCurse())
                {
                    _display.MessagePrint("You feel as if someone is watching over you.");
                    identified = true;
                }

                break;

            case 6:
                identified = Spells.LightArea(_game.CharacterRow, _game.CharacterColumn);
                break;

            case 7:
                SummonSome(undead: false, ref identified);
                break;

            case 8:
                _loop.Combat.Teleport(10);
                identified = true;
                break;

            case 9:
                _loop.Combat.Teleport(100);
                identified = true;
                break;

            case 10:
                // Two levels up or one down, never staying put.
                _game.DungeonLevel += -3 + (2 * Rng.RandInt(2));

                if (_game.DungeonLevel < 1)
                {
                    _game.DungeonLevel = 1;
                }

                _loop.NewLevel = true;
                identified = true;
                break;

            case 11:
                if (!Player.ConfusingTouch)
                {
                    _display.MessagePrint("Your hands begin to glow.");
                    Player.ConfusingTouch = true;
                    identified = true;
                }

                break;

            case 12:
                identified = true;
                Spells.MapArea();
                break;

            case 13:
                identified = Spells.SleepAdjacent(_game.CharacterRow, _game.CharacterColumn);
                break;

            case 14:
                identified = true;
                Spells.WardingGlyph();
                break;

            case 15: identified = Spells.DetectTreasure(); break;
            case 16: identified = Spells.DetectObject(); break;
            case 17: identified = Spells.DetectTrap(); break;
            case 18: identified = Spells.DetectSecretDoors(); break;

            case 19:
                _display.MessagePrint("This is a mass genocide scroll.");
                Spells.MassGenocide();
                identified = true;
                break;

            case 20:
                identified = Spells.DetectInvisibleMonsters();
                break;

            case 21:
                _display.MessagePrint("There is a high pitched humming noise.");
                Spells.AggravateMonsters(20);
                identified = true;
                break;

            case 22: identified = Spells.TrapCreation(); break;
            case 23: identified = Spells.DestroyDoorsAndTraps(); break;
            case 24: identified = Spells.DoorCreation(); break;

            case 25:
                _display.MessagePrint("This is a Recharge-Item scroll.");
                identified = true;
                usedUp = RechargeSomething(60);
                break;

            case 26:
                _display.MessagePrint("This is a genocide scroll.");
                GenocideSomething();
                identified = true;
                break;

            case 27:
                identified = Spells.UnlightArea(_game.CharacterRow, _game.CharacterColumn);
                break;

            case 28:
                identified = Spells.ProtectFromEvil();
                break;

            case 29:
                identified = true;
                Spells.CreateFood();
                break;

            case 30:
                identified = Spells.DispelCreature(CreatureDefense.Undead, 60);
                break;

            case 33:
                identified |= EnchantWeapon(
                    weaponToHit: true, weaponToDamage: true, greater: true);
                break;

            case 34:
                identified |= CurseWeapon();
                break;

            case 35:
                identified |= EnchantArmour(greater: true);
                break;

            case 36:
                identified |= CurseArmour();
                break;

            case 37:
                identified = false;
                SummonSome(undead: true, ref identified);
                break;

            case 38:
                identified = true;
                Spells.Bless(Rng.RandInt(12) + 6);
                break;

            case 39:
                identified = true;
                Spells.Bless(Rng.RandInt(24) + 12);
                break;

            case 40:
                identified = true;
                Spells.Bless(Rng.RandInt(48) + 24);
                break;

            case 41:
                identified = true;

                if (Player.WordOfRecall == 0)
                {
                    Player.WordOfRecall = 25 + Rng.RandInt(30);
                }

                _display.MessagePrint("The air about you becomes charged.");
                break;

            case 42:
                Spells.DestroyArea(_game.CharacterRow, _game.CharacterColumn);
                identified = true;
                break;

            default:
                _display.MessagePrint("Internal error in scroll()");
                break;
        }

        return slot;
    }

    /// <summary>The flags of a scroll of identify, used to find it again.</summary>
    private const uint IdentifyScrollFlag = 0x00000008;

    /// <summary>
    /// The tail of read_scroll(): what reading it taught, and what became of it.
    /// </summary>
    private void Finish(int slot, bool identified, bool usedUp)
    {
        InvenType scroll = Pack[slot];

        if (identified)
        {
            if (!_game.Knowledge.IsKindKnown(scroll))
            {
                // Rounding the half-way case up.
                Player.Experience += (scroll.Level + (Player.Level >> 1)) / Player.Level;
                _loop.Levelling.PrintExperience();

                slot = Pack.Identify(slot, _display);
                scroll = Pack[slot];
            }
        }
        else if (!_game.Knowledge.IsKindKnown(scroll))
        {
            _game.Knowledge.MarkTried(scroll);
        }

        if (usedUp)
        {
            DescribeRemaining(slot);
            Pack.Destroy(slot);
        }
    }

    /// <summary>
    /// Says what is left of a pile after one is used. Mirrors desc_remain().
    /// </summary>
    private void DescribeRemaining(int slot)
    {
        InvenType item = Pack[slot];

        item.Number--;
        string description = _game.Names.Describe(item, withArticle: true);
        item.Number++;

        // The description already ends in a full stop.
        _display.MessagePrint("You have " + description);
    }

    // ------------------------------------------------------ what they ask for

    private bool IdentifySomething()
    {
        int? chosen = ChooseItem("Item you wish identified?", 0, Inventory.Size);

        if (chosen is not int slot)
        {
            return false;
        }

        Spells.IdentifyItem(slot);
        return true;
    }

    private bool RechargeSomething(int strength)
    {
        if (!Pack.FindRange(ItemCategory.Staff, ItemCategory.Wand,
                            out int first, out int last))
        {
            _display.MessagePrint("You have nothing to recharge.");
            return false;
        }

        int? chosen = ChooseItem("Recharge which item?", first, last);

        if (chosen is not int slot)
        {
            return false;
        }

        Spells.Recharge(slot, strength);
        return true;
    }

    private void GenocideSomething()
    {
        char? symbol = ChooseSymbol("Which type of creature do you wish exterminated?");

        if (symbol is char letter)
        {
            Spells.Genocide(letter);
        }
    }

    private void SummonSome(bool undead, ref bool identified)
    {
        var generator = new DungeonGenerator(_game);

        // FAITHFUL QUIRK: the original re-rolls the bound on every pass, so how
        // many arrive is a random walk rather than one roll of three.
        for (int i = 0; i < Rng.RandInt(3); i++)
        {
            int row = _game.CharacterRow;
            int column = _game.CharacterColumn;

            identified |= undead
                ? generator.SummonUndead(ref row, ref column)
                : generator.SummonMonster(ref row, ref column, false);
        }
    }

    // ------------------------------------------------------------ enchantment

    /// <summary>
    /// Enchants what is wielded. The greater scroll rolls each number up to
    /// twice rather than once.
    /// </summary>
    private bool EnchantWeapon(
        bool weaponToHit, bool weaponToDamage, bool greater = false)
    {
        InvenType weapon = Pack[Inventory.WieldSlot];

        if (weapon.TVal == ItemCategory.Nothing)
        {
            return false;
        }

        _display.MessagePrint(
            "Your " + _game.Names.Describe(weapon, withArticle: false)
            + (greater ? " glows brightly!" : " glows faintly!"));

        bool worked = false;

        if (weaponToHit)
        {
            short toHit = weapon.ToHit;

            for (int i = 0; i < Rolls(greater); i++)
            {
                worked |= Spells.Enchant(ref toHit, 10);
            }

            weapon.ToHit = toHit;
        }

        if (weaponToDamage)
        {
            // A bow or a sling has a low base damage of its own, so limiting
            // its enchantment by that would make one hardly worth enchanting.
            int limit = weapon.TVal is >= ItemCategory.Hafted and <= ItemCategory.Digging
                ? weapon.DamageDice * weapon.DamageSides
                : 10;

            short toDamage = weapon.ToDam;

            for (int i = 0; i < Rolls(greater); i++)
            {
                worked |= Spells.Enchant(ref toDamage, limit);
            }

            weapon.ToDam = toDamage;
        }

        if (worked)
        {
            weapon.Flags &= ~ItemFlags.Cursed;
            _loop.Equipment.Recalculate();
        }
        else
        {
            _display.MessagePrint("The enchantment fails.");
        }

        return true;
    }

    /// <summary>
    /// How many times the greater scroll tries. FAITHFUL QUIRK: this is the
    /// loop bound in the original, re-rolled on every pass, so a greater scroll
    /// can fail to try even once.
    /// </summary>
    private int Rolls(bool greater) => greater ? Rng.RandInt(2) : 1;

    /// <summary>
    /// Enchants a piece of armour. A cursed piece is always the one chosen -
    /// which is how a scroll of enchantment doubles as a way out of cursed
    /// boots.
    /// </summary>
    private bool EnchantArmour(bool greater)
    {
        int slot = PickArmourToEnchant();

        if (slot <= 0)
        {
            return false;
        }

        InvenType armour = Pack[slot];

        _display.MessagePrint(
            "Your " + _game.Names.Describe(armour, withArticle: false)
            + (greater ? " glows brightly!" : " glows faintly!"));

        bool worked = false;
        short toArmour = armour.ToAc;

        // The greater scroll's bound is re-rolled on every pass as well, but
        // with a one added, so it always tries at least twice.
        for (int i = 0; i < (greater ? Rng.RandInt(2) + 1 : 1); i++)
        {
            worked |= Spells.Enchant(ref toArmour, 10);
        }

        armour.ToAc = toArmour;

        if (worked)
        {
            armour.Flags &= ~ItemFlags.Cursed;
            _loop.Equipment.Recalculate();
        }
        else
        {
            _display.MessagePrint("The enchantment fails.");
        }

        return true;
    }

    private int PickArmourToEnchant()
    {
        Span<int> worn = stackalloc int[ArmourSlots.Length];
        int count = 0;

        foreach (int slot in ArmourSlots)
        {
            if (Pack[slot].TVal != ItemCategory.Nothing)
            {
                worn[count++] = slot;
            }
        }

        int chosen = count > 0 ? worn[Rng.RandInt(count) - 1] : 0;

        // A cursed piece takes priority over the roll, in a fixed order.
        foreach (int slot in ArmourSlotsInOrder)
        {
            if ((Pack[slot].Flags & ItemFlags.Cursed) != 0)
            {
                return slot;
            }
        }

        return chosen;
    }

    /// <summary>
    /// Ruins what is wielded. Mirrors the curse weapon scroll.
    /// </summary>
    private bool CurseWeapon()
    {
        InvenType weapon = Pack[Inventory.WieldSlot];

        if (weapon.TVal == ItemCategory.Nothing)
        {
            return false;
        }

        _display.MessagePrint(
            "Your " + _game.Names.Describe(weapon, withArticle: false)
            + " glows black, fades.");

        // Mirrors unmagic_name(): whatever made it special is gone.
        weapon.SpecialName = SpecialName.None;

        weapon.ToHit = (short)(-Rng.RandInt(5) - Rng.RandInt(5));
        weapon.ToDam = (short)(-Rng.RandInt(5) - Rng.RandInt(5));
        weapon.ToAc = 0;

        // The bonuses have to be taken off while the old flags still say what
        // they were, and put back on after the flags have been replaced, or
        // whatever the weapon granted would be left switched on.
        _loop.Equipment.ApplyItem(weapon, -1);
        weapon.Flags = ItemFlags.Cursed;
        _loop.Equipment.Recalculate();

        return true;
    }

    /// <summary>
    /// Ruins a piece of armour. Unlike the enchanting scroll this picks by
    /// rolling against each piece in turn, so the body armour is the most
    /// likely to suffer.
    /// </summary>
    private bool CurseArmour()
    {
        int slot = PickArmourToCurse();

        if (slot <= 0)
        {
            return false;
        }

        InvenType armour = Pack[slot];

        _display.MessagePrint(
            "Your " + _game.Names.Describe(armour, withArticle: false)
            + " glows black, fades.");

        armour.SpecialName = SpecialName.None;

        armour.Flags = ItemFlags.Cursed;
        armour.ToHit = 0;
        armour.ToDam = 0;
        armour.ToAc = (short)(-Rng.RandInt(5) - Rng.RandInt(5));

        _loop.Equipment.Recalculate();
        return true;
    }

    private int PickArmourToCurse()
    {
        // The body armour is rolled for at one in four, the rest at one in
        // three, in a fixed order.
        foreach (int slot in ArmourSlotsInOrder)
        {
            if (Pack[slot].TVal != ItemCategory.Nothing
                && Rng.RandInt(slot == Inventory.BodySlot ? 4 : 3) == 1)
            {
                return slot;
            }
        }

        // Nothing was picked, so the first piece worn is taken instead.
        foreach (int slot in ArmourSlotsInOrder)
        {
            if (Pack[slot].TVal != ItemCategory.Nothing)
            {
                return slot;
            }
        }

        return 0;
    }
}
