// Ported from the combat half of Umoria 5.6 source/moria3.c - py_attack,
// mon_take_hit, monster_death and summon_object - with the arithmetic behind
// them from source/misc3.c and test_hit from source/moria1.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Which of the five things a level improves is being asked about. Mirrors
/// Umoria's CLA_* indexes into class_level_adj.
/// </summary>
public static class LevelSkill
{
    public const int Fighting = 0;
    public const int Shooting = 1;
    public const int MagicDevice = 2;
    public const int Disarming = 3;

    /// <summary>Saving throws, and the odd hit that is nobody's speciality.</summary>
    public const int SaveAndMisc = 4;
}

/// <summary>
/// Hitting things, and what happens when they die.
///
/// A blow is three separate rolls: whether it lands, how hard, and whether it
/// was good enough to count for extra. The first is deliberately generous at
/// both ends - one roll in twenty always misses and one always hits - so no
/// amount of armour makes a creature untouchable, and no amount of skill makes a
/// fight certain.
/// </summary>
public sealed class Combat
{
    /// <summary>
    /// How much each point of to-hit is worth against armour. Umoria's
    /// BTH_PLUS_ADJ.
    /// </summary>
    public const int ToHitWeight = 3;

    /// <summary>The most experience a character can hold. Umoria's MAX_EXP.</summary>
    public const int MaxExperience = 9999999;

    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Combat(GameState game, Display display, GameLoop loop)
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
    /// Whether a blow lands. Mirrors test_hit().
    ///
    /// One roll in twenty always misses and one always hits, whatever the
    /// numbers say. Being hit at is disturbing in itself, which is why this
    /// interrupts a rest before working anything out.
    /// </summary>
    public bool TestHit(int baseToHit, int level, int plusToHit, int armour, int skill)
    {
        _loop.Disturb(true, false);

        int chance = baseToHit + (plusToHit * ToHitWeight)
            + (level * GameTables.ClassLevelAdjust[Player.Class][skill]);

        // The plus can be negative when the weapon is too heavy to swing.
        int die = Rng.RandInt(20);

        return die != 1
            && (die == 20 || (chance > 0 && Rng.RandInt(chance) > armour));
    }

    /// <summary>
    /// How many blows a weapon allows, and what it costs in accuracy. Mirrors
    /// attack_blows().
    ///
    /// Strength against the weapon's weight decides how often it can be swung,
    /// and dexterity how well. A weapon too heavy to lift properly is swung once
    /// and clumsily.
    /// </summary>
    public int AttackBlows(int weight, out int plusToHit)
    {
        int strength = Player.UseStat[Stat.Strength];
        int dexterity = Player.UseStat[Stat.Dexterity];

        if (strength * 15 < weight)
        {
            plusToHit = (strength * 15) - weight;
            return 1;
        }

        plusToHit = 0;

        int dexterityBand = dexterity switch
        {
            < 10 => 0,
            < 19 => 1,
            < 68 => 2,
            < 108 => 3,
            < 118 => 4,
            _ => 5,
        };

        int ratio = strength * 10 / weight;
        int strengthBand = ratio switch
        {
            < 2 => 0,
            < 3 => 1,
            < 4 => 2,
            < 5 => 3,
            < 7 => 4,
            < 9 => 5,
            _ => 6,
        };

        return GameTables.BlowsTable[strengthBand][dexterityBand];
    }

