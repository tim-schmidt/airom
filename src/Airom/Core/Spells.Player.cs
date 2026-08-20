// Ported from the player-facing half of Umoria 5.6 source/spells.c - the cures,
// the losses and the small comforts - with add_food from source/misc1.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

public sealed partial class Spells
{
    /// <summary>
    /// Heals the player. Mirrors hp_player().
    ///
    /// How it is described depends on how much was healed rather than on what
    /// did the healing, which is why a small potion and a weak prayer read the
    /// same.
    /// </summary>
    /// <returns>Whether anything was healed, which is what identifies a potion.</returns>
    public bool HealPlayer(int amount)
    {
        if (Player.CurrentHitPoints >= Player.MaxHitPoints)
        {
            return false;
        }

        Player.CurrentHitPoints += amount;

        if (Player.CurrentHitPoints > Player.MaxHitPoints)
        {
            Player.CurrentHitPoints = Player.MaxHitPoints;
            Player.HitPointFraction = 0;
        }

        _display.PrintCurrentHitPoints(Player);

        int scale = amount / 5;

        _display.MessagePrint(scale switch
        {
            0 => "You feel a little better.",
            < 3 => "You feel better.",
            < 7 => "You feel much better.",
            _ => "You feel very good.",
        });

        return true;
    }

    /// <summary>
    /// Cures confusion. Mirrors cure_confusion().
    ///
    /// The counter is set to one rather than nothing, so the turn that follows
    /// clears it properly and prints what it should.
    /// </summary>
    public bool CureConfusion()
    {
        if (Player.Confused <= 1)
        {
            return false;
        }

        Player.Confused = 1;
        return true;
    }

    /// <summary>Cures blindness. Mirrors cure_blindness().</summary>
    public bool CureBlindness()
    {
        if (Player.Blind <= 1)
        {
            return false;
        }

        Player.Blind = 1;
        return true;
    }

    /// <summary>Cures poisoning. Mirrors cure_poison().</summary>
    public bool CurePoison()
    {
        if (Player.Poisoned <= 1)
        {
            return false;
        }

        Player.Poisoned = 1;
        return true;
    }

    /// <summary>Steadies the nerves. Mirrors remove_fear().</summary>
    public bool RemoveFear()
    {
        if (Player.Afraid <= 1)
        {
            return false;
        }

        Player.Afraid = 1;
        return true;
    }

    /// <summary>
    /// Slows a poison rather than curing it. Mirrors slow_poison().
    /// </summary>
    public bool SlowPoison()
    {
        if (Player.Poisoned <= 0)
        {
            return false;
        }

        Player.Poisoned /= 2;

        if (Player.Poisoned < 1)
        {
            Player.Poisoned = 1;
        }

        _display.MessagePrint("The effect of the poison has been reduced.");
        return true;
    }

    /// <summary>Blesses the player. Mirrors bless().</summary>
    public void Bless(int turns) => Player.Blessed += turns;

    /// <summary>Grants sight of the invisible. Mirrors detect_inv2().</summary>
    public void DetectInvisibleFor(int turns) => Player.DetectInvisible += turns;

    /// <summary>Wards off evil. Mirrors protect_evil().</summary>
    public bool ProtectFromEvil()
    {
        bool wasUnprotected = Player.ProtectionFromEvil == 0;
        Player.ProtectionFromEvil += Rng.RandInt(25) + 3 * Player.Level;
        return wasUnprotected;
    }

    /// <summary>
    /// Gives back the level a drain took. Mirrors restore_level().
    ///
    /// The experience is restored to the highest it has ever been, so a drained
    /// character is made whole rather than merely levelled up again.
    /// </summary>
    public bool RestoreLevel()
    {
        if (Player.MaxExperience <= Player.Experience)
        {
            return false;
        }

        _display.MessagePrint("You feel your life energies returning.");

        // The loop is not redundant: printing the experience can level the
        // character back down, which lowers it again.
        while (Player.Experience < Player.MaxExperience)
        {
            Player.Experience = Player.MaxExperience;
            _loop.Levelling.PrintExperience();
        }

        return true;
    }

    // ------------------------------------------------------------- the losses

    /// <summary>
    /// Drains a stat, unless it is being sustained. Mirrors the lose_* family,
    /// which differ only in what they say.
    /// </summary>
    public void LoseStat(int stat)
    {
        (bool sustained, string lost, string held) = stat switch
        {
            Stat.Strength => (Player.SustainStrength,
                "You feel very sick.", "You feel sick for a moment,  it passes."),
            Stat.Intelligence => (Player.SustainIntelligence,
                "You become very dizzy.", "You become dizzy for a moment,  it passes."),
            Stat.Wisdom => (Player.SustainWisdom,
                "You feel very naive.", "You feel naive for a moment,  it passes."),
            Stat.Dexterity => (Player.SustainDexterity,
                "You feel very sore.", "You feel sore for a moment,  it passes."),
            Stat.Constitution => (Player.SustainConstitution,
                "You feel very sick.", "You feel sick for a moment,  it passes."),
            _ => (Player.SustainCharisma,
                "Your skin starts to itch.",
                "Your skin starts to itch, but feels better now."),
        };

        if (sustained)
        {
            _display.MessagePrint(held);
            return;
        }

        _loop.Stats.Decrease(stat);
        _display.MessagePrint(lost);
    }

    // --------------------------------------------------------------- feeding

    /// <summary>
    /// Eats. Mirrors add_food().
    ///
    /// Eating past full is possible and costs speed: the player is credited with
    /// a fiftieth of the excess as food and slowed for that many turns, so
    /// gorging before a fight is a real mistake rather than a free top-up.
    /// </summary>
    public void AddFood(int amount)
    {
        if (Player.Food < 0)
        {
            Player.Food = 0;
        }

        Player.Food += amount;

        if (Player.Food > GameLoop.FoodMax)
        {
            _display.MessagePrint("You are bloated from overeating.");

            int excess = Math.Min(Player.Food - GameLoop.FoodMax, amount);
            int penalty = excess / 50;

            Player.Slowed += penalty;

            Player.Food = excess == amount
                ? Player.Food - amount + penalty
                : GameLoop.FoodMax + penalty;
        }
        else if (Player.Food > GameLoop.FoodFull)
        {
            _display.MessagePrint("You are full.");
        }
    }
}
