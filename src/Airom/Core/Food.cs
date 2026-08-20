// Ported from the effects half of Umoria 5.6 source/eat.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Eating things.
///
/// Mushrooms work like potions: a bit set of effects, each asked separately
/// whether the player noticed. What makes them different is that several of them
/// are simply bad, and the only way to find out which is to eat one - the level
/// of the mushroom scales how bad, so a deep one is a worse gamble than a
/// shallow one.
/// </summary>
public sealed class Food
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Food(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
    }

    private Player Player => _game.Player;

    private Rng Rng => _game.Rng;

    private Spells Spells => _loop.Spells;

    /// <summary>
    /// Eats something and applies everything it does. Mirrors the body of eat(),
    /// less the prompting.
    /// </summary>
    /// <returns>Whether the player learned what it was.</returns>
    public bool Eat(InvenType food)
    {
        ArgumentNullException.ThrowIfNull(food);

        uint flags = food.Flags;
        bool identified = false;

        while (flags != 0)
        {
            int effect = System.Numerics.BitOperations.TrailingZeroCount(flags) + 1;
            flags &= ~(1u << (effect - 1));

            identified |= Apply(effect, food.Level);
        }

        return identified;
    }

    private bool Apply(int effect, int level)
    {
        switch (effect)
        {
            case 1:
                Player.Poisoned += Rng.RandInt(10) + level;
                return true;

            case 2:
                Player.Blind += Rng.RandInt(250) + (10 * level) + 100;
                _display.PrintMap();
                _display.MessagePrint("A veil of darkness surrounds you.");
                return true;

            case 3:
                Player.Afraid += Rng.RandInt(10) + level;
                _display.MessagePrint("You feel terrified!");
                return true;

            case 4:
                Player.Confused += Rng.RandInt(10) + level;
                _display.MessagePrint("You feel drugged.");
                return true;

            case 5:
                Player.Hallucinating += Rng.RandInt(200) + (25 * level) + 200;
                _display.MessagePrint("You feel drugged.");
                return true;

            case 6: return Spells.CurePoison();
            case 7: return Spells.CureBlindness();

            case 8: // steadies the nerves, without the message a cure gives
                if (Player.Afraid <= 1)
                {
                    return false;
                }

                Player.Afraid = 1;
                return true;

            case 9: return Spells.CureConfusion();

            case 10: Spells.LoseStat(Stat.Strength); return true;
            case 11: Spells.LoseStat(Stat.Constitution); return true;
            case 12: Spells.LoseStat(Stat.Intelligence); return true;
            case 13: Spells.LoseStat(Stat.Wisdom); return true;
            case 14: Spells.LoseStat(Stat.Dexterity); return true;
            case 15: Spells.LoseStat(Stat.Charisma); return true;

            case 16: return Restore(Stat.Strength, "You feel your strength returning.");
            case 17: return Restore(Stat.Constitution, "You feel your health returning.");
            case 18: return Restore(Stat.Intelligence, "Your head spins a moment.");
            case 19: return Restore(Stat.Wisdom, "You feel your wisdom returning.");
            case 20: return Restore(Stat.Dexterity, "You feel more dextrous.");
            case 21: return Restore(Stat.Charisma, "Your skin stops itching.");

            case 22: return Spells.HealPlayer(Rng.RandInt(6));
            case 23: return Spells.HealPlayer(Rng.RandInt(12));
            case 24: return Spells.HealPlayer(Rng.RandInt(18));
            case 25: return Spells.HealPlayer(Rng.DamRoll(3, 6));
            case 26: return Spells.HealPlayer(Rng.DamRoll(3, 12));

            case 27: _loop.TakeHit(Rng.RandInt(18), "poisonous food."); return true;
            case 28: _loop.TakeHit(Rng.RandInt(8), "poisonous food."); return true;
            case 29: _loop.TakeHit(Rng.DamRoll(2, 8), "poisonous food."); return true;
            case 30: _loop.TakeHit(Rng.DamRoll(3, 8), "poisonous food."); return true;

            default:
                _display.MessagePrint("Internal error in eat()");
                return false;
        }
    }

    private bool Restore(int stat, string message)
    {
        if (!_loop.Stats.Restore(stat))
        {
            return false;
        }

        _display.MessagePrint(message);
        return true;
    }
}
