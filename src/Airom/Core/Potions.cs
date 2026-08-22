// Ported from the effects half of Umoria 5.6 source/potions.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Drinking things.
///
/// A potion's effects are a bit set rather than a single number, so one bottle
/// can do several things at once - and each of them is asked separately whether
/// the player noticed. That is what identifies the potion: not drinking it, but
/// seeing it work. A potion of restore strength drunk by someone whose strength
/// is already whole teaches nothing, and stays a mystery.
/// </summary>
public sealed class Potions
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Potions(GameState game, Display display, GameLoop loop)
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
    /// Drinks something out of the pack. Mirrors quaff(), less the prompting.
    ///
    /// Working out what a potion was is worth experience in itself - the only
    /// experience in the game that comes from understanding rather than from
    /// killing - and it is divided by the player's level, so a novice learns
    /// more from the same bottle than a veteran does.
    /// </summary>
    public void Drink(int slot)
    {
        InvenType potion = _game.Inventory[slot];
        _loop.FreeTurn = false;

        bool identified = Quaff(potion);

        if (identified)
        {
            if (!_game.Knowledge.IsKindKnown(potion))
            {
                // Rounded half-way up, which is what the integer division does
                // with the half level added first.
                Player.Experience += (potion.Level + (Player.Level >> 1)) / Player.Level;
                _loop.Levelling.PrintExperience();

                slot = _game.Inventory.Identify(slot, _display);
                potion = _game.Inventory[slot];
            }
        }
        else if (!_game.Knowledge.IsKindKnown(potion))
        {
            _game.Knowledge.MarkTried(potion);
        }

        _loop.Spells.AddFood(potion.P1);
        DescribeRemaining(slot);
        _game.Inventory.Destroy(slot);
    }

    /// <summary>
    /// The drink command: asks which potion, then drinks it. Mirrors the front
    /// of quaff().
    /// </summary>
    public void Quaff()
    {
        _loop.FreeTurn = true;

        if (_game.Inventory.Count == 0)
        {
            _display.MessagePrint("But you are not carrying anything.");
            return;
        }

        if (!_game.Inventory.FindRange(ItemCategory.Potion1, ItemCategory.Potion2,
                                       out int first, out int last))
        {
            _display.MessagePrint("You are not carrying any potions.");
            return;
        }

        if (_loop.InventoryScreen.GetItem("Quaff which potion?", first, last)
            is int slot)
        {
            Drink(slot);
        }
    }

    /// <summary>
    /// Says what is left of the pile. Mirrors desc_remain(), which counts the
    /// pile one short so that the last one reads as "no more".
    /// </summary>
    private void DescribeRemaining(int slot)
    {
        InvenType item = _game.Inventory[slot];

        item.Number--;
        string description = _game.Names.Describe(item, withArticle: true);
        item.Number++;

        _display.MessagePrint("You have " + description);
    }

    /// <summary>
    /// Drinks a potion and applies everything it does. Mirrors the body of
    /// quaff(), less the prompting.
    ///
    /// The second potion table continues the first, so its flags are numbered
    /// from thirty-three - which is why the switch runs past thirty-two.
    /// </summary>
    /// <returns>Whether the player learned what the potion was.</returns>
    public bool Quaff(InvenType potion)
    {
        ArgumentNullException.ThrowIfNull(potion);

        uint flags = potion.Flags;
        bool identified = false;

        if (flags == 0)
        {
            _display.MessagePrint("You feel less thirsty.");
            identified = true;
        }

        while (flags != 0)
        {
            int effect = System.Numerics.BitOperations.TrailingZeroCount(flags) + 1;
            flags &= ~(1u << (effect - 1));

            if (potion.TVal == ItemCategory.Potion2)
            {
                effect += 32;
            }

            identified |= Apply(effect);
        }

        return identified;
    }

    private bool Apply(int effect)
    {
        switch (effect)
        {
            case 1: return Raise(Stat.Strength, "Wow!  What bulging muscles!");
            case 2: Spells.LoseStat(Stat.Strength); return true;
            case 3: return Restore(Stat.Strength, "You feel warm all over.");

            case 4: return Raise(Stat.Intelligence, "Aren't you brilliant!");
            case 5: Spells.LoseStat(Stat.Intelligence); return true;
            case 6: return Restore(Stat.Intelligence, "You have have a warm feeling.");

            case 7: return Raise(Stat.Wisdom, "You suddenly have a profound thought!");
            case 8: Spells.LoseStat(Stat.Wisdom); return true;
            case 9: return Restore(Stat.Wisdom, "You feel your wisdom returning.");

            case 10: return Raise(Stat.Charisma, "Gee, ain't you cute!");
            case 11: Spells.LoseStat(Stat.Charisma); return true;
            case 12: return Restore(Stat.Charisma, "You feel your looks returning.");

            case 13: return Spells.HealPlayer(Rng.DamRoll(2, 7));
            case 14: return Spells.HealPlayer(Rng.DamRoll(4, 7));
            case 15: return Spells.HealPlayer(Rng.DamRoll(6, 7));
            case 16: return Spells.HealPlayer(1000);

            case 17: return Raise(Stat.Constitution, "You feel tingly for a moment.");

            case 18: return GainExperience();

            case 19: // sleep
                if (Player.FreeAction)
                {
                    return false;
                }

                // Paralysis must already be nothing, or the potion could not
                // have been drunk.
                _display.MessagePrint("You fall asleep.");
                Player.Paralysis += Rng.RandInt(4) + 4;
                return true;

            case 20: // blindness
            {
                bool noticed = Player.Blind == 0;

                if (noticed)
                {
                    _display.MessagePrint("You are covered by a veil of darkness.");
                }

                Player.Blind += Rng.RandInt(100) + 100;
                return noticed;
            }

            case 21: // confusion, of the cheerful kind
            {
                bool noticed = Player.Confused == 0;

                if (noticed)
                {
                    _display.MessagePrint("Hey!  This is good stuff!  * Hick! *");
                }

                Player.Confused += Rng.RandInt(20) + 12;
                return noticed;
            }

            case 22: // poison
            {
                bool noticed = Player.Poisoned == 0;

                if (noticed)
                {
                    _display.MessagePrint("You feel very sick.");
                }

                Player.Poisoned += Rng.RandInt(15) + 10;
                return noticed;
            }

            case 23: // haste
            {
                bool noticed = Player.Hasted == 0;
                Player.Hasted += Rng.RandInt(25) + 15;
                return noticed;
            }

            case 24: // slowness
            {
                bool noticed = Player.Slowed == 0;
                Player.Slowed += Rng.RandInt(25) + 15;
                return noticed;
            }

            case 26: return Raise(Stat.Dexterity, "You feel more limber!");
            case 27: return Restore(Stat.Dexterity, "You feel less clumsy.");
            case 28: return Restore(Stat.Constitution, "You feel your health returning!");

            case 29: return Spells.CureBlindness();
            case 30: return Spells.CureConfusion();
            case 31: return Spells.CurePoison();

            case 33: return LearnMagic();

            case 34: return LoseMemories();

            case 35: // vomiting
                Spells.CurePoison();

                if (Player.Food > 150)
                {
                    Player.Food = 150;
                }

                Player.Paralysis = 4;
                _display.MessagePrint("The potion makes you vomit!");
                return true;

            case 36:
            {
                bool noticed = Player.Invulnerable == 0;
                Player.Invulnerable += Rng.RandInt(10) + 10;
                return noticed;
            }

            case 37:
            {
                bool noticed = Player.Hero == 0;
                Player.Hero += Rng.RandInt(25) + 25;
                return noticed;
            }

            case 38:
            {
                bool noticed = Player.SuperHero == 0;
                Player.SuperHero += Rng.RandInt(25) + 25;
                return noticed;
            }

            case 39: return Spells.RemoveFear();
            case 40: return Spells.RestoreLevel();

            case 41:
            {
                bool noticed = Player.ResistHeat == 0;
                Player.ResistHeat += Rng.RandInt(10) + 10;
                return noticed;
            }

            case 42:
            {
                bool noticed = Player.ResistCold == 0;
                Player.ResistCold += Rng.RandInt(10) + 10;
                return noticed;
            }

            case 43:
            {
                bool noticed = Player.DetectInvisible == 0;
                Spells.DetectInvisibleFor(Rng.RandInt(12) + 12);
                return noticed;
            }

            case 44: return Spells.SlowPoison();
            case 45: return Spells.CurePoison();

            case 46: // restore mana
                if (Player.CurrentMana >= Player.MaxMana)
                {
                    return false;
                }

                Player.CurrentMana = Player.MaxMana;
                _display.MessagePrint("Your feel your head clear.");
                _display.PrintCurrentMana(Player);
                return true;

            case 47:
            {
                bool noticed = Player.TimedInfravision == 0;

                if (noticed)
                {
                    _display.MessagePrint("Your eyes begin to tingle.");
                }

                Player.TimedInfravision += 100 + Rng.RandInt(100);
                return noticed;
            }

            default:
                _display.MessagePrint("Internal error in potion()");
                return false;
        }
    }

    private bool Raise(int stat, string message)
    {
        if (!_loop.Stats.Increase(stat))
        {
            return false;
        }

        _display.MessagePrint(message);
        return true;
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

    /// <summary>
    /// A draught of experience, capped so that one bottle cannot carry a
    /// character the whole way.
    /// </summary>
    private bool GainExperience()
    {
        if (Player.Experience >= Combat.MaxExperience)
        {
            return false;
        }

        int gain = (Player.Experience / 2) + 10;

        if (gain > 100000)
        {
            gain = 100000;
        }

        Player.Experience += gain;
        _display.MessagePrint("You feel more experienced.");
        _loop.Levelling.PrintExperience();
        return true;
    }

    /// <summary>
    /// Between a fifth and two fifths of the player's experience, drunk away.
    ///
    /// The arithmetic goes to some trouble to avoid overflowing a signed long on
    /// a very experienced character, which is why the large case is scaled
    /// rather than rolled directly.
    /// </summary>
    private bool LoseMemories()
    {
        if (Player.Experience <= 0)
        {
            return false;
        }

        _display.MessagePrint("You feel your memories fade.");

        int lost = Player.Experience / 5;

        if (Player.Experience > GameLoop.MaxShort)
        {
            int scale = int.MaxValue / Player.Experience;
            lost += (int)((long)Rng.RandInt(scale) * Player.Experience / (scale * 5L));
        }
        else
        {
            lost += Rng.RandInt(Player.Experience) / 5;
        }

        _loop.MonsterAttack.LoseExperience(lost);
        return true;
    }

    /// <summary>
    /// A potion of learning. A spellcaster gains what they can learn; anyone
    /// else notices that something they are carrying is enchanted.
    ///
    /// Pending: calc_spells() and calc_mana() belong to the spell half of
    /// misc3.c, so a caster learns nothing from this yet.
    /// </summary>
    private bool LearnMagic()
    {
        int realm = GameTables.Classes[Player.Class].SpellRealm;

        if (realm is SpellRealm.Mage or SpellRealm.Priest)
        {
            _loop.Stats.RecalculateSpells(
                realm == SpellRealm.Mage ? Stat.Intelligence : Stat.Wisdom);
            return false;
        }

        bool noticed = false;

        for (int slot = Inventory.WieldSlot; slot < Inventory.Size; slot++)
        {
            InvenType worn = _game.Inventory[slot];

            if (worn.TVal != ItemCategory.Nothing && ItemKnowledge.IsUnnoticedEnchantment(worn))
            {
                _display.MessagePrint(
                    "There's something about what you are "
                    + Inventory.DescribeUse(slot) + "...");

                ItemKnowledge.AddInscription(worn, Identification.Magik);
                noticed = true;
            }
        }

        return noticed;
    }
}
