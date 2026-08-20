// Ported from Umoria 5.6 source/creature.c - what the monsters do with their
// turn, less make_attack and mon_cast_spell.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The monsters' half of the turn.
///
/// Every monster on the level gets a look in, from the end of the list
/// backwards. What it does depends on how fast it is, whether it is asleep,
/// whether it can see the player and what its own flags allow: some only ever
/// attack, some wander at random, some walk through walls, and some breed until
/// stopped.
///
/// The list is scanned in place, so a monster that dies mid-scan cannot simply
/// be removed - another would take its index and get a second turn. Deaths
/// during the scan are marked and cleared up on the way past instead.
/// </summary>
public sealed class MonsterAi
{
    /// <summary>How far a monster can be seen at all. Umoria's MAX_SIGHT.</summary>
    public const int MaxSight = 20;

    /// <summary>The most a level will breed. Umoria's MAX_MON_MULT.</summary>
    public const int MaxBred = 75;

    /// <summary>How often breeding is even considered. Umoria's MON_MULT_ADJ.</summary>
    public const int BreedInterval = 7;

    /// <summary>A rune of protection's strength. Umoria's OBJ_RUNE_PROT.</summary>
    public const int RuneProtection = 3000;

    private readonly GameState _game;
    private readonly Display _display;
    private readonly GameLoop _loop;

    public MonsterAi(GameState game, Display display, GameLoop loop)
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
    /// Decides whether a monster can be seen, and draws or erases it. Mirrors
    /// update_mon().
    ///
    /// Three things can reveal one: ordinary sight of a lit square, seeing the
    /// invisible, and infravision, which shows warm-blooded creatures in the
    /// dark. Each of the last two teaches the player something, so noticing an
    /// invisible monster is recorded as well as acted on.
    /// </summary>
    public void UpdateMonster(int index)
    {
        Monster monster = _game.Monsters[index];
        bool seen = false;

        if (monster.DistanceToPlayer <= MaxSight
            && (Player.Status & PlayerStatus.Blind) == 0
            && _display.Panel.Contains(monster.Row, monster.Column))
        {
            if (_game.Wizard)
            {
                seen = true;
            }
            else if (LineOfSight.Between(
                Cave, _game.CharacterRow, _game.CharacterColumn,
                monster.Row, monster.Column))
            {
                CaveSquare square = Cave[monster.Row, monster.Column];
                CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

                if (square.PermanentLight || square.TemporaryLight
                    || (_game.Running && monster.DistanceToPlayer < 2 && _game.PlayerLight))
                {
                    if ((creature.MoveFlags & CreatureMove.Invisible) == 0)
                    {
                        seen = true;
                    }
                    else if (Player.SeeInvisible)
                    {
                        seen = true;
                        _game.Memories[monster.CreatureIndex].Move |= CreatureMove.Invisible;
                    }
                }
                else if (Player.SeeInfrared > 0
                         && monster.DistanceToPlayer <= Player.SeeInfrared
                         && (creature.DefenseFlags & CreatureDefense.Infravision) != 0)
                {
                    seen = true;
                    _game.Memories[monster.CreatureIndex].Defense |= CreatureDefense.Infravision;
                }
            }
        }

        if (seen)
        {
            if (!monster.Visible)
            {
                _loop.Disturb(true, false);
                monster.Visible = true;
                _loop.Lighting.LightSpot(monster.Row, monster.Column);

                // The inventory screen needs to know the map was drawn over.
                _display.ScreenChanged = true;
            }
        }
        else if (monster.Visible)
        {
            monster.Visible = false;
            _loop.Lighting.LightSpot(monster.Row, monster.Column);
            _display.ScreenChanged = true;
        }
    }

    /// <summary>
    /// How many moves a monster gets this turn. Mirrors movement_rate().
    ///
    /// The player always moves at least once, so a slowed player is handled by
    /// moving the monsters faster rather than by moving the player less. A
    /// resting player gives even a fast monster one move, which is what stops a
    /// rest being fatal at speed.
    /// </summary>
    public int MovementRate(int speed)
    {
        if (speed > 0)
        {
            return Player.Rest != 0 ? 1 : speed;
        }

        // Negative here: a move on one turn in however many.
        return _game.Turn % (2 - speed) == 0 ? 1 : 0;
    }

