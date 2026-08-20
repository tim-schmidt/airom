// Ported from the world-changing half of Umoria 5.6 source/spells.c -
// earthquakes, destruction, walls, and the spells that work on everything at
// once.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

public partial class Spells
{
    /// <summary>
    /// Turns a wall to mud along a line. Mirrors wall_to_mud().
    ///
    /// The ray passes through walls as it eats them, so a single casting can
    /// open a corridor rather than a doorway. Anything made of stone that stands
    /// in it takes the spell badly.
    /// </summary>
    public bool WallToMud(int direction, int row, int column)
    {
        bool changed = false;
        bool finished = false;
        int distance = 0;

        do
        {
            Cave.Move(direction, ref row, ref column);
            distance++;

            CaveSquare square = Cave[row, column];

            if (distance == BoltRange)
            {
                finished = true;
            }

            if (square.Feature >= CaveFeature.MinCaveWall
                && square.Feature != CaveFeature.BoundaryWall)
            {
                finished = true;
                _loop.Doors.TunnelWall(row, column, 1, 0);

                if (IsSeen(row, column))
                {
                    _display.MessagePrint("The wall turns into mud.");
                    changed = true;
                }
            }
            else if (square.ObjectIndex != 0
                     && square.Feature >= CaveFeature.MinClosedSpace)
            {
                finished = true;

                if (_display.Panel.Contains(row, column) && IsSeen(row, column))
                {
                    _display.MessagePrint(
                        "The " + _game.Names.Describe(
                            _game.Objects[square.ObjectIndex], withArticle: false)
                        + " turns into mud.");

                    changed = true;
                }

                if (_game.Objects[square.ObjectIndex].TVal == ItemCategory.Rubble)
                {
                    _loop.Movement.DeleteObject(row, column);

                    // Rubble sometimes had something under it.
                    if (Rng.RandInt(10) == 1)
                    {
                        new DungeonGenerator(_game, _display).PlaceObject(row, column, false);

                        if (IsSeen(row, column))
                        {
                            _display.MessagePrint("You have found something!");
                        }
                    }

                    _loop.Lighting.LightSpot(row, column);
                }
                else
                {
                    _loop.Movement.DeleteObject(row, column);
                }
            }

            if (square.MonsterIndex > 1)
            {
                Monster monster = _game.Monsters[square.MonsterIndex];
                CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

                if ((creature.DefenseFlags & CreatureDefense.HurtByStone) != 0)
                {
                    string name = MonsterName(monster);
                    int killed = _loop.Combat.MonsterTakeHit(square.MonsterIndex, 100);

                    // These are said whether or not the creature can be seen.
                    if (killed >= 0)
                    {
                        _game.Memories[killed].Defense |= CreatureDefense.HurtByStone;
                        _display.MessagePrint(name + " dissolves!");
                        _loop.Levelling.PrintExperience();
                    }
                    else
                    {
                        _game.Memories[monster.CreatureIndex].Defense |=
                            CreatureDefense.HurtByStone;
                        _display.MessagePrint(name + " grunts in pain!");
                    }

                    finished = true;
                }
            }
        }
        while (!finished);

        return changed;
    }

    /// <summary>
    /// Destroys the doors and traps along a line, and unlocks the chests.
    /// Mirrors td_destroy2().
    /// </summary>
    public bool DestroyDoorsAlong(int direction, int row, int column)
    {
        bool destroyed = false;
        int distance = 0;
        CaveSquare square;

        do
        {
            Cave.Move(direction, ref row, ref column);
            distance++;
            square = Cave[row, column];

            // The first closed square is entered rather than stopped at, since
            // it may be a secret door.
            if (square.ObjectIndex != 0)
            {
                InvenType item = _game.Objects[square.ObjectIndex];

                if (item.TVal is ItemCategory.InvisibleTrap or ItemCategory.ClosedDoor
                    or ItemCategory.VisibleTrap or ItemCategory.OpenDoor
                    or ItemCategory.SecretDoor)
                {
                    if (_loop.Movement.DeleteObject(row, column))
                    {
                        _display.MessagePrint("There is a bright flash of light!");
                        destroyed = true;
                    }
                }
                else if (item.TVal == ItemCategory.Chest && item.Flags != 0)
                {
                    _display.MessagePrint("Click!");
                    item.Flags &= ~(ChestFlags.Trapped | ChestFlags.Locked);
                    destroyed = true;
                    item.SpecialName = SpecialName.Unlocked;
                    _game.Knowledge.LearnEnchantment(item);
                }
            }
        }
        while (distance <= BoltRange || square.Feature <= CaveFeature.MaxOpenSpace);

        return destroyed;
    }

