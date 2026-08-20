// Ported from Umoria 5.6 source/spells.c - the damage engine and the spells
// that are aimed at monsters.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The kinds of harm a spell can do. Mirrors Umoria's GF_*.
/// </summary>
public static class SpellElement
{
    public const int MagicMissile = 0;
    public const int Lightning = 1;
    public const int PoisonGas = 2;
    public const int Acid = 3;
    public const int Frost = 4;
    public const int Fire = 5;

    /// <summary>A priest's orb, which is harmless to anything but the evil.</summary>
    public const int HolyOrb = 6;
}

/// <summary>
/// Spells, and the three shapes they come in.
///
/// A bolt strikes the first thing in its path. A ball flies until it hits
/// something and then bursts over everything within two squares. A breath is a
/// ball centred on the player rather than aimed, which is what a dragon does.
///
/// All three share one idea: what a creature is made of decides what hurts it.
/// Something vulnerable to an element takes double, something that breathes it
/// takes a quarter - and either way, seeing it happen teaches the player
/// something about the creature.
/// </summary>
public sealed class Spells
{
    /// <summary>How far a bolt or a ball travels. Umoria's OBJ_BOLT_RANGE.</summary>
    public const int BoltRange = 18;

    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public Spells(GameState game, Display display, GameLoop loop)
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

    private Cave Cave => _game.Cave;

    /// <summary>
    /// What an element does: which breath resists it, which weakness it exploits,
    /// and what it destroys in the pack. Mirrors get_flags().
    /// </summary>
    public static (uint Breath, ushort Weakness, Func<IItemAttributes, bool> Destroys)
        GetFlags(int element) => element switch
    {
        SpellElement.MagicMissile => (0u, (ushort)0, ItemSets.Never),
        SpellElement.Lightning =>
            (CreatureSpell.BreatheLightning, CreatureDefense.HurtByLight,
             ItemSets.DestroyedByLightning),
        SpellElement.PoisonGas =>
            (CreatureSpell.BreatheGas, CreatureDefense.HurtByPoison, ItemSets.Never),
        SpellElement.Acid =>
            (CreatureSpell.BreatheAcid, CreatureDefense.HurtByAcid, ItemSets.DestroyedByAcid),
        SpellElement.Frost =>
            (CreatureSpell.BreatheFrost, CreatureDefense.HurtByFrost, ItemSets.DestroyedByFrost),
        SpellElement.Fire =>
            (CreatureSpell.BreatheFire, CreatureDefense.HurtByFire, ItemSets.DestroyedByFire),
        SpellElement.HolyOrb => (0u, CreatureDefense.Evil, ItemSets.Never),
        _ => (0u, (ushort)0, ItemSets.Never),
    };

    /// <summary>How a monster is named when it can be seen, and when it cannot.</summary>
    public static string MonsterName(Monster monster) =>
        monster is null ? "It"
            : monster.Visible
                ? "The " + GameTables.CreatureList[monster.CreatureIndex].Name
                : "It";

    /// <inheritdoc cref="MonsterName"/>
    public static string LowerMonsterName(Monster monster) =>
        monster is null ? "it"
            : monster.Visible
                ? "the " + GameTables.CreatureList[monster.CreatureIndex].Name
                : "it";

    /// <summary>
    /// Adjusts damage for what the target is made of, and records what the
    /// player saw. Shared by all three shapes.
    /// </summary>
    private int AdjustForDefences(
        Monster monster, CreatureType creature, int damage, uint breath, ushort weakness)
    {
        if ((weakness & creature.DefenseFlags) != 0)
        {
            if (monster.Visible)
            {
                _game.Memories[monster.CreatureIndex].Defense |= weakness;
            }

            return damage * 2;
        }

        if ((breath & creature.SpellFlags) != 0)
        {
            if (monster.Visible)
            {
                _game.Memories[monster.CreatureIndex].Spells |= breath;
            }

            return damage / 4;
        }

        return damage;
    }

