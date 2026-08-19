// The TR_* item flag constants from Umoria 5.6 source/constant.h.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Data;

/// <summary>
/// Ability bits carried by an object (Umoria's TR_* flags).
///
/// Not a [Flags] enum, because the low six bits are also read as a group -
/// <see cref="Stats"/> masks the six stat bonuses so the code can ask "does
/// this modify any stat" in one test - and <see cref="EgoWeapon"/> does the
/// same for the slay bits. Named masks over a plain uint keep that usable.
/// </summary>
public static class ItemFlags
{
    /// <summary>The six stat bonuses as a group; these must stay the low six bits.</summary>
    public const uint Stats = 0x0000003F;

    public const uint Strength = 0x00000001;
    public const uint Intelligence = 0x00000002;
    public const uint Wisdom = 0x00000004;
    public const uint Dexterity = 0x00000008;
    public const uint Constitution = 0x00000010;
    public const uint Charisma = 0x00000020;
    public const uint Search = 0x00000040;
    public const uint SlowDigest = 0x00000080;
    public const uint Stealth = 0x00000100;
    public const uint Aggravate = 0x00000200;
    public const uint Teleport = 0x00000400;
    public const uint Regenerate = 0x00000800;
    public const uint Speed = 0x00001000;

    /// <summary>All the slay and brand bits as a group.</summary>
    public const uint EgoWeapon = 0x0007E000;

    public const uint SlayDragon = 0x00002000;
    public const uint SlayAnimal = 0x00004000;
    public const uint SlayEvil = 0x00008000;
    public const uint SlayUndead = 0x00010000;
    public const uint FrostBrand = 0x00020000;
    public const uint FlameTongue = 0x00040000;

    public const uint ResistFire = 0x00080000;
    public const uint ResistAcid = 0x00100000;
    public const uint ResistCold = 0x00200000;
    public const uint SustainStat = 0x00400000;
    public const uint FreeAction = 0x00800000;
    public const uint SeeInvisible = 0x01000000;
    public const uint ResistLight = 0x02000000;
    public const uint FeatherFall = 0x04000000;
    public const uint Blind = 0x08000000;
    public const uint Timid = 0x10000000;
    public const uint Tunnel = 0x20000000;
    public const uint Infravision = 0x40000000;
    public const uint Cursed = 0x80000000;
}