    /// <summary>
    /// Turns a monster into something else. Mirrors poly_monster().
    ///
    /// The new creature is drawn from the whole list rather than from this
    /// depth, so a polymorph is a gamble rather than a cure. Unlike mass_poly()
    /// this has no exception for a winning creature.
    /// </summary>
    public bool PolymorphMonster(int direction, int row, int column) =>
        AlongBolt(direction, row, column, index =>
        {
            Monster monster = _game.Monsters[index];
            CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

            if (Rng.RandInt(MonsterLevels.MaxMonsterLevel) <= creature.Level)
            {
                _display.MessagePrint(MonsterName(monster) + " is unaffected.");
                return false;
            }

            int newRow = monster.Row;
            int newColumn = monster.Column;

            _game.Monsters.Delete(index, Cave, _loop.Lighting);

            // Drawn from the whole creature list rather than from this depth,
            // so what the monster becomes is a real gamble.
            int kind = Rng.RandInt(
                MonsterLevels.LevelTotals[MonsterLevels.MaxMonsterLevel]
                - MonsterLevels.LevelTotals[0]) - 1 + MonsterLevels.LevelTotals[0];

            // FAITHFUL QUIRK: the original re-tests the result against the
            // panel and the light here, but only ever sets it to what it
            // already is, so whether the player saw it makes no difference.
            return new DungeonGenerator(_game, _display)
                .PlaceMonster(newRow, newColumn, kind, asleep: false);
        });

    /// <summary>Makes a copy of a monster. Mirrors clone_monster().</summary>
    public bool CloneMonster(int direction, int row, int column) =>
        AlongBolt(direction, row, column, index =>
        {
            Monster monster = _game.Monsters[index];
            monster.Sleep = 0;

            // The monster index passed is zero rather than this creature's own:
            // nothing can reach here from a monster's turn, so there is no
            // record that needs protecting from the copy.
            return _loop.MonsterAi.MultiplyMonster(
                monster.Row, monster.Column, monster.CreatureIndex, 0);
        });

    /// <summary>
    /// Builds a wall in front of the player. Mirrors build_wall().
    ///
    /// The wall runs until it meets something closed. Anything standing where
    /// it goes is crushed, unless it can walk through rock - in which case the
    /// wall is built around it, and the things that live in stone are the
    /// better for it.
    /// </summary>
    public bool BuildWall(int direction, int row, int column)
    {
        bool built = false;
        bool finished = false;
        int distance = 0;

        do
        {
            Cave.Move(direction, ref row, ref column);
            distance++;

            CaveSquare square = Cave[row, column];

            if (distance > BoltRange || square.Feature >= CaveFeature.MinClosedSpace)
            {
                finished = true;
                continue;
            }

            if (square.ObjectIndex != 0)
            {
                _loop.Movement.DeleteObject(row, column);
            }

            if (square.MonsterIndex > 1)
            {
                // Whatever is here stops the wall going any further.
                finished = true;
                CrushUnderWall(square.MonsterIndex);
            }

            square.Feature = CaveFeature.MagmaWall;
            square.FieldMark = false;

            // Permanently lit if the player's own light is on it, so a wall
            // built in the dark stays dark.
            square.PermanentLight = square.TemporaryLight || square.PermanentLight;

            _loop.Lighting.LightSpot(row, column);
            built = true;
        }
        while (!finished);

        return built;
    }