    /// <summary>Lights a monster that has just appeared. Mirrors check_mon_lite().</summary>
    private bool CheckMonsterLight(int row, int column)
    {
        int index = Cave[row, column].MonsterIndex;

        if (index <= 1)
        {
            return false;
        }

        UpdateMonster(index);
        return _game.Monsters[index].Visible;
    }

    /// <summary>
    /// Chooses which way a monster would rather go. Mirrors get_moves().
    ///
    /// Five directions are returned in order of preference: straight at the
    /// player first, then the two either side, then the two beyond those. The
    /// awkward arithmetic that picks between them is the original's way of
    /// avoiding the "diamond manoeuvre", where a monster approaching diagonally
    /// wobbles from side to side instead of closing.
    /// </summary>
    public void GetMoves(int index, Span<int> moves)
    {
        Monster monster = _game.Monsters[index];

        int y = monster.Row - _game.CharacterRow;
        int x = monster.Column - _game.CharacterColumn;

        int value;
        int absY;

        if (y < 0)
        {
            value = 8;
            absY = -y;
        }
        else
        {
            value = 0;
            absY = y;
        }

        int absX;

        if (x > 0)
        {
            value += 4;
            absX = x;
        }
        else
        {
            absX = -x;
        }

        if (absY > absX << 1)
        {
            value += 2;
        }
        else if (absX > absY << 1)
        {
            value++;
        }

        switch (value)
        {
            case 0:
                moves[0] = 9;
                Fill(moves, absY > absX ? [8, 6, 7, 3] : [6, 8, 3, 7]);
                break;

            case 1:
            case 9:
                moves[0] = 6;
                Fill(moves, y < 0 ? [3, 9, 2, 8] : [9, 3, 8, 2]);
                break;

            case 2:
            case 6:
                moves[0] = 8;
                Fill(moves, x < 0 ? [9, 7, 6, 4] : [7, 9, 4, 6]);
                break;

            case 4:
                moves[0] = 7;
                Fill(moves, absY > absX ? [8, 4, 9, 1] : [4, 8, 1, 9]);
                break;

            case 5:
            case 13:
                moves[0] = 4;
                Fill(moves, y < 0 ? [1, 7, 2, 8] : [7, 1, 8, 2]);
                break;

            case 8:
                moves[0] = 3;
                Fill(moves, absY > absX ? [2, 6, 1, 9] : [6, 2, 9, 1]);
                break;

            case 10:
            case 14:
                moves[0] = 2;
                Fill(moves, x < 0 ? [3, 1, 6, 4] : [1, 3, 4, 6]);
                break;

            case 12:
                moves[0] = 1;
                Fill(moves, absY > absX ? [2, 4, 3, 7] : [4, 2, 7, 3]);
                break;

            default:
                break;
        }
    }

    private static void Fill(Span<int> moves, ReadOnlySpan<int> rest)
    {
        for (int i = 0; i < rest.Length; i++)
        {
            moves[i + 1] = rest[i];
        }
    }

