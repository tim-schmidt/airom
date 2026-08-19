// The TV_* category constants from Umoria 5.6 source/constant.h.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Data;

/// <summary>
/// Object category numbers (Umoria's tval).
///
/// These are deliberately not an enum. The values are ordered, and the game
/// tests ranges against them - "wearable" is anything between
/// <see cref="MinWear"/> and <see cref="MaxWear"/>, "enchantable" likewise -
/// so the boundary markers are as load-bearing as the categories themselves.
/// Turning them into an enum would invite treating them as opaque labels.
/// </summary>
public static class ItemCategory
{
    /// <summary>Sentinel used by find_range() to mean "do not search".</summary>
    public const byte Never = unchecked((byte)-1);

    public const byte Nothing = 0;
    public const byte Misc = 1;
    public const byte Chest = 2;

    /// <summary>Lowest category that can be worn or wielded.</summary>
    public const byte MinWear = 10;

    /// <summary>Lowest category that accepts an enchantment.</summary>
    public const byte MinEnchant = 10;

    public const byte SlingAmmo = 10;
    public const byte Bolt = 11;
    public const byte Arrow = 12;
    public const byte Spike = 13;
    public const byte Light = 15;
    public const byte Bow = 20;
    public const byte Hafted = 21;
    public const byte Polearm = 22;
    public const byte Sword = 23;
    public const byte Digging = 25;
    public const byte Boots = 30;
    public const byte Gloves = 31;
    public const byte Cloak = 32;
    public const byte Helm = 33;
    public const byte Shield = 34;
    public const byte HardArmor = 35;
    public const byte SoftArmor = 36;

    /// <summary>Highest category that accepts an enchantment.</summary>
    public const byte MaxEnchant = 39;

    public const byte Amulet = 40;
    public const byte Ring = 45;

    /// <summary>Highest category that can be worn or wielded.</summary>
    public const byte MaxWear = 50;

    public const byte Staff = 55;
    public const byte Wand = 65;
    public const byte Scroll1 = 70;
    public const byte Scroll2 = 71;
    public const byte Potion1 = 75;
    public const byte Potion2 = 76;
    public const byte Flask = 77;
    public const byte Food = 80;
    public const byte MagicBook = 90;
    public const byte PrayerBook = 91;

    /// <summary>Highest category that is a carryable object rather than terrain.</summary>
    public const byte MaxObject = 99;

    public const byte Gold = 100;

    /// <summary>Highest category the player can pick up.</summary>
    public const byte MaxPickUp = 100;

    public const byte InvisibleTrap = 101;

    /// <summary>Lowest category drawn on the map as a feature.</summary>
    public const byte MinVisible = 102;

    public const byte VisibleTrap = 102;
    public const byte Rubble = 103;

    /// <summary>Lowest category that is a door of some kind.</summary>
    public const byte MinDoors = 104;

    public const byte OpenDoor = 104;
    public const byte ClosedDoor = 105;
    public const byte UpStair = 107;
    public const byte DownStair = 108;
    public const byte SecretDoor = 109;
    public const byte StoreDoor = 110;

    /// <summary>Highest category drawn on the map as a feature.</summary>
    public const byte MaxVisible = 110;
}
