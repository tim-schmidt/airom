// Ported from the stat handling in Umoria 5.6 source/misc3.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The six stats and the arithmetic that moves them.
///
/// Umoria stores a stat in one byte over a range that is not linear: 3 to 18 are
/// themselves, and above that each further ten stands for a percentile step, so
/// 108 means 18/90 and 118 means the maximum of 18/100. That is why raising a
/// stat past eighteen adds ten rather than one, and why the arithmetic here
/// looks arbitrary until the scale is in mind.
///
/// Three numbers are kept for each stat. The maximum is the highest it has ever
/// been, which restoration returns it to. The current is what it is now, after
/// draining. What is actually used is the current plus whatever equipment is
/// lending, which is what every roll in the game reads.
/// </summary>
public class Stats
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Stats(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    /// <summary>Damage bonus from strength. Mirrors todam_adj().</summary>
    public static int DamageBonus(Player player) => CharacterCreation.DamageBonus(player);

    /// <summary>To-hit bonus from dexterity and strength. Mirrors tohit_adj().</summary>
    public static int HitBonus(Player player) => CharacterCreation.HitBonus(player);

    /// <summary>Armour class bonus from dexterity. Mirrors toac_adj().</summary>
    public static int ArmourBonus(Player player) => CharacterCreation.ArmourBonus(player);

    /// <summary>
    /// The general adjustment a stat gives, from nothing at three to seven at
    /// the very top. Mirrors stat_adj().
    ///
    /// This is the one every other roll reaches for: saving throws, picking
    /// locks, learning spells. The bands are wide at the bottom and narrow at
    /// the top, so the first few points matter more than the last few.
    /// </summary>
    public static int Adjustment(Player player, int stat)
    {
        ArgumentNullException.ThrowIfNull(player);

        return player.UseStat[stat] switch
        {
            > 117 => 7,
            > 107 => 6,
            > 87 => 5,
            > 67 => 4,
            > 17 => 3,
            > 14 => 2,
            > 7 => 1,
            _ => 0,
        };
    }

    /// <summary>
    /// The adjustment to disarming from dexterity. Mirrors todis_adj().
    /// </summary>
    public static int DisarmBonus(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        return player.UseStat[Stat.Dexterity] switch
        {
            < 4 => -8,
            4 => -6,
            5 => -4,
            6 => -2,
            7 => -1,
            < 13 => 0,
            < 16 => 1,
            < 18 => 2,
            < 59 => 4,
            < 94 => 5,
            < 117 => 6,
            _ => 8,
        };
    }

    private Player Player => _game.Player;

    /// <summary>
    /// Moves a stat by so many steps, on Umoria's own scale. Mirrors
    /// modify_stat().
    ///
    /// Below eighteen a step is a point; above it a step is ten, which is one
    /// percentile band; and the top of the range is 18/100 rather than anything
    /// higher. Coming down, the bands are given back in the same sizes, and the
    /// floor is three.
    /// </summary>
    public static int Modify(Player player, int stat, int amount)
    {
        ArgumentNullException.ThrowIfNull(player);

        int value = player.CurrentStat[stat];
        int steps = Math.Abs(amount);

        for (int i = 0; i < steps; i++)
        {
            if (amount > 0)
            {
                if (value < 18)
                {
                    value++;
                }
                else if (value < 108)
                {
                    value += 10;
                }
                else
                {
                    value = 118;
                }
            }
            else
            {
                if (value > 27)
                {
                    value -= 10;
                }
                else if (value > 18)
                {
                    value = 18;
                }
                else if (value > 3)
                {
                    value--;
                }
            }
        }

        return value;
    }

    /// <summary>
    /// Works out the value a stat is actually used at, and tells whatever
    /// depends on it. Mirrors set_use_stat().
    ///
    /// Each stat has something behind it: strength decides what can be carried,
    /// dexterity the armour class, constitution the hit points, and intelligence
    /// or wisdom the spells - so changing one has to reach further than the
    /// number on the screen.
    /// </summary>
    public void SetUseStat(int stat)
    {
        Player.UseStat[stat] = (byte)Modify(Player, stat, Player.ModStat[stat]);

        if (stat == Stat.Strength)
        {
            Player.Status |= PlayerStatus.WeightChanged;
            _loop.Equipment.Recalculate();
        }
        else if (stat == Stat.Dexterity)
        {
            _loop.Equipment.Recalculate();
        }
        else if (stat == Stat.Intelligence
                 && GameTables.Classes[Player.Class].SpellRealm == SpellRealm.Mage)
        {
            RecalculateSpells(Stat.Intelligence);
        }
        else if (stat == Stat.Wisdom
                 && GameTables.Classes[Player.Class].SpellRealm == SpellRealm.Priest)
        {
            RecalculateSpells(Stat.Wisdom);
        }
        else if (stat == Stat.Constitution)
        {
            RecalculateHitPoints();
        }
    }

    /// <summary>
    /// Raises a stat by a randomised step. Mirrors inc_stat().
    ///
    /// The gain is a fraction of what is left to the maximum, so a stat close to
    /// the top moves very little and a poor one moves a lot.
    /// </summary>
    /// <returns>False if it was already at the maximum.</returns>
    public bool Increase(int stat)
    {
        int value = Player.CurrentStat[stat];
        if (value >= 118)
        {
            return false;
        }

        if (value < 18)
        {
            value++;
        }
        else if (value < 116)
        {
            // A sixth to a third of the distance from the maximum.
            int gain = (((118 - value) / 3) + 1) >> 1;
            value += _game.Rng.RandInt(gain) + gain;
        }
        else
        {
            value++;
        }

        Player.CurrentStat[stat] = (byte)value;

        if (value > Player.MaxStat[stat])
        {
            Player.MaxStat[stat] = (byte)value;
        }

        SetUseStat(stat);
        _display.PrintStat(Player, stat);
        return true;
    }

    /// <summary>
    /// Lowers a stat by a randomised step. Mirrors dec_stat().
    ///
    /// Above eighteen the loss stops at eighteen rather than falling through the
    /// percentile range in one go.
    /// </summary>
    /// <returns>False if it was already at the floor of three.</returns>
    public bool Decrease(int stat)
    {
        int value = Player.CurrentStat[stat];
        if (value <= 3)
        {
            return false;
        }

        if (value < 19)
        {
            value--;
        }
        else if (value < 117)
        {
            int loss = (((118 - value) >> 1) + 1) >> 1;
            value += -_game.Rng.RandInt(loss) - loss;

            if (value < 18)
            {
                value = 18;
            }
        }
        else
        {
            value--;
        }

        Player.CurrentStat[stat] = (byte)value;
        SetUseStat(stat);
        _display.PrintStat(Player, stat);
        return true;
    }

    /// <summary>
    /// Restores a stat to the highest it has been. Mirrors res_stat().
    /// </summary>
    /// <returns>False if nothing was owed.</returns>
    public bool Restore(int stat)
    {
        int owed = Player.MaxStat[stat] - Player.CurrentStat[stat];
        if (owed == 0)
        {
            return false;
        }

        Player.CurrentStat[stat] = (byte)(Player.CurrentStat[stat] + owed);
        SetUseStat(stat);
        _display.PrintStat(Player, stat);
        return true;
    }

    /// <summary>
    /// Lends a stat some points, the way a worn ring does. Mirrors bst_stat().
    ///
    /// Nothing is printed here on purpose: this can run inside a shop or the
    /// inventory screen, where the sidebar is not on show. The flag is raised
    /// instead, and the turn redraws it later.
    /// </summary>
    public void Boost(int stat, int amount)
    {
        Player.ModStat[stat] += amount;
        SetUseStat(stat);
        Player.Status |= PlayerStatus.Strength << stat;
    }

    /// <summary>
    /// Pending: calc_spells() and calc_mana() belong to the spell half of
    /// misc3.c, which is not ported.
    /// </summary>
    protected internal virtual void RecalculateSpells(int stat)
    {
    }

    /// <summary>Recomputes the hit points. Mirrors calc_hitpoints().</summary>
    protected internal virtual void RecalculateHitPoints() =>
        _loop.Levelling.CalculateHitPoints();
}
