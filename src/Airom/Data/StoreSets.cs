// Ported from the store predicates in Umoria 5.6 source/sets.c and the
// store_buy dispatch table in source/tables.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Data;

/// <summary>
/// Which of the six town stores will buy a given category of item.
///
/// In the C these are six functions gathered into a store_buy[] array of
/// function pointers, indexed by store number. The indices are load-bearing -
/// they line up with the rows of <see cref="GameTables.StoreChoice"/> and with
/// the owner groups in <see cref="GameTables.Owners"/> - so they are named here
/// rather than left as bare numbers.
/// </summary>
public static class StoreSets
{
    public const int GeneralStore = 0;
    public const int Armory = 1;
    public const int Weaponsmith = 2;
    public const int Temple = 3;
    public const int Alchemist = 4;
    public const int MagicShop = 5;

    /// <summary>Number of stores; Umoria's MAX_STORES.</summary>
    public const int StoreCount = 6;

    /// <summary>
    /// Dispatches to the right store predicate. Mirrors indexing Umoria's
    /// store_buy[] table.
    /// </summary>
    public static bool Buys(int storeIndex, byte tval) => storeIndex switch
    {
        GeneralStore => GeneralStoreBuys(tval),
        Armory => ArmoryBuys(tval),
        Weaponsmith => WeaponsmithBuys(tval),
        Temple => TempleBuys(tval),
        Alchemist => AlchemistBuys(tval),
        MagicShop => MagicShopBuys(tval),
        _ => false,
    };

    /// <summary>Mirrors general_store(): supplies and light sources.</summary>
    public static bool GeneralStoreBuys(byte tval) => tval switch
    {
        ItemCategory.Digging
            or ItemCategory.Boots
            or ItemCategory.Cloak
            or ItemCategory.Food
            or ItemCategory.Flask
            or ItemCategory.Light
            or ItemCategory.Spike => true,
        _ => false,
    };

    /// <summary>Mirrors armory(): worn protection only, no weapons.</summary>
    public static bool ArmoryBuys(byte tval) => tval switch
    {
        ItemCategory.Boots
            or ItemCategory.Gloves
            or ItemCategory.Helm
            or ItemCategory.Shield
            or ItemCategory.HardArmor
            or ItemCategory.SoftArmor => true,
        _ => false,
    };

    /// <summary>Mirrors weaponsmith(): weapons and ammunition.</summary>
    public static bool WeaponsmithBuys(byte tval) => tval switch
    {
        ItemCategory.SlingAmmo
            or ItemCategory.Bolt
            or ItemCategory.Arrow
            or ItemCategory.Bow
            or ItemCategory.Hafted
            or ItemCategory.Polearm
            or ItemCategory.Sword => true,
        _ => false,
    };

    /// <summary>
    /// Mirrors temple(). Takes hafted weapons but no edged ones - the priestly
    /// blunt-weapon convention - along with prayer books and consumables.
    /// </summary>
    public static bool TempleBuys(byte tval) => tval switch
    {
        ItemCategory.Hafted
            or ItemCategory.Scroll1
            or ItemCategory.Scroll2
            or ItemCategory.Potion1
            or ItemCategory.Potion2
            or ItemCategory.PrayerBook => true,
        _ => false,
    };

    /// <summary>Mirrors alchemist(): scrolls and potions only.</summary>
    public static bool AlchemistBuys(byte tval) => tval switch
    {
        ItemCategory.Scroll1
            or ItemCategory.Scroll2
            or ItemCategory.Potion1
            or ItemCategory.Potion2 => true,
        _ => false,
    };

    /// <summary>Mirrors magic_shop(): enchanted goods and magic books.</summary>
    public static bool MagicShopBuys(byte tval) => tval switch
    {
        ItemCategory.Amulet
            or ItemCategory.Ring
            or ItemCategory.Staff
            or ItemCategory.Wand
            or ItemCategory.Scroll1
            or ItemCategory.Scroll2
            or ItemCategory.Potion1
            or ItemCategory.Potion2
            or ItemCategory.MagicBook => true,
        _ => false,
    };
}