    /// <summary>
    /// Tries to move a monster, taking the first of its preferred directions
    /// that works. Mirrors make_move().
    ///
    /// Five attempts, and what counts as working depends on the monster: open
    /// floor for anything, rock as well for something that phases, and a door
    /// for something with hands. Walking into the player is an attack, and
    /// walking into another monster is only allowed for the ones that eat their
    /// own kind.
    /// </summary>
    public void MakeMove(int index, Span<int> moves, ref uint learned)
    {
        Monster monster = _game.Monsters[index];
        uint flags = GameTables.CreatureList[monster.CreatureIndex].MoveFlags;

        int attempt = 0;
        bool tookTurn = false;

        do
        {
            int newRow = monster.Row;
            int newColumn = monster.Column;
            Cave.Move(moves[attempt], ref newRow, ref newColumn);

            CaveSquare square = Cave[newRow, newColumn];

            if (square.Feature != CaveFeature.BoundaryWall)
            {
                bool canMove = false;

                if (square.Feature <= CaveFeature.MaxOpenSpace)
                {
                    canMove = true;
                }
                else if ((flags & CreatureMove.Phase) != 0)
                {
                    canMove = true;
                    learned |= CreatureMove.Phase;
                }
                else if (square.ObjectIndex != 0)
                {
                    canMove = TryDoor(monster, square, flags, newRow, newColumn,
                        ref tookTurn, ref learned);
                }

                // A rune of warding may hold it back, and may break.
                if (canMove && square.ObjectIndex != 0
                    && _game.Objects[square.ObjectIndex].TVal == ItemCategory.VisibleTrap
                    && _game.Objects[square.ObjectIndex].SubVal == 99)
                {
                    if (Rng.RandInt(RuneProtection)
                        < GameTables.CreatureList[monster.CreatureIndex].Level)
                    {
                        if (newRow == _game.CharacterRow && newColumn == _game.CharacterColumn)
                        {
                            _display.MessagePrint("The rune of protection is broken!");
                        }

                        _loop.Movement.DeleteObject(newRow, newColumn);
                    }
                    else
                    {
                        canMove = false;

                        // Something that only moves in order to attack has still
                        // spent its turn being held off.
                        if ((flags & CreatureMove.AttackOnly) != 0)
                        {
                            tookTurn = true;
                        }
                    }
                }

                if (canMove && square.MonsterIndex == 1)
                {
                    // A monster faster than the player may have arrived beside
                    // them this same turn, so it may not be lit yet.
                    if (!monster.Visible)
                    {
                        UpdateMonster(index);
                    }

                    _loop.MonsterAttack.MakeAttack(index);
                    canMove = false;
                    tookTurn = true;
                }
                else if (canMove && square.MonsterIndex > 1
                         && (newRow != monster.Row || newColumn != monster.Column))
                {
                    canMove = TryEat(index, monster, square, flags, ref learned);
                }

                if (canMove)
                {
                    Step(index, monster, flags, newRow, newColumn, ref learned);
                    tookTurn = true;
                }
            }

            attempt++;
        }
        while (!tookTurn && attempt < 5);
    }

    /// <summary>
    /// A door in the way: opened by something with hands, battered by something
    /// without.
    /// </summary>
    private bool TryDoor(
        Monster monster, CaveSquare square, uint flags, int row, int column,
        ref bool tookTurn, ref uint learned)
    {
        InvenType door = _game.Objects[square.ObjectIndex];

        if ((flags & CreatureMove.OpensDoors) != 0)
        {
            bool stuck = false;
            bool opened = false;

            if (door.TVal == ItemCategory.ClosedDoor)
            {
                tookTurn = true;

                if (door.P1 == 0)
                {
                    opened = true;
                }
                else if (door.P1 > 0)
                {
                    // Locked: strength against the lock.
                    if (Rng.RandInt((monster.HitPoints + 1) * (50 + door.P1))
                        < 40 * (monster.HitPoints - 10 - door.P1))
                    {
                        door.P1 = 0;
                    }
                }
                else
                {
                    // Stuck: strength against the jam.
                    if (Rng.RandInt((monster.HitPoints + 1) * (50 - door.P1))
                        < 40 * (monster.HitPoints - 10 + door.P1))
                    {
                        _display.MessagePrint("You hear a door burst open!");
                        _loop.Disturb(true, false);
                        stuck = true;
                        opened = true;
                    }
                }
            }
            else if (door.TVal == ItemCategory.SecretDoor)
            {
                tookTurn = true;
                opened = true;
            }

            if (opened)
            {
                door.CopyFrom(OpenDoorObject);

                if (stuck)
                {
                    // An even chance of having broken it on the way through.
                    door.P1 = (short)(1 - Rng.RandInt(2));
                }

                square.Feature = CaveFeature.CorridorFloor;
                _loop.Lighting.LightSpot(row, column);
                learned |= CreatureMove.OpensDoors;

                // Opening it was the whole turn; walking through is next turn.
                return false;
            }

            return false;
        }

        // No hands: it can only batter the thing down.
        if (door.TVal == ItemCategory.ClosedDoor)
        {
            tookTurn = true;

            if (Rng.RandInt((monster.HitPoints + 1) * (80 + Math.Abs(door.P1)))
                < 40 * (monster.HitPoints - 20 - Math.Abs(door.P1)))
            {
                door.CopyFrom(OpenDoorObject);
                door.P1 = (short)(1 - Rng.RandInt(2));
                square.Feature = CaveFeature.CorridorFloor;
                _loop.Lighting.LightSpot(row, column);
                _display.MessagePrint("You hear a door burst open!");
                _loop.Disturb(true, false);
            }
        }

        return false;
    }

