// Ported from the inventory half of Umoria 5.6 source/misc3.c, with carry()
// from source/moria3.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// What the player is carrying.
///
/// One array holds both the pack and what is worn: slots below
/// <see cref="WieldSlot"/> are the pack, and the twelve above it are the
/// equipment, each a fixed place for one kind of thing. That is why the pack can
/// only hold twenty-two items - the rest of the array is spoken for.
///
/// The pack is kept sorted, by category and then by subval, so an item's letter
/// stays where the player expects it between visits.
/// </summary>
public sealed class Inventory
{
    /// <summary>The wielded weapon, and the first of the worn slots. Umoria's INVEN_WIELD.</summary>
    public const int WieldSlot = 22;

    public const int HeadSlot = 23;
    public const int NeckSlot = 24;
    public const int BodySlot = 25;
    public const int ArmSlot = 26;
    public const int HandsSlot = 27;
    public const int RightRingSlot = 28;
    public const int LeftRingSlot = 29;
    public const int FeetSlot = 30;
    public const int OuterSlot = 31;
    public const int LightSlot = 32;

    /// <summary>The second weapon, which is carried rather than worn.</summary>
    public const int AuxiliarySlot = 33;

    /// <summary>How many slots there are in all. Umoria's INVEN_ARRAY_SIZE.</summary>
    public const int Size = AuxiliarySlot + 1;

    /// <summary>
    /// How a worn item is being used, for the messages that mention it. Mirrors
    /// describe_use().
    /// </summary>
    public static string DescribeUse(int slot) => slot switch
    {
        WieldSlot => "wielding",
        HeadSlot => "wearing on your head",
        NeckSlot => "wearing around your neck",
        BodySlot => "wearing on your body",
        ArmSlot => "wearing on your arm",
        HandsSlot => "wearing on your hands",
        RightRingSlot => "wearing on your right hand",
        LeftRingSlot => "wearing on your left hand",
        FeetSlot => "wearing on your feet",
        OuterSlot => "wearing about your body",
        LightSlot => "using to light the way",
        AuxiliarySlot => "holding ready by your side",
        _ => "carrying in your pack",
    };

    /// <summary>Weight a point of strength carries, in tenths of a pound.</summary>
    public const int WeightPerStrength = 100;

    /// <summary>The most anyone can carry, however strong.</summary>
    public const int MaxWeightLimit = 3000;

    private readonly GameState _game;
    private readonly InvenType[] _items = new InvenType[Size];

    public Inventory(GameState game)
    {
        ArgumentNullException.ThrowIfNull(game);
        _game = game;

        for (int i = 0; i < _items.Length; i++)
        {
            _items[i] = new InvenType();
        }
    }

    public InvenType this[int slot] => _items[slot];

    /// <summary>How many items are in the pack. Umoria's inven_ctr.</summary>
    public int Count { get; private set; }

    /// <summary>What the pack weighs, in tenths of a pound. Umoria's inven_weight.</summary>
    public int Weight { get; private set; }

    /// <summary>
    /// How badly the pack is slowing the player down, in points of speed.
    /// Umoria's pack_heavy.
    /// </summary>
    public int PackBurden { get; internal set; }

    /// <summary>Whether the wielded weapon is too heavy. Umoria's weapon_heavy.</summary>
    public bool WeaponTooHeavy { get; internal set; }

    /// <summary>How many things are worn or wielded. Umoria's equip_ctr.</summary>
    public int EquipmentCount { get; internal set; }

    /// <summary>
    /// Spends one of a wielded pile of missiles: they are swung as a weapon and
    /// used up as they are. Mirrors the weight bookkeeping inside py_attack().
    /// </summary>
    public void SpendWielded(int weight)
    {
        Weight -= weight;
        _game.Player.Status |= PlayerStatus.WeightChanged;

        if (this[WieldSlot].Number == 0)
        {
            EquipmentCount--;
        }
    }

    /// <summary>
    /// Counts a newly worn or wielded item into the equipment. The item itself
    /// is written into its slot by the caller, as the original does.
    /// </summary>
    public void EquipWielded(int weight)
    {
        EquipmentCount++;
        Weight += weight;
        _game.Player.Status |= PlayerStatus.WeightChanged;
    }

    /// <summary>Empties the pack and the worn slots alike.</summary>
    public void Reset()
    {
        foreach (InvenType item in _items)
        {
            item.Clear();
        }

        Count = 0;
        EquipmentCount = 0;
        Weight = 0;
        PackBurden = 0;
        WeaponTooHeavy = false;
    }

