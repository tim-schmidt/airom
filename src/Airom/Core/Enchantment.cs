// Ported from magic_treasure() in Umoria 5.6 source/misc2.c, with magik() and
// m_bonus() from source/misc1.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Turns a freshly stamped item into a specific one: enchanted, cursed, charged
/// or merely counted. Mirrors magic_treasure().
///
/// This is the largest single function in Umoria - 994 lines - and almost all of
/// it is a switch on the item's category. The shape is worth preserving rather
/// than refactoring into per-category classes: every branch draws from the same
/// generator in a fixed order, and moving a draw changes every item generated
/// afterwards.
///
/// Three numbers steer the whole thing, computed once from the depth:
/// <c>chance</c> that anything happens at all, <c>special</c> that it is
/// something notable, and <c>cursed</c> that it went the other way. All three
/// climb with depth and then stop, so a deep level is not simply a better
/// version of a shallow one - it is a more extreme one.
/// </summary>
public sealed class Enchantment(GameState game)
{
    private const int BaseMagic = 15;    // OBJ_BASE_MAGIC
    private const int MaxMagic = 70;     // OBJ_BASE_MAX
    private const int SpecialDivisor = 6;  // OBJ_DIV_SPECIAL
    private const int CursedDivisor = 13;  // OBJ_DIV_CURSED

    private const int StandardDeviationAdjust = 125; // OBJ_STD_ADJ
    private const int MinimumDeviation = 7;          // OBJ_STD_MIN

    private const int MaxShort = 32767;

    private readonly GameState _game = game;

    private Rng Rng => _game.Rng;

    /// <summary>
    /// A percentage roll. Mirrors magik(). Note it is inclusive, so a chance of
    /// 100 always succeeds and a chance of 0 never does.
    /// </summary>
    private bool Magik(int chance) => Rng.RandInt(100) <= chance;

    /// <summary>
    /// Rolls an enchantment bonus. Mirrors m_bonus().
    ///
    /// The spread widens with depth up to a per-item ceiling, and the result is
    /// the magnitude of a normal deviate divided by ten. So most bonuses sit
    /// near the base and large ones are rare at any depth, but become less rare
    /// as the player descends.
    /// </summary>
    private int Bonus(int baseValue, int maxStandard, int level)
    {
        int deviation = (StandardDeviationAdjust * level / 100) + MinimumDeviation;

        // The level check guards the same overflow the original notes: a large
        // level can push the product past the ceiling on its own.
        if (deviation > maxStandard || level > maxStandard)
        {
            deviation = maxStandard;
        }

        int rolled = (Math.Abs(Rng.RandNor(0, deviation)) / 10) + baseValue;
        return rolled < baseValue ? baseValue : rolled;
    }

