// Ported from inven_type in Umoria 5.6 source/types.h, invcopy() in
// source/desc.c, and popt()/pusht() in source/misc1.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// A concrete item, as opposed to the template it was stamped from. Mirrors
/// Umoria's inven_type.
///
/// It duplicates most of <see cref="TreasureType"/> rather than pointing at it,
/// because an item's own values drift from its template - enchantments, charges
/// used, a price haggled. <see cref="Index"/> keeps the link back.
/// </summary>
public sealed class InvenType : IItemAttributes
{
    /// <summary>Row this was copied from in <see cref="GameTables.ObjectList"/>.</summary>
    public int Index { get; set; }

    /// <summary>Special name, e.g. "of Slay Evil". Umoria's name2 / SN_* values.</summary>
    public byte SpecialName { get; set; }

    /// <summary>Player's inscription, up to twelve characters in the C.</summary>
    public string Inscription { get; set; } = string.Empty;

    public uint Flags { get; set; }

    public byte TVal { get; set; }

    public char DisplayChar { get; set; }

    /// <summary>Catch-all parameter: food value, charges, bonus magnitude.</summary>
    public short P1 { get; set; }

    public int Cost { get; set; }

    public byte SubVal { get; set; }

    public byte Number { get; set; }

    public ushort Weight { get; set; }

    public short ToHit { get; set; }

    public short ToDam { get; set; }

    public short Ac { get; set; }

    public short ToAc { get; set; }

    public byte DamageDice { get; set; }

    public byte DamageSides { get; set; }

    public byte Level { get; set; }

    /// <summary>What the player has learned about this item.</summary>
    public byte Identification { get; set; }

    /// <summary>
    /// Copies another item's current state, enchantments and all. CopyFrom only
    /// stamps the table template, which is not the same thing once an item has
    /// been enchanted or partly used.
    /// </summary>
    public void CopyStateFrom(InvenType other)
    {
        ArgumentNullException.ThrowIfNull(other);

        Index = other.Index;
        SpecialName = other.SpecialName;
        Inscription = other.Inscription;
        Flags = other.Flags;
        TVal = other.TVal;
        DisplayChar = other.DisplayChar;
        P1 = other.P1;
        Cost = other.Cost;
        SubVal = other.SubVal;
        Number = other.Number;
        Weight = other.Weight;
        ToHit = other.ToHit;
        ToDam = other.ToDam;
        Ac = other.Ac;
        ToAc = other.ToAc;
        DamageDice = other.DamageDice;
        DamageSides = other.DamageSides;
        Level = other.Level;
        Identification = other.Identification;
    }

    /// <summary>
    /// The last row of the object table: an item called "nothing", which is what
    /// an empty slot holds. Umoria's OBJ_NOTHING.
    /// </summary>
    public const int Nothing = 417;

    /// <summary>
    /// Empties this slot. Mirrors invcopy(ptr, OBJ_NOTHING), which is the only
    /// way the original ever empties one.
    ///
    /// Emptying is stamping a row of the table, not zeroing the struct, and the
    /// difference is not academic: "nothing" has a subvalue of sixty-four, and
    /// several commands ask a slot what it is by its subvalue. Pouring oil is
    /// the plainest case - it takes a subvalue of nought to mean a lamp, so a
    /// zeroed light slot is a lamp that is not there, and filling it lights a
    /// player who is carrying no light at all.
    /// </summary>
    public void Clear() => CopyFrom(Nothing);

    /// <summary>
    /// Stamps this item from an object table row. Mirrors invcopy().
    /// </summary>
    public void CopyFrom(int objectListIndex)
    {
        TreasureType from = GameTables.ObjectList[objectListIndex];

        Index = objectListIndex;
        SpecialName = 0; // SN_NULL
        Inscription = string.Empty;
        Flags = from.Flags;
        TVal = from.TVal;
        DisplayChar = from.DisplayChar;
        P1 = from.P1;
        Cost = from.Cost;
        SubVal = from.SubVal;
        Number = from.Number;
        Weight = from.Weight;
        ToHit = from.ToHit;
        ToDam = from.ToDam;
        Ac = from.Ac;
        ToAc = from.ToAc;
        DamageDice = from.DamageDice;
        DamageSides = from.DamageSides;
        Level = from.Level;
        Identification = 0;
    }
}