    /// <summary>
    /// What a wall built on top of a monster does to it. Shared in spirit with
    /// the earthquake, which crushes the same way.
    /// </summary>
    private void CrushUnderWall(int index)
    {
        Monster monster = _game.Monsters[index];
        CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

        if ((creature.MoveFlags & CreatureMove.Phase) != 0)
        {
            // An earth elemental, an earth spirit or a Xorn is at home in it.
            if (creature.DisplayChar is 'E' or 'X')
            {
                monster.HitPoints += Rng.DamRoll(4, 8);
            }

            return;
        }

        // Something that never moves cannot get out of the way at all.
        int damage = (creature.MoveFlags & CreatureMove.AttackOnly) != 0
            ? 3000
            : Rng.DamRoll(4, 8);

        string name = MonsterName(monster);
        _display.MessagePrint(name + " wails out in pain!");

        if (_loop.Combat.MonsterTakeHit(index, damage) >= 0)
        {
            _display.MessagePrint(name + " is embedded in the rock.");
            _loop.Levelling.PrintExperience();
        }
    }

    // ---------------------------------------------------- everything at once

    /// <summary>
    /// Puts every monster in sight to sleep. Mirrors sleep_monsters2().
    /// </summary>
    public bool SleepEveryMonster() => ToEveryMonster((index, monster, creature) =>
    {
        string name = MonsterName(monster);

        if (Rng.RandInt(MonsterLevels.MaxMonsterLevel) < creature.Level
            || (creature.DefenseFlags & CreatureDefense.NeverSleeps) != 0)
        {
            if (!monster.Visible)
            {
                return false;
            }

            if ((creature.DefenseFlags & CreatureDefense.NeverSleeps) != 0)
            {
                _game.Memories[monster.CreatureIndex].Defense |= CreatureDefense.NeverSleeps;
            }

            _display.MessagePrint(name + " is unaffected.");
            return false;
        }

        monster.Sleep = 500;

        if (!monster.Visible)
        {
            return false;
        }

        _display.MessagePrint(name + " falls asleep.");
        return true;
    });

    /// <summary>
    /// Hurries or slows every monster in sight. Mirrors speed_monsters().
    ///
    /// The speed changes whether or not the monster can be seen; only a visible
    /// one is reported, and only a visible one counts as having identified what
    /// did it.
    /// </summary>
    public bool ChangeEveryMonsterSpeed(int amount) =>
        ToEveryMonster((index, monster, creature) =>
    {
        string name = MonsterName(monster);

        if (amount > 0)
        {
            monster.Speed += amount;
            monster.Sleep = 0;

            if (!monster.Visible)
            {
                return false;
            }

            _display.MessagePrint(name + " starts moving faster.");
            return true;
        }

        // Deep creatures resist being slowed, which is what keeps the Balrog
        // dangerous.
        if (Rng.RandInt(MonsterLevels.MaxMonsterLevel) > creature.Level)
        {
            monster.Speed += amount;
            monster.Sleep = 0;

            if (!monster.Visible)
            {
                return false;
            }

            _display.MessagePrint(name + " starts moving slower.");
            return true;
        }

        if (!monster.Visible)
        {
            return false;
        }

        monster.Sleep = 0;
        _display.MessagePrint(name + " is unaffected.");
        return false;
    });

    /// <summary>
    /// Runs something over every monster the player can see. The list is walked
    /// backwards, as the rest of the monster code walks it.
    /// </summary>
    private bool ToEveryMonster(Func<int, Monster, CreatureType, bool> affect)
    {
        bool anything = false;

        for (int i = _game.Monsters.Count - 1; i >= MonsterPool.FirstIndex; i--)
        {
            Monster monster = _game.Monsters[i];

            if (monster.DistanceToPlayer <= MonsterAi.MaxSight
                && LineOfSight.Between(
                    Cave, _game.CharacterRow, _game.CharacterColumn,
                    monster.Row, monster.Column)
                && affect(i, monster, GameTables.CreatureList[monster.CreatureIndex]))
            {
                anything = true;
            }
        }

        return anything;
    }