    /// <summary>
    /// Shoots a bolt, which strikes the first thing it meets. Mirrors
    /// fire_bolt().
    ///
    /// The bolt is drawn a square at a time and rubbed out behind itself, which
    /// is the whole of the animation.
    /// </summary>
    public void FireBolt(int element, int direction, int row, int column, int damage, string name)
    {
        (uint breath, ushort weakness, _) = GetFlags(element);

        int lastRow = row;
        int lastColumn = column;
        int distance = 0;
        bool finished = false;

        do
        {
            Cave.Move(direction, ref row, ref column);
            distance++;

            CaveSquare square = Cave[row, column];
            _loop.Lighting.LightSpot(lastRow, lastColumn);

            if (distance > BoltRange || square.Feature >= CaveFeature.MinClosedSpace)
            {
                finished = true;
            }
            else if (square.MonsterIndex > 1)
            {
                finished = true;
                Strike(square, damage, breath, weakness, name);
            }
            else if (_display.Panel.Contains(row, column) && Player.Blind < 1)
            {
                _display.PrintAt('*', row, column);
                _display.Refresh();
            }

            lastRow = row;
            lastColumn = column;
        }
        while (!finished);
    }

    private void Strike(
        CaveSquare square, int damage, uint breath, ushort weakness, string name)
    {
        Monster monster = _game.Monsters[square.MonsterIndex];
        CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

        // Light the monster up long enough to draw it, so the player sees what
        // the bolt hit even in the dark.
        bool wasLit = square.PermanentLight;
        square.PermanentLight = true;
        _loop.MonsterAi.UpdateMonster(square.MonsterIndex);
        square.PermanentLight = wasLit;
        _display.Refresh();

        _display.MessagePrint(
            "The " + name + " strikes " + LowerMonsterName(monster) + ".");

        damage = AdjustForDefences(monster, creature, damage, breath, weakness);

        string described = MonsterName(monster);
        int killed = _loop.Combat.MonsterTakeHit(square.MonsterIndex, damage);

        if (killed >= 0)
        {
            _display.MessagePrint(described + " dies in a fit of agony.");
            _loop.Levelling.PrintExperience();
        }
        else if (damage > 0)
        {
            _display.MessagePrint(described + " screams in agony.");
        }
    }

    /// <summary>
    /// Throws a ball, which bursts over everything within two squares of where
    /// it stops. Mirrors fire_ball().
    ///
    /// The damage falls off with distance from the middle, and anything the
    /// element destroys is destroyed wherever it lies - so a fireball down a
    /// corridor burns the scrolls on the floor as well as the monsters.
    /// </summary>
    public void FireBall(int element, int direction, int row, int column, int damage, string name)
    {
        (uint breath, ushort weakness, Func<IItemAttributes, bool> destroys) = GetFlags(element);

        const int radius = 2;
        int hit = 0;
        int killed = 0;
        int lastRow = row;
        int lastColumn = column;
        int distance = 0;
        bool finished = false;

        do
        {
            Cave.Move(direction, ref row, ref column);
            distance++;
            _loop.Lighting.LightSpot(lastRow, lastColumn);

            if (distance > BoltRange)
            {
                finished = true;
                continue;
            }

            CaveSquare square = Cave[row, column];

            if (square.Feature < CaveFeature.MinClosedSpace && square.MonsterIndex <= 1)
            {
                if (_display.Panel.Contains(row, column) && Player.Blind < 1)
                {
                    _display.PrintAt('*', row, column);
                    _display.Refresh();
                }

                lastRow = row;
                lastColumn = column;
                continue;
            }

            finished = true;

            // A ball that runs into a wall bursts on the square before it.
            if (square.Feature >= CaveFeature.MinClosedSpace)
            {
                row = lastRow;
                column = lastColumn;
            }

            for (int y = row - radius; y <= row + radius; y++)
            {
                for (int x = column - radius; x <= column + radius; x++)
                {
                    if (!Cave.InBounds(y, x)
                        || Cave.Distance(row, column, y, x) > radius
                        || !LineOfSight.Between(Cave, row, column, y, x))
                    {
                        continue;
                    }

                    CaveSquare hitSquare = Cave[y, x];

                    if (hitSquare.ObjectIndex != 0
                        && destroys(_game.Objects[hitSquare.ObjectIndex]))
                    {
                        _loop.Movement.DeleteObject(y, x);
                    }

                    if (hitSquare.Feature > CaveFeature.MaxOpenSpace)
                    {
                        continue;
                    }

                    if (hitSquare.MonsterIndex > 1)
                    {
                        Monster monster = _game.Monsters[hitSquare.MonsterIndex];
                        CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

                        bool wasLit = hitSquare.PermanentLight;
                        hitSquare.PermanentLight = true;
                        _loop.MonsterAi.UpdateMonster(hitSquare.MonsterIndex);

                        hit++;

                        int share = AdjustForDefences(
                            monster, creature, damage, breath, weakness);

                        share /= Cave.Distance(y, x, row, column) + 1;

                        if (_loop.Combat.MonsterTakeHit(hitSquare.MonsterIndex, share) >= 0)
                        {
                            killed++;
                        }

                        hitSquare.PermanentLight = wasLit;
                    }
                    else if (_display.Panel.Contains(y, x) && Player.Blind < 1)
                    {
                        _display.PrintAt('*', y, x);
                    }
                }
            }

            _display.Refresh();
            Redraw(row, column, radius);

            if (hit == 1)
            {
                _display.MessagePrint("The " + name + " envelops a creature!");
            }
            else if (hit > 1)
            {
                _display.MessagePrint("The " + name + " envelops several creatures!");
            }

            if (killed == 1)
            {
                _display.MessagePrint("There is a scream of agony!");
            }
            else if (killed > 1)
            {
                _display.MessagePrint("There are several screams of agony!");
            }

            _loop.Levelling.PrintExperience();
        }
        while (!finished);
    }

