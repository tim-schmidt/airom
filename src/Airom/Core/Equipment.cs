// Ported from the equipment half of Umoria 5.6 source/moria1.c - py_bonuses and
// calc_bonuses - with check_strength from source/misc3.c and the object
// housekeeping from source/moria3.c and source/misc1.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// What worn equipment does for the player.
///
/// Umoria keeps two kinds of bookkeeping here. Some effects are cumulative and
/// applied as an item goes on or comes off - a second ring of strength adds to
/// the first - and those go through <see cref="ApplyItem"/>. The rest depend on
/// everything worn at once and are recomputed from scratch by
/// <see cref="Recalculate"/>: a resistance is either granted by something or it
/// is not, however many things grant it.
/// </summary>
public sealed class Equipment
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Equipment(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    private Player Player => _game.Player;

    private Inventory Pack => _game.Inventory;

    /// <summary>
    /// Applies, or takes back, what one item grants. Mirrors py_bonuses().
    ///
    /// <paramref name="factor"/> is 1 when something is put on and -1 when it
    /// comes off, so the same arithmetic does both. Only what stacks lives here:
    /// blindness and fear are inflicted on the way on and are not undone by
    /// taking the thing off again.
    /// </summary>
    public void ApplyItem(InvenType item, int factor)
    {
        ArgumentNullException.ThrowIfNull(item);

        int amount = item.P1 * factor;

        if ((item.Flags & ItemFlags.Stats) != 0)
        {
            for (int i = 0; i < Stat.Count; i++)
            {
                if ((1u << i & item.Flags) != 0)
                {
                    _loop.Stats.Boost(i, amount);
                }
            }
        }

        if ((item.Flags & ItemFlags.Search) != 0)
        {
            Player.Search += amount;
            Player.SearchFrequency -= amount;
        }

        if ((item.Flags & ItemFlags.Stealth) != 0)
        {
            Player.Stealth += amount;
        }

        if ((item.Flags & ItemFlags.Speed) != 0)
        {
            _loop.ChangeSpeed(-amount);
        }

        // Cursed effects only ever arrive; taking the item off does not lift
        // them.
        if ((item.Flags & ItemFlags.Blind) != 0 && factor > 0)
        {
            Player.Blind += 1000;
        }

        if ((item.Flags & ItemFlags.Timid) != 0 && factor > 0)
        {
            Player.Afraid += 50;
        }

        if ((item.Flags & ItemFlags.Infravision) != 0)
        {
            Player.SeeInfrared += amount;
        }
    }

    /// <summary>
    /// Recomputes everything that depends on the whole of what is worn. Mirrors
    /// calc_bonuses().
    ///
    /// The player's own to-hit and to-damage are worked out from their stats
    /// again, then every worn item adds to them. The displayed values are kept
    /// separately and only include what the player knows about, which is what
    /// makes an unidentified weapon worth trying.
    ///
    /// Digestion is bracketed by the two flags that change it, so a ring of
    /// regeneration taken off gives the food back rather than leaving the player
    /// permanently ravenous.
    /// </summary>
    public void Recalculate()
    {
        if (Player.SlowDigestion)
        {
            Player.FoodDigested++;
        }

        if (Player.Regenerates)
        {
            Player.FoodDigested -= 3;
        }

        Player.SeeInvisible = false;
        Player.RandomTeleport = false;
        Player.FreeAction = false;
        Player.SlowDigestion = false;
        Player.AggravatesMonsters = false;
        Player.SustainStrength = false;
        Player.SustainIntelligence = false;
        Player.SustainWisdom = false;
        Player.SustainConstitution = false;
        Player.SustainDexterity = false;
        Player.SustainCharisma = false;
        Player.FireResistant = false;
        Player.AcidResistant = false;
        Player.ColdResistant = false;
        Player.Regenerates = false;
        Player.LightResistant = false;
        Player.FeatherFall = false;

        int oldDisplayedAc = Player.DisplayedArmourClass;

        Player.PlusToHit = Stats.HitBonus(Player);
        Player.PlusToDamage = Stats.DamageBonus(Player);
        Player.PlusToArmourClass = Stats.ArmourBonus(Player);
        Player.ArmourClass = 0;

        Player.DisplayedPlusToHit = Player.PlusToHit;
        Player.DisplayedPlusToDamage = Player.PlusToDamage;
        Player.DisplayedArmourClass = 0;
        Player.DisplayedToArmourClass = Player.PlusToArmourClass;

        for (int slot = Inventory.WieldSlot; slot < Inventory.LightSlot; slot++)
        {
            InvenType item = Pack[slot];
            if (item.TVal == ItemCategory.Nothing)
            {
                continue;
            }

            Player.PlusToHit += item.ToHit;

            // A bow does no damage itself; the arrow does.
            if (item.TVal != ItemCategory.Bow)
            {
                Player.PlusToDamage += item.ToDam;
            }

            Player.PlusToArmourClass += item.ToAc;
            Player.ArmourClass += item.Ac;

            if (ItemKnowledge.IsEnchantmentKnown(item))
            {
                Player.DisplayedPlusToHit += item.ToHit;

                if (item.TVal != ItemCategory.Bow)
                {
                    Player.DisplayedPlusToDamage += item.ToDam;
                }

                Player.DisplayedToArmourClass += item.ToAc;
                Player.DisplayedArmourClass += item.Ac;
            }
            else if ((item.Flags & ItemFlags.Cursed) == 0)
            {
                // The base armour of an honest item is plain to see, whether or
                // not its enchantment is.
                Player.DisplayedArmourClass += item.Ac;
            }
        }

        Player.DisplayedArmourClass += Player.DisplayedToArmourClass;

        if (Pack.WeaponTooHeavy)
        {
            Player.DisplayedPlusToHit +=
                (Player.UseStat[Stat.Strength] * 15) - Pack[Inventory.WieldSlot].Weight;
        }

        // Temporary spells, which are not worn but count the same way.
        if (Player.Invulnerable > 0)
        {
            Player.ArmourClass += 100;
            Player.DisplayedArmourClass += 100;
        }

        if (Player.Blessed > 0)
        {
            Player.ArmourClass += 2;
            Player.DisplayedArmourClass += 2;
        }

        if (Player.DetectInvisible > 0)
        {
            Player.SeeInvisible = true;
        }

        // The armour class cannot be printed here: this may be running inside a
        // shop, where the sidebar is not on screen.
        if (oldDisplayedAc != Player.DisplayedArmourClass)
        {
            Player.Status |= PlayerStatus.ArmourChanged;
        }

        uint worn = 0;
        for (int slot = Inventory.WieldSlot; slot < Inventory.LightSlot; slot++)
        {
            worn |= Pack[slot].Flags;
        }

        Player.SlowDigestion = (worn & ItemFlags.SlowDigest) != 0;
        Player.AggravatesMonsters = (worn & ItemFlags.Aggravate) != 0;
        Player.RandomTeleport = (worn & ItemFlags.Teleport) != 0;
        Player.Regenerates = (worn & ItemFlags.Regenerate) != 0;
        Player.FireResistant = (worn & ItemFlags.ResistFire) != 0;
        Player.AcidResistant = (worn & ItemFlags.ResistAcid) != 0;
        Player.ColdResistant = (worn & ItemFlags.ResistCold) != 0;
        Player.FreeAction = (worn & ItemFlags.FreeAction) != 0;
        Player.LightResistant = (worn & ItemFlags.ResistLight) != 0;
        Player.FeatherFall = (worn & ItemFlags.FeatherFall) != 0;

        if ((worn & ItemFlags.SeeInvisible) != 0)
        {
            Player.SeeInvisible = true;
        }

        // Which stat an item sustains is held in its p1 rather than in a flag of
        // its own, so these have to be looked at one item at a time.
        for (int slot = Inventory.WieldSlot; slot < Inventory.LightSlot; slot++)
        {
            InvenType item = Pack[slot];
            if ((item.Flags & ItemFlags.SustainStat) == 0)
            {
                continue;
            }

            switch (item.P1)
            {
                case 1: Player.SustainStrength = true; break;
                case 2: Player.SustainIntelligence = true; break;
                case 3: Player.SustainWisdom = true; break;
                case 4: Player.SustainConstitution = true; break;
                case 5: Player.SustainDexterity = true; break;
                case 6: Player.SustainCharisma = true; break;
                default: break;
            }
        }

        if (Player.SlowDigestion)
        {
            Player.FoodDigested--;
        }

        if (Player.Regenerates)
        {
            Player.FoodDigested += 3;
        }
    }

    /// <summary>
    /// Checks whether the player is strong enough for what they are carrying and
    /// what they are holding. Mirrors check_strength().
    ///
    /// Too heavy a weapon costs accuracy; too heavy a pack costs speed, by a
    /// point for every multiple of the limit. Both are reported when they change
    /// and not before, so the message comes once rather than every turn.
    /// </summary>
    public void CheckStrength()
    {
        InvenType weapon = Pack[Inventory.WieldSlot];

        if (weapon.TVal != ItemCategory.Nothing
            && Player.UseStat[Stat.Strength] * 15 < weapon.Weight)
        {
            if (!Pack.WeaponTooHeavy)
            {
                _display.MessagePrint("You have trouble wielding such a heavy weapon.");
                Pack.WeaponTooHeavy = true;
                Recalculate();
            }
        }
        else if (Pack.WeaponTooHeavy)
        {
            Pack.WeaponTooHeavy = false;

            if (weapon.TVal != ItemCategory.Nothing)
            {
                _display.MessagePrint("You are strong enough to wield your weapon.");
            }

            Recalculate();
        }

        int limit = Pack.WeightLimit();
        int burden = limit < Pack.Weight ? Pack.Weight / (limit + 1) : 0;

        if (Pack.PackBurden != burden)
        {
            _display.MessagePrint(
                Pack.PackBurden < burden
                    ? "Your pack is so heavy that it slows you down."
                    : "You move more easily under the weight of your pack.");

            _loop.ChangeSpeed(burden - Pack.PackBurden);
            Pack.PackBurden = burden;
        }

        Player.Status &= ~PlayerStatus.WeightChanged;
    }
}
