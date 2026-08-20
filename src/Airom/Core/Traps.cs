// Ported from the trap half of Umoria 5.6 source/moria3.c - hit_trap and
// chest_trap - together with openobject, closeobject and twall.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Traps, chests and doors - the three things on the floor that do something
/// back.
///
/// Every trap is one entry in the object table, and which one it is decides
/// what it does: the subval is a small number this switches on. The damage is
/// rolled from the entry's own dice, so a pit on the first level and a pit on
/// the fiftieth are the same trap doing the same thing.
/// </summary>
public sealed class Traps
{
    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Traps(GameState game, Display display, GameLoop loop)
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

    /// <summary>
    /// Springs whatever the player just stood on. Mirrors hit_trap().
    ///
    /// A sprung trap is revealed first, whether or not it does anything, so the
    /// player learns where it was. Feather fall saves the falls, free action
    /// saves the holds, and a saving throw saves nothing here - traps are not
    /// magic.
    /// </summary>
    public void HitTrap(int row, int column)
    {
        _loop.Movement.EndFind();
        _loop.Movement.ChangeTrap(row, column);

        CaveSquare square = _game.Cave[row, column];
        InvenType trap = _game.Objects[square.ObjectIndex];
        int damage = Rng.DamRoll(trap.DamageDice, trap.DamageSides);

        switch (trap.SubVal)
        {
            case 1: // an open pit
                _display.MessagePrint("You fell into a pit!");
                FallInto(trap, damage);
                break;

            case 2: // an arrow trap
                if (ThrownAtPlayer())
                {
                    _loop.TakeHit(damage, _game.Names.Describe(trap, withArticle: true));
                    _display.MessagePrint("An arrow hits you.");
                }
                else
                {
                    _display.MessagePrint("An arrow barely misses you.");
                }

                break;

            case 3: // a covered pit, which is left open behind them
                _display.MessagePrint("You fell into a covered pit.");
                FallInto(trap, damage);
                new DungeonGenerator(_game, _display).PlaceTrap(row, column, 0);
                break;

            case 4: // a trap door
                _display.MessagePrint("You fell through a trap door!");
                _loop.NewLevel = true;
                _game.DungeonLevel++;
                FallInto(trap, damage);

                // Forced out before the next level is generated, or the message
                // would arrive after the new level had been drawn.
                _display.MessagePrint(null);
                break;

            case 5: // sleep gas
                if (Player.Paralysis == 0)
                {
                    _display.MessagePrint("A strange white mist surrounds you!");

                    if (Player.FreeAction)
                    {
                        _display.MessagePrint("You are unaffected.");
                    }
                    else
                    {
                        _display.MessagePrint("You fall asleep.");
                        Player.Paralysis += Rng.RandInt(10) + 4;
                    }
                }

                break;

            case 6: // something hidden under a rock
                _loop.Movement.DeleteObject(row, column);
                new DungeonGenerator(_game, _display).PlaceObject(row, column, false);
                _display.MessagePrint("Hmmm, there was something under this rock.");
                break;

            case 7: // a dart that saps strength
                DartAtStat(trap, damage, Stat.Strength, Player.SustainStrength,
                    "A small dart weakens you!");
                break;

            case 8: // a teleport trap
                _loop.Teleporting = true;
                _display.MessagePrint("You hit a teleport trap!");

                // Lit before the player is thrown away from it, so they see what
                // caught them.
                _loop.Lighting.MoveLight(row, column, row, column);
                break;

            case 9: // falling rock, which leaves rubble behind
                _loop.TakeHit(damage, "a falling rock");
                _loop.Movement.DeleteObject(row, column);
                new DungeonGenerator(_game, _display).PlaceRubble(row, column);
                _display.MessagePrint("You are hit by falling rock.");
                break;

            case 10: // corroding gas
                // The message comes before the damage, which reads better than
                // being told what corroded before being told why.
                _display.MessagePrint("A strange red gas surrounds you.");
                _loop.Damage.CorrodeGas("corrosion gas");
                break;

            case 11: // a summoning rune, which is used up
                _loop.Movement.DeleteObject(row, column);

                int summoned = 2 + Rng.RandInt(3);
                for (int i = 0; i < summoned; i++)
                {
                    int y = row;
                    int x = column;
                    new DungeonGenerator(_game, _display).SummonMonster(ref y, ref x, false);
                }

                break;

            case 12: // fire
                _display.MessagePrint("You are enveloped in flames!");
                _loop.Damage.FireDamage(damage, "a fire trap");
                break;

            case 13: // acid
                _display.MessagePrint("You are splashed with acid!");
                _loop.Damage.AcidDamage(damage, "an acid trap");
                break;

            case 14: // poison gas
                _display.MessagePrint("A pungent green gas surrounds you!");
                _loop.Damage.PoisonGas(damage, "a poison gas trap");
                break;

            case 15: // blinding gas
                _display.MessagePrint("A black gas surrounds you!");
                Player.Blind += Rng.RandInt(50) + 50;
                break;

            case 16: // confusing gas
                _display.MessagePrint("A gas of scintillating colors surrounds you!");
                Player.Confused += Rng.RandInt(15) + 15;
                break;

            case 17: // a dart that slows
                if (ThrownAtPlayer())
                {
                    _loop.TakeHit(damage, _game.Names.Describe(trap, withArticle: true));
                    _display.MessagePrint("A small dart hits you!");

                    if (Player.FreeAction)
                    {
                        _display.MessagePrint("You are unaffected.");
                    }
                    else
                    {
                        Player.Slowed += Rng.RandInt(20) + 10;
                    }
                }
                else
                {
                    _display.MessagePrint("A small dart barely misses you.");
                }

                break;

            case 18: // a dart that saps health
                DartAtStat(trap, damage, Stat.Constitution, Player.SustainConstitution,
                    "A small dart saps your health!");
                break;

            case 19: // a secret door, which does nothing when stood on
            case 99: // a rune that scares monsters, not the player
                break;

            // The town's "traps" are the shop doors.
            case 101: EnterStore(0); break;
            case 102: EnterStore(1); break;
            case 103: EnterStore(2); break;
            case 104: EnterStore(3); break;
            case 105: EnterStore(4); break;
            case 106: EnterStore(5); break;

            default:
                _display.MessagePrint("Unknown trap value.");
                break;
        }
    }

