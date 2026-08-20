// Ported from the levelling half of Umoria 5.6 source/misc3.c - gain_level,
// prt_experience and calc_hitpoints.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Experience, levels and the hit points that come with them.
///
/// A character's hit points are not accumulated as they are earned: the whole
/// curve is rolled once at creation and the total is looked up by level, so
/// gaining a level cannot be re-rolled by reloading. What varies is the
/// constitution bonus, which is why a ring of constitution changes the maximum
/// at once rather than from the next level on.
/// </summary>
public sealed class Levelling
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Levelling(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    private Player Player => _game.Player;

    /// <summary>
    /// Works out what the maximum hit points should be, and adjusts the current
    /// ones to match. Mirrors calc_hitpoints().
    ///
    /// The current total is moved in proportion rather than by the difference,
    /// so a character at half health stays at half health when their maximum
    /// changes. Heroism is added on top, which is why it can be lost without
    /// killing anyone outright.
    /// </summary>
    public void CalculateHitPoints()
    {
        int hitPoints = Player.HitPointsByLevel[Player.Level - 1]
            + (CharacterCreation.ConstitutionBonus(Player) * Player.Level);

        // Always at least a point a level, and one over.
        if (hitPoints < Player.Level + 1)
        {
            hitPoints = Player.Level + 1;
        }

        if ((Player.Status & PlayerStatus.Heroism) != 0)
        {
            hitPoints += 10;
        }

        if ((Player.Status & PlayerStatus.SuperHeroism) != 0)
        {
            hitPoints += 20;
        }

        // The maximum can be zero while the character is still being made.
        if (hitPoints == Player.MaxHitPoints || Player.MaxHitPoints == 0)
        {
            return;
        }

        // Divided before multiplying, to keep the arithmetic inside a long at
        // the cost of a little accuracy - the original's own trade.
        long scaled = (((long)Player.CurrentHitPoints << 16) + Player.HitPointFraction)
            / Player.MaxHitPoints * hitPoints;

        Player.CurrentHitPoints = (int)(scaled >> 16);
        Player.HitPointFraction = (int)(scaled & 0xFFFF);
        Player.MaxHitPoints = hitPoints;

        // The hit points cannot be printed here: this may be running inside a
        // shop or the inventory screen.
        Player.Status |= PlayerStatus.HitPointsChanged;
    }

    /// <summary>
    /// Takes a level. Mirrors gain_level().
    ///
    /// Experience earned past what the level needed is halved, so gaining
    /// several levels at once does not carry the whole surplus forward.
    /// </summary>
    public void GainLevel()
    {
        Player.Level++;
        _display.MessagePrint(
            "Welcome to level " + Player.Level.ToString(CultureInfo.InvariantCulture) + ".");

        CalculateHitPoints();

        int needed = GameTables.PlayerExperience[Player.Level - 1]
            * Player.ExperienceFactor / 100;

        if (Player.Experience > needed)
        {
            Player.Experience = needed + ((Player.Experience - needed) / 2);
        }

        _display.PrintLevel(Player);
        _display.PrintTitle(Player);

        int realm = GameTables.Classes[Player.Class].SpellRealm;

        if (realm == SpellRealm.Mage)
        {
            _loop.Stats.RecalculateSpells(Stat.Intelligence);
        }
        else if (realm == SpellRealm.Priest)
        {
            _loop.Stats.RecalculateSpells(Stat.Wisdom);
        }
    }

    /// <summary>
    /// Redraws the experience, taking any levels it has earned on the way.
    /// Mirrors prt_experience().
    ///
    /// Levelling happens here rather than where the experience is awarded, so
    /// that the "welcome to level" message arrives after whatever earned it.
    /// </summary>
    public void PrintExperience()
    {
        if (Player.Experience > Combat.MaxExperience)
        {
            Player.Experience = Combat.MaxExperience;
        }

        while (Player.Level < Player.MaxLevel
               && GameTables.PlayerExperience[Player.Level - 1]
                  * Player.ExperienceFactor / 100 <= Player.Experience)
        {
            GainLevel();
        }

        if (Player.Experience > Player.MaxExperience)
        {
            Player.MaxExperience = Player.Experience;
        }

        _display.PrintExperienceValue(Player);
    }
}