    /// <summary>
    /// A breath, which is a ball centred on the breather. Mirrors breath().
    ///
    /// The player gets no experience for anything it kills, since the killing
    /// was somebody else's - which is why this cannot simply call the ordinary
    /// wounding.
    /// </summary>
    public void Breath(int element, int row, int column, int damage, string killer, int monsterIndex)
    {
        (uint breath, ushort weakness, Func<IItemAttributes, bool> destroys) = GetFlags(element);

        const int radius = 2;

        for (int y = row - radius; y <= row + radius; y++)
        {
            for (int x = column - radius; x <= column + radius; x++)
            {
                if (!Cave.InBounds(y, x)
                    || Cave.Distance(row, column, y, x) > radius
                    || !LineOfSight.Between(Cave, row, column, y, x))
                {
                    continue;
                }

                CaveSquare square = Cave[y, x];

                if (square.ObjectIndex != 0 && destroys(_game.Objects[square.ObjectIndex]))
                {
                    _loop.Movement.DeleteObject(y, x);
                }

                if (square.Feature > CaveFeature.MaxOpenSpace)
                {
                    continue;
                }

                // The status bit is read rather than the counter: blindness
                // inflicted by an earlier monster this turn should not hide a
                // breath that is already on its way.
                if (_display.Panel.Contains(y, x) && (Player.Status & PlayerStatus.Blind) == 0)
                {
                    _display.PrintAt('*', y, x);
                }

                if (square.MonsterIndex > 1)
                {
                    BreatheOnMonster(square, y, x, row, column, damage, breath, weakness,
                        monsterIndex);
                }
                else if (square.MonsterIndex == 1)
                {
                    int share = damage / (Cave.Distance(y, x, row, column) + 1);

                    // At least a point, which also keeps the poison roll from
                    // being asked for a random number in the range of nothing.
                    if (share == 0)
                    {
                        share = 1;
                    }

                    switch (element)
                    {
                        case SpellElement.Lightning:
                            _loop.Damage.LightningDamage(share, killer);
                            break;
                        case SpellElement.PoisonGas:
                            _loop.Damage.PoisonGas(share, killer);
                            break;
                        case SpellElement.Acid:
                            _loop.Damage.AcidDamage(share, killer);
                            break;
                        case SpellElement.Frost:
                            _loop.Damage.ColdDamage(share, killer);
                            break;
                        case SpellElement.Fire:
                            _loop.Damage.FireDamage(share, killer);
                            break;
                        default:
                            break;
                    }
                }
            }
        }

        _display.Refresh();
        Redraw(row, column, radius);
    }