    /// <summary>
    /// What a weapon's own magic adds. Mirrors tot_dam().
    ///
    /// A weapon that slays a kind of creature does so only against that kind,
    /// and using it teaches the player what the creature is - which is why the
    /// monster memory is written here rather than only when one dies.
    /// </summary>
    public int TotalDamage(InvenType weapon, int damage, int creatureIndex)
    {
        ArgumentNullException.ThrowIfNull(weapon);

        bool applies = (weapon.Flags & ItemFlags.EgoWeapon) != 0
            && ((weapon.TVal >= ItemCategory.SlingAmmo && weapon.TVal <= ItemCategory.Arrow)
                || (weapon.TVal >= ItemCategory.Hafted && weapon.TVal <= ItemCategory.Sword)
                || weapon.TVal == ItemCategory.Flask);

        if (!applies)
        {
            return damage;
        }

        CreatureType creature = GameTables.CreatureList[creatureIndex];
        MonsterMemory memory = _game.Memories[creatureIndex];

        if ((creature.DefenseFlags & CreatureDefense.Dragon) != 0
            && (weapon.Flags & ItemFlags.SlayDragon) != 0)
        {
            memory.Defense |= CreatureDefense.Dragon;
            return damage * 4;
        }

        if ((creature.DefenseFlags & CreatureDefense.Undead) != 0
            && (weapon.Flags & ItemFlags.SlayUndead) != 0)
        {
            memory.Defense |= CreatureDefense.Undead;
            return damage * 3;
        }

        if ((creature.DefenseFlags & CreatureDefense.Animal) != 0
            && (weapon.Flags & ItemFlags.SlayAnimal) != 0)
        {
            memory.Defense |= CreatureDefense.Animal;
            return damage * 2;
        }

        if ((creature.DefenseFlags & CreatureDefense.Evil) != 0
            && (weapon.Flags & ItemFlags.SlayEvil) != 0)
        {
            memory.Defense |= CreatureDefense.Evil;
            return damage * 2;
        }

        if ((creature.DefenseFlags & CreatureDefense.HurtByFrost) == 0
            && (weapon.Flags & ItemFlags.FrostBrand) != 0)
        {
            return damage * 3 / 2;
        }

        if ((creature.DefenseFlags & CreatureDefense.HurtByFire) == 0
            && (weapon.Flags & ItemFlags.FlameTongue) != 0)
        {
            return damage * 3 / 2;
        }

        return damage;
    }

    /// <summary>
    /// Whether a blow was good enough to count for more. Mirrors
    /// critical_blow().
    ///
    /// A heavy weapon, a well-made one and an experienced arm all help. The
    /// multiplier depends on a second roll, so the same blow can be good,
    /// excellent or great.
    /// </summary>
    public int CriticalBlow(int weight, int plusToHit, int damage, int skill)
    {
        int chance = weight + (5 * plusToHit)
            + (GameTables.ClassLevelAdjust[Player.Class][skill] * Player.Level);

        if (Rng.RandInt(5000) > chance)
        {
            return damage;
        }

        weight += Rng.RandInt(650);

        if (weight < 400)
        {
            _display.MessagePrint("It was a good hit! (x2 damage)");
            return (2 * damage) + 5;
        }

        if (weight < 700)
        {
            _display.MessagePrint("It was an excellent hit! (x3 damage)");
            return (3 * damage) + 10;
        }

        if (weight < 900)
        {
            _display.MessagePrint("It was a superb hit! (x4 damage)");
            return (4 * damage) + 15;
        }

        _display.MessagePrint("It was a *GREAT* hit! (x5 damage)");
        return (5 * damage) + 20;
    }

    /// <summary>Whether the player shrugs something off. Mirrors player_saves().</summary>
    public bool PlayerSaves()
    {
        int chance = Player.Save + Stats.Adjustment(Player, Stat.Wisdom)
            + (GameTables.ClassLevelAdjust[Player.Class][LevelSkill.SaveAndMisc]
               * Player.Level / 3);

        return Rng.RandInt(100) <= chance;
    }