    /// <summary>
    /// Applies the magic appropriate to an item's category and the depth it was
    /// found at.
    /// </summary>
    public void Apply(InvenType item, int level)
    {
        ArgumentNullException.ThrowIfNull(item);

        int chance = Math.Min(BaseMagic + level, MaxMagic);
        int special = chance / SpecialDivisor;
        int cursed = 10 * chance / CursedDivisor;

        switch (item.TVal)
        {
            case ItemCategory.Shield:
            case ItemCategory.HardArmor:
            case ItemCategory.SoftArmor:
                Armour(item, level, chance, special, cursed);
                break;

            case ItemCategory.Hafted:
            case ItemCategory.Polearm:
            case ItemCategory.Sword:
                Weapon(item, level, chance, special, cursed);
                break;

            case ItemCategory.Bow:
                item.Identification |= Identification.ShowHitDam;
                if (Magik(chance))
                {
                    item.ToHit += (short)Bonus(1, 30, level);
                    item.ToDam += (short)Bonus(1, 20, level);
                }
                else if (Magik(cursed))
                {
                    item.ToHit -= (short)Bonus(1, 50, level);
                    item.ToDam -= (short)Bonus(1, 30, level);
                    item.Flags |= ItemFlags.Cursed;
                    item.Cost = 0;
                }

                break;

            case ItemCategory.Digging:
                item.Identification |= Identification.ShowHitDam;
                if (Magik(chance))
                {
                    if (Rng.RandInt(3) < 3)
                    {
                        item.P1 += (short)Bonus(0, 25, level);
                    }
                    else
                    {
                        item.P1 = (short)-Bonus(1, 30, level);
                        item.Cost = 0;
                        item.Flags |= ItemFlags.Cursed;
                    }
                }

                break;

            case ItemCategory.Gloves:
                Gloves(item, level, chance, special, cursed);
                break;

            case ItemCategory.Boots:
                Boots(item, level, chance, special, cursed);
                break;

            case ItemCategory.Helm:
                Helm(item, level, chance, special, cursed);
                break;

            case ItemCategory.Ring:
                Ring(item, level, cursed);
                break;

            case ItemCategory.Amulet:
                Amulet(item, level, cursed);
                break;

            case ItemCategory.Light:
                // Even sub-values are the store's, odd ones the dungeon's. A
                // dungeon find arrives partly used, and is renumbered to the
                // store's version so the two stack.
                if (item.SubVal % 2 == 1)
                {
                    item.P1 = (short)Rng.RandInt(item.P1);
                    item.SubVal -= 1;
                }

                break;

            case ItemCategory.Wand:
                item.P1 = (short)WandCharges(item.SubVal);
                break;

            case ItemCategory.Staff:
                StaffCharges(item);
                break;

            case ItemCategory.Cloak:
                Cloak(item, level, chance, special, cursed);
                break;

            case ItemCategory.Chest:
                Chest(item, level);
                break;

            case ItemCategory.SlingAmmo:
            case ItemCategory.Spike:
            case ItemCategory.Bolt:
            case ItemCategory.Arrow:
                Missiles(item, level, chance, special, cursed);
                break;

            case ItemCategory.Food:
                // Several rows hold the same food at different levels so it
                // appears more often; they are flattened to one level here so
                // the duplicates behave identically.
                if (item.SubVal == 90)
                {
                    item.Level = 0;
                }
                else if (item.SubVal == 92)
                {
                    item.Level = 6;
                }

                break;

            case ItemCategory.Scroll1:
                item.Level = item.SubVal switch
                {
                    67 => 1,  // identify
                    69 => 0,  // light
                    80 => 5,  // trap detection
                    81 => 5,  // door and stair location
                    _ => item.Level,
                };
                break;

            case ItemCategory.Potion1:
                if (item.SubVal == 76) // cure light wounds
                {
                    item.Level = 0;
                }

                break;

            default:
                break;
        }
    }

    private void Armour(InvenType item, int level, int chance, int special, int cursed)
    {
        if (Magik(chance))
        {
            item.ToAc += (short)Bonus(1, 30, level);
            if (!Magik(special))
            {
                return;
            }

            switch (Rng.RandInt(9))
            {
                case 1:
                    item.Flags |= ItemFlags.ResistLight | ItemFlags.ResistCold
                        | ItemFlags.ResistAcid | ItemFlags.ResistFire;
                    item.SpecialName = SpecialName.Resistance;
                    item.ToAc += 5;
                    item.Cost += 2500;
                    break;
                case 2:
                    item.Flags |= ItemFlags.ResistAcid;
                    item.SpecialName = SpecialName.ResistAcid;
                    item.Cost += 1000;
                    break;
                case 3:
                case 4:
                    item.Flags |= ItemFlags.ResistFire;
                    item.SpecialName = SpecialName.ResistFire;
                    item.Cost += 600;
                    break;
                case 5:
                case 6:
                    item.Flags |= ItemFlags.ResistCold;
                    item.SpecialName = SpecialName.ResistCold;
                    item.Cost += 600;
                    break;
                default: // 7, 8, 9
                    item.Flags |= ItemFlags.ResistLight;
                    item.SpecialName = SpecialName.ResistLightning;
                    item.Cost += 500;
                    break;
            }
        }
        else if (Magik(cursed))
        {
            item.ToAc -= (short)Bonus(1, 40, level);
            item.Cost = 0;
            item.Flags |= ItemFlags.Cursed;
        }
    }

