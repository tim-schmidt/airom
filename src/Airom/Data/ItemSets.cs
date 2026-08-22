// Ported from Umoria 5.6 source/sets.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

namespace Airom.Data;

/// <summary>
/// The attributes the sets.c predicates read. Both the object table rows and,
/// later, a carried item satisfy this, which is why the predicates take an
/// interface rather than a concrete type - the C ones were split across
/// treasure_type and inven_type for the same reason.
/// </summary>
public interface IItemAttributes
{
    /// <summary>Category number (one of the ItemCategory values).</summary>
    byte TVal { get; }

    /// <summary>Ability bits (see <see cref="ItemFlags"/>).</summary>
    uint Flags { get; }

    /// <summary>Weight in tenths of a pound.</summary>
    ushort Weight { get; }
}

/// <summary>
/// Which items are affected by which element. Mirrors the set_* predicates in
/// Umoria's sets.c, which are passed around as function pointers by the
/// damage-handling code.
///
/// The overlaps are deliberate and asymmetric in ways worth not "tidying":
/// arrows burn but bolts do not, while acid affects both; fire destroys
/// potions but acid does not.
/// </summary>
public static class ItemSets
{
    private static bool Resists(IItemAttributes item, uint flag) => (item.Flags & flag) != 0;

    /// <summary>Metal gear that acid pits without destroying. Mirrors set_corrodes().</summary>
    public static bool Corrodes(IItemAttributes item) => item.TVal switch
    {
        ItemCategory.Sword
            or ItemCategory.Helm
            or ItemCategory.Shield
            or ItemCategory.HardArmor
            or ItemCategory.Wand => true,
        _ => false,
    };

    /// <summary>Mirrors set_flammable(). Items of (RF) survive.</summary>
    public static bool IsFlammable(IItemAttributes item) => item.TVal switch
    {
        ItemCategory.Arrow
            or ItemCategory.Bow
            or ItemCategory.Hafted
            or ItemCategory.Polearm
            or ItemCategory.Boots
            or ItemCategory.Gloves
            or ItemCategory.Cloak
            or ItemCategory.SoftArmor => !Resists(item, ItemFlags.ResistFire),

        ItemCategory.Staff
            or ItemCategory.Scroll1
            or ItemCategory.Scroll2 => true,

        _ => false,
    };

    /// <summary>Mirrors set_frost_destroy(): liquids shatter.</summary>
    public static bool DestroyedByFrost(IItemAttributes item) => item.TVal switch
    {
        ItemCategory.Potion1 or ItemCategory.Potion2 or ItemCategory.Flask => true,
        _ => false,
    };

    /// <summary>Mirrors set_acid_affect(). Note this includes bolts, which fire does not.</summary>
    public static bool AffectedByAcid(IItemAttributes item) => item.TVal switch
    {
        ItemCategory.Misc or ItemCategory.Chest => true,

        ItemCategory.Bolt
            or ItemCategory.Arrow
            or ItemCategory.Bow
            or ItemCategory.Hafted
            or ItemCategory.Polearm
            or ItemCategory.Boots
            or ItemCategory.Gloves
            or ItemCategory.Cloak
            or ItemCategory.SoftArmor => !Resists(item, ItemFlags.ResistAcid),

        _ => false,
    };

    /// <summary>Mirrors set_lightning_destroy().</summary>
    public static bool DestroyedByLightning(IItemAttributes item) => item.TVal switch
    {
        ItemCategory.Ring or ItemCategory.Wand or ItemCategory.Spike => true,
        _ => false,
    };

    /// <summary>
    /// Mirrors set_null(), which Umoria passes where a damage type destroys
    /// nothing. Kept rather than inlined so the dispatch sites read the same.
    /// </summary>
    public static bool Never(IItemAttributes item) => false;

    /// <summary>Mirrors set_acid_destroy(). Wider than <see cref="AffectedByAcid"/>.</summary>
    public static bool DestroyedByAcid(IItemAttributes item) => item.TVal switch
    {
        ItemCategory.Arrow
            or ItemCategory.Bow
            or ItemCategory.Hafted
            or ItemCategory.Polearm
            or ItemCategory.Boots
            or ItemCategory.Gloves
            or ItemCategory.Cloak
            or ItemCategory.Helm
            or ItemCategory.Shield
            or ItemCategory.HardArmor
            or ItemCategory.SoftArmor => !Resists(item, ItemFlags.ResistAcid),

        ItemCategory.Staff
            or ItemCategory.Scroll1
            or ItemCategory.Scroll2
            or ItemCategory.Food
            or ItemCategory.OpenDoor
            or ItemCategory.ClosedDoor => true,

        _ => false,
    };

    /// <summary>Mirrors set_fire_destroy().</summary>
    public static bool DestroyedByFire(IItemAttributes item) => item.TVal switch
    {
        ItemCategory.Arrow
            or ItemCategory.Bow
            or ItemCategory.Hafted
            or ItemCategory.Polearm
            or ItemCategory.Boots
            or ItemCategory.Gloves
            or ItemCategory.Cloak
            or ItemCategory.SoftArmor => !Resists(item, ItemFlags.ResistFire),

        ItemCategory.Staff
            or ItemCategory.Scroll1
            or ItemCategory.Scroll2
            or ItemCategory.Potion1
            or ItemCategory.Potion2
            or ItemCategory.Flask
            or ItemCategory.Food
            or ItemCategory.OpenDoor
            or ItemCategory.ClosedDoor => true,

        _ => false,
    };

    /// <summary>
    /// Items too bulky to be found inside a chest. Mirrors set_large(). Weapons
    /// qualify only above 15 pounds, so a dagger fits and a two-handed sword
    /// does not.
    /// </summary>
    public static bool IsTooLargeForChest(IItemAttributes item) => item.TVal switch
    {
        ItemCategory.Chest
            or ItemCategory.Bow
            or ItemCategory.Polearm
            or ItemCategory.HardArmor
            or ItemCategory.SoftArmor
            or ItemCategory.Staff => true,

        ItemCategory.Hafted
            or ItemCategory.Sword
            or ItemCategory.Digging => item.Weight > 150,

        _ => false,
    };
}
