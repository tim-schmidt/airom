// The CM_*, CS_* and CD_* creature flag constants from Umoria 5.6
// source/constant.h.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Data;

/// <summary>
/// Movement, behaviour and treasure bits packed into
/// <see cref="CreatureType.MoveFlags"/> (Umoria's cmove).
///
/// Two hazards live in here.
///
/// First, there are two unrelated groups of "RANDOM" flags. Bits 3-5
/// (<see cref="RandomMove"/>) are how erratically the monster walks. Bits 26-27
/// (<see cref="Drop60Percent"/>, <see cref="Drop90Percent"/>) are chances to
/// drop an extra item, read only by the death routine. The C names them
/// CM_20/40/75_RANDOM and CM_60/90_RANDOM, which reads as one family and is not.
///
/// Second, <see cref="Treasure"/> is a five-bit *field* covering bits 26-30,
/// deliberately overlapping the five individual drop flags. The death routine
/// reads the flags one at a time; the monster-memory code reads the whole group
/// as a number via <see cref="TreasureShift"/> so it can remember the largest
/// haul a player has seen this monster drop.
/// </summary>
public static class CreatureMove
{
    /// <summary>Mask over the movement-style bits.</summary>
    public const uint AllMoveFlags = 0x0000003F;

    /// <summary>Never moves; strikes only what is adjacent.</summary>
    public const uint AttackOnly = 0x00000001;

    public const uint MoveNormal = 0x00000002;

    /// <summary>For Quylthulgs, which have no physical movement.</summary>
    public const uint OnlyMagic = 0x00000004;

    /// <summary>Mask over the erratic-movement bits. Unrelated to the drop bits.</summary>
    public const uint RandomMove = 0x00000038;

    public const uint Move20PercentRandom = 0x00000008;
    public const uint Move40PercentRandom = 0x00000010;
    public const uint Move75PercentRandom = 0x00000020;

    /// <summary>Mask over the special-ability bits.</summary>
    public const uint Special = 0x003F0000;

    public const uint Invisible = 0x00010000;
    public const uint OpensDoors = 0x00020000;

    /// <summary>Passes through walls.</summary>
    public const uint Phase = 0x00040000;

    public const uint EatsOtherMonsters = 0x00080000;
    public const uint PicksUpObjects = 0x00100000;

    /// <summary>Breeds explosively when left alone.</summary>
    public const uint Multiplies = 0x00200000;

    /// <summary>Drops small objects rather than the usual range.</summary>
    public const uint SmallObject = 0x00800000;

    public const uint CarriesObject = 0x01000000;
    public const uint CarriesGold = 0x02000000;

    /// <summary>
    /// Mask over bits 26-30, read as a whole number for monster memory. Overlaps
    /// every flag from <see cref="Drop60Percent"/> to <see cref="Drop4d2Objects"/>.
    /// </summary>
    public const uint Treasure = 0x7C000000;

    /// <summary>Right shift that turns <see cref="Treasure"/> into a number.</summary>
    public const int TreasureShift = 26;

    /// <summary>60% chance of one extra dropped item. A drop flag, not a movement flag.</summary>
    public const uint Drop60Percent = 0x04000000;

    /// <summary>90% chance of one extra dropped item. A drop flag, not a movement flag.</summary>
    public const uint Drop90Percent = 0x08000000;

    public const uint Drop1d2Objects = 0x10000000;
    public const uint Drop2d2Objects = 0x20000000;
    public const uint Drop4d2Objects = 0x40000000;

    /// <summary>Killing this monster wins the game. Only the Balrog has it.</summary>
    public const uint Win = 0x80000000;
}

/// <summary>
/// Spell and breath bits packed into <see cref="CreatureType.SpellFlags"/>
/// (Umoria's spells).
///
/// The low nibble is not a flag. <see cref="Frequency"/> holds a small integer:
/// the monster attempts a spell on roughly one turn in that many, and a value of
/// zero means it never casts. constant.h notes the consequence - a monster with
/// breath bits set but no frequency is not a breather at all; those bits are
/// then read as which elements it *resists*, which is how spells.c uses them
/// when working out damage from the player's elemental attacks.
/// </summary>
public static class CreatureSpell
{
    /// <summary>
    /// Low nibble, holding a 1-in-N spell frequency rather than a flag. Zero
    /// means the monster never casts, which changes how the breath bits read.
    /// </summary>
    public const uint Frequency = 0x0000000F;

    /// <summary>Mask over the castable-spell bits.</summary>
    public const uint Spells = 0x0001FFF0;

    public const uint TeleportShort = 0x00000010;
    public const uint TeleportLong = 0x00000020;

    /// <summary>Teleports the player to the monster.</summary>
    public const uint TeleportTo = 0x00000040;

    public const uint CauseLightWounds = 0x00000080;
    public const uint CauseSeriousWounds = 0x00000100;
    public const uint HoldPerson = 0x00000200;
    public const uint Blind = 0x00000400;
    public const uint Confuse = 0x00000800;
    public const uint Fear = 0x00001000;
    public const uint SummonMonster = 0x00002000;
    public const uint SummonUndead = 0x00004000;
    public const uint SlowPerson = 0x00008000;
    public const uint DrainMana = 0x00010000;

    /// <summary>
    /// Mask over the breath bits. With no <see cref="Frequency"/> set these
    /// indicate resistance instead of a breath attack.
    /// </summary>
    public const uint Breathe = 0x00F80000;

    public const uint BreatheLightning = 0x00080000;
    public const uint BreatheGas = 0x00100000;
    public const uint BreatheAcid = 0x00200000;
    public const uint BreatheFrost = 0x00400000;
    public const uint BreatheFire = 0x00800000;
}

/// <summary>
/// Kind, vulnerability and toughness bits packed into
/// <see cref="CreatureType.DefenseFlags"/> (Umoria's cdefense).
///
/// The first four say what the monster *is*, which is what slay weapons test
/// against. The <see cref="Weakness"/> group says what it takes extra damage
/// from - so a bit being set here is a liability for the monster, the opposite
/// sense to the resistance bits in <see cref="CreatureSpell"/>.
/// </summary>
public static class CreatureDefense
{
    public const ushort Dragon = 0x0001;
    public const ushort Animal = 0x0002;
    public const ushort Evil = 0x0004;
    public const ushort Undead = 0x0008;

    /// <summary>Mask over the six extra-damage vulnerabilities.</summary>
    public const ushort Weakness = 0x03F0;

    public const ushort HurtByFrost = 0x0010;
    public const ushort HurtByFire = 0x0020;
    public const ushort HurtByPoison = 0x0040;
    public const ushort HurtByAcid = 0x0080;
    public const ushort HurtByLight = 0x0100;

    /// <summary>Takes extra damage from stone-to-mud.</summary>
    public const ushort HurtByStone = 0x0200;

    /// <summary>Never sleeps; always aware of the player.</summary>
    public const ushort NeverSleeps = 0x1000;

    /// <summary>Visible to infravision.</summary>
    public const ushort Infravision = 0x2000;

    /// <summary>Always rolls maximum hit points instead of dice.</summary>
    public const ushort MaxHitPoints = 0x4000;
}