    private void Weapon(InvenType item, int level, int chance, int special, int cursed)
    {
        item.Identification |= Identification.ShowHitDam;

        if (Magik(chance))
        {
            item.ToHit += (short)Bonus(0, 40, level);

            // The damage bonus scales with the weapon's own dice, so a heavy
            // weapon gains more than a dagger from the same roll.
            int dice = item.DamageDice * item.DamageSides;
            item.ToDam += (short)Bonus(0, 4 * dice, dice * level / 10);

            // Weapons became rarer when the treasure distribution changed, so
            // the special chance is inflated to keep ego weapons as common as
            // they were. Missiles carry the same adjustment.
            if (!Magik(3 * special / 2))
            {
                return;
            }

            switch (Rng.RandInt(16))
            {
                case 1: // Holy Avenger
                    item.Flags |= ItemFlags.SeeInvisible | ItemFlags.SustainStat
                        | ItemFlags.SlayUndead | ItemFlags.SlayEvil | ItemFlags.Strength;
                    item.ToHit += 5;
                    item.ToDam += 5;
                    item.ToAc += (short)Rng.RandInt(4);

                    // p1 carries both the strength bonus and the sustain.
                    item.P1 = (short)Rng.RandInt(4);
                    item.SpecialName = SpecialName.HolyAvenger;
                    item.Cost += item.P1 * 500;
                    item.Cost += 10000;
                    break;

                case 2: // Defender
                    item.Flags |= ItemFlags.FeatherFall | ItemFlags.ResistLight
                        | ItemFlags.SeeInvisible | ItemFlags.FreeAction
                        | ItemFlags.ResistCold | ItemFlags.ResistAcid
                        | ItemFlags.ResistFire | ItemFlags.Regenerate | ItemFlags.Stealth;
                    item.ToHit += 3;
                    item.ToDam += 3;
                    item.ToAc += (short)(5 + Rng.RandInt(5));
                    item.SpecialName = SpecialName.Defender;
                    item.P1 = (short)Rng.RandInt(3); // stealth
                    item.Cost += item.P1 * 500;
                    item.Cost += 7500;
                    break;

                case 3:
                case 4:
                    item.Flags |= ItemFlags.SlayAnimal;
                    item.ToHit += 2;
                    item.ToDam += 2;
                    item.SpecialName = SpecialName.SlayAnimal;
                    item.Cost += 3000;
                    break;

                case 5:
                case 6:
                    item.Flags |= ItemFlags.SlayDragon;
                    item.ToHit += 3;
                    item.ToDam += 3;
                    item.SpecialName = SpecialName.SlayDragon;
                    item.Cost += 4000;
                    break;

                case 7:
                case 8:
                    item.Flags |= ItemFlags.SlayEvil;
                    item.ToHit += 3;
                    item.ToDam += 3;
                    item.SpecialName = SpecialName.SlayEvil;
                    item.Cost += 4000;
                    break;

                case 9:
                case 10:
                    item.Flags |= ItemFlags.SeeInvisible | ItemFlags.SlayUndead;
                    item.ToHit += 3;
                    item.ToDam += 3;
                    item.SpecialName = SpecialName.SlayUndead;
                    item.Cost += 5000;
                    break;

                case 11:
                case 12:
                case 13:
                    item.Flags |= ItemFlags.FlameTongue;
                    item.ToHit++;
                    item.ToDam += 3;
                    item.SpecialName = SpecialName.FlameTongue;
                    item.Cost += 2000;
                    break;

                default: // 14, 15, 16
                    item.Flags |= ItemFlags.FrostBrand;
                    item.ToHit++;
                    item.ToDam++;
                    item.SpecialName = SpecialName.FrostBrand;
                    item.Cost += 1200;
                    break;
            }
        }
        else if (Magik(cursed))
        {
            item.ToHit -= (short)Bonus(1, 55, level);
            int dice = item.DamageDice * item.DamageSides;
            item.ToDam -= (short)Bonus(1, 11 * dice / 2, dice * level / 10);
            item.Flags |= ItemFlags.Cursed;
            item.Cost = 0;
        }
    }

    private void Gloves(InvenType item, int level, int chance, int special, int cursed)
    {
        if (Magik(chance))
        {
            item.ToAc += (short)Bonus(1, 20, level);
            if (!Magik(special))
            {
                return;
            }

            if (Rng.RandInt(2) == 1)
            {
                item.Flags |= ItemFlags.FreeAction;
                item.SpecialName = SpecialName.FreeAction;
                item.Cost += 1000;
            }
            else
            {
                item.Identification |= Identification.ShowHitDam;
                item.ToHit += (short)(1 + Rng.RandInt(3));
                item.ToDam += (short)(1 + Rng.RandInt(3));
                item.SpecialName = SpecialName.Slaying;
                item.Cost += (item.ToHit + item.ToDam) * 250;
            }
        }
        else if (Magik(cursed))
        {
            if (Magik(special))
            {
                if (Rng.RandInt(2) == 1)
                {
                    item.Flags |= ItemFlags.Dexterity;
                    item.SpecialName = SpecialName.Clumsiness;
                }
                else
                {
                    item.Flags |= ItemFlags.Strength;
                    item.SpecialName = SpecialName.Weakness;
                }

                item.Identification |= Identification.ShowP1;
                item.P1 = (short)-Bonus(1, 10, level);
            }

            item.ToAc -= (short)Bonus(1, 40, level);
            item.Flags |= ItemFlags.Cursed;
            item.Cost = 0;
        }
    }