    /// <summary>
    /// Another monster in the way, which only the cannibals may pass - and only
    /// by eating what is there.
    /// </summary>
    private bool TryEat(
        int index, Monster monster, CaveSquare square, uint flags, ref uint learned)
    {
        Monster other = _game.Monsters[square.MonsterIndex];

        bool canEat = (flags & CreatureMove.EatsOtherMonsters) != 0
            && GameTables.CreatureList[monster.CreatureIndex].KillExperience
               >= GameTables.CreatureList[other.CreatureIndex].KillExperience;

        if (!canEat)
        {
            return false;
        }

        if (other.Visible)
        {
            learned |= CreatureMove.EatsOtherMonsters;
        }

        // Eating one that has already had its turn is safe; eating one that has
        // not would let an already-moved monster take its place and move twice,
        // so that death is delayed instead.
        if (index < square.MonsterIndex)
        {
            _game.Monsters.Delete(square.MonsterIndex, Cave, _loop.Lighting);
        }
        else
        {
            _game.Monsters.MarkDead(square.MonsterIndex, Cave, _loop.Lighting);
        }

        return true;
    }

    /// <summary>Actually moves a monster, picking up anything it eats on the way.</summary>
    private void Step(
        int index, Monster monster, uint flags, int newRow, int newColumn, ref uint learned)
    {
        if ((flags & CreatureMove.PicksUpObjects) != 0)
        {
            CaveSquare square = Cave[newRow, newColumn];

            if (square.ObjectIndex != 0
                && _game.Objects[square.ObjectIndex].TVal <= ItemCategory.MaxObject)
            {
                learned |= CreatureMove.PicksUpObjects;
                _loop.Movement.DeleteObject(newRow, newColumn);
            }
        }

        _loop.Lighting.MoveRecord(monster.Row, monster.Column, newRow, newColumn);

        if (monster.Visible)
        {
            monster.Visible = false;
            _loop.Lighting.LightSpot(monster.Row, monster.Column);
        }

        monster.Row = newRow;
        monster.Column = newColumn;
        monster.DistanceToPlayer =
            Cave.Distance(_game.CharacterRow, _game.CharacterColumn, newRow, newColumn);
    }

    /// <summary>The open door a forced door becomes. Umoria's OBJ_OPEN_DOOR.</summary>
    private const int OpenDoorObject = 367;