/// <summary>
/// The objects lying on the current level. Mirrors Umoria's t_list array with
/// its tcptr high-water mark.
///
/// Squares refer to items by index, so the indices are the identity - which is
/// why removing an item moves the last one into the gap and patches the square
/// that pointed at it, rather than leaving a hole.
/// </summary>
public sealed class ObjectPool
{
    /// <summary>
    /// Umoria's MIN_TRIX. Index 0 is reserved to mean "no object", so the list
    /// starts at 1.
    /// </summary>
    public const int FirstIndex = 1;

    /// <summary>Umoria's MAX_TALLOC: objects allowed on one level.</summary>
    public const int Capacity = 175;

    private readonly InvenType[] _items;

    public ObjectPool()
    {
        _items = new InvenType[Capacity];
        for (int i = 0; i < Capacity; i++)
        {
            _items[i] = new InvenType();
        }

        Count = FirstIndex;
    }

    /// <summary>High-water mark. Umoria's tcptr.</summary>
    public int Count { get; private set; }

    /// <summary>Puts the mark back where a saved game left it.</summary>
    internal void SetCount(int count) => Count = count;

    public InvenType this[int index] => _items[index];

    /// <summary>Clears the list for a new level. Mirrors tlink().</summary>
    public void Reset()
    {
        foreach (InvenType item in _items)
        {
            item.Clear();
        }

        Count = FirstIndex;
    }

    /// <summary>
    /// What makes room when the list is full. Mirrors compact_objects(), which
    /// popt() calls before it gives up. Attached by <see cref="Compaction"/>.
    /// </summary>
    public Action? Compactor { get; set; }

    /// <summary>
    /// Claims the next free slot. Mirrors popt().
    ///
    /// A full list is not the end of the level: distant objects are thrown away
    /// until there is room. Compaction always finds something eventually, since
    /// it closes in until it does.
    /// </summary>
    public int Allocate()
    {
        if (Count == Capacity)
        {
            if (Compactor is null)
            {
                throw new InvalidOperationException(
                    "The object list is full and nothing is attached to compact it.");
            }

            Compactor();
        }

        return Count++;
    }

    /// <summary>
    /// Releases a slot, moving the last item down into it. Mirrors pusht().
    ///
    /// The square holding the moved item has to be repointed, which is why this
    /// needs the cave. Umoria scans the whole grid to find it.
    /// </summary>
    public void Release(int index, Cave cave)
    {
        ArgumentNullException.ThrowIfNull(cave);

        int last = Count - 1;
        if (index != last)
        {
            CopyInto(_items[index], _items[last]);

            for (int row = 0; row < cave.Height; row++)
            {
                for (int column = 0; column < cave.Width; column++)
                {
                    if (cave[row, column].ObjectIndex == last)
                    {
                        cave[row, column].ObjectIndex = index;
                    }
                }
            }
        }

        _items[last].Clear();
        Count--;
    }

    private static void CopyInto(InvenType target, InvenType source)
    {
        target.Index = source.Index;
        target.SpecialName = source.SpecialName;
        target.Inscription = source.Inscription;
        target.Flags = source.Flags;
        target.TVal = source.TVal;
        target.DisplayChar = source.DisplayChar;
        target.P1 = source.P1;
        target.Cost = source.Cost;
        target.SubVal = source.SubVal;
        target.Number = source.Number;
        target.Weight = source.Weight;
        target.ToHit = source.ToHit;
        target.ToDam = source.ToDam;
        target.Ac = source.Ac;
        target.ToAc = source.ToAc;
        target.DamageDice = source.DamageDice;
        target.DamageSides = source.DamageSides;
        target.Level = source.Level;
        target.Identification = source.Identification;
    }
}
