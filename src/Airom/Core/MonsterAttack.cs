// Ported from make_attack in Umoria 5.6 source/creature.c, with the small
// spells.c helpers it needs - teleport_away, lose_exp and aggravate_monster.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// What a monster does when it reaches the player.
///
/// A creature's attacks are a list of indexes into one shared table, so the same
/// bite can belong to a dozen different things. Each entry carries three
/// separate ideas: how it is delivered, what it looks like, and what it does -
/// which is why a monster can claw for cold and another can claw for acid.
///
/// The player learns by being hit. Every attack that is noticed is counted in
/// the monster memory, so the description of a creature fills in over a
/// campaign rather than being given.
/// </summary>
public sealed class MonsterAttack
{
    /// <summary>How much of the player's experience a life-drain takes. Umoria's MON_DRAIN_LIFE.</summary>
    public const int DrainLifePercent = 2;

    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public MonsterAttack(GameState game, Display display, GameLoop loop)
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

    private Inventory Pack => _game.Inventory;

    /// <summary>
    /// A monster attacks the player. Mirrors make_attack().
    ///
    /// Every attack in the creature's list is tried in turn, each with its own
    /// chance to land, and each stops if the player dies partway through.
    /// Protection from evil turns an attack aside entirely, which is the one
    /// case where a hit is announced as repelled rather than missed.
    /// </summary>
    public void MakeAttack(int index)
    {
        // Do not beat a dead body.
        if (_loop.Dead)
        {
            return;
        }

        Monster monster = _game.Monsters[index];
        CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

        string describer = monster.Visible ? "The " + creature.Name + " " : "It ";

        // How it would be written on a tombstone.
        string killer = (creature.MoveFlags & CreatureMove.Win) != 0
            ? "The " + creature.Name
            : (IsVowel(creature.Name[0]) ? "an " : "a ") + creature.Name;

        int attackNumber = 0;
        ReadOnlySpan<byte> attacks = creature.Attacks;

        for (int slot = 0; slot < attacks.Length && attacks[slot] != 0 && !_loop.Dead; slot++)
        {
            MonsterAttackType attack = GameTables.MonsterAttacks[attacks[slot]];
            int type = attack.Type;
            int description = attack.Description;

            // Protection from evil turns aside anything evil the player has the
            // level to face down.
            if (Player.ProtectionFromEvil > 0
                && (creature.DefenseFlags & CreatureDefense.Evil) != 0
                && Player.Level + 1 > creature.Level)
            {
                if (monster.Visible)
                {
                    _game.Memories[monster.CreatureIndex].Defense |= CreatureDefense.Evil;
                }

                type = 99;
                description = 99;
            }

            bool landed = Lands(type, creature);

            if (!landed)
            {
                // Only the physical attacks are worth reporting as a miss.
                if (description is >= 1 and <= 3 or 6)
                {
                    _loop.Disturb(true, false);
                    _display.MessagePrint(describer + "misses you.");
                }

                if (attackNumber < MonsterMemory.MaxAttacks - 1)
                {
                    attackNumber++;
                }
                else
                {
                    break;
                }

                continue;
            }

            _loop.Disturb(true, false);
            Announce(describer, description);

            // An unseen attacker teaches nothing, and a monster may be visible
            // when it strikes and gone by the time it matters.
            bool visible = monster.Visible;
            bool noticed = visible;

            int damage = Rng.DamRoll(attack.Dice, attack.Sides);
            noticed = Resolve(type, damage, killer, creature, monster, index, noticed);

            // Confusing touch is settled here rather than in the movement, so
            // that a repelled monster is not confused by an attack that never
            // landed.
            if (Player.ConfusingTouch && description != 99)
            {
                ConfuseAttacker(monster, creature, describer, visible);
            }

            // The count goes up when something was noticed, and also when the
            // player has felt this attack before - which is how the damage
            // range fills in.
            MonsterMemory memory = _game.Memories[monster.CreatureIndex];

            if ((noticed || (visible && memory.Attacks[attackNumber] != 0 && type != 99))
                && memory.Attacks[attackNumber] < byte.MaxValue)
            {
                memory.Attacks[attackNumber]++;
            }

            if (_loop.Dead && memory.Deaths < GameLoop.MaxShort)
            {
                memory.Deaths++;
            }

            if (attackNumber < MonsterMemory.MaxAttacks - 1)
            {
                attackNumber++;
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>
    /// Whether one attack gets through. Each kind has its own base chance, and
    /// two of them - stealing - are rolled against the player's level rather
    /// than their armour.
    /// </summary>
    private bool Lands(int type, CreatureType creature)
    {
        int armour = Player.ArmourClass + Player.PlusToArmourClass;

        return type switch
        {
            1 => Hit(60, creature, armour),
            2 => Hit(-3, creature, armour),
            3 or 4 or 5 or 7 or 8 => Hit(10, creature, armour),
            6 or 9 or 15 or 16 => Hit(0, creature, armour),
            10 or 11 or 17 or 18 => Hit(2, creature, armour),
            12 => Hit(5, creature, Player.Level) && Player.Gold > 0,
            13 => Hit(2, creature, Player.Level) && Pack.Count > 0,
            14 or 19 or 22 or 23 => Hit(5, creature, armour),
            20 => true,
            21 => Hit(20, creature, armour),
            24 => Hit(15, creature, armour) && Pack.Count > 0,
            99 => true,
            _ => false,
        };
    }

    private bool Hit(int baseToHit, CreatureType creature, int against) =>
        _loop.Combat.TestHit(
            baseToHit, creature.Level, 0, against, LevelSkill.SaveAndMisc);

    /// <summary>What the attack looked like, which is separate from what it did.</summary>
    private void Announce(string describer, int description)
    {
        string message = description switch
        {
            1 => describer + "hits you.",
            2 => describer + "bites you.",
            3 => describer + "claws you.",
            4 => describer + "stings you.",
            5 => describer + "touches you.",
            7 => describer + "gazes at you.",
            8 => describer + "breathes on you.",
            9 => describer + "spits on you.",
            10 => describer + "makes a horrible wail.",
            12 => describer + "crawls on you.",
            13 => describer + "releases a cloud of spores.",
            14 => describer + "begs you for money.",
            15 => "You've been slimed!",
            16 => describer + "crushes you.",
            17 => describer + "tramples you.",
            18 => describer + "drools on you.",
            19 => describer + Insult(),
            99 => describer + "is repelled.",
            _ => string.Empty,
        };

        if (message.Length != 0)
        {
            _display.MessagePrint(message);
        }
    }

    /// <summary>The nine ways a creature can be rude, for the ones that only insult.</summary>
    private string Insult() => Rng.RandInt(9) switch
    {
        1 => "insults you!",
        2 => "insults your mother!",
        3 => "gives you the finger!",
        4 => "humiliates you!",
        5 => "wets on your leg!",
        6 => "defiles you!",
        7 => "dances around you!",
        8 => "makes obscene gestures!",
        _ => "moons you!!!",
    };

    /// <summary>
    /// What the attack actually does.
    /// </summary>
    /// <returns>
    /// Whether the player noticed anything, which is what decides if the monster
    /// memory learns from it.
    /// </returns>
    private bool Resolve(
        int type, int damage, string killer, CreatureType creature, Monster monster,
        int index, bool noticed)
    {
        switch (type)
        {
            case 1:
                // Armour soaks a proportion rather than a fixed amount, rounding
                // the halfway case down.
                damage -= (Player.ArmourClass + Player.PlusToArmourClass) * damage / 200;
                _loop.TakeHit(damage, killer);
                return noticed;

            case 2:
                _loop.TakeHit(damage, killer);

                if (Player.SustainStrength)
                {
                    _display.MessagePrint("You feel weaker for a moment, but it passes.");
                }
                else if (Rng.RandInt(2) == 1)
                {
                    _display.MessagePrint("You feel weaker.");
                    _loop.Stats.Decrease(Stat.Strength);
                }
                else
                {
                    return false;
                }

                return noticed;

            case 3:
                _loop.TakeHit(damage, killer);

                if (Rng.RandInt(2) != 1)
                {
                    return false;
                }

                if (Player.Confused < 1)
                {
                    _display.MessagePrint("You feel confused.");
                    Player.Confused += Rng.RandInt(creature.Level);
                }
                else
                {
                    noticed = false;
                }

                Player.Confused += 3;
                return noticed;

            case 4:
                _loop.TakeHit(damage, killer);

                if (_loop.Combat.PlayerSaves())
                {
                    _display.MessagePrint("You resist the effects!");
                }
                else if (Player.Afraid < 1)
                {
                    _display.MessagePrint("You are suddenly afraid!");
                    Player.Afraid += 3 + Rng.RandInt(creature.Level);
                }
                else
                {
                    Player.Afraid += 3;
                    return false;
                }

                return noticed;

            case 5:
                _display.MessagePrint("You are enveloped in flames!");
                _loop.Damage.FireDamage(damage, killer);
                return noticed;

            case 6:
                _display.MessagePrint("You are covered in acid!");
                _loop.Damage.AcidDamage(damage, killer);
                return noticed;

            case 7:
                _display.MessagePrint("You are covered with frost!");
                _loop.Damage.ColdDamage(damage, killer);
                return noticed;

            case 8:
                _display.MessagePrint("Lightning strikes you!");
                _loop.Damage.LightningDamage(damage, killer);
                return noticed;

            case 9:
                _display.MessagePrint("A stinging red gas swirls about you.");
                _loop.Damage.CorrodeGas(killer);
                _loop.TakeHit(damage, killer);
                return noticed;

            case 10:
                _loop.TakeHit(damage, killer);

                if (Player.Blind < 1)
                {
                    Player.Blind += 10 + Rng.RandInt(creature.Level);
                    _display.MessagePrint("Your eyes begin to sting.");
                }
                else
                {
                    Player.Blind += 5;
                    return false;
                }

                return noticed;

            case 11:
                _loop.TakeHit(damage, killer);

                if (_loop.Combat.PlayerSaves())
                {
                    _display.MessagePrint("You resist the effects!");
                }
                else if (Player.Paralysis < 1)
                {
                    if (Player.FreeAction)
                    {
                        _display.MessagePrint("You are unaffected.");
                    }
                    else
                    {
                        Player.Paralysis = Rng.RandInt(creature.Level) + 3;
                        _display.MessagePrint("You are paralyzed.");
                    }
                }
                else
                {
                    return false;
                }

                return noticed;

            case 12:
                StealGold(index);
                return noticed;

            case 13:
                StealItem(index);
                return noticed;

            case 14:
                _loop.TakeHit(damage, killer);
                _display.MessagePrint("You feel very sick.");
                Player.Poisoned += Rng.RandInt(creature.Level) + 5;
                return noticed;

            case 15:
                _loop.TakeHit(damage, killer);

                if (Player.SustainDexterity)
                {
                    _display.MessagePrint("You feel clumsy for a moment, but it passes.");
                }
                else
                {
                    _display.MessagePrint("You feel more clumsy.");
                    _loop.Stats.Decrease(Stat.Dexterity);
                }

                return noticed;

            case 16:
                _loop.TakeHit(damage, killer);

                if (Player.SustainConstitution)
                {
                    _display.MessagePrint("Your body resists the effects of the disease.");
                }
                else
                {
                    _display.MessagePrint("Your health is damaged!");
                    _loop.Stats.Decrease(Stat.Constitution);
                }

                return noticed;

            case 17:
                _loop.TakeHit(damage, killer);
                _display.MessagePrint("You have trouble thinking clearly.");

                if (Player.SustainIntelligence)
                {
                    _display.MessagePrint("But your mind quickly clears.");
                }
                else
                {
                    _loop.Stats.Decrease(Stat.Intelligence);
                }

                return noticed;

            case 18:
                _loop.TakeHit(damage, killer);

                if (Player.SustainWisdom)
                {
                    _display.MessagePrint("Your wisdom is sustained.");
                }
                else
                {
                    _display.MessagePrint("Your wisdom is drained.");
                    _loop.Stats.Decrease(Stat.Wisdom);
                }

                return noticed;

            case 19:
                _display.MessagePrint("You feel your life draining away!");
                LoseExperience(damage + (Player.Experience / 100 * DrainLifePercent));
                return noticed;

            case 20:
                AggravateMonsters(20);
                return noticed;

            case 21:
                return Disenchant() && noticed;

            case 22:
                if (FindInPack(ItemCategory.Food, out int food))
                {
                    Pack.Destroy(food);
                    _display.MessagePrint("It got at your rations!");
                    return noticed;
                }

                return false;

            case 23:
                return EatLight() && noticed;

            case 24:
                return EatCharges(monster, creature) && noticed;

            default:
                return false;
        }
    }

    /// <summary>
    /// A thief takes gold and often vanishes with it, which is what makes one
    /// worth killing before it lands a blow.
    /// </summary>
    private void StealGold(int index)
    {
        if (Player.Paralysis < 1 && Rng.RandInt(124) < Player.UseStat[Stat.Dexterity])
        {
            _display.MessagePrint("You quickly protect your money pouch!");
        }
        else
        {
            int taken = (Player.Gold / 10) + Rng.RandInt(25);
            Player.Gold = taken > Player.Gold ? 0 : Player.Gold - taken;
            _display.MessagePrint("Your purse feels lighter.");
            _display.PrintGold(Player);
        }

        if (Rng.RandInt(2) == 1)
        {
            _display.MessagePrint("There is a puff of smoke!");
            TeleportAway(index, MonsterAi.MaxSight);
        }
    }

    private void StealItem(int index)
    {
        if (Player.Paralysis < 1 && Rng.RandInt(124) < Player.UseStat[Stat.Dexterity])
        {
            _display.MessagePrint("You grab hold of your backpack!");
        }
        else
        {
            Pack.Destroy(Rng.RandInt(Pack.Count) - 1);
            _display.MessagePrint("Your backpack feels lighter.");
        }

        if (Rng.RandInt(2) == 1)
        {
            _display.MessagePrint("There is a puff of smoke!");
            TeleportAway(index, MonsterAi.MaxSight);
        }
    }

    /// <summary>
    /// Rubs the enchantment off one worn thing. Nothing is sent below zero, so a
    /// plain item cannot be made cursed this way.
    /// </summary>
    private bool Disenchant()
    {
        int slot = Rng.RandInt(7) switch
        {
            1 => Inventory.WieldSlot,
            2 => Inventory.BodySlot,
            3 => Inventory.ArmSlot,
            4 => Inventory.OuterSlot,
            5 => Inventory.HandsSlot,
            6 => Inventory.HeadSlot,
            _ => Inventory.FeetSlot,
        };

        InvenType item = Pack[slot];
        bool changed = false;

        if (item.ToHit > 0)
        {
            item.ToHit -= (short)Rng.RandInt(2);
            item.ToHit = Math.Max(item.ToHit, (short)0);
            changed = true;
        }

        if (item.ToDam > 0)
        {
            item.ToDam -= (short)Rng.RandInt(2);
            item.ToDam = Math.Max(item.ToDam, (short)0);
            changed = true;
        }

        if (item.ToAc > 0)
        {
            item.ToAc -= (short)Rng.RandInt(2);
            item.ToAc = Math.Max(item.ToAc, (short)0);
            changed = true;
        }

        if (changed)
        {
            _display.MessagePrint("There is a static feeling in the air.");
            _loop.Equipment.Recalculate();
        }

        return changed;
    }

    /// <summary>Drinks the oil out of the player's lamp, but never puts it out.</summary>
    /// <summary>Reached from tests, which cannot arrange to be bitten.</summary>
    public void EatLightForTest() => EatLight();

    private bool EatLight()
    {
        InvenType light = _game.Inventory[Inventory.LightSlot];

        if (light.P1 <= 0)
        {
            return false;
        }

        light.P1 -= (short)(250 + Rng.RandInt(250));

        // Never quite out: something that drinks a lamp dry leaves a mouthful,
        // so the player is robbed rather than blinded.
        if (light.P1 < 1)
        {
            light.P1 = 1;
        }

        if (Player.Blind >= 1)
        {
            return false;
        }

        _display.MessagePrint("Your light dims.");
        return true;
    }

    /// <summary>
    /// Drains a wand or a staff, and heals on what it drinks - which is why a
    /// full wand is worth keeping away from one.
    /// </summary>
    private bool EatCharges(Monster monster, CreatureType creature)
    {
        int slot = Rng.RandInt(Pack.Count) - 1;
        InvenType item = Pack[slot];

        if ((item.TVal != ItemCategory.Staff && item.TVal != ItemCategory.Wand)
            || item.P1 <= 0)
        {
            return false;
        }

        monster.HitPoints += creature.Level * item.P1;
        item.P1 = 0;

        if (!ItemKnowledge.IsEnchantmentKnown(item))
        {
            ItemKnowledge.AddInscription(item, Identification.Empty);
        }

        _display.MessagePrint("Energy drains from your pack!");
        return true;
    }

    /// <summary>Glowing hands, settled after the monster has actually landed a blow.</summary>
    private void ConfuseAttacker(
        Monster monster, CreatureType creature, string describer, bool visible)
    {
        _display.MessagePrint("Your hands stop glowing.");
        Player.ConfusingTouch = false;

        if (Rng.RandInt(MonsterLevels.MaxMonsterLevel) < creature.Level
            || (creature.DefenseFlags & CreatureDefense.NeverSleeps) != 0)
        {
            _display.MessagePrint(describer + "is unaffected.");
        }
        else
        {
            _display.MessagePrint(describer + "appears confused.");

            monster.Confused = monster.Confused != 0
                ? monster.Confused + 3
                : 2 + Rng.RandInt(16);
        }

        if (visible && !_loop.Dead && Rng.RandInt(4) == 1)
        {
            _game.Memories[monster.CreatureIndex].Defense |=
                (ushort)(creature.DefenseFlags & CreatureDefense.NeverSleeps);
        }
    }

    /// <summary>
    /// Throws a monster somewhere else on the level. Mirrors teleport_away().
    ///
    /// The distance grows every ten failed attempts, so a monster boxed into a
    /// corner still gets away rather than looping forever.
    /// </summary>
    public void TeleportAway(int index, int distance)
    {
        Monster monster = _game.Monsters[index];

        int row;
        int column;
        int tries = 0;

        do
        {
            do
            {
                row = monster.Row + (Rng.RandInt((2 * distance) + 1) - (distance + 1));
                column = monster.Column + (Rng.RandInt((2 * distance) + 1) - (distance + 1));
            }
            while (!_game.Cave.InBounds(row, column));

            tries++;

            if (tries > 9)
            {
                tries = 0;
                distance += 5;
            }
        }
        while (_game.Cave[row, column].Feature >= CaveFeature.MinClosedSpace
               || _game.Cave[row, column].MonsterIndex != 0);

        _loop.Lighting.MoveRecord(monster.Row, monster.Column, row, column);
        _loop.Lighting.LightSpot(monster.Row, monster.Column);

        monster.Row = row;
        monster.Column = column;

        // It is not visible where it has landed until something says so.
        monster.Visible = false;
        monster.DistanceToPlayer =
            Cave.Distance(_game.CharacterRow, _game.CharacterColumn, row, column);

        _loop.MonsterAi.UpdateMonster(index);
    }

    /// <summary>
    /// Drains experience, and levels the player back down if it took enough.
    /// Mirrors lose_exp().
    /// </summary>
    public void LoseExperience(int amount)
    {
        Player.Experience = amount > Player.Experience ? 0 : Player.Experience - amount;
        _loop.Levelling.PrintExperience();

        int level = 0;
        while (GameTables.PlayerExperience[level] * Player.ExperienceFactor / 100
               <= Player.Experience)
        {
            level++;
        }

        // Once more, because level one's cost is the first entry.
        level++;

        if (Player.Level == level)
        {
            return;
        }

        Player.Level = level;
        _loop.Levelling.CalculateHitPoints();

        int realm = GameTables.Classes[Player.Class].SpellRealm;

        if (realm is SpellRealm.Mage or SpellRealm.Priest)
        {
            _loop.Stats.RecalculateSpells(
                realm == SpellRealm.Mage ? Stat.Intelligence : Stat.Wisdom);
        }

        _display.PrintLevel(Player);
        _display.PrintTitle(Player);
    }

    /// <summary>
    /// Wakes every monster nearby and hurries them along. Mirrors
    /// aggravate_monster().
    /// </summary>
    public bool AggravateMonsters(int distance)
    {
        bool stirred = false;

        for (int i = _game.Monsters.Count - 1; i >= MonsterPool.FirstIndex; i--)
        {
            Monster monster = _game.Monsters[i];
            monster.Sleep = 0;

            if (monster.DistanceToPlayer <= distance && monster.Speed < 2)
            {
                monster.Speed++;
                stirred = true;
            }
        }

        if (stirred)
        {
            _display.MessagePrint("You hear a sudden stirring in the distance!");
        }

        return stirred;
    }

    /// <summary>
    /// Casts one of a monster's spells. Mirrors mon_cast_spell().
    ///
    /// Three things must hold before anything is cast: the frequency roll, a
    /// range the spell can reach, and a clear line of sight. Then one spell is
    /// picked at random from the ones the creature knows - the flags are a bit
    /// set, and the choice is made by pulling the set bits out one at a time.
    ///
    /// Casting is loud: everything except the two quiet spells interrupts a
    /// rest or a run.
    /// </summary>
    /// <returns>Whether the monster spent its turn casting.</returns>
    public bool CastSpell(int index)
    {
        if (_loop.Dead)
        {
            return false;
        }

        Monster monster = _game.Monsters[index];
        CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

        // A one in however many chance, and only within range and in sight.
        if (Rng.RandInt(creature.SpellFrequency) != 1
            || monster.DistanceToPlayer > MaxSpellDistance
            || !LineOfSight.Between(
                _game.Cave, _game.CharacterRow, _game.CharacterColumn,
                monster.Row, monster.Column))
        {
            return false;
        }

        // It may not be lit yet, and the player should see what cast at them.
        _loop.MonsterAi.UpdateMonster(index);

        string describer = monster.Visible ? "The " + creature.Name + " " : "It ";

        string killer = (creature.MoveFlags & CreatureMove.Win) != 0
            ? "The " + creature.Name
            : (IsVowel(creature.Name[0]) ? "an " : "a ") + creature.Name;

        // Pull the known spells out of the flags, one bit at a time.
        Span<int> choices = stackalloc int[32];
        int count = 0;
        uint bits = creature.SpellFlags & ~CreatureSpell.Frequency;

        while (bits != 0)
        {
            int bit = System.Numerics.BitOperations.TrailingZeroCount(bits);
            choices[count++] = bit;
            bits &= ~(1u << bit);
        }

        int spell = choices[Rng.RandInt(count) - 1] + 1;

        // Everything except slipping away and draining mana is loud enough to
        // interrupt whatever the player was doing.
        if (spell > 6 && spell != 17)
        {
            _loop.Disturb(true, false);
        }

        // The spells with no announcement of their own are simply "a spell".
        if ((spell is < 14 and > 6) || spell == 16)
        {
            _display.MessagePrint(describer + "casts a spell.");
        }

        Cast(spell, index, monster, creature, describer, killer);

        if (monster.Visible)
        {
            MonsterMemory memory = _game.Memories[monster.CreatureIndex];
            memory.Spells |= 1u << (spell - 1);

            // The frequency is learned by counting castings, up to the real
            // value.
            if ((memory.Spells & CreatureSpell.Frequency) != CreatureSpell.Frequency)
            {
                memory.Spells++;
            }

            if (_loop.Dead && memory.Deaths < GameLoop.MaxShort)
            {
                memory.Deaths++;
            }
        }

        return true;
    }

    private void Cast(
        int spell, int index, Monster monster, CreatureType creature,
        string describer, string killer)
    {
        switch (spell)
        {
            case 5: // slip away a short distance
                TeleportAway(index, 5);
                break;

            case 6: // slip away entirely
                TeleportAway(index, MonsterAi.MaxSight);
                break;

            case 7: // drag the player over
                _loop.Spells.TeleportTo(monster.Row, monster.Column);
                break;

            case 8:
                Wound(3, killer);
                break;

            case 9:
                Wound(8, killer);
                break;

            case 10: // hold
                if (Player.FreeAction)
                {
                    _display.MessagePrint("You are unaffected.");
                }
                else if (_loop.Combat.PlayerSaves())
                {
                    _display.MessagePrint("You resist the effects of the spell.");
                }
                else if (Player.Paralysis > 0)
                {
                    Player.Paralysis += 2;
                }
                else
                {
                    Player.Paralysis = Rng.RandInt(5) + 4;
                }

                break;

            case 11: // blind
                if (_loop.Combat.PlayerSaves())
                {
                    _display.MessagePrint("You resist the effects of the spell.");
                }
                else if (Player.Blind > 0)
                {
                    Player.Blind += 6;
                }
                else
                {
                    Player.Blind += 12 + Rng.RandInt(3);
                }

                break;

            case 12: // confuse
                if (_loop.Combat.PlayerSaves())
                {
                    _display.MessagePrint("You resist the effects of the spell.");
                }
                else if (Player.Confused > 0)
                {
                    Player.Confused += 2;
                }
                else
                {
                    Player.Confused = Rng.RandInt(5) + 3;
                }

                break;

            case 13: // frighten
                if (_loop.Combat.PlayerSaves())
                {
                    _display.MessagePrint("You resist the effects of the spell.");
                }
                else if (Player.Afraid > 0)
                {
                    Player.Afraid += 2;
                }
                else
                {
                    Player.Afraid = Rng.RandInt(5) + 3;
                }

                break;

            case 14: // call for help
                _display.MessagePrint(describer + "magically summons a monster!");
                Summon(index, undead: false);
                break;

            case 15:
                _display.MessagePrint(describer + "magically summons an undead!");
                Summon(index, undead: true);
                break;

            case 16: // slow
                if (Player.FreeAction)
                {
                    _display.MessagePrint("You are unaffected.");
                }
                else if (_loop.Combat.PlayerSaves())
                {
                    _display.MessagePrint("You resist the effects of the spell.");
                }
                else if (Player.Slowed > 0)
                {
                    Player.Slowed += 2;
                }
                else
                {
                    Player.Slowed = Rng.RandInt(5) + 3;
                }

                break;

            case 17: // drink the player's magic, and grow fat on it
                DrainMana(monster, creature, describer);
                break;

            case 20:
                _display.MessagePrint(describer + "breathes lightning.");
                Breathe(SpellElement.Lightning, monster.HitPoints / 4, killer, index);
                break;

            case 21:
                _display.MessagePrint(describer + "breathes gas.");
                Breathe(SpellElement.PoisonGas, monster.HitPoints / 3, killer, index);
                break;

            case 22:
                _display.MessagePrint(describer + "breathes acid.");
                Breathe(SpellElement.Acid, monster.HitPoints / 3, killer, index);
                break;

            case 23:
                _display.MessagePrint(describer + "breathes frost.");
                Breathe(SpellElement.Frost, monster.HitPoints / 3, killer, index);
                break;

            case 24:
                _display.MessagePrint(describer + "breathes fire.");
                Breathe(SpellElement.Fire, monster.HitPoints / 3, killer, index);
                break;

            default:
                _display.MessagePrint(describer + "cast unknown spell.");
                break;
        }
    }

    private void Wound(int dice, string killer)
    {
        if (_loop.Combat.PlayerSaves())
        {
            _display.MessagePrint("You resist the effects of the spell.");
        }
        else
        {
            _loop.TakeHit(Rng.DamRoll(dice, 8), killer);
        }
    }

    private void Breathe(int element, int damage, string killer, int index) =>
        _loop.Spells.Breath(
            element, _game.CharacterRow, _game.CharacterColumn, damage, killer, index);

    /// <summary>
    /// Calls something else in. The monster being processed has to be named, in
    /// case the list has to be compacted to make room.
    /// </summary>
    private void Summon(int index, bool undead)
    {
        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;

        _game.Monsters.ScanIndex = index;

        var generator = new DungeonGenerator(_game, _display);

        if (undead)
        {
            generator.SummonUndead(ref row, ref column);
        }
        else
        {
            generator.SummonMonster(ref row, ref column, false);
        }

        _game.Monsters.ScanIndex = -1;

        _loop.MonsterAi.UpdateMonster(_game.Cave[row, column].MonsterIndex);
    }

    /// <summary>
    /// Drinks the player's spell points and heals on them, which is why a mage
    /// is worth more to some monsters than a warrior.
    /// </summary>
    private void DrainMana(Monster monster, CreatureType creature, string describer)
    {
        if (Player.CurrentMana <= 0)
        {
            return;
        }

        _loop.Disturb(true, false);
        _display.MessagePrint(describer + "draws psychic energy from you!");

        if (monster.Visible)
        {
            _display.MessagePrint(describer + "appears healthier.");
        }

        int drawn = (Rng.RandInt(creature.Level) >> 1) + 1;

        if (drawn > Player.CurrentMana)
        {
            drawn = Player.CurrentMana;
            Player.CurrentMana = 0;
            Player.ManaFraction = 0;
        }
        else
        {
            Player.CurrentMana -= drawn;
        }

        _display.PrintCurrentMana(Player);
        monster.HitPoints += 6 * drawn;
    }

    /// <summary>How far a monster's spell reaches. Umoria's MAX_SPELL_DIS.</summary>
    public const int MaxSpellDistance = 20;

    private bool FindInPack(int category, out int slot)
    {
        for (int i = 0; i < Pack.Count; i++)
        {
            if (Pack[i].TVal == category)
            {
                slot = i;
                return true;
            }
        }

        slot = -1;
        return false;
    }

    private static bool IsVowel(char letter) =>
        letter is 'a' or 'e' or 'i' or 'o' or 'u' or 'A' or 'E' or 'I' or 'O' or 'U';
}