    private void Boots(InvenType item, int level, int chance, int special, int cursed)
    {
        if (Magik(chance))
        {
            item.ToAc += (short)Bonus(1, 20, level);
            if (!Magik(special))
            {
                return;
            }

            int roll = Rng.RandInt(12);
            if (roll > 5)
            {
                item.Flags |= ItemFlags.FeatherFall;
                item.SpecialName = SpecialName.SlowDescent;
                item.Cost += 250;
            }
            else if (roll == 1)
            {
                item.Flags |= ItemFlags.Speed;
                item.SpecialName = SpecialName.Speed;
                item.Identification |= Identification.ShowP1;
                item.P1 = 1;
                item.Cost += 5000;
            }
            else // 2 to 5
            {
                item.Flags |= ItemFlags.Stealth;
                item.Identification |= Identification.ShowP1;
                item.P1 = (short)Rng.RandInt(3);
                item.SpecialName = SpecialName.Stealth;
                item.Cost += 500;
            }
        }
        else if (Magik(cursed))
        {
            int roll = Rng.RandInt(3);
            if (roll == 1)
            {
                item.Flags |= ItemFlags.Speed;
                item.SpecialName = SpecialName.Slowness;
                item.Identification |= Identification.ShowP1;
                item.P1 = -1;
            }
            else if (roll == 2)
            {
                item.Flags |= ItemFlags.Aggravate;
                item.SpecialName = SpecialName.Noise;
            }
            else
            {
                item.SpecialName = SpecialName.GreatMass;
                item.Weight = (ushort)(item.Weight * 5);
            }

            item.Cost = 0;
            item.ToAc -= (short)Bonus(2, 45, level);
            item.Flags |= ItemFlags.Cursed;
        }
    }