    /// <summary>
    /// Polymorphs every monster in sight. Mirrors mass_poly().
    ///
    /// Unlike the bolt version this spares a winning creature, and unlike the
    /// rest of the spells that work on everything at once it needs no line of
    /// sight - being within sight range is enough.
    /// </summary>
    public bool PolymorphEveryMonster()
    {
        bool anything = false;

        for (int i = _game.Monsters.Count - 1; i >= MonsterPool.FirstIndex; i--)
        {
            Monster monster = _game.Monsters[i];

            if (monster.DistanceToPlayer > MonsterAi.MaxSight
                || (GameTables.CreatureList[monster.CreatureIndex].MoveFlags
                    & CreatureMove.Win) != 0)
            {
                continue;
            }

            int row = monster.Row;
            int column = monster.Column;

            _game.Monsters.Delete(i, Cave, _loop.Lighting);

            // FAITHFUL QUIRK: the result is overwritten each time round rather
            // than accumulated, so the answer is whatever happened to the last
            // monster polymorphed.
            anything = new DungeonGenerator(_game, _display)
                .PlaceMonster(row, column, DrawAnyCreature(), asleep: false);
        }

        return anything;
    }

    /// <summary>
    /// Draws a creature from the whole table rather than from a depth. Used by
    /// the polymorph spells.
    /// </summary>
    private int DrawAnyCreature() =>
        Rng.RandInt(MonsterLevels.LevelTotals[MonsterLevels.MaxMonsterLevel]
                    - MonsterLevels.LevelTotals[0])
        - 1 + MonsterLevels.LevelTotals[0];

    /// <summary>
    /// Removes every monster in sight from the level. Mirrors mass_genocide().
    ///
    /// The creatures are not stopped from appearing again later - this clears
    /// the level, it does not banish the kind.
    /// </summary>
    public bool MassGenocide()
    {
        bool killed = false;

        for (int i = _game.Monsters.Count - 1; i >= MonsterPool.FirstIndex; i--)
        {
            Monster monster = _game.Monsters[i];

            if (monster.DistanceToPlayer <= MonsterAi.MaxSight
                && (GameTables.CreatureList[monster.CreatureIndex].MoveFlags
                    & CreatureMove.Win) == 0)
            {
                _game.Monsters.Delete(i, Cave, _loop.Lighting);
                killed = true;
            }
        }

        return killed;
    }

    /// <summary>
    /// Removes every monster of one letter from the level. Mirrors genocide().
    ///
    /// A winning creature survives, and is named while it does - the spell is
    /// powerful enough that silence would read as a bug rather than as an
    /// escape.
    /// </summary>
    public bool Genocide(char symbol)
    {
        bool killed = false;

        for (int i = _game.Monsters.Count - 1; i >= MonsterPool.FirstIndex; i--)
        {
            Monster monster = _game.Monsters[i];
            CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

            if (creature.DisplayChar != symbol)
            {
                continue;
            }

            if ((creature.MoveFlags & CreatureMove.Win) == 0)
            {
                _game.Monsters.Delete(i, Cave, _loop.Lighting);
                killed = true;
            }
            else
            {
                _display.MessagePrint("The " + creature.Name + " is unaffected.");
            }
        }

        return killed;
    }

    /// <summary>
    /// Wakes the level and hurries what is close. Mirrors aggravate_monster().
    ///
    /// Everything wakes, however far away; only what is within the given
    /// distance is hurried, and only up to a speed of two.
    /// </summary>
    public bool AggravateMonsters(int distance)
    {
        bool aggravated = false;

        for (int i = _game.Monsters.Count - 1; i >= MonsterPool.FirstIndex; i--)
        {
            Monster monster = _game.Monsters[i];
            monster.Sleep = 0;

            if (monster.DistanceToPlayer <= distance && monster.Speed < 2)
            {
                monster.Speed++;
                aggravated = true;
            }
        }

        if (aggravated)
        {
            _display.MessagePrint("You hear a sudden stirring in the distance!");
        }

        return aggravated;
    }