    /// <summary>
    /// Scatters what a dying creature was carrying. Mirrors summon_object().
    ///
    /// Twenty tries are made per item to find a lit, empty floor square in
    /// sight, which is why a monster killed in a doorway drops its treasure into
    /// the room rather than into the rock.
    /// </summary>
    /// <returns>
    /// What the player was seen to get: the count of objects in the low byte and
    /// the gold above it, exactly as the original packs it.
    /// </returns>
    public int SummonObject(int row, int column, int count, int kind)
    {
        int wanted = kind is 1 or 5 ? 1 : 256;
        int seen = 0;

        do
        {
            int tries = 0;
            do
            {
                int y = row - 3 + Rng.RandInt(5);
                int x = column - 3 + Rng.RandInt(5);

                if (_game.Cave.InBounds(y, x)
                    && LineOfSight.Between(_game.Cave, row, column, y, x))
                {
                    CaveSquare square = _game.Cave[y, x];

                    if (square.Feature <= CaveFeature.MaxOpenSpace && square.ObjectIndex == 0)
                    {
                        if (kind is 3 or 7)
                        {
                            // Half objects, half gold.
                            wanted = Rng.RandInt(100) < 50 ? 1 : 256;
                        }

                        var generator = new DungeonGenerator(_game);

                        if (wanted == 1)
                        {
                            generator.PlaceObject(y, x, kind >= 4);
                        }
                        else
                        {
                            generator.PlaceGold(y, x);
                        }

                        _loop.Lighting.LightSpot(y, x);

                        if (square.PermanentLight || square.TemporaryLight || square.FieldMark)
                        {
                            seen += wanted;
                        }

                        tries = 20;
                    }
                }

                tries++;
            }
            while (tries <= 20);

            count--;
        }
        while (count != 0);

        return seen;
    }

    /// <summary>
    /// Rewards the killer. Mirrors monster_death().
    ///
    /// What a creature drops is decided by the same flags that say what it
    /// carries, and the counts are rolled here rather than being fixed - which
    /// is why the same kind of monster is worth robbing twice.
    /// </summary>
    /// <returns>The flags the player was seen to learn, for the monster memory.</returns>
    public uint MonsterDeath(int row, int column, uint flags)
    {
        int kind = 0;

        if ((flags & CreatureMove.CarriesObject) != 0)
        {
            kind = 1;
        }

        if ((flags & CreatureMove.CarriesGold) != 0)
        {
            kind += 2;
        }

        if ((flags & CreatureMove.SmallObject) != 0)
        {
            kind += 4;
        }

        int count = 0;

        if ((flags & CreatureMove.Drop60Percent) != 0 && Rng.RandInt(100) < 60)
        {
            count++;
        }

        if ((flags & CreatureMove.Drop90Percent) != 0 && Rng.RandInt(100) < 90)
        {
            count++;
        }

        if ((flags & CreatureMove.Drop1d2Objects) != 0)
        {
            count += Rng.RandInt(2);
        }

        if ((flags & CreatureMove.Drop2d2Objects) != 0)
        {
            count += Rng.DamRoll(2, 2);
        }

        if ((flags & CreatureMove.Drop4d2Objects) != 0)
        {
            count += Rng.DamRoll(4, 2);
        }

        int seen = count > 0 ? SummonObject(row, column, count, kind) : 0;

        if ((flags & CreatureMove.Win) != 0 && !_loop.Dead)
        {
            _game.TotalWinner = true;
            _display.PrintWinner(Player);
            _display.MessagePrint("*** CONGRATULATIONS *** You have won the game.");
            _display.MessagePrint(
                "You cannot save this game, but you may retire when ready.");
        }

        if (seen == 0)
        {
            return 0;
        }

        uint learned = 0;

        if ((seen & 255) != 0)
        {
            learned |= CreatureMove.CarriesObject;

            if ((kind & 0x04) != 0)
            {
                learned |= CreatureMove.SmallObject;
            }
        }

        if (seen >= 256)
        {
            learned |= CreatureMove.CarriesGold;
        }

        // The count of things dropped, packed into the treasure field.
        int dropped = (seen % 256) + (seen / 256);
        return learned | ((uint)dropped << CreatureMove.TreasureShift);
    }

    /// <summary>
    /// Wounds a monster, and removes it if that was enough. Mirrors
    /// mon_take_hit().
    ///
    /// The experience is divided by the player's level, so the same kill is
    /// worth less the stronger they get, and the remainder is carried in
    /// sixteenths rather than dropped.
    /// </summary>
    /// <returns>The kind of creature killed, or -1 if it survived.</returns>
    public int MonsterTakeHit(int monsterIndex, int damage)
    {
        Monster monster = _game.Monsters[monsterIndex];
        monster.HitPoints -= damage;
        monster.Sleep = 0;

        if (monster.HitPoints >= 0)
        {
            return -1;
        }

        CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];
        uint learned = MonsterDeath(monster.Row, monster.Column, creature.MoveFlags);