    /// <summary>Falling, which feather fall turns into a gentle landing.</summary>
    private void FallInto(InvenType trap, int damage)
    {
        if (Player.FeatherFall)
        {
            _display.MessagePrint("You gently float down.");
        }
        else
        {
            _loop.TakeHit(damage, _game.Names.Describe(trap, withArticle: true));
        }
    }

    /// <summary>
    /// Whether something the trap threw finds its mark. The trap rolls against
    /// armour like any other attacker, at a fixed skill of its own.
    /// </summary>
    private bool ThrownAtPlayer() =>
        _loop.Combat.TestHit(
            125, 0, 0, Player.ArmourClass + Player.PlusToArmourClass, LevelSkill.SaveAndMisc);

    /// <summary>
    /// A dart that drains a stat, unless that stat is being sustained.
    /// </summary>
    private void DartAtStat(InvenType trap, int damage, int stat, bool sustained, string message)
    {
        if (!ThrownAtPlayer())
        {
            _display.MessagePrint("A small dart barely misses you.");
            return;
        }

        if (sustained)
        {
            _display.MessagePrint("A small dart hits you.");
            return;
        }

        _loop.Stats.Decrease(stat);
        _loop.TakeHit(damage, _game.Names.Describe(trap, withArticle: true));
        _display.MessagePrint(message);
    }

    /// <summary>
    /// Disarms a floor trap or a chest. Mirrors disarm_trap().
    ///
    /// The skill is the character's disarming, doubled for dexterity, plus wits
    /// and a share of their level; anything that clouds the senses divides it by
    /// ten, and the three of them stack, so a blind, confused, hallucinating
    /// character is a thousand times worse at it.
    ///
    /// Failing is not the same as failing badly: a near miss is just a wasted
    /// turn, but a bad one sets the thing off.
    /// </summary>
    public void DisarmTrap()
    {
        (bool taken, int direction) = _loop.ReadDirection();

        if (!taken)
        {
            return;
        }

        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;
        _game.Cave.Move(direction, ref row, ref column);

        CaveSquare square = _game.Cave[row, column];

        if (square.MonsterIndex > 1 && square.ObjectIndex != 0
            && (_game.Objects[square.ObjectIndex].TVal == ItemCategory.VisibleTrap
                || _game.Objects[square.ObjectIndex].TVal == ItemCategory.Chest))
        {
            Monster monster = _game.Monsters[square.MonsterIndex];

            string name = monster.Visible
                ? "The " + GameTables.CreatureList[monster.CreatureIndex].Name
                : "Something";

            _display.MessagePrint(name + " is in your way!");
            return;
        }

        if (square.ObjectIndex == 0)
        {
            NothingToDisarm();
            return;
        }

        int skill = Player.Disarm
            + (2 * Stats.DisarmBonus(Player))
            + Stats.Adjustment(Player, Stat.Intelligence)
            + (GameTables.ClassLevelAdjust[Player.Class][LevelSkill.Disarming]
               * Player.Level / 3);

        if (Player.Blind > 0 || _loop.Lighting.NoLight())
        {
            skill /= 10;
        }

        if (Player.Confused > 0)
        {
            skill /= 10;
        }

        if (Player.Hallucinating > 0)
        {
            skill /= 10;
        }

        InvenType trap = _game.Objects[square.ObjectIndex];

        if (trap.TVal == ItemCategory.VisibleTrap)
        {
            DisarmFloorTrap(trap, skill, direction, row, column);
        }
        else if (trap.TVal == ItemCategory.Chest)
        {
            DisarmChest(trap, skill, row, column);
        }
        else
        {
            NothingToDisarm();
        }
    }

    private void NothingToDisarm()
    {
        _display.MessagePrint("I do not see anything to disarm there.");
        _loop.FreeTurn = true;
    }