    /// <summary>
    /// Hurts every creature of a kind in sight. Mirrors dispel_creature().
    /// </summary>
    public bool DispelCreature(ushort defence, int damage)
    {
        bool anything = false;

        for (int i = _game.Monsters.Count - 1; i >= MonsterPool.FirstIndex; i--)
        {
            Monster monster = _game.Monsters[i];
            CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

            if (monster.DistanceToPlayer > MonsterAi.MaxSight
                || (creature.DefenseFlags & defence) == 0
                || !LineOfSight.Between(
                    Cave, _game.CharacterRow, _game.CharacterColumn,
                    monster.Row, monster.Column))
            {
                continue;
            }

            if (monster.Visible)
            {
                _game.Memories[monster.CreatureIndex].Defense |= defence;
            }

            anything = true;
            string name = MonsterName(monster);

            if (_loop.Combat.MonsterTakeHit(i, Rng.RandInt(damage)) >= 0)
            {
                _display.MessagePrint(name + " dissolves!");
                _loop.Levelling.PrintExperience();
            }
            else
            {
                _display.MessagePrint(name + " shudders.");
            }
        }

        return anything;
    }

    /// <summary>
    /// Frightens the undead into fleeing. Mirrors turn_undead().
    /// </summary>
    public bool TurnUndead()
    {
        bool turned = false;

        for (int i = _game.Monsters.Count - 1; i >= MonsterPool.FirstIndex; i--)
        {
            Monster monster = _game.Monsters[i];
            CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

            if ((creature.DefenseFlags & CreatureDefense.Undead) == 0
                || monster.DistanceToPlayer > MonsterAi.MaxSight
                || !LineOfSight.Between(
                    Cave, _game.CharacterRow, _game.CharacterColumn,
                    monster.Row, monster.Column))
            {
                continue;
            }

            string name = MonsterName(monster);

            if (Player.Level + 1 > creature.Level || Rng.RandInt(5) == 1)
            {
                // Nothing is said, remembered or counted for something the
                // player cannot see - but it flees all the same.
                if (monster.Visible)
                {
                    _display.MessagePrint(name + " runs frantically!");
                    turned = true;
                    _game.Memories[monster.CreatureIndex].Defense |= CreatureDefense.Undead;
                }

                // The undead flee by being confused, which is the one way they
                // can be.
                monster.Confused = Player.Level;
            }
            else if (monster.Visible)
            {
                _display.MessagePrint(name + " is unaffected.");
            }
        }

        return turned;
    }

    /// <summary>
    /// Lays a rune the monsters will not cross. Mirrors warding_glyph().
    /// </summary>
    public void WardingGlyph()
    {
        CaveSquare square = Cave[_game.CharacterRow, _game.CharacterColumn];

        if (square.ObjectIndex != 0)
        {
            return;
        }

        int slot = _game.Objects.Allocate();
        square.ObjectIndex = slot;
        _game.Objects[slot].CopyFrom(WardingRuneObject);
    }

    /// <summary>The rune of protection. Umoria's OBJ_SCARE_MON.</summary>
    private const int WardingRuneObject = 398;