    private void Helm(InvenType item, int level, int chance, int special, int cursed)
    {
        // Crowns are worth more, and are correspondingly more likely to be
        // enchanted - the chance is lifted by their own price.
        if (item.SubVal is >= 6 and <= 8)
        {
            chance += item.Cost / 100;
            special += special;
        }

        if (Magik(chance))
        {
            item.ToAc += (short)Bonus(1, 20, level);
            if (!Magik(special))
            {
                return;
            }

            if (item.SubVal < 6)
            {
                int roll = Rng.RandInt(3);
                item.Identification |= Identification.ShowP1;
                if (roll == 1)
                {
                    item.P1 = (short)Rng.RandInt(2);
                    item.Flags |= ItemFlags.Intelligence;
                    item.SpecialName = SpecialName.Intelligence;
                    item.Cost += item.P1 * 500;
                }
                else if (roll == 2)
                {
                    item.P1 = (short)Rng.RandInt(2);
                    item.Flags |= ItemFlags.Wisdom;
                    item.SpecialName = SpecialName.Wisdom;
                    item.Cost += item.P1 * 500;
                }
                else
                {
                    item.P1 = (short)(1 + Rng.RandInt(4));
                    item.Flags |= ItemFlags.Infravision;
                    item.SpecialName = SpecialName.Infravision;
                    item.Cost += item.P1 * 250;
                }

                return;
            }

            switch (Rng.RandInt(6))
            {
                case 1:
                    item.Identification |= Identification.ShowP1;
                    item.P1 = (short)Rng.RandInt(3);
                    item.Flags |= ItemFlags.FreeAction | ItemFlags.Constitution
                        | ItemFlags.Dexterity | ItemFlags.Strength;
                    item.SpecialName = SpecialName.Might;
                    item.Cost += 1000 + (item.P1 * 500);
                    break;
                case 2:
                    item.Identification |= Identification.ShowP1;
                    item.P1 = (short)Rng.RandInt(3);
                    item.Flags |= ItemFlags.Charisma | ItemFlags.Wisdom;
                    item.SpecialName = SpecialName.Lordliness;
                    item.Cost += 1000 + (item.P1 * 500);
                    break;
                case 3:
                    item.Identification |= Identification.ShowP1;
                    item.P1 = (short)Rng.RandInt(3);
                    item.Flags |= ItemFlags.ResistLight | ItemFlags.ResistCold
                        | ItemFlags.ResistAcid | ItemFlags.ResistFire
                        | ItemFlags.Intelligence;
                    item.SpecialName = SpecialName.Magi;
                    item.Cost += 3000 + (item.P1 * 500);
                    break;
                case 4:
                    item.Identification |= Identification.ShowP1;
                    item.P1 = (short)Rng.RandInt(3);
                    item.Flags |= ItemFlags.Charisma;
                    item.SpecialName = SpecialName.Beauty;
                    item.Cost += 750;
                    break;
                case 5:
                    item.Identification |= Identification.ShowP1;
                    item.P1 = (short)(5 * (1 + Rng.RandInt(4)));
                    item.Flags |= ItemFlags.SeeInvisible | ItemFlags.Search;
                    item.SpecialName = SpecialName.Seeing;
                    item.Cost += 1000 + (item.P1 * 100);
                    break;
                default: // 6
                    item.Flags |= ItemFlags.Regenerate;
                    item.SpecialName = SpecialName.Regeneration;
                    item.Cost += 1500;
                    break;
            }
        }
        else if (Magik(cursed))
        {
            item.ToAc -= (short)Bonus(1, 45, level);
            item.Flags |= ItemFlags.Cursed;
            item.Cost = 0;

            if (!Magik(special))
            {
                return;
            }

            switch (Rng.RandInt(7))
            {
                case 1:
                    item.Identification |= Identification.ShowP1;
                    item.P1 = (short)-Rng.RandInt(5);
                    item.Flags |= ItemFlags.Intelligence;
                    item.SpecialName = SpecialName.Stupidity;
                    break;
                case 2:
                    item.Identification |= Identification.ShowP1;
                    item.P1 = (short)-Rng.RandInt(5);
                    item.Flags |= ItemFlags.Wisdom;
                    item.SpecialName = SpecialName.Dullness;
                    break;
                case 3:
                    item.Flags |= ItemFlags.Blind;
                    item.SpecialName = SpecialName.Blindness;
                    break;
                case 4:
                    item.Flags |= ItemFlags.Timid;
                    item.SpecialName = SpecialName.Timidness;
                    break;
                case 5:
                    item.Identification |= Identification.ShowP1;
                    item.P1 = (short)-Rng.RandInt(5);
                    item.Flags |= ItemFlags.Strength;
                    item.SpecialName = SpecialName.Weakness;
                    break;
                case 6:
                    item.Flags |= ItemFlags.Teleport;
                    item.SpecialName = SpecialName.Teleportation;
                    break;
                default: // 7
                    item.Identification |= Identification.ShowP1;
                    item.P1 = (short)-Rng.RandInt(5);
                    item.Flags |= ItemFlags.Charisma;
                    item.SpecialName = SpecialName.Ugliness;
                    break;
            }
        }
    }