        if ((Player.Blind < 1 && monster.Visible)
            || (creature.MoveFlags & CreatureMove.Win) != 0)
        {
            MonsterMemory memory = _game.Memories[monster.CreatureIndex];

            // Remember the most treasure this kind has ever been seen to drop,
            // rather than the last.
            uint known = (memory.Move & CreatureMove.Treasure) >> CreatureMove.TreasureShift;

            if (known > ((learned & CreatureMove.Treasure) >> CreatureMove.TreasureShift))
            {
                learned = (learned & ~CreatureMove.Treasure)
                    | (known << CreatureMove.TreasureShift);
            }

            memory.Move = (memory.Move & ~CreatureMove.Treasure) | learned;

            if (memory.Kills < GameLoop.MaxShort)
            {
                memory.Kills++;
            }
        }

        long gained = (long)creature.KillExperience * creature.Level;
        int experience = (int)(gained / Player.Level);
        long fraction = ((gained % Player.Level) * 0x10000L / Player.Level)
            + Player.ExperienceFraction;

        if (fraction >= 0x10000L)
        {
            experience++;
            Player.ExperienceFraction = (int)(fraction - 0x10000L);
        }
        else
        {
            Player.ExperienceFraction = (int)fraction;
        }

        Player.Experience += experience;

        // The experience cannot be printed here: the "welcome to level" message
        // would arrive before the "monster dies" one.
        int killed = monster.CreatureIndex;

        // Deleting a monster while creatures() is scanning the list would give
        // another monster two turns, so the delete is done in two halves when
        // the call came from inside that scan.
        if (_game.Monsters.ScanIndex < monsterIndex)
        {
            _game.Monsters.Delete(monsterIndex, _game.Cave, _loop.Lighting);
        }
        else
        {
            _game.Monsters.MarkDead(monsterIndex, _game.Cave, _loop.Lighting);
        }