    private void BreatheOnMonster(
        CaveSquare square, int y, int x, int row, int column, int damage,
        uint breath, ushort weakness, int monsterIndex)
    {
        Monster monster = _game.Monsters[square.MonsterIndex];
        CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

        int share = damage;

        if ((weakness & creature.DefenseFlags) != 0)
        {
            share *= 2;
        }
        else if ((breath & creature.SpellFlags) != 0)
        {
            share /= 4;
        }

        share /= Cave.Distance(y, x, row, column) + 1;

        monster.HitPoints -= share;
        monster.Sleep = 0;

        if (monster.HitPoints >= 0)
        {
            return;
        }

        uint learned = _loop.Combat.MonsterDeath(
            monster.Row, monster.Column, creature.MoveFlags);

        if (monster.Visible)
        {
            MonsterMemory memory = _game.Memories[monster.CreatureIndex];

            uint known = (memory.Move & CreatureMove.Treasure) >> CreatureMove.TreasureShift;

            if (known > ((learned & CreatureMove.Treasure) >> CreatureMove.TreasureShift))
            {
                learned = (learned & ~CreatureMove.Treasure)
                    | (known << CreatureMove.TreasureShift);
            }

            memory.Move = learned | (memory.Move & ~CreatureMove.Treasure);
        }

        // The same two-step delete the rest of the monster code uses.
        if (monsterIndex < square.MonsterIndex)
        {
            _game.Monsters.Delete(square.MonsterIndex, Cave, _loop.Lighting);
        }
        else
        {
            _game.Monsters.MarkDead(square.MonsterIndex, Cave, _loop.Lighting);
        }
    }

    private void Redraw(int row, int column, int radius)
    {
        for (int y = row - radius; y <= row + radius; y++)
        {
            for (int x = column - radius; x <= column + radius; x++)
            {
                if (Cave.InBounds(y, x) && _display.Panel.Contains(y, x)
                    && Cave.Distance(row, column, y, x) <= radius)
                {
                    _loop.Lighting.LightSpot(y, x);
                }
            }
        }
    }

    // ------------------------------------------------------- aimed at monsters

    /// <summary>
    /// Walks a bolt path and hands the first monster met to <paramref name="hit"/>.
    /// Every aimed spell in the file has this shape.
    /// </summary>
    private bool AlongBolt(int direction, int row, int column, Func<int, bool> hit)
    {
        int distance = 0;

        while (true)
        {
            Cave.Move(direction, ref row, ref column);
            distance++;

            CaveSquare square = Cave[row, column];

            if (distance > BoltRange || square.Feature >= CaveFeature.MinClosedSpace)
            {
                return false;
            }

            if (square.MonsterIndex > 1)
            {
                return hit(square.MonsterIndex);
            }
        }
    }

    /// <summary>Wounds whatever is in the way. Mirrors hp_monster().</summary>
    public bool WoundMonster(int direction, int row, int column, int damage) =>
        AlongBolt(direction, row, column, index =>
        {
            Monster monster = _game.Monsters[index];
            string name = MonsterName(monster);

            if (_loop.Combat.MonsterTakeHit(index, damage) >= 0)
            {
                _display.MessagePrint(name + " dies in a fit of agony.");
                _loop.Levelling.PrintExperience();
            }
            else if (damage > 0)
            {
                _display.MessagePrint(name + " screams in agony.");
            }

            return true;
        });

    /// <summary>
    /// Drains the life out of something living. Mirrors drain_life().
    ///
    /// The undead have no life to drain, and finding that out is worth knowing.
    /// </summary>
    public bool DrainLife(int direction, int row, int column) =>
        AlongBolt(direction, row, column, index =>
        {
            Monster monster = _game.Monsters[index];
            CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

            if ((creature.DefenseFlags & CreatureDefense.Undead) != 0)
            {
                _game.Memories[monster.CreatureIndex].Defense |= CreatureDefense.Undead;
                return false;
            }

            string name = MonsterName(monster);

            if (_loop.Combat.MonsterTakeHit(index, 75) >= 0)
            {
                _display.MessagePrint(name + " dies in a fit of agony.");
                _loop.Levelling.PrintExperience();
            }
            else
            {
                _display.MessagePrint(name + " screams in agony.");
            }

            return true;
        });

