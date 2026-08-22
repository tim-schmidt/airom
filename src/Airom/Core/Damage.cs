// Ported from the damage half of Umoria 5.6 source/moria2.c - minus_ac and the
// six elemental attacks.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The elements, and what they do to a person and their belongings.
///
/// Each one damages the player and then goes through the pack looking for
/// something to ruin. Resisting an element divides the damage rather than
/// stopping it, and the two kinds of resistance - permanent from equipment and
/// temporary from a potion - stack, so a fire-resistant character who has just
/// drunk resistance takes a ninth.
/// </summary>
public sealed class Damage
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Damage(GameState game, Display display, GameLoop loop)
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
    /// The armour slots something corrosive can eat, in the order the original
    /// gathers them.
    /// </summary>
    private static readonly int[] ArmourSlots =
    [
        Inventory.BodySlot,
        Inventory.ArmSlot,
        Inventory.OuterSlot,
        Inventory.HandsSlot,
        Inventory.HeadSlot,
        Inventory.FeetSlot,
    ];

    /// <summary>
    /// Damages one piece of worn armour. Mirrors minus_ac().
    ///
    /// One worn piece is picked at random, so a well-armoured character loses
    /// their armour slowly rather than all of it at once. A piece that resists
    /// says so and takes nothing.
    /// </summary>
    /// <returns>Whether anything absorbed the attack.</returns>
    public bool DamageArmour(uint resistFlag)
    {
        Span<int> worn = stackalloc int[ArmourSlots.Length];
        int count = 0;

        foreach (int slot in ArmourSlots)
        {
            if (Pack[slot].TVal != ItemCategory.Nothing)
            {
                worn[count++] = slot;
            }
        }

        if (count == 0)
        {
            return false;
        }

        InvenType item = Pack[worn[_game.Rng.RandInt(count) - 1]];

        if ((item.Flags & resistFlag) != 0)
        {
            _display.MessagePrint(
                "Your " + _game.Names.Describe(item, withArticle: false) + " resists damage!");
            return true;
        }

        if (item.Ac + item.ToAc > 0)
        {
            _display.MessagePrint(
                "Your " + _game.Names.Describe(item, withArticle: false) + " is damaged!");
            item.ToAc--;
            _loop.Equipment.Recalculate();
            return true;
        }

        return false;
    }

    /// <summary>Corrodes armour, and anything in the pack that corrodes. Mirrors corrode_gas().</summary>
    public void CorrodeGas(string killedBy)
    {
        if (!DamageArmour(ItemFlags.ResistAcid))
        {
            _loop.TakeHit(_game.Rng.RandInt(8), killedBy);
        }

        if (Pack.Damage(ItemSets.Corrodes, 5) > 0)
        {
            _display.MessagePrint("There is an acrid smell coming from your pack.");
        }
    }

    /// <summary>Poisons the player. Mirrors poison_gas().</summary>
    public void PoisonGas(int damage, string killedBy)
    {
        _loop.TakeHit(damage, killedBy);
        Player.Poisoned += 12 + _game.Rng.RandInt(damage);
    }

    /// <summary>Burns the player, and anything flammable they carry. Mirrors fire_dam().</summary>
    public void FireDamage(int damage, string killedBy)
    {
        if (Player.FireResistant)
        {
            damage /= 3;
        }

        if (Player.ResistHeat > 0)
        {
            damage /= 3;
        }

        _loop.TakeHit(damage, killedBy);

        if (Pack.Damage(ItemSets.IsFlammable, 3) > 0)
        {
            _display.MessagePrint("There is smoke coming from your pack!");
        }
    }

    /// <summary>Freezes the player, and shatters what frost breaks. Mirrors cold_dam().</summary>
    public void ColdDamage(int damage, string killedBy)
    {
        if (Player.ColdResistant)
        {
            damage /= 3;
        }

        if (Player.ResistCold > 0)
        {
            damage /= 3;
        }

        _loop.TakeHit(damage, killedBy);

        if (Pack.Damage(ItemSets.DestroyedByFrost, 5) > 0)
        {
            _display.MessagePrint("Something shatters inside your pack!");
        }
    }

    /// <summary>Shocks the player. Mirrors light_dam().</summary>
    public void LightningDamage(int damage, string killedBy)
    {
        _loop.TakeHit(Player.LightResistant ? damage / 3 : damage, killedBy);

        if (Pack.Damage(ItemSets.DestroyedByLightning, 3) > 0)
        {
            _display.MessagePrint("There are sparks coming from your pack!");
        }
    }

    /// <summary>
    /// Splashes the player with acid. Mirrors acid_dam().
    ///
    /// Armour taking the brunt of it and resistance each halve what gets
    /// through, and both together reduce it to a third.
    /// </summary>
    public void AcidDamage(int damage, string killedBy)
    {
        int divisor = 0;

        if (DamageArmour(ItemFlags.ResistAcid))
        {
            divisor = 1;
        }

        if (Player.AcidResistant)
        {
            divisor += 2;
        }

        _loop.TakeHit(damage / (divisor + 1), killedBy);

        if (Pack.Damage(ItemSets.AffectedByAcid, 3) > 0)
        {
            _display.MessagePrint("There is an acrid smell coming from your pack!");
        }
    }
}