    /// <summary>
    /// Breeds a monster into a nearby square. Mirrors multiply_monster().
    ///
    /// Eighteen tries at a square within two, never the one it started on -
    /// which would make an invisible, invincible monster of the pair.
    /// </summary>
    public bool MultiplyMonster(int row, int column, int creatureIndex, int index)
    {
        for (int attempt = 0; attempt <= 18; attempt++)
        {
            int y = row - 2 + Rng.RandInt(3);
            int x = column - 2 + Rng.RandInt(3);

            if (!Cave.InBounds(y, x) || (y == row && x == column))
            {
                continue;
            }

            CaveSquare square = Cave[y, x];

            if (square.Feature > CaveFeature.MaxOpenSpace || square.ObjectIndex != 0
                || square.MonsterIndex == 1)
            {
                continue;
            }

            if (square.MonsterIndex > 1)
            {
                // Something is there already, which only a cannibal may clear.
                bool canEat =
                    (GameTables.CreatureList[creatureIndex].MoveFlags
                     & CreatureMove.EatsOtherMonsters) != 0
                    && GameTables.CreatureList[creatureIndex].KillExperience
                       >= GameTables.CreatureList[
                           _game.Monsters[square.MonsterIndex].CreatureIndex].KillExperience;

                if (!canEat)
                {
                    continue;
                }

                if (index < square.MonsterIndex)
                {
                    _game.Monsters.Delete(square.MonsterIndex, Cave, _loop.Lighting);
                }
                else
                {
                    _game.Monsters.MarkDead(square.MonsterIndex, Cave, _loop.Lighting);
                }
            }

            // Compacting the monster list needs to know which monster is being
            // processed, in case it is the one that has to go.
            _game.Monsters.ScanIndex = index;
            bool placed = new DungeonGenerator(_game, _display).PlaceMonster(y, x, creatureIndex, false);
            _game.Monsters.ScanIndex = -1;

            if (!placed)
            {
                return false;
            }

            _game.Monsters.BredCount++;
            return CheckMonsterLight(y, x);
        }

        return false;
    }

    /// <summary>
    /// One monster's turn. Mirrors mon_move().
    ///
    /// The order is fixed and matters: breeding first, then digging out of rock,
    /// then confusion, then spells, and only then ordinary movement. A monster
    /// stuck in rock is given a turn even when it cannot be seen, so that it
    /// digs itself out or dies rather than sitting there forever.
    /// </summary>
    public void MonsterMove(int index, ref uint learned)
    {
        Monster monster = _game.Monsters[index];
        CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];
        Span<int> moves = stackalloc int[9];

        // Resting can be negative, so only the size of it is used.
        int rest = Math.Abs(Player.Rest);

        if ((creature.MoveFlags & CreatureMove.Multiplies) != 0
            && MaxBred >= _game.Monsters.BredCount
            && rest % BreedInterval == 0)
        {
            int neighbours = 0;

            for (int y = monster.Row - 1; y <= monster.Row + 1; y++)
            {
                for (int x = monster.Column - 1; x <= monster.Column + 1; x++)
                {
                    if (Cave.InBounds(y, x) && Cave[y, x].MonsterIndex > 1)
                    {
                        neighbours++;
                    }
                }
            }

            // The roll cannot be made with a count of zero, and a lone breeder
            // should still be able to breed.
            if (neighbours == 0)
            {
                neighbours++;
            }

            if (neighbours < 4 && Rng.RandInt(neighbours * BreedInterval) == 1
                && MultiplyMonster(monster.Row, monster.Column, monster.CreatureIndex, index))
            {
                learned |= CreatureMove.Multiplies;
            }
        }

        bool acted = false;

        // Stuck in rock: it must get out at once, or die trying.
        if ((creature.MoveFlags & CreatureMove.Phase) == 0
            && Cave[monster.Row, monster.Column].Feature >= CaveFeature.MinCaveWall)
        {
            // Already dead: a monster faster than the player gets several moves,
            // and should not be killed twice on the way through the rock.
            if (monster.HitPoints < 0)
            {
                return;
            }

            int count = 0;
            int direction = 1;

            // The loops run in the order the number pad does, from 1 to 9, and
            // the player's own square is never a candidate.
            for (int y = monster.Row + 1; y >= monster.Row - 1; y--)
            {
                for (int x = monster.Column - 1; x <= monster.Column + 1; x++)
                {
                    if (direction != 5 && Cave[y, x].Feature <= CaveFeature.MaxOpenSpace
                        && Cave[y, x].MonsterIndex != 1)
                    {
                        moves[count++] = direction;
                    }

                    direction++;
                }
            }

            if (count != 0)
            {
                // One of them, chosen at random, goes first.
                int pick = Rng.RandInt(count) - 1;
                (moves[0], moves[pick]) = (moves[pick], moves[0]);
                MakeMove(index, moves, ref learned);
            }

            if (Cave[monster.Row, monster.Column].Feature >= CaveFeature.MinCaveWall)
            {
                // Still in the rock: it digs, and the rock hurts.
                _game.Monsters.ScanIndex = index;
                int killed = _loop.Combat.MonsterTakeHit(index, Rng.DamRoll(8, 8));
                _game.Monsters.ScanIndex = -1;

                if (killed >= 0)
                {
                    _display.MessagePrint("You hear a scream muffled by rock!");
                    _loop.Levelling.PrintExperience();
                }
                else
                {
                    _display.MessagePrint("A creature digs itself out from the rock!");
                    _loop.Doors.TunnelWall(monster.Row, monster.Column, 1, 0);
                }
            }

            return;
        }

