// Ported from m_attack_type in Umoria 5.6 source/types.h.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Data;

/// <summary>
/// One way a monster can attack. Mirrors m_attack_type.
///
/// Three separate ideas live in one entry, which is what lets the table be
/// shared: how the attack is delivered, what it looks like when it lands, and
/// the dice it rolls. A claw that freezes and a claw that burns differ only in
/// the first of those.
/// </summary>
public sealed class MonsterAttackType(byte type, byte description, byte dice, byte sides)
{
    /// <summary>What the attack does. Mirrors attack_type.</summary>
    public byte Type { get; } = type;

    /// <summary>What the attack looks like. Mirrors attack_desc.</summary>
    public byte Description { get; } = description;

    public byte Dice { get; } = dice;

    public byte Sides { get; } = sides;
}