    /// <summary>
    /// Shakes the level apart. Mirrors earthquake().
    ///
    /// One square in eight within eight of the player is thrown about: floor
    /// becomes rock, rock becomes floor, and anything caught in the change is
    /// crushed - except the things that live in stone, which grow stronger on
    /// it.
    /// </summary>
    public void Earthquake()
    {
        for (int row = _game.CharacterRow - 8; row <= _game.CharacterRow + 8; row++)
        {
            for (int column = _game.CharacterColumn - 8;
                 column <= _game.CharacterColumn + 8;
                 column++)
            {
                if ((row == _game.CharacterRow && column == _game.CharacterColumn)
                    || !Cave.InBounds(row, column)
                    || Rng.RandInt(8) != 1)
                {
                    continue;
                }

                CaveSquare square = Cave[row, column];

                if (square.ObjectIndex != 0)
                {
                    _loop.Movement.DeleteObject(row, column);
                }

                if (square.MonsterIndex > 1)
                {
                    // FAITHFUL QUIRK: the original assigns what mon_take_hit()
                    // returns to the same variable it is using for the row, so
                    // hitting a monster throws the sweep somewhere else
                    // entirely - back to the top of the level if the monster
                    // survived, or to the creature's index in the table if it
                    // died. The shape of the quake really does depend on what
                    // was standing in it.
                    row = CrushInEarthquake(square.MonsterIndex) ?? row;
                }

                // The square is still the one the sweep was on: the original
                // holds a pointer to it that the clobbered row does not touch.
                if (square.Feature >= CaveFeature.MinCaveWall
                    && square.Feature != CaveFeature.BoundaryWall)
                {
                    square.Feature = CaveFeature.CorridorFloor;
                    square.PermanentLight = false;
                    square.FieldMark = false;
                }
                else if (square.Feature <= CaveFeature.MaxCaveFloor)
                {
                    int roll = Rng.RandInt(10);

                    square.Feature = roll < 6 ? CaveFeature.QuartzWall
                        : roll < 9 ? CaveFeature.MagmaWall
                        : CaveFeature.GraniteWall;

                    square.FieldMark = false;
                }

                // Lit by the clobbered row, which is part of the same quirk.
                if (Cave.InBounds(row, column))
                {
                    _loop.Lighting.LightSpot(row, column);
                }
            }
        }
    }

    /// <summary>
    /// Crushes what the quake caught. Returns what mon_take_hit() returned,
    /// which the caller uses in place of its row - see the quirk there - or
    /// nothing when the creature phased and no blow was struck.
    /// </summary>
    private int? CrushInEarthquake(int index)
    {
        Monster monster = _game.Monsters[index];
        CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

        if ((creature.MoveFlags & CreatureMove.Phase) != 0)
        {
            if (creature.DisplayChar is 'E' or 'X')
            {
                monster.HitPoints += Rng.DamRoll(4, 8);
            }

            // Nothing was struck, so the row is left alone.
            return null;
        }

        int damage = (creature.MoveFlags & CreatureMove.AttackOnly) != 0
            ? 3000
            : Rng.DamRoll(4, 8);

        string name = MonsterName(monster);
        _display.MessagePrint(name + " wails out in pain!");

        int killed = _loop.Combat.MonsterTakeHit(index, damage);

        if (killed >= 0)
        {
            _display.MessagePrint(name + " is embedded in the rock.");
            _loop.Levelling.PrintExperience();
        }

        return killed;
    }

    /// <summary>
    /// Replaces one square as the destruction rolls over it. Mirrors
    /// replace_spot().
    /// </summary>
    private void ReplaceSpot(int row, int column, int kind)
    {
        CaveSquare square = Cave[row, column];

        square.Feature = kind switch
        {
            1 or 2 or 3 => CaveFeature.CorridorFloor,
            4 or 7 or 10 => CaveFeature.GraniteWall,
            5 or 8 or 11 => CaveFeature.MagmaWall,
            _ => CaveFeature.QuartzWall,
        };

        square.PermanentLight = false;
        square.FieldMark = false;

        // Whatever room this was part of is gone.
        square.LitRoom = false;

        if (square.ObjectIndex != 0)
        {
            _loop.Movement.DeleteObject(row, column);
        }

        if (square.MonsterIndex > 1)
        {
            _game.Monsters.Delete(square.MonsterIndex, Cave, _loop.Lighting);
        }
    }

