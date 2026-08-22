// The SN_*, CH_* and ID_* constants from Umoria 5.6 source/constant.h.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Data;

/// <summary>
/// The suffix an enchanted item carries - "of Slay Evil", "of Stupidity".
/// Mirrors Umoria's SN_* values, stored in an item's name2 field.
///
/// These are indices into a parallel table of names, so the numbers themselves
/// are the identity and cannot be reordered.
/// </summary>
public static class SpecialName
{
    public const byte None = 0;

    /// <summary>Resistance to everything: the "(R)" suffix.</summary>
    public const byte Resistance = 1;

    public const byte ResistAcid = 2;
    public const byte ResistFire = 3;
    public const byte ResistCold = 4;
    public const byte ResistLightning = 5;

    public const byte HolyAvenger = 6;
    public const byte Defender = 7;
    public const byte SlayAnimal = 8;
    public const byte SlayDragon = 9;
    public const byte SlayEvil = 10;
    public const byte SlayUndead = 11;
    public const byte FlameTongue = 12;
    public const byte FrostBrand = 13;

    public const byte FreeAction = 14;
    public const byte Slaying = 15;
    public const byte Clumsiness = 16;
    public const byte Weakness = 17;
    public const byte SlowDescent = 18;
    public const byte Speed = 19;
    public const byte Stealth = 20;
    public const byte Slowness = 21;
    public const byte Noise = 22;
    public const byte GreatMass = 23;
    public const byte Intelligence = 24;
    public const byte Wisdom = 25;
    public const byte Infravision = 26;
    public const byte Might = 27;
    public const byte Lordliness = 28;
    public const byte Magi = 29;
    public const byte Beauty = 30;
    public const byte Seeing = 31;
    public const byte Regeneration = 32;
    public const byte Stupidity = 33;
    public const byte Dullness = 34;
    public const byte Blindness = 35;
    public const byte Timidness = 36;
    public const byte Teleportation = 37;
    public const byte Ugliness = 38;
    public const byte Protection = 39;
    public const byte Irritation = 40;
    public const byte Vulnerability = 41;
    public const byte Enveloping = 42;
    public const byte Fire = 43;
    public const byte SlayEvilMissile = 44;
    public const byte DragonSlaying = 45;
    public const byte Empty = 46;
    public const byte Locked = 47;
    public const byte PoisonNeedle = 48;
    public const byte GasTrap = 49;
    public const byte ExplosionDevice = 50;
    public const byte SummoningRunes = 51;
    public const byte MultipleTraps = 52;
    public const byte Disarmed = 53;
    public const byte Unlocked = 54;
    public const byte SlayAnimalMissile = 55;

    /// <summary>How many names exist. Umoria's SN_ARRAY_SIZE.</summary>
    public const byte Count = 56;
}

/// <summary>
/// What a chest is protected by, stored in its flags. Mirrors Umoria's CH_*.
/// </summary>
public static class ChestFlags
{
    public const uint Locked = 0x00000001;

    /// <summary>Mask over every trap kind, for asking "is this trapped at all".</summary>
    public const uint Trapped = 0x000001F0;

    public const uint LoseStrength = 0x00000010;
    public const uint Poison = 0x00000020;
    public const uint Paralyse = 0x00000040;
    public const uint Explode = 0x00000080;
    public const uint Summon = 0x00000100;
}

/// <summary>
/// What the player has learned about an item, and how it should be displayed.
/// Mirrors Umoria's ID_*.
/// </summary>
public static class Identification
{
    /// <summary>Known to be enchanted.</summary>
    public const byte Magik = 0x01;

    /// <summary>Known to be cursed.</summary>
    public const byte Damned = 0x02;

    /// <summary>A wand or staff known to be out of charges.</summary>
    public const byte Empty = 0x04;

    /// <summary>Fully identified.</summary>
    public const byte Known = 0x08;

    /// <summary>Bought from a store, so its nature was never in doubt.</summary>
    public const byte StoreBought = 0x10;

    /// <summary>Show the to-hit and to-damage bonuses once identified.</summary>
    public const byte ShowHitDam = 0x20;

    /// <summary>Suppress the p1 value even though the item has one.</summary>
    public const byte NoShowP1 = 0x40;

    /// <summary>Show the p1 value.</summary>
    public const byte ShowP1 = 0x80;
}