    /// <summary>
    /// Rings are keyed entirely by sub-value: which ring it is decides what
    /// enchanting it means. A cursed ring inverts its own bonus and its price,
    /// which is how a negative cost marks something worthless but not free.
    /// </summary>
    private void Ring(InvenType item, int level, int cursed)
    {
        switch (item.SubVal)
        {
            case 0:
            case 1:
            case 2:
            case 3:
                if (Magik(cursed))
                {
                    item.P1 = (short)-Bonus(1, 20, level);
                    item.Flags |= ItemFlags.Cursed;
                    item.Cost = -item.Cost;
                }
                else
                {
                    item.P1 = (short)Bonus(1, 10, level);
                    item.Cost += item.P1 * 100;
                }

                break;

            case 4:
                if (Magik(cursed))
                {
                    item.P1 = (short)-Rng.RandInt(3);
                    item.Flags |= ItemFlags.Cursed;
                    item.Cost = -item.Cost;
                }
                else
                {
                    item.P1 = 1;
                }

                break;

            case 5:
                item.P1 = (short)(5 * Bonus(1, 20, level));
                item.Cost += item.P1 * 50;
                if (Magik(cursed))
                {
                    item.P1 = (short)-item.P1;
                    item.Flags |= ItemFlags.Cursed;
                    item.Cost = -item.Cost;
                }

                break;

            case 19: // increase damage
                item.ToDam += (short)Bonus(1, 20, level);
                item.Cost += item.ToDam * 100;
                if (Magik(cursed))
                {
                    item.ToDam = (short)-item.ToDam;
                    item.Flags |= ItemFlags.Cursed;
                    item.Cost = -item.Cost;
                }

                break;

            case 20: // increase to-hit
                item.ToHit += (short)Bonus(1, 20, level);
                item.Cost += item.ToHit * 100;
                if (Magik(cursed))
                {
                    item.ToHit = (short)-item.ToHit;
                    item.Flags |= ItemFlags.Cursed;
                    item.Cost = -item.Cost;
                }

                break;

            case 21: // protection
                item.ToAc += (short)Bonus(1, 20, level);
                item.Cost += item.ToAc * 100;
                if (Magik(cursed))
                {
                    item.ToAc = (short)-item.ToAc;
                    item.Flags |= ItemFlags.Cursed;
                    item.Cost = -item.Cost;
                }

                break;

            case 24:
            case 25:
            case 26:
            case 27:
            case 28:
            case 29:
                item.Identification |= Identification.NoShowP1;
                break;

            case 30: // slaying
                item.Identification |= Identification.ShowHitDam;
                item.ToDam += (short)Bonus(1, 25, level);
                item.ToHit += (short)Bonus(1, 25, level);
                item.Cost += (item.ToHit + item.ToDam) * 100;
                if (Magik(cursed))
                {
                    item.ToHit = (short)-item.ToHit;
                    item.ToDam = (short)-item.ToDam;
                    item.Flags |= ItemFlags.Cursed;
                    item.Cost = -item.Cost;
                }

                break;

            default:
                break;
        }
    }

    private void Amulet(InvenType item, int level, int cursed)
    {
        if (item.SubVal < 2)
        {
            if (Magik(cursed))
            {
                item.P1 = (short)-Bonus(1, 20, level);
                item.Flags |= ItemFlags.Cursed;
                item.Cost = -item.Cost;
            }
            else
            {
                item.P1 = (short)Bonus(1, 10, level);
                item.Cost += item.P1 * 100;
            }
        }
        else if (item.SubVal == 2)
        {
            item.P1 = (short)(5 * Bonus(1, 25, level));
            if (Magik(cursed))
            {
                item.P1 = (short)-item.P1;
                item.Cost = -item.Cost;
                item.Flags |= ItemFlags.Cursed;
            }
            else
            {
                item.Cost += 50 * item.P1;
            }
        }
        else if (item.SubVal == 8)
        {
            // The amulet of the magi is never cursed.
            item.P1 = (short)(5 * Bonus(1, 25, level));
            item.Cost += 20 * item.P1;
        }
    }

    /// <summary>Charges for a wand, by which wand it is.</summary>
    private int WandCharges(byte subVal) => subVal switch
    {
        0 => Rng.RandInt(10) + 6,
        1 => Rng.RandInt(8) + 6,
        2 => Rng.RandInt(5) + 6,
        3 => Rng.RandInt(8) + 6,
        4 => Rng.RandInt(4) + 3,
        5 => Rng.RandInt(8) + 6,
        6 => Rng.RandInt(20) + 12,
        7 => Rng.RandInt(20) + 12,
        8 => Rng.RandInt(10) + 6,
        9 => Rng.RandInt(12) + 6,
        10 => Rng.RandInt(10) + 12,
        11 => Rng.RandInt(3) + 3,
        12 => Rng.RandInt(8) + 6,
        13 => Rng.RandInt(10) + 6,
        14 => Rng.RandInt(5) + 3,
        15 => Rng.RandInt(5) + 3,
        16 => Rng.RandInt(5) + 6,
        17 => Rng.RandInt(5) + 4,
        18 => Rng.RandInt(8) + 4,
        19 => Rng.RandInt(6) + 2,
        20 => Rng.RandInt(4) + 2,
        21 => Rng.RandInt(8) + 6,
        22 => Rng.RandInt(5) + 2,
        23 => Rng.RandInt(12) + 12,
        _ => 0,
    };