        if (monster.Confused != 0)
        {
            if ((creature.DefenseFlags & CreatureDefense.Undead) != 0)
            {
                // The undead are only ever confused by being turned, so they
                // flee rather than stagger.
                GetMoves(index, moves);
                moves[0] = 10 - moves[0];
                moves[1] = 10 - moves[1];
                moves[2] = 10 - moves[2];
                moves[3] = Rng.RandInt(9);
                moves[4] = Rng.RandInt(9);
            }
            else
            {
                for (int i = 0; i < 5; i++)
                {
                    moves[i] = Rng.RandInt(9);
                }
            }

            // Something that never moves does not stagger either.
            if ((creature.MoveFlags & CreatureMove.AttackOnly) == 0)
            {
                MakeMove(index, moves, ref learned);
            }

            monster.Confused--;
            acted = true;
        }
        else if (creature.CastsSpells)
        {
            acted = _loop.MonsterAttack.CastSpell(index);
        }

        if (acted)
        {
            return;
        }

        if ((creature.MoveFlags & CreatureMove.Move75PercentRandom) != 0
            && Rng.RandInt(100) < 75)
        {
            RandomMoves(moves);
            learned |= CreatureMove.Move75PercentRandom;
            MakeMove(index, moves, ref learned);
        }
        else if ((creature.MoveFlags & CreatureMove.Move40PercentRandom) != 0
                 && Rng.RandInt(100) < 40)
        {
            RandomMoves(moves);
            learned |= CreatureMove.Move40PercentRandom;
            MakeMove(index, moves, ref learned);
        }
        else if ((creature.MoveFlags & CreatureMove.Move20PercentRandom) != 0
                 && Rng.RandInt(100) < 20)
        {
            RandomMoves(moves);
            learned |= CreatureMove.Move20PercentRandom;
            MakeMove(index, moves, ref learned);
        }
        else if ((creature.MoveFlags & CreatureMove.MoveNormal) != 0)
        {
            // Even a purposeful monster wanders once in two hundred turns.
            if (Rng.RandInt(200) == 1)
            {
                RandomMoves(moves);
            }
            else
            {
                GetMoves(index, moves);
            }

            learned |= CreatureMove.MoveNormal;
            MakeMove(index, moves, ref learned);
        }
        else if ((creature.MoveFlags & CreatureMove.AttackOnly) != 0)
        {
            if (monster.DistanceToPlayer < 2)
            {
                GetMoves(index, moves);
                MakeMove(index, moves, ref learned);
            }
            else
            {
                // Learn that it did not move when it should have.
                learned |= CreatureMove.AttackOnly;
            }
        }
        else if ((creature.MoveFlags & CreatureMove.OnlyMagic) != 0
                 && monster.DistanceToPlayer < 2)
        {
            MonsterMemory memory = _game.Memories[monster.CreatureIndex];

            // A little hack for the Quylthulgs, which have no physical attack at
            // all: counting the turns they stand there is how the player
            // eventually notices, and then how they learn its speed.
            if (memory.Attacks[0] < byte.MaxValue)
            {
                memory.Attacks[0]++;
            }

            if (memory.Attacks[0] > 20)
            {
                memory.Move |= CreatureMove.OnlyMagic;
            }
        }
    }

    private void RandomMoves(Span<int> moves)
    {
        for (int i = 0; i < 5; i++)
        {
            moves[i] = Rng.RandInt(9);
        }
    }

    /// <summary>
    /// Every monster takes its turn. Mirrors creatures().
    ///
    /// The list is walked backwards, because a monster added during the scan -
    /// by breeding, say - lands at the end and should not also move this turn.
    /// A sleeping monster may be woken by the noise the player makes, which is
    /// why stealth is worth having: the check is the cube of a random number
    /// against a shift by the player's stealth.
    /// </summary>
    /// <param name="attack">
    /// False to light the monsters without moving them, which is what a change
    /// of light asks for.
    /// </param>
    public void Creatures(bool attack)
    {
        for (int i = _game.Monsters.Count - 1; i >= MonsterPool.FirstIndex && !_loop.Dead; i--)
        {
            Monster monster = _game.Monsters[i];

            // Something eaten or breathed on during this scan: clear it up now
            // rather than processing it.
            if (monster.HitPoints < 0)
            {
                _game.Monsters.Compact(i, Cave);
                continue;
            }

            monster.DistanceToPlayer = Cave.Distance(
                _game.CharacterRow, _game.CharacterColumn, monster.Row, monster.Column);

            if (!attack)
            {
                UpdateMonster(i);
                continue;
            }

            int moves = MovementRate(monster.Speed);

            if (moves <= 0)
            {
                UpdateMonster(i);
            }
            else
            {
                while (moves > 0)
                {
                    moves--;
                    TakeOneTurn(i, monster);
                }
            }

            // It may have been killed during its own move.
            if (monster.HitPoints < 0)
            {
                _game.Monsters.Compact(i, Cave);
            }
        }
    }

    private void TakeOneTurn(int index, Monster monster)
    {
        bool woke = false;
        bool ignored = false;
        uint learned = 0;

        CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

        bool awake = monster.Visible
            || monster.DistanceToPlayer <= creature.AreaOfEffect
            // Something trapped in rock is given a turn regardless, so that it
            // digs out or dies at once.
            || ((creature.MoveFlags & CreatureMove.Phase) == 0
                && Cave[monster.Row, monster.Column].Feature >= CaveFeature.MinCaveWall);

        if (awake)
        {
            if (monster.Sleep > 0)
            {
                if (Player.AggravatesMonsters)
                {
                    monster.Sleep = 0;
                }
                else if ((Player.Rest == 0 && Player.Paralysis < 1) || Rng.RandInt(50) == 1)
                {
                    int notice = Rng.RandInt(1024);

                    // The cube of the roll against the player's stealth: a
                    // stealthy character has to be very unlucky to be heard.
                    if (notice * notice * notice <= 1 << (29 - Player.Stealth))
                    {
                        monster.Sleep -= 100 / monster.DistanceToPlayer;

                        if (monster.Sleep > 0)
                        {
                            ignored = true;
                        }
                        else
                        {
                            woke = true;
                            monster.Sleep = 0;
                        }
                    }
                }
            }

            if (monster.Stunned != 0)
            {
                // The Balrog recovers instantly: a hundred squared is beyond the
                // roll.
                if (Rng.RandInt(5000) < creature.Level * creature.Level)
                {
                    monster.Stunned = 0;
                }
                else
                {
                    monster.Stunned--;
                }

                if (monster.Stunned == 0 && monster.Visible)
                {
                    _display.MessagePrint(
                        "The " + creature.Name + " recovers and glares at you.");
                }
            }

            if (monster.Sleep == 0 && monster.Stunned == 0)
            {
                MonsterMove(index, ref learned);
            }
        }

        UpdateMonster(index);

        if (!monster.Visible)
        {
            return;
        }

        MonsterMemory memory = _game.Memories[monster.CreatureIndex];

        if (woke)
        {
            if (memory.Wake < byte.MaxValue)
            {
                memory.Wake++;
            }
        }
        else if (ignored)
        {
            if (memory.Ignore < byte.MaxValue)
            {
                memory.Ignore++;
            }
        }

        memory.Move |= learned;
    }
}
