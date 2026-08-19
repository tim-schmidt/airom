// Ported from the treasure_type struct in Umoria 5.6 source/types.h.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Data;

/// <summary>
/// One row of the object table - the template from which a concrete carried
/// item is built. Mirrors Umoria's treasure_type.
///
/// The integer widths are the ones the C used. They are kept deliberately
/// narrow rather than widened to int: the generated table passes literals into
/// this constructor, so anything that would have overflowed the original struct
/// becomes a compile error instead of a silent truncation.
/// </summary>
/// <param name="name">
/// Display name. A leading "&amp;" is Umoria's marker for "insert an article
/// here", and "~" marks where a plural "s" belongs.
/// </param>
/// <param name="flags">Ability bits; meaning depends on the category.</param>
/// <param name="tval">Category number (one of the TV_* values).</param>
/// <param name="displayChar">Map and inventory glyph.</param>
/// <param name="p1">
/// Catch-all magical parameter: food value for food, charges for wands and
/// staves, bonus magnitude for stat-modifying items.
/// </param>
/// <param name="cost">Relative cost in stores.</param>
/// <param name="subval">Sub-category number, used for stacking and sorting.</param>
/// <param name="number">How many appear at once.</param>
/// <param name="weight">Weight in tenths of a pound.</param>
/// <param name="toHit">Bonus to hit.</param>
/// <param name="toDam">Bonus to damage.</param>
/// <param name="ac">Base armour class.</param>
/// <param name="toAc">Magical bonus to armour class.</param>
/// <param name="damageDice">Number of damage dice.</param>
/// <param name="damageSides">Sides per damage die.</param>
/// <param name="level">Lowest dungeon level on which the item can be found.</param>
public sealed class TreasureType(
    string name,
    uint flags,
    byte tval,
    char displayChar,
    short p1,
    int cost,
    byte subval,
    byte number,
    ushort weight,
    short toHit,
    short toDam,
    short ac,
    short toAc,
    byte damageDice,
    byte damageSides,
    byte level)
{
    public string Name { get; } = name;

    public uint Flags { get; } = flags;

    public byte TVal { get; } = tval;

    public char DisplayChar { get; } = displayChar;

    public short P1 { get; } = p1;

    public int Cost { get; } = cost;

    public byte SubVal { get; } = subval;

    public byte Number { get; } = number;

    public ushort Weight { get; } = weight;

    public short ToHit { get; } = toHit;

    public short ToDam { get; } = toDam;

    public short Ac { get; } = ac;

    public short ToAc { get; } = toAc;

    /// <summary>Number of damage dice; the first byte of the C damage[2].</summary>
    public byte DamageDice { get; } = damageDice;

    /// <summary>Sides per damage die; the second byte of the C damage[2].</summary>
    public byte DamageSides { get; } = damageSides;

    public byte Level { get; } = level;

    public override string ToString() => Name;
}