    /// <summary>
    /// Hurries or slows a monster. Mirrors speed_monster().
    ///
    /// Hurrying always works; slowing is resisted by anything strong enough, and
    /// either way the thing wakes up.
    /// </summary>
    public bool SpeedMonster(int direction, int row, int column, int amount) =>
        AlongBolt(direction, row, column, index =>
        {
            Monster monster = _game.Monsters[index];
            CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];
            string name = MonsterName(monster);

            if (amount > 0)
            {
                monster.Speed += amount;
                monster.Sleep = 0;
                _display.MessagePrint(name + " starts moving faster.");
                return true;
            }

            if (Rng.RandInt(MonsterLevels.MaxMonsterLevel) > creature.Level)
            {
                monster.Speed += amount;
                monster.Sleep = 0;
                _display.MessagePrint(name + " starts moving slower.");
                return true;
            }

            monster.Sleep = 0;
            _display.MessagePrint(name + " is unaffected.");
            return false;
        });

    /// <summary>Confuses a monster. Mirrors confuse_monster().</summary>
    public bool ConfuseMonster(int direction, int row, int column) =>
        AlongBolt(direction, row, column, index =>
        {
            Monster monster = _game.Monsters[index];
            CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];
            string name = MonsterName(monster);

            if (Rng.RandInt(MonsterLevels.MaxMonsterLevel) < creature.Level
                || (CreatureDefense.NeverSleeps & creature.DefenseFlags) != 0)
            {
                if (monster.Visible
                    && (creature.DefenseFlags & CreatureDefense.NeverSleeps) != 0)
                {
                    _game.Memories[monster.CreatureIndex].Defense |=
                        CreatureDefense.NeverSleeps;
                }

                monster.Sleep = 0;
                _display.MessagePrint(name + " is unaffected.");
                return false;
            }

            monster.Confused = monster.Confused != 0
                ? monster.Confused + 3
                : 2 + Rng.RandInt(16);
            _display.MessagePrint(name + " appears confused.");
            return true;
        });

    /// <summary>Puts a monster to sleep. Mirrors sleep_monster().</summary>
    public bool SleepMonster(int direction, int row, int column) =>
        AlongBolt(direction, row, column, index => PutToSleep(index));

    /// <summary>
    /// Puts everything beside the player to sleep. Mirrors sleep_monsters1().
    /// </summary>
    public bool SleepAdjacent(int row, int column)
    {
        bool slept = false;

        for (int y = row - 1; y <= row + 1; y++)
        {
            for (int x = column - 1; x <= column + 1; x++)
            {
                if (Cave[y, x].MonsterIndex > 1 && PutToSleep(Cave[y, x].MonsterIndex))
                {
                    slept = true;
                }
            }
        }

        return slept;
    }

    private bool PutToSleep(int index)
    {
        Monster monster = _game.Monsters[index];
        CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];
        string name = MonsterName(monster);

        if (Rng.RandInt(MonsterLevels.MaxMonsterLevel) < creature.Level
            || (CreatureDefense.NeverSleeps & creature.DefenseFlags) != 0)
        {
            if (monster.Visible && (creature.DefenseFlags & CreatureDefense.NeverSleeps) != 0)
            {
                _game.Memories[monster.CreatureIndex].Defense |= CreatureDefense.NeverSleeps;
            }

            _display.MessagePrint(name + " is unaffected.");
            return false;
        }

        monster.Sleep = 500;
        _display.MessagePrint(name + " falls asleep.");
        return true;
    }

    /// <summary>
    /// Throws a monster somewhere else. Mirrors teleport_monster().
    /// </summary>
    public bool TeleportMonster(int direction, int row, int column) =>
        AlongBolt(direction, row, column, index =>
        {
            _loop.MonsterAttack.TeleportAway(index, MonsterAi.MaxSight * 5);
            return true;
        });

    /// <summary>
    /// Brings the player to something. Mirrors teleport_to().
    ///
    /// The square is picked outwards from the target, so the player lands beside
    /// what called them rather than on top of it.
    /// </summary>
    public void TeleportTo(int targetRow, int targetColumn)
    {
        int distance = 1;
        int tries = 0;
        int row;
        int column;

        do
        {
            row = targetRow + (Rng.RandInt((2 * distance) + 1) - (distance + 1));
            column = targetColumn + (Rng.RandInt((2 * distance) + 1) - (distance + 1));
            tries++;

            if (tries > 9)
            {
                tries = 0;
                distance++;
            }
        }
        while (!Cave.InBounds(row, column)
               || Cave[row, column].Feature >= CaveFeature.MinClosedSpace
               || Cave[row, column].MonsterIndex >= 2);

        _loop.Lighting.MoveRecord(_game.CharacterRow, _game.CharacterColumn, row, column);

        for (int y = _game.CharacterRow - 1; y <= _game.CharacterRow + 1; y++)
        {
            for (int x = _game.CharacterColumn - 1; x <= _game.CharacterColumn + 1; x++)
            {
                Cave[y, x].TemporaryLight = false;
                _loop.Lighting.LightSpot(y, x);
            }
        }

        _loop.Lighting.LightSpot(_game.CharacterRow, _game.CharacterColumn);

        _game.CharacterRow = row;
        _game.CharacterColumn = column;

        _loop.Lighting.CheckView();
        _loop.LightMonsters();
    }
}