        return killed;
    }

    /// <summary>
    /// The player hits whatever is on a square. Mirrors py_attack().
    ///
    /// Bare hands get two feeble blows; a weapon gets as many as the arm can
    /// manage. An unseen creature is harder to hit, and missiles used as a
    /// melee weapon are spent as they are swung.
    /// </summary>
    public void Attack(int row, int column)
    {
        int monsterIndex = _game.Cave[row, column].MonsterIndex;
        Monster monster = _game.Monsters[monsterIndex];
        int creatureIndex = monster.CreatureIndex;
        CreatureType creature = GameTables.CreatureList[creatureIndex];

        monster.Sleep = 0;

        InvenType weapon = _game.Inventory[Inventory.WieldSlot];

        // The player only names what they can see.
        string name = monster.Visible ? "the " + creature.Name : "it";

        int blows;
        int plusToHit;

        if (weapon.TVal != ItemCategory.Nothing)
        {
            blows = AttackBlows(weapon.Weight, out plusToHit);
        }
        else
        {
            blows = 2;
            plusToHit = -3;
        }

        // Missiles swung by hand get one blow, whatever the arm could manage.
        if (weapon.TVal >= ItemCategory.SlingAmmo && weapon.TVal <= ItemCategory.Spike)
        {
            blows = 1;
        }

        plusToHit += Player.PlusToHit;

        int baseToHit = monster.Visible
            ? Player.BaseToHit
            : (Player.BaseToHit / 2)
              - (plusToHit * (ToHitWeight - 1))
              - (Player.Level
                 * GameTables.ClassLevelAdjust[Player.Class][LevelSkill.Fighting] / 2);

        do
        {
            if (TestHit(baseToHit, Player.Level, plusToHit, creature.Ac,
                        LevelSkill.Fighting))
            {
                _display.MessagePrint("You hit " + name + ".");

                int damage;

                if (weapon.TVal != ItemCategory.Nothing)
                {
                    damage = Rng.DamRoll(weapon.DamageDice, weapon.DamageSides);
                    damage = TotalDamage(weapon, damage, creatureIndex);
                    damage = CriticalBlow(weapon.Weight, plusToHit, damage, LevelSkill.Fighting);
                }
                else
                {
                    damage = Rng.DamRoll(1, 1);
                    damage = CriticalBlow(1, 0, damage, LevelSkill.Fighting);
                }

                damage += Player.PlusToDamage;

                if (damage < 0)
                {
                    damage = 0;
                }

                if (Player.ConfusingTouch)
                {
                    ConfuseWithTouch(monster, creature, creatureIndex, name);
                }

                if (MonsterTakeHit(monsterIndex, damage) >= 0)
                {
                    _display.MessagePrint("You have slain " + name + ".");
                    _loop.Levelling.PrintExperience();
                    blows = 0;
                }

                // Missiles are used up as they are swung.
                if (weapon.TVal >= ItemCategory.SlingAmmo && weapon.TVal <= ItemCategory.Spike)
                {
                    weapon.Number--;
                    _game.Inventory.SpendWielded(weapon.Weight);

                    if (weapon.Number == 0)
                    {
                        _loop.Equipment.ApplyItem(weapon, -1);
                        weapon.Clear();
                        _loop.Equipment.Recalculate();
                    }
                }
            }
            else
            {
                _display.MessagePrint("You miss " + name + ".");
            }

            blows--;
        }
        while (blows >= 1);
    }

    /// <summary>
    /// Glowing hands: the next blow confuses whatever it lands on. Mirrors the
    /// confuse branch inside py_attack().
    /// </summary>
    private void ConfuseWithTouch(
        Monster monster, CreatureType creature, int creatureIndex, string name)
    {
        Player.ConfusingTouch = false;
        _display.MessagePrint("Your hands stop glowing.");

        if ((creature.DefenseFlags & CreatureDefense.NeverSleeps) != 0
            || Rng.RandInt(MonsterLevels.MaxMonsterLevel) < creature.Level)
        {
            _display.MessagePrint(name + " is unaffected.");
        }
        else
        {
            _display.MessagePrint(name + " appears confused.");

            monster.Confused = monster.Confused != 0
                ? monster.Confused + 3
                : 2 + Rng.RandInt(16);
        }

        // Seeing it shrug the confusion off is how the player learns it cannot
        // be put to sleep.
        if (monster.Visible && Rng.RandInt(4) == 1)
        {
            _game.Memories[creatureIndex].Defense |=
                (ushort)(creature.DefenseFlags & CreatureDefense.NeverSleeps);
        }
    }

    /// <summary>
    /// Throws the player somewhere else on the level. Mirrors teleport().
    ///
    /// A square is picked at random and then walked halfway towards the player
    /// until it is close enough, so somewhere nearby is far likelier than
    /// somewhere at the limit.
    /// </summary>
    public void Teleport(int distance)
    {
        int row;
        int column;

        do
        {
            row = Rng.RandInt(_game.Cave.Height) - 1;
            column = Rng.RandInt(_game.Cave.Width) - 1;

            while (Cave.Distance(row, column, _game.CharacterRow, _game.CharacterColumn)
                   > distance)
            {
                row += (_game.CharacterRow - row) / 2;
                column += (_game.CharacterColumn - column) / 2;
            }
        }
        while (_game.Cave[row, column].Feature >= CaveFeature.MinClosedSpace
               || _game.Cave[row, column].MonsterIndex >= 2);

        _loop.Lighting.MoveRecord(
            _game.CharacterRow, _game.CharacterColumn, row, column);

        for (int y = _game.CharacterRow - 1; y <= _game.CharacterRow + 1; y++)
        {
            for (int x = _game.CharacterColumn - 1; x <= _game.CharacterColumn + 1; x++)
            {
                _game.Cave[y, x].TemporaryLight = false;
                _loop.Lighting.LightSpot(y, x);
            }
        }

        _loop.Lighting.LightSpot(_game.CharacterRow, _game.CharacterColumn);

        _game.CharacterRow = row;
        _game.CharacterColumn = column;

        _loop.Lighting.CheckView();
        _loop.LightMonsters();
        _loop.Teleporting = false;
    }
}