    /// <summary>
    /// How much the player can carry before it starts to tell. Mirrors
    /// weight_limit().
    ///
    /// Strength does most of the work, but the player's own weight counts too -
    /// a heavier character carries more.
    /// </summary>
    public int WeightLimit()
    {
        int limit = (_game.Player.UseStat[Stat.Strength] * WeightPerStrength)
            + _game.Player.Weight;

        return Math.Min(limit, MaxWeightLimit);
    }

    /// <summary>
    /// Whether there is room for an item. Mirrors inven_check_num().
    ///
    /// Room means either a free slot or a pile it can join. Two piles only merge
    /// when the player knows the same about both, so a known potion never
    /// disappears into a stack of unknown ones.
    /// </summary>
    public bool HasRoomFor(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (Count < WieldSlot)
        {
            return true;
        }

        if (item.SubVal < ItemCategory.SingleStackMin)
        {
            return false;
        }

        for (int i = 0; i < Count; i++)
        {
            if (StacksWith(_items[i], item))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// This code must stay in step with <see cref="Carry"/>: the check and the
    /// carrying decide the same thing, and disagreeing would lose an item.
    /// </summary>
    private bool StacksWith(InvenType existing, InvenType item) =>
        existing.TVal == item.TVal
        && existing.SubVal == item.SubVal
        && existing.Number + item.Number < 256
        // They either always stack, or they have to agree on their p1.
        && (item.SubVal < ItemCategory.GroupMin || existing.P1 == item.P1)
        // And only if the player knows the same about both.
        && _game.Knowledge.IsKindKnown(existing) == _game.Knowledge.IsKindKnown(item);

    /// <summary>
    /// Whether picking something up would change how fast the player moves.
    /// Mirrors inven_check_weight().
    /// </summary>
    public bool CanCarryWithoutSlowing(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        int limit = WeightLimit();
        int newWeight = (item.Number * item.Weight) + Weight;
        int burden = limit < newWeight ? newWeight / (limit + 1) : 0;

        return PackBurden == burden;
    }

    /// <summary>
    /// Adds an item to the pack, and says where it landed. Mirrors
    /// inven_carry().
    ///
    /// Anything that stacks joins its pile. Anything else is inserted in order -
    /// by category, and within a category by subval, but only for the kinds whose
    /// look never hides what they are. The rest go in at the front of their
    /// category, since sorting them would give away what they are.
    /// </summary>
    public int Carry(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        int category = item.TVal;
        int subval = item.SubVal;
        bool alwaysKnown = ItemKnowledge.OffsetOf(item) == -1;

        int slot;
        for (slot = 0; ; slot++)
        {
            InvenType existing = _items[slot];

            if (subval >= ItemCategory.SingleStackMin && StacksWith(existing, item))
            {
                existing.Number += item.Number;
                break;
            }

            if ((category == existing.TVal && subval < existing.SubVal && alwaysKnown)
                || category > existing.TVal)
            {
                for (int i = Count - 1; i >= slot; i--)
                {
                    _items[i + 1].CopyStateFrom(_items[i]);
                }

                _items[slot].CopyStateFrom(item);
                Count++;
                break;
            }
        }

        Weight += item.Number * item.Weight;
        _game.Player.Status |= PlayerStatus.WeightChanged;

        return slot;
    }

    /// <summary>
    /// Finds the run of slots holding one or two categories of thing. Mirrors
    /// find_range().
    ///
    /// The pack is kept sorted by category, so everything of a kind is in one
    /// unbroken run and a prompt can be limited to it - "Read which scroll?"
    /// offers the scrolls and nothing else.
    /// </summary>
    /// <returns>Whether anything of either category is carried.</returns>
    public bool FindRange(int category1, int category2, out int first, out int last)
    {
        first = -1;
        last = -1;
        bool found = false;

        for (int i = 0; i < Count; i++)
        {
            byte category = _items[i].TVal;

            if (!found)
            {
                if (category == category1 || category == category2)
                {
                    found = true;
                    first = i;
                }
            }
            else if (category != category1 && category != category2)
            {
                last = i - 1;
                break;
            }
        }

        if (found && last == -1)
        {
            last = Count - 1;
        }

        return found;
    }

    /// <summary>
    /// Learns what an item is, and merges it with any matching pile already in
    /// the pack. Mirrors identify() in desc.c.
    ///
    /// The merging is what lets a potion bought from a shop and an identical
    /// one found in the dungeon become one pile once the player knows they are
    /// the same thing.
    /// </summary>
    /// <returns>
    /// The slot the item ends up in, which is not always the one it started in:
    /// merging keeps the earlier of the two slots.
    /// </returns>
    public int Identify(int slot, Display? display = null)
    {
        InvenType item = _items[slot];

        if ((item.Flags & ItemFlags.Cursed) != 0)
        {
            ItemKnowledge.AddInscription(item, Identification.Damned);
        }

        if (_game.Knowledge.IsKindKnown(item))
        {
            return slot;
        }

        _game.Knowledge.LearnKind(item);

        byte category = item.TVal;
        byte kind = item.SubVal;

        // Only the singly-stacking range can merge: below it every item is
        // distinct, and above it a pile already stacks by itself.
        if (kind < ItemCategory.SingleStackMin || kind >= ItemCategory.GroupMin)
        {
            return slot;
        }

        for (int i = 0; i < Count; i++)
        {
            InvenType other = _items[i];

            if (other.TVal != category || other.SubVal != kind || i == slot
                || other.Number + item.Number >= 256)
            {
                continue;
            }

            // The pile is kept in the earlier of the two slots.
            if (slot > i)
            {
                (slot, i) = (i, slot);
            }

            display?.MessagePrint(
                "You combine similar objects from the shop and dungeon.");

            _items[slot].Number += _items[i].Number;
            Count--;

            for (int j = i; j < Count; j++)
            {
                _items[j].CopyStateFrom(_items[j + 1]);
            }

            _items[Count].Clear();
        }

        return slot;
    }

    /// <summary>
    /// Counts a worn item out of the equipment. Mirrors the two lines takeoff()
    /// opens with, before it says anything.
    /// </summary>
    public void Unequip(int weight)
    {
        EquipmentCount--;
        Weight -= weight;
        _game.Player.Status |= PlayerStatus.WeightChanged;
    }

    /// <summary>
    /// Adds weight without counting anything into the equipment. What wearing
    /// does to the total before the item has left the pack.
    /// </summary>
    public void EquipWeightOnly(int weight)
    {
        Weight += weight;
        _game.Player.Status |= PlayerStatus.WeightChanged;
    }

    /// <summary>
    /// Forgets what is carried weighs anything. A safety measure the original
    /// takes when the last thing is put down, since nothing carried can weigh
    /// something.
    /// </summary>
    public void ClearWeight() => Weight = 0;

    /// <summary>
    /// Takes a whole pack slot out, whatever is stacked in it. What drop_all
    /// does in inven_drop().
    /// </summary>
    public void DropWhole(int slot)
    {
        InvenType item = _items[slot];
        Weight -= item.Weight * item.Number;
        Count--;

        for (int i = slot; i < Count; i++)
        {
            _items[i].CopyStateFrom(_items[i + 1]);
        }

        _items[Count].Clear();
    }

    /// <summary>
    /// Takes one out of a pack slot and leaves the rest. The other half of
    /// inven_drop().
    /// </summary>
    public void DropOne(int slot)
    {
        InvenType item = _items[slot];
        Weight -= item.Weight;
        item.Number--;
    }

    /// <summary>
    /// Takes one item, or one of a pile, out of the pack. Mirrors
    /// inven_destroy().
    /// </summary>
    public void Destroy(int slot)
    {
        InvenType item = _items[slot];

        if (item.Number > 1 && item.SubVal <= ItemCategory.SingleStackMax)
        {
            item.Number--;
            Weight -= item.Weight;
        }
        else
        {
            Weight -= item.Weight * item.Number;

            for (int i = slot; i < Count - 1; i++)
            {
                _items[i].CopyStateFrom(_items[i + 1]);
            }

            _items[Count - 1].Clear();
            Count--;
        }

        _game.Player.Status |= PlayerStatus.WeightChanged;
    }

    /// <summary>
    /// Copies an item, taking one of a pile rather than the pile. Mirrors
    /// take_one_item().
    /// </summary>
    public static void TakeOne(InvenType destination, InvenType source)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(source);

        destination.CopyStateFrom(source);

        if (destination.Number > 1
            && destination.SubVal >= ItemCategory.SingleStackMin
            && destination.SubVal <= ItemCategory.SingleStackMax)
        {
            destination.Number = 1;
        }
    }

    /// <summary>
    /// Destroys items of a kind, each on a percentage chance. Mirrors
    /// inven_damage().
    ///
    /// Every slot is rolled for separately, which is why a fire can burn two
    /// scrolls and leave a third.
    /// </summary>
    /// <returns>How many were destroyed.</returns>
    public int Damage(Func<InvenType, bool> affects, int percent)
    {
        ArgumentNullException.ThrowIfNull(affects);

        int destroyed = 0;

        for (int i = 0; i < Count; i++)
        {
            if (affects(_items[i]) && _game.Rng.RandInt(100) < percent)
            {
                Destroy(i);
                destroyed++;
            }
        }

        return destroyed;
    }
}