    /// <summary>
    /// The spell of destruction. Mirrors destroy_area().
    ///
    /// A winning creature caught in it counts as having teleported to another
    /// level rather than as having been killed, so the game cannot be won this
    /// way.
    /// </summary>
    public void DestroyArea(int row, int column)
    {
        if (_game.DungeonLevel > 0)
        {
            for (int y = row - 15; y <= row + 15; y++)
            {
                for (int x = column - 15; x <= column + 15; x++)
                {
                    if (!Cave.InBounds(y, x)
                        || Cave[y, x].Feature == CaveFeature.BoundaryWall)
                    {
                        continue;
                    }

                    int distance = Cave.Distance(y, x, row, column);

                    if (distance == 0)
                    {
                        // The player's own square is cleared but not walled in.
                        ReplaceSpot(y, x, 1);
                    }
                    else if (distance < 13)
                    {
                        ReplaceSpot(y, x, Rng.RandInt(6));
                    }
                    else if (distance < 16)
                    {
                        ReplaceSpot(y, x, Rng.RandInt(9));
                    }
                }
            }
        }

        _display.MessagePrint("There is a searing blast of light!");
        Player.Blind += 10 + Rng.RandInt(10);
    }

    // ---------------------------------------------------------- on the pack

    // ------------------------------------------------------ what they ask for
    //
    // Three of the effects in this file cannot start until the player has
    // answered a question. The questions are seams - get_item() and get_com()
    // belong to the part of misc3.c and io.c that is not ported yet - and they
    // live here rather than with the scrolls because a spell reaches the same
    // three functions that a scroll does.

    /// <summary>
    /// Asks which carried item to work on. Pending: get_item() from misc3.c.
    ///
    /// Returning nothing means the player declined, which for a scroll leaves
    /// the scroll unused.
    /// </summary>
    protected internal virtual int? ChooseItem(string prompt, int first, int last) => null;

    /// <summary>
    /// Asks for a single letter. Pending: get_com() from io.c. Used only by
    /// genocide.
    /// </summary>
    protected internal virtual char? ChooseSymbol(string prompt) => null;

    /// <summary>
    /// Identifies something the player picks. Mirrors ident_spell().
    /// </summary>
    /// <returns>Whether anything was chosen.</returns>
    public bool IdentSpell()
    {
        int? chosen = ChooseItem("Item you wish identified?", 0, Inventory.Size);

        if (chosen is not int slot)
        {
            return false;
        }

        IdentifyItem(slot);
        return true;
    }

    /// <summary>
    /// Recharges a wand or a staff the player picks. Mirrors recharge().
    /// </summary>
    /// <returns>Whether anything was chosen.</returns>
    public bool RechargeItem(int strength)
    {
        if (!_game.Inventory.FindRange(ItemCategory.Staff, ItemCategory.Wand,
                                       out int first, out int last))
        {
            _display.MessagePrint("You have nothing to recharge.");
            return false;
        }

        int? chosen = ChooseItem("Recharge which item?", first, last);

        if (chosen is not int slot)
        {
            return false;
        }

        Recharge(slot, strength);
        return true;
    }

    /// <summary>
    /// Wipes out every creature of a kind the player names. Mirrors genocide(),
    /// which asks for the letter itself.
    /// </summary>
    public bool GenocideSpell()
    {
        char? symbol = ChooseSymbol("Which type of creature do you wish exterminated?");

        return symbol is char letter && Genocide(letter);
    }

    /// <summary>
    /// Tells the player exactly what something is. Mirrors the working half of
    /// ident_spell(); the prompting belongs to the caller.
    /// </summary>
    /// <returns>The slot the item ended in, which merging can change.</returns>
    public int IdentifyItem(int slot)
    {
        slot = _game.Inventory.Identify(slot, _display);

        InvenType item = _game.Inventory[slot];
        _game.Knowledge.LearnEnchantment(item);

        string description = _game.Names.Describe(item, withArticle: true);

        if (slot >= Inventory.WieldSlot)
        {
            // Knowing what is worn can change what it is worth wearing.
            _loop.Equipment.Recalculate();
            _display.MessagePrint(
                Inventory.DescribeUse(slot) + ": " + description);
        }
        else
        {
            _display.MessagePrint(
                (char)(slot + 97) + " " + description);
        }

        return slot;
    }