    /// <summary>
    /// A trap in the floor. Whether it is disarmed or set off, the character
    /// steps onto the square either way - which is why setting one off hurts.
    /// </summary>
    private void DisarmFloorTrap(
        InvenType trap, int skill, int direction, int row, int column)
    {
        if (skill + 100 - trap.Level > Rng.RandInt(100))
        {
            _display.MessagePrint("You have disarmed the trap.");
            Player.Experience += trap.P1;
            _loop.Movement.DeleteObject(row, column);

            StepOnto(direction);
            _loop.Levelling.PrintExperience();
            return;
        }

        // The bound is only rolled when there is something to roll: randint(0)
        // is not a question the generator can answer.
        if (skill > 5 && Rng.RandInt(skill) > 5)
        {
            _display.CountMessagePrint("You failed to disarm the trap.");
            return;
        }

        _display.MessagePrint("You set the trap off!");
        StepOnto(direction);
    }

    /// <summary>
    /// Steps onto the square that was being worked on, with the confusion put
    /// aside so that a confused character still lands on the trap they were
    /// fiddling with rather than wandering off.
    /// </summary>
    private void StepOnto(int direction)
    {
        int confused = Player.Confused;
        Player.Confused = 0;
        _loop.Movement.MoveChar(direction, false);
        Player.Confused = confused;
    }

    /// <summary>
    /// A chest, which is a different thing: the trap has to be known about
    /// before it can be worked on, and there is no stepping onto anything.
    /// </summary>
    private void DisarmChest(InvenType chest, int skill, int row, int column)
    {
        if (!ItemKnowledge.IsEnchantmentKnown(chest))
        {
            _display.MessagePrint("I don't see a trap.");
            _loop.FreeTurn = true;
            return;
        }

        if ((chest.Flags & ChestFlags.Trapped) == 0)
        {
            _display.MessagePrint("The chest was not trapped.");
            _loop.FreeTurn = true;
            return;
        }

        if (skill - chest.Level > Rng.RandInt(100))
        {
            chest.Flags &= ~ChestFlags.Trapped;

            chest.SpecialName = (chest.Flags & ChestFlags.Locked) != 0
                ? SpecialName.Locked
                : SpecialName.Disarmed;

            _display.MessagePrint("You have disarmed the chest.");
            _game.Knowledge.LearnEnchantment(chest);
            Player.Experience += chest.Level;
            _loop.Levelling.PrintExperience();
            return;
        }

        if (skill > 5 && Rng.RandInt(skill) > 5)
        {
            _display.CountMessagePrint("You failed to disarm the chest.");
            return;
        }

        _display.MessagePrint("You set a trap off!");
        _game.Knowledge.LearnEnchantment(chest);
        ChestTrap(row, column);
    }

    /// <summary>
    /// Springs whatever was set on a chest. Mirrors chest_trap().
    ///
    /// A chest can carry several traps at once, and all of them go off, which is
    /// what makes an unopened chest worth disarming rather than prising.
    /// </summary>
    public void ChestTrap(int row, int column)
    {
        InvenType chest = _game.Objects[_game.Cave[row, column].ObjectIndex];

        if ((chest.Flags & ChestFlags.LoseStrength) != 0)
        {
            _display.MessagePrint("A small needle has pricked you!");

            if (Player.SustainStrength)
            {
                _display.MessagePrint("You are unaffected.");
            }
            else
            {
                _loop.Stats.Decrease(Stat.Strength);
                _loop.TakeHit(Rng.DamRoll(1, 4), "a poison needle");
                _display.MessagePrint("You feel weakened!");
            }
        }

        if ((chest.Flags & ChestFlags.Poison) != 0)
        {
            _display.MessagePrint("A small needle has pricked you!");
            _loop.TakeHit(Rng.DamRoll(1, 6), "a poison needle");
            Player.Poisoned += 10 + Rng.RandInt(20);
        }

        if ((chest.Flags & ChestFlags.Paralyse) != 0)
        {
            _display.MessagePrint("A puff of yellow gas surrounds you!");

            if (Player.FreeAction)
            {
                _display.MessagePrint("You are unaffected.");
            }
            else
            {
                _display.MessagePrint("You choke and pass out.");
                Player.Paralysis = 10 + Rng.RandInt(20);
            }
        }

        if ((chest.Flags & ChestFlags.Summon) != 0)
        {
            for (int i = 0; i < 3; i++)
            {
                int y = row;
                int x = column;
                new DungeonGenerator(_game, _display).SummonMonster(ref y, ref x, false);
            }
        }

        if ((chest.Flags & ChestFlags.Explode) != 0)
        {
            _display.MessagePrint("There is a sudden explosion!");
            _loop.Movement.DeleteObject(row, column);
            _loop.TakeHit(Rng.DamRoll(5, 8), "an exploding chest");
        }
    }

    /// <summary>
    /// Pending: the shops are store1.c and store2.c, which are not ported.
    /// </summary>
    private void EnterStore(int which) =>
        _display.MessagePrint("The shops are not ported yet.");
}