    /// <summary>
    /// Charges for a staff. Two of them also override their own level, which is
    /// why this cannot be a plain expression like the wands.
    /// </summary>
    private void StaffCharges(InvenType item)
    {
        switch (item.SubVal)
        {
            case 0: item.P1 = (short)(Rng.RandInt(20) + 12); break;
            case 1: item.P1 = (short)(Rng.RandInt(8) + 6); break;
            case 2: item.P1 = (short)(Rng.RandInt(5) + 6); break;
            case 3: item.P1 = (short)(Rng.RandInt(20) + 12); break;
            case 4: item.P1 = (short)(Rng.RandInt(15) + 6); break;
            case 5: item.P1 = (short)(Rng.RandInt(4) + 5); break;
            case 6: item.P1 = (short)(Rng.RandInt(5) + 3); break;

            case 7:
                item.P1 = (short)(Rng.RandInt(3) + 1);
                item.Level = 10;
                break;

            case 8: item.P1 = (short)(Rng.RandInt(3) + 1); break;
            case 9: item.P1 = (short)(Rng.RandInt(5) + 6); break;
            case 10: item.P1 = (short)(Rng.RandInt(10) + 12); break;
            case 11: item.P1 = (short)(Rng.RandInt(5) + 6); break;
            case 12: item.P1 = (short)(Rng.RandInt(5) + 6); break;
            case 13: item.P1 = (short)(Rng.RandInt(5) + 6); break;
            case 14: item.P1 = (short)(Rng.RandInt(10) + 12); break;
            case 15: item.P1 = (short)(Rng.RandInt(3) + 4); break;
            case 16: item.P1 = (short)(Rng.RandInt(5) + 6); break;
            case 17: item.P1 = (short)(Rng.RandInt(5) + 6); break;
            case 18: item.P1 = (short)(Rng.RandInt(3) + 4); break;
            case 19: item.P1 = (short)(Rng.RandInt(10) + 12); break;
            case 20: item.P1 = (short)(Rng.RandInt(3) + 4); break;
            case 21: item.P1 = (short)(Rng.RandInt(3) + 4); break;

            case 22:
                item.P1 = (short)(Rng.RandInt(10) + 6);
                item.Level = 5;
                break;

            default:
                break;
        }
    }

    private void Cloak(InvenType item, int level, int chance, int special, int cursed)
    {
        if (Magik(chance))
        {
            if (Magik(special))
            {
                if (Rng.RandInt(2) == 1)
                {
                    item.SpecialName = SpecialName.Protection;
                    item.ToAc += (short)Bonus(2, 40, level);
                    item.Cost += 250;
                }
                else
                {
                    item.ToAc += (short)Bonus(1, 20, level);
                    item.Identification |= Identification.ShowP1;
                    item.P1 = (short)Rng.RandInt(3);
                    item.Flags |= ItemFlags.Stealth;
                    item.SpecialName = SpecialName.Stealth;
                    item.Cost += 500;
                }
            }
            else
            {
                item.ToAc += (short)Bonus(1, 20, level);
            }
        }
        else if (Magik(cursed))
        {
            int roll = Rng.RandInt(3);
            if (roll == 1)
            {
                item.Flags |= ItemFlags.Aggravate;
                item.SpecialName = SpecialName.Irritation;
                item.ToAc -= (short)Bonus(1, 10, level);
                item.Identification |= Identification.ShowHitDam;
                item.ToHit -= (short)Bonus(1, 10, level);
                item.ToDam -= (short)Bonus(1, 10, level);
                item.Cost = 0;
            }
            else if (roll == 2)
            {
                item.SpecialName = SpecialName.Vulnerability;
                item.ToAc -= (short)Bonus(10, 100, level + 50);
                item.Cost = 0;
            }
            else
            {
                item.SpecialName = SpecialName.Enveloping;
                item.ToAc -= (short)Bonus(1, 10, level);
                item.Identification |= Identification.ShowHitDam;
                item.ToHit -= (short)Bonus(2, 40, level + 10);
                item.ToDam -= (short)Bonus(2, 40, level + 10);
                item.Cost = 0;
            }

            item.Flags |= ItemFlags.Cursed;
        }
    }