    /// <summary>
    /// Puts charges back into a wand or a staff. Mirrors the working half of
    /// recharge(); the prompting belongs to the caller.
    ///
    /// Recharge I is a strength of twenty, recharge II of sixty. A deep item, or
    /// one that still holds charges, is harder to recharge and can be destroyed
    /// outright by the attempt.
    /// </summary>
    public void Recharge(int slot, int strength)
    {
        InvenType item = _game.Inventory[slot];

        // Can go negative, so it is tested before it is rolled against.
        int chance = strength + 50 - item.Level - item.P1;

        chance = chance < 19 ? 1 : Rng.RandInt(chance / 10);

        if (chance == 1)
        {
            _display.MessagePrint("There is a bright flash of light.");
            _game.Inventory.Destroy(slot);
            return;
        }

        int added = (strength / (item.Level + 2)) + 1;
        item.P1 += (short)(2 + Rng.RandInt(added));

        // The count is no longer the one the player knew, and the item is
        // plainly not empty any more.
        if (ItemKnowledge.IsEnchantmentKnown(item))
        {
            ItemKnowledge.ForgetEnchantment(item);
        }

        ItemKnowledge.ClearEmpty(item);
    }

    /// <summary>
    /// Puts a mushroom under the player. Mirrors create_food().
    ///
    /// FAITHFUL QUIRK: the original calls place_object() first and then
    /// overwrites what it made with a mushroom, so the roll for a random object
    /// is made and thrown away - and the level feeling that goes with it is
    /// spent.
    /// </summary>
    /// <returns>
    /// Whether the food was made. False means the turn was free, since there
    /// was already something underfoot.
    /// </returns>
    public bool CreateFood()
    {
        CaveSquare square = Cave[_game.CharacterRow, _game.CharacterColumn];

        if (square.ObjectIndex != 0)
        {
            // Nothing is done here rather than destroying what the player is
            // standing on, and the turn is given back so that the scroll or the
            // spell points are not spent for nothing.
            _display.MessagePrint("There is already an object under you.");
            _loop.FreeTurn = true;
            return false;
        }

        new DungeonGenerator(_game, _display).PlaceObject(
            _game.CharacterRow, _game.CharacterColumn, false);

        _game.Objects[square.ObjectIndex].CopyFrom(MushroomObject);
        return true;
    }

    /// <summary>A mushroom of food. Umoria's OBJ_MUSH.</summary>
    private const int MushroomObject = 397;


    /// <summary>
    /// Enchants one number on an item. Mirrors enchant().
    ///
    /// The better the item already is, the less likely another point is, and
    /// once past the limit only a one in a hundred roll lets it through - which
    /// is what keeps a stack of scrolls from making an ordinary sword perfect.
    /// </summary>
    public bool Enchant(ref short value, int limit)
    {
        if (limit <= 0)
        {
            return false;
        }

        int chance = 0;

        if (value > 0)
        {
            chance = value;

            // Very rarely, allow enchantment past the limit.
            if (Rng.RandInt(100) == 1)
            {
                chance = Rng.RandInt(chance) - 1;
            }
        }

        if (Rng.RandInt(limit) > chance)
        {
            value++;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Lifts the curse from what is worn. Mirrors remove_curse().
    /// </summary>
    public bool RemoveCurse()
    {
        bool lifted = false;

        for (int slot = Inventory.WieldSlot; slot <= Inventory.OuterSlot; slot++)
        {
            InvenType item = _game.Inventory[slot];

            if ((item.Flags & ItemFlags.Cursed) != 0)
            {
                item.Flags &= ~ItemFlags.Cursed;
                _loop.Equipment.Recalculate();
                lifted = true;
            }
        }

        return lifted;
    }
}