    /// <summary>
    /// A chest's contents are already decided; this decides what guards them.
    /// The roll widens with depth, so deep chests skew towards the nastier
    /// entries at the end of the list - and anything past the table is the worst
    /// combination.
    /// </summary>
    private void Chest(InvenType item, int level)
    {
        switch (Rng.RandInt(level + 4))
        {
            case 1:
                item.Flags = 0;
                item.SpecialName = SpecialName.Empty;
                break;
            case 2:
                item.Flags |= ChestFlags.Locked;
                item.SpecialName = SpecialName.Locked;
                break;
            case 3:
            case 4:
                item.Flags |= ChestFlags.LoseStrength | ChestFlags.Locked;
                item.SpecialName = SpecialName.PoisonNeedle;
                break;
            case 5:
            case 6:
                item.Flags |= ChestFlags.Poison | ChestFlags.Locked;
                item.SpecialName = SpecialName.PoisonNeedle;
                break;
            case 7:
            case 8:
            case 9:
                item.Flags |= ChestFlags.Paralyse | ChestFlags.Locked;
                item.SpecialName = SpecialName.GasTrap;
                break;
            case 10:
            case 11:
                item.Flags |= ChestFlags.Explode | ChestFlags.Locked;
                item.SpecialName = SpecialName.ExplosionDevice;
                break;
            case 12:
            case 13:
            case 14:
                item.Flags |= ChestFlags.Summon | ChestFlags.Locked;
                item.SpecialName = SpecialName.SummoningRunes;
                break;
            case 15:
            case 16:
            case 17:
                item.Flags |= ChestFlags.Paralyse | ChestFlags.Poison
                    | ChestFlags.LoseStrength | ChestFlags.Locked;
                item.SpecialName = SpecialName.MultipleTraps;
                break;
            default:
                item.Flags |= ChestFlags.Summon | ChestFlags.Explode | ChestFlags.Locked;
                item.SpecialName = SpecialName.MultipleTraps;
                break;
        }
    }

    private void Missiles(InvenType item, int level, int chance, int special, int cursed)
    {
        // Spikes fall into this branch for the count and serial number below,
        // but are never enchanted - they are not fired.
        if (item.TVal is ItemCategory.SlingAmmo or ItemCategory.Bolt or ItemCategory.Arrow)
        {
            item.Identification |= Identification.ShowHitDam;

            if (Magik(chance))
            {
                item.ToHit += (short)Bonus(1, 35, level);
                item.ToDam += (short)Bonus(1, 35, level);

                if (Magik(3 * special / 2))
                {
                    switch (Rng.RandInt(10))
                    {
                        case 1:
                        case 2:
                        case 3:
                            item.SpecialName = SpecialName.Slaying;
                            item.ToHit += 5;
                            item.ToDam += 5;
                            item.Cost += 20;
                            break;
                        case 4:
                        case 5:
                            item.Flags |= ItemFlags.FlameTongue;
                            item.ToHit += 2;
                            item.ToDam += 4;
                            item.SpecialName = SpecialName.Fire;
                            item.Cost += 25;
                            break;
                        case 6:
                        case 7:
                            item.Flags |= ItemFlags.SlayEvil;
                            item.ToHit += 3;
                            item.ToDam += 3;
                            item.SpecialName = SpecialName.SlayEvilMissile;
                            item.Cost += 25;
                            break;
                        case 8:
                        case 9:
                            item.Flags |= ItemFlags.SlayAnimal;
                            item.ToHit += 2;
                            item.ToDam += 2;
                            item.SpecialName = SpecialName.SlayAnimalMissile;
                            item.Cost += 30;
                            break;
                        default: // 10
                            item.Flags |= ItemFlags.SlayDragon;
                            item.ToHit += 3;
                            item.ToDam += 3;
                            item.SpecialName = SpecialName.DragonSlaying;
                            item.Cost += 35;
                            break;
                    }
                }
            }
            else if (Magik(cursed))
            {
                item.ToHit -= (short)Bonus(5, 55, level);
                item.ToDam -= (short)Bonus(5, 55, level);
                item.Flags |= ItemFlags.Cursed;
                item.Cost = 0;
            }
        }

        // Missiles arrive in a pile, seven dice worth.
        int count = 0;
        for (int i = 0; i < 7; i++)
        {
            count += Rng.RandInt(6);
        }

        item.Number = (byte)count;

        // A serial number, so two piles of otherwise identical arrows do not
        // merge. It wraps rather than saturating, which is why it is allowed to
        // go negative.
        if (_game.MissileCounter == MaxShort)
        {
            _game.MissileCounter = -MaxShort - 1;
        }
        else
        {
            _game.MissileCounter++;
        }

        item.P1 = (short)_game.MissileCounter;
    }
}
